using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Performs a deferred restore before feature initialization and under the
/// lifetime lock acquired by App. Never opens the live database while other
/// Daycord features are active.
/// </summary>
public sealed class PendingRestoreCoordinator : IDisposable
{
    private readonly AppDataPaths _paths;
    private readonly SqliteSnapshotValidator _validator = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _instanceLock;
    private bool _disposed;

    /// <summary>True only when this launch installed a verified emergency backup.</summary>
    public bool IsEmergencyRestoreInstalled { get; private set; }

    public PendingRestoreCoordinator(AppDataPaths paths) => _paths = paths;

    public void AcquireApplicationLock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instanceLock is not null)
            return;

        _paths.EnsureDataDirectory();

        _instanceLock = new FileStream(
            Path.Combine(_paths.DataDirectory, ".daycord-instance.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public async Task<bool> ApplyPendingRestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            RequireApplicationLock();

            return await Task.Run(
                () => ApplyPendingRestore(cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Prepares a verified backup for emergency restoration.
    /// Requires the application instance lock. Preparation does not change
    /// the live database; installation is performed separately through
    /// ApplyPendingRestoreAsync.
    /// </summary>
    public async Task PrepareEmergencyRestoreAsync(
        string selectedBackupPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            RequireApplicationLock();
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(
                () => new EmergencyRestorePreparation(_paths)
                    .Prepare(selectedBackupPath, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAfterInitializationAsync()
    {
        await _gate.WaitAsync();

        try
        {
            RequireApplicationLock();

            await Task.Run(() =>
            {
                var manifest = _paths.PendingRestoreManifestPath;

                if (!File.Exists(manifest))
                    throw new InvalidOperationException(
                        "The restore journal is missing.");

                var state = PendingRestoreStateStore.Read(manifest);

                if (state.Mode == PendingRestoreMode.Emergency)
                {
                    new EmergencyRestoreInstaller(_paths)
                        .CompleteAfterInitialization();
                    return;
                }

                if (state.Phase != PendingRestorePhase.Installed)
                    throw new InvalidOperationException(
                        "The restore was not installed.");

                ValidateSafety(state);
                ValidateRawArchiveIfPresent(state);
                RequireValidLiveDatabaseSnapshot();

                // Only a successfully initialized database can be committed.
                // Keep the safety backup permanently.
                File.Delete(manifest);
                TryDeleteFile(StagedPath(state));
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RollbackAfterInitializationFailureAsync()
    {
        await _gate.WaitAsync();

        try
        {
            RequireApplicationLock();

            await Task.Run(() =>
            {
                var manifest = _paths.PendingRestoreManifestPath;

                if (!File.Exists(manifest))
                    return;

                var state = PendingRestoreStateStore.Read(manifest);

                if (state.Mode == PendingRestoreMode.Emergency)
                {
                    // An emergency restore may have no valid safety snapshot.
                    // Never roll back to an unreadable original database.
                    new EmergencyRestoreInstaller(_paths)
                        .MarkInitializationFailed();
                    return;
                }

                if (state.Phase is PendingRestorePhase.Installing or
                    PendingRestorePhase.Installed or
                    PendingRestorePhase.RollingBack)
                {
                    Rollback(state);
                }
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool ApplyPendingRestore(CancellationToken cancellationToken)
    {
        IsEmergencyRestoreInstalled = false;
        var manifest = _paths.PendingRestoreManifestPath;

        if (!File.Exists(manifest))
            return false;

        var state = PendingRestoreStateStore.Read(manifest);

        if (state.Mode == PendingRestoreMode.Emergency)
        {
            // This branch is intentionally before the normal restore phase
            // handling: a damaged original must not enter normal rollback.
            var installed = new EmergencyRestoreInstaller(_paths)
                .Apply(state, cancellationToken);
            IsEmergencyRestoreInstalled = installed;
            return installed;
        }

        // A crash during replacement leaves the final state ambiguous.
        // Never infer successful installation from daycord.db bytes alone.
        if (state.Phase is PendingRestorePhase.Installing or
            PendingRestorePhase.RollingBack)
        {
            Rollback(state);

            throw new InvalidOperationException(
                "An interrupted restore was rolled back. Restart Daycord.");
        }

        try
        {
            if (state.Phase == PendingRestorePhase.Installed)
            {
                ValidateSafety(state);
                ValidateRawArchiveIfPresent(state);
                ValidateStaged(state);
                RequireValidLiveDatabaseSnapshot();

                return true;
            }

            if (state.Phase is not (
                PendingRestorePhase.Prepared or
                PendingRestorePhase.RawCaptured or
                PendingRestorePhase.SafetyCreated))
            {
                throw new InvalidDataException(
                    "Unknown restore journal phase.");
            }

            ValidateStaged(state);
            cancellationToken.ThrowIfCancellationRequested();

            var rawArchive = new RawDatabaseRecoveryArchive(_paths);

            if (state.Phase == PendingRestorePhase.Prepared)
            {
                // A published archive may exist if the process terminated
                // immediately before updating the journal. Find and verify
                // that exact operation's archive rather than duplicating it.
                // No active database modification has happened yet.
                var reference = rawArchive.EnsureCaptured(
                    state.OperationId, cancellationToken);

                state = state with
                {
                    Phase = PendingRestorePhase.RawCaptured,
                    RawArchiveDirectoryName = reference.DirectoryName,
                    RawArchiveManifestSha256 = reference.ManifestSha256
                };

                PendingRestoreStateStore.Update(manifest, state);
            }

            if (state.Phase == PendingRestorePhase.RawCaptured)
            {
                // Never continue after a crash without proving that every
                // original file is still preserved under its recorded hash.
                ValidateRawArchiveIfPresent(state);
                cancellationToken.ThrowIfCancellationRequested();

                // Normal Restore requires a valid safety snapshot.
                var safetyName = CreateSafetyBackup(cancellationToken);
                var safetyPath = Path.Combine(
                    _paths.BeforeRestoreBackupDirectory, safetyName);

                state = state with
                {
                    Phase = PendingRestorePhase.SafetyCreated,
                    SafetyBackupFileName = safetyName,
                    SafetyBackupSha256 =
                        PendingRestoreStateStore.GetSha256(safetyPath)
                };

                PendingRestoreStateStore.Update(manifest, state);
            }

            // A restarted SafetyCreated operation must reuse the recorded
            // safety snapshot and raw archive, not create replacements.
            ValidateSafety(state);
            ValidateRawArchiveIfPresent(state);

            cancellationToken.ThrowIfCancellationRequested();

            Install(state);
            return true;
        }
        catch
        {
            // A failure after the installation journal was written
            // must never lead to an ordinary application startup.
            if (File.Exists(manifest))
            {
                var current = PendingRestoreStateStore.Read(manifest);

                if (current.Phase is PendingRestorePhase.Installing or
                    PendingRestorePhase.Installed or
                    PendingRestorePhase.RollingBack)
                {
                    Rollback(current);
                }
            }

            throw;
        }
    }

    private void Install(PendingRestoreState state)
    {
        ValidateStaged(state);
        ValidateSafety(state);
        ValidateRawArchiveIfPresent(state);

        var installTemp = NewInstallTempPath();

        try
        {
            // Create a standalone DELETE-mode SQLite snapshot before
            // making any changes to the current database.
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                StagedPath(state),
                installTemp);

            RequireValidSnapshot(installTemp);
            SqliteSnapshotIO.RequireNoSidecars(installTemp);

            // Checkpoint the current WAL and leave WAL mode.
            // No live SQLite connection may remain after this.
            PrepareDatabaseForReplacement();

            state = state with
            {
                Phase = PendingRestorePhase.Installing
            };

            PendingRestoreStateStore.Update(
                _paths.PendingRestoreManifestPath,
                state);

            // Source and destination are in the same directory.
            File.Move(
                installTemp,
                _paths.DatabasePath,
                overwrite: true);

            RequireValidSnapshot(_paths.DatabasePath);
            SqliteSnapshotIO.RequireNoSidecars(_paths.DatabasePath);

            PendingRestoreStateStore.Update(
                _paths.PendingRestoreManifestPath,
                state with
                {
                    Phase = PendingRestorePhase.Installed
                });
        }
        finally
        {
            TryDeleteFile(installTemp);
        }
    }

    private void Rollback(PendingRestoreState state)
    {
        // These references must still describe the exact immutable safety
        // artifacts recorded before the original installation began.
        ValidateSafety(state);
        ValidateRawArchiveIfPresent(state);

        var manifest = _paths.PendingRestoreManifestPath;

        // Persist intent before touching the active file set. This transition
        // is idempotent and recognized by the next launch after a crash.
        PendingRestoreStateStore.Update(
            manifest,
            state with { Phase = PendingRestorePhase.RollingBack });

        // If the last attempt installed and verified the safety copy, but
        // crashed before deleting the journal, simply finish that attempt.
        // In particular, never quarantine an already-restored database.
        if (IsSafetyAlreadyInstalled(state))
        {
            File.Delete(manifest);
            return;
        }

        var temporary = NewInstallTempPath();
        try
        {
            // Prepare and verify the replacement *before* moving any of the
            // active files. The original safety backup is never consumed.
            SqliteSnapshotIO.CopyDurably(SafetyPath(state), temporary);
            RequireValidSnapshot(temporary);
            SqliteSnapshotIO.RequireNoSidecars(temporary);

            if (!HashMatches(temporary, state.SafetyBackupSha256!))
                throw new InvalidDataException(
                    "The prepared rollback copy differs from its safety snapshot.");

            // Do not open the possibly corrupt active database through SQLite.
            // Quarantine the installed .db and any remaining WAL/SHM/journal.
            // A deterministic directory permits retry after a crash between
            // individual file moves. Existing quarantined files are retained.
            new RollbackFileQuarantine(_paths)
                .MoveActiveFilesAside(state.OperationId);

            // The source path must now be absent. Refuse overwrite if an
            // external process has recreated it in the meantime.
            File.Move(temporary, _paths.DatabasePath);

            RequireInstalledSafety(state);

            // Only a verified rollback clears the journal. Safety, staging
            // and quarantine artifacts are preserved for investigation.
            File.Delete(manifest);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private bool IsSafetyAlreadyInstalled(PendingRestoreState state)
    {
        if (!File.Exists(_paths.DatabasePath) ||
            File.Exists(_paths.DatabasePath + "-wal") ||
            File.Exists(_paths.DatabasePath + "-shm") ||
            File.Exists(_paths.DatabasePath + "-journal"))
        {
            return false;
        }

        if (!HashMatches(_paths.DatabasePath, state.SafetyBackupSha256!))
            return false;

        // A matching hash is not enough: the file must be a valid Daycord
        // snapshot before the rollback journal can be cleared.
        RequireInstalledSafety(state);
        return true;
    }

    private void RequireInstalledSafety(PendingRestoreState state)
    {
        if (!HashMatches(_paths.DatabasePath, state.SafetyBackupSha256!))
            throw new IOException(
                "The installed rollback database does not match the safety snapshot.");

        RequireValidSnapshot(_paths.DatabasePath);
        SqliteSnapshotIO.RequireNoSidecars(_paths.DatabasePath);
    }

    private string CreateSafetyBackup(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException(
                "The current database is missing.",
                _paths.DatabasePath);
        }

        _paths.EnsureBeforeRestoreBackupDirectory();

        var name =
            $"daycord-before-restore-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.db";

        var destination = Path.Combine(
            _paths.BeforeRestoreBackupDirectory,
            name);

        var temporary = destination + ".tmp.db";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            SqliteSnapshotIO.CreateStandaloneSnapshot(
                _paths.DatabasePath,
                temporary);

            RequireValidSnapshot(temporary);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(temporary, destination);
            return name;
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private void ValidateStaged(PendingRestoreState state)
    {
        var path = StagedPath(state);

        if (!HashMatches(path, state.StagedSha256))
        {
            throw new InvalidDataException(
                "The staged backup is missing or has changed.");
        }

        RequireValidSnapshot(path);
        SqliteSnapshotIO.RequireNoSidecars(path);
    }

    private void ValidateSafety(PendingRestoreState state)
    {
        var path = SafetyPath(state);

        if (!HashMatches(
                path,
                state.SafetyBackupSha256!))
        {
            throw new InvalidDataException(
                "The safety backup is missing or has changed.");
        }

        RequireValidSnapshot(path);
        SqliteSnapshotIO.RequireNoSidecars(path);
    }

    private void ValidateRawArchiveIfPresent(PendingRestoreState state)
    {
        // Older journals do not have a raw-archive reference and remain
        // compatible. Newly created operations always record both fields.
        if (state.RawArchiveDirectoryName is null)
            return;

        new RawDatabaseRecoveryArchive(_paths).ValidateReference(
            state.OperationId,
            state.RawArchiveDirectoryName,
            state.RawArchiveManifestSha256
                ?? throw new InvalidDataException(
                    "The raw recovery archive checksum is missing."));
    }

    private void RequireValidLiveDatabaseSnapshot()
    {
        _paths.EnsureRestoreStagingDirectory();

        var path = Path.Combine(
            _paths.RestoreStagingDirectory,
            $".validate-restored-{Guid.NewGuid():N}.db");

        try
        {
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                _paths.DatabasePath,
                path);

            RequireValidSnapshot(path);
        }
        finally
        {
            TryDeleteFile(path);
        }
    }

    private void RequireValidSnapshot(string path)
    {
        var validation = _validator.Validate(path);

        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                $"SQLite snapshot validation failed: {validation.Status}.");
        }
    }

    private void PrepareDatabaseForReplacement()
    {
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException(
                "The current database is missing.",
                _paths.DatabasePath);
        }

        var options = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 5
        };

        using (var connection =
               new SqliteConnection(options.ToString()))
        {
            connection.Open();

            using (var mode = connection.CreateCommand())
            {
                mode.CommandText = "PRAGMA journal_mode;";

                var current = Convert.ToString(
                    mode.ExecuteScalar());

                if (string.Equals(
                        current,
                        "wal",
                        StringComparison.OrdinalIgnoreCase))
                {
                    using var checkpoint =
                        connection.CreateCommand();

                    checkpoint.CommandText =
                        "PRAGMA wal_checkpoint(TRUNCATE);";

                    using var rows = checkpoint.ExecuteReader();

                    if (!rows.Read() || rows.GetInt32(0) != 0)
                    {
                        throw new IOException(
                            "Cannot checkpoint SQLite WAL; " +
                            "another connection may be active.");
                    }
                }
            }

            using var changeMode =
                connection.CreateCommand();

            changeMode.CommandText =
                "PRAGMA journal_mode = DELETE;";

            var result = Convert.ToString(
                changeMode.ExecuteScalar());

            if (!string.Equals(
                    result,
                    "delete",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Cannot safely leave SQLite WAL mode.");
            }
        }

        SqliteSnapshotIO.RequireNoSidecars(
            _paths.DatabasePath);
    }

    private static bool HashMatches(
        string path,
        string expected)
    {
        return File.Exists(path) &&
               string.Equals(
                   PendingRestoreStateStore.GetSha256(path),
                   expected,
                   StringComparison.OrdinalIgnoreCase);
    }

    private string StagedPath(PendingRestoreState state)
    {
        return Path.Combine(
            _paths.RestoreStagingDirectory,
            state.StagedFileName);
    }

    private string SafetyPath(PendingRestoreState state)
    {
        return Path.Combine(
            _paths.BeforeRestoreBackupDirectory,
            state.SafetyBackupFileName ??
            throw new InvalidDataException(
                "Safety path is missing."));
    }

    private string NewInstallTempPath()
    {
        return Path.Combine(
            _paths.DataDirectory,
            $".daycord-install-{Guid.NewGuid():N}.db");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void RequireApplicationLock()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_instanceLock is null)
        {
            throw new InvalidOperationException(
                "The Daycord instance lock has not been acquired.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _instanceLock?.Dispose();
        _gate.Dispose();
    }
}