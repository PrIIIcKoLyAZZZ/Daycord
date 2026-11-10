namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Installs a verified emergency backup when the current SQLite database
/// cannot be opened. Called by PendingRestoreCoordinator during startup
/// while the application instance lock is held and normal application
/// SQLite connections have not been opened.
/// </summary>
internal sealed class EmergencyRestoreInstaller
{
    private readonly AppDataPaths _paths;
    private readonly SqliteSnapshotValidator _validator = new();

    public EmergencyRestoreInstaller(AppDataPaths paths) => _paths = paths;

    /// <summary>
    /// Applies an emergency journal. Returns true only when a validated backup
    /// is installed and ordinary schema/settings initialization may begin.
    /// Never automatically retries an interrupted, ambiguous installation.
    /// </summary>
    public bool Apply(PendingRestoreState state, CancellationToken cancellationToken)
    {
        RequireEmergency(state);
        var journal = _paths.PendingRestoreManifestPath;

        if (state.Phase == PendingRestorePhase.EmergencyNeedsAttention)
            throw new InvalidOperationException(
                "Emergency recovery needs explicit intervention; automatic retry is blocked.");

        try
        {
            ValidateStaged(state);
            cancellationToken.ThrowIfCancellationRequested();

            if (state.Phase == PendingRestorePhase.Prepared)
            {
                // Preserve the original DB and its existing sidecars before
                // any active file can be moved. An unreadable but existing DB
                // can still be archived byte-for-byte.
                var raw = new RawDatabaseRecoveryArchive(_paths)
                    .EnsureCaptured(state.OperationId, cancellationToken);

                state = state with
                {
                    Phase = PendingRestorePhase.RawCaptured,
                    RawArchiveDirectoryName = raw.DirectoryName,
                    RawArchiveManifestSha256 = raw.ManifestSha256
                };
                PendingRestoreStateStore.Update(journal, state);
            }

            ValidateRaw(state);

            if (state.Phase == PendingRestorePhase.RawCaptured)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Durable intent is recorded before the first file move.
                // No validated safety snapshot of the corrupt DB is claimed.
                state = state with { Phase = PendingRestorePhase.EmergencyInstalling };
                PendingRestoreStateStore.Update(journal, state);

                InstallFresh(state);
                return true;
            }

            if (state.Phase == PendingRestorePhase.EmergencyInstalling)
            {
                // This is an interrupted installation. Only an exact installed
                // staged snapshot can be recognized automatically. Otherwise
                // leave all preserved files untouched for explicit recovery.
                if (!IsExactStagedSnapshotInstalled(state))
                    throw new InvalidOperationException(
                        "Emergency installation was interrupted in an ambiguous state.");

                PendingRestoreStateStore.Update(
                    journal,
                    state with { Phase = PendingRestorePhase.EmergencyInstalled });
                return true;
            }

            if (state.Phase == PendingRestorePhase.EmergencyInstalled)
            {
                RequireValidLiveSnapshot();
                return true;
            }

            throw new InvalidDataException("Unsupported emergency restore phase.");
        }
        catch
        {
            // Do not reinstall the same failed backup on every launch.
            // Crucially, do not try to 'roll back' to a corrupt original.
            // The original raw archive and quarantine remain preserved.
            try
            {
                var current = PendingRestoreStateStore.Read(journal);
                if (current.Mode == PendingRestoreMode.Emergency &&
                    current.Phase != PendingRestorePhase.EmergencyNeedsAttention)
                {
                    PendingRestoreStateStore.Update(
                        journal,
                        current with { Phase = PendingRestorePhase.EmergencyNeedsAttention });
                }
            }
            catch
            {
                // Never mask the original error. The journal remains in place
                // and the coordinator must block ordinary application startup.
            }

            throw;
        }
    }

    /// <summary>
    /// Called when migration/settings initialization of an emergency-installed
    /// database failed. Preserve the installed DB and evidence; do not restore
    /// corrupt data and do not report success.
    /// </summary>
    public void MarkInitializationFailed()
    {
        var journal = _paths.PendingRestoreManifestPath;
        var state = PendingRestoreStateStore.Read(journal);
        RequireEmergency(state);

        if (state.Phase == PendingRestorePhase.EmergencyNeedsAttention)
            return;

        if (state.Phase != PendingRestorePhase.EmergencyInstalled)
            throw new InvalidOperationException(
                "Emergency restore cannot be finalized from this phase.");

        PendingRestoreStateStore.Update(
            journal,
            state with { Phase = PendingRestorePhase.EmergencyNeedsAttention });
    }

    /// <summary>
    /// Only call after the normal schema and settings initialization succeeded.
    /// Original raw and quarantined artifacts are intentionally retained.
    /// </summary>
    public void CompleteAfterInitialization()
    {
        var journal = _paths.PendingRestoreManifestPath;
        var state = PendingRestoreStateStore.Read(journal);
        RequireEmergency(state);

        if (state.Phase != PendingRestorePhase.EmergencyInstalled)
            throw new InvalidOperationException(
                "Emergency installation is not ready for completion.");

        ValidateStaged(state);
        ValidateRaw(state);
        RequireValidLiveSnapshot();

        File.Delete(journal);
        // Staged data are not deleted here; leaving them available makes the
        // recovery outcome inspectable even if final cleanup is interrupted.
    }

    private void InstallFresh(PendingRestoreState state)
    {
        var temporary = Path.Combine(
            _paths.DataDirectory, $".daycord-emergency-{Guid.NewGuid():N}.db");

        try
        {
            // Staged snapshots were already normalized to standalone DELETE
            // journal mode. Copy exact bytes, validate, and flush before move.
            SqliteSnapshotIO.CopyDurably(StagedPath(state), temporary);
            RequireValidSnapshot(temporary);
            SqliteSnapshotIO.RequireNoSidecars(temporary);

            if (!HashMatches(temporary, state.StagedSha256))
                throw new InvalidDataException(
                    "Emergency install copy differs from its staged snapshot.");

            // The journal is EmergencyInstalling at this point. Move the
            // entire active file set without opening it through SQLite.
            // Reusing the deterministic quarantine also protects interruption
            // between individual moves. No files are overwritten.
            new RollbackFileQuarantine(_paths)
                .MoveActiveFilesAside(state.OperationId);

            File.Move(temporary, _paths.DatabasePath);

            if (!IsExactStagedSnapshotInstalled(state))
                throw new IOException(
                    "The installed emergency database failed verification.");

            PendingRestoreStateStore.Update(
                _paths.PendingRestoreManifestPath,
                state with { Phase = PendingRestorePhase.EmergencyInstalled });
        }
        finally
        {
            // This path is always generated locally; never delete user files.
            TryDeleteTemporary(temporary);
        }
    }

    private bool IsExactStagedSnapshotInstalled(PendingRestoreState state)
    {
        if (!HashMatches(_paths.DatabasePath, state.StagedSha256) ||
            AnySidecars(_paths.DatabasePath))
            return false;

        RequireValidSnapshot(_paths.DatabasePath);
        return true;
    }

    private void RequireValidLiveSnapshot()
    {
        _paths.EnsureRestoreStagingDirectory();
        var temporary = Path.Combine(
            _paths.RestoreStagingDirectory,
            $".validate-emergency-{Guid.NewGuid():N}.db");

        try
        {
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                _paths.DatabasePath, temporary);
            RequireValidSnapshot(temporary);
        }
        finally
        {
            TryDeleteTemporary(temporary);
        }
    }

    private void ValidateStaged(PendingRestoreState state)
    {
        var staged = StagedPath(state);
        if (!HashMatches(staged, state.StagedSha256))
            throw new InvalidDataException("Staged emergency backup is missing or altered.");

        SqliteSnapshotIO.RequireNoSidecars(staged);
        RequireValidSnapshot(staged);
    }

    private void ValidateRaw(PendingRestoreState state)
    {
        if (state.RawArchiveDirectoryName is null ||
            state.RawArchiveManifestSha256 is null)
        {
            throw new InvalidDataException(
                "The original database has not been preserved.");
        }

        new RawDatabaseRecoveryArchive(_paths).ValidateReference(
            state.OperationId,
            state.RawArchiveDirectoryName,
            state.RawArchiveManifestSha256);
    }

    private void RequireValidSnapshot(string path)
    {
        var result = _validator.Validate(path);
        if (!result.IsValid)
            throw new InvalidDataException(
                $"Emergency SQLite snapshot validation failed: {result.Status}.");
    }

    private string StagedPath(PendingRestoreState state) =>
        Path.Combine(_paths.RestoreStagingDirectory, state.StagedFileName);

    private static bool HashMatches(string path, string expected) =>
        File.Exists(path) && string.Equals(
            PendingRestoreStateStore.GetSha256(path), expected,
            StringComparison.OrdinalIgnoreCase);

    private static bool AnySidecars(string dbPath) =>
        File.Exists(dbPath + "-wal") ||
        File.Exists(dbPath + "-shm") ||
        File.Exists(dbPath + "-journal");

    private static void RequireEmergency(PendingRestoreState state)
    {
        if (state.Mode != PendingRestoreMode.Emergency)
            throw new InvalidOperationException(
                "Normal restore cannot use the emergency installer.");
    }

    private static void TryDeleteTemporary(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            try
            {
                if (File.Exists(path + suffix))
                    File.Delete(path + suffix);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}