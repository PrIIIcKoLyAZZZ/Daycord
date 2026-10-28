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

                if (state.Phase != PendingRestorePhase.Installed)
                    throw new InvalidOperationException(
                        "The restore was not installed.");

                ValidateSafety(state);
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
        var manifest = _paths.PendingRestoreManifestPath;

        if (!File.Exists(manifest))
            return false;

        var state = PendingRestoreStateStore.Read(manifest);

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
                ValidateStaged(state);
                RequireValidLiveDatabaseSnapshot();

                return true;
            }

            if (state.Phase is not (
                PendingRestorePhase.Prepared or
                PendingRestorePhase.SafetyCreated))
            {
                throw new InvalidDataException(
                    "Unknown restore journal phase.");
            }

            ValidateStaged(state);
            cancellationToken.ThrowIfCancellationRequested();

            // Take a fresh safety backup even if an earlier process
            // already reached SafetyCreated.
            var safetyName = CreateSafetyBackup(cancellationToken);

            var safetyPath = Path.Combine(
                _paths.BeforeRestoreBackupDirectory,
                safetyName);

            state = state with
            {
                Phase = PendingRestorePhase.SafetyCreated,
                SafetyBackupFileName = safetyName,
                SafetyBackupSha256 =
                    PendingRestoreStateStore.GetSha256(safetyPath)
            };

            PendingRestoreStateStore.Update(manifest, state);

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
        ValidateSafety(state);

        // Save rollback intent before modifying any files.
        // Repeating rollback after a crash is safe.
        PendingRestoreStateStore.Update(
            _paths.PendingRestoreManifestPath,
            state with
            {
                Phase = PendingRestorePhase.RollingBack
            });

        var temporary = NewInstallTempPath();

        try
        {
            SqliteSnapshotIO.CopyDurably(
                SafetyPath(state),
                temporary);

            RequireValidSnapshot(temporary);
            SqliteSnapshotIO.RequireNoSidecars(temporary);

            if (!HashMatches(
                    temporary,
                    state.SafetyBackupSha256!))
            {
                throw new InvalidDataException(
                    "The rollback copy did not match the safety backup.");
            }

            if (File.Exists(_paths.DatabasePath))
            {
                PrepareDatabaseForReplacement();
            }
            else
            {
                SqliteSnapshotIO.RequireNoSidecars(
                    _paths.DatabasePath);
            }

            File.Move(
                temporary,
                _paths.DatabasePath,
                overwrite: true);

            if (!HashMatches(
                    _paths.DatabasePath,
                    state.SafetyBackupSha256!))
            {
                throw new IOException(
                    "The restored safety backup has a different checksum.");
            }

            RequireValidSnapshot(_paths.DatabasePath);
            SqliteSnapshotIO.RequireNoSidecars(
                _paths.DatabasePath);

            // Keep the safety backup and staged file.
            // The original database is usable again.
            File.Delete(_paths.PendingRestoreManifestPath);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
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