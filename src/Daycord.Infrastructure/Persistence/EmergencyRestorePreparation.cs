namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Stages a verified SQLite backup for emergency recovery.
/// Does not open or replace the live database. Invoked by
/// PendingRestoreCoordinator while its instance lock is held.
/// </summary>
internal sealed class EmergencyRestorePreparation
{
    private readonly AppDataPaths _paths;
    private readonly SqliteSnapshotValidator _validator = new();

    public EmergencyRestorePreparation(AppDataPaths paths)
    {
        _paths = paths;
    }

    public void Prepare(
        string selectedBackupPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = GetSafeSourcePath(selectedBackupPath);
        var manifestPath = _paths.PendingRestoreManifestPath;
        var stagingDirectory = _paths.RestoreStagingDirectory;

        // Raw preservation currently requires an existing original main
        // database file, even when its SQLite contents are corrupt.
        if (!File.Exists(_paths.DatabasePath) ||
            (File.GetAttributes(_paths.DatabasePath) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Emergency recovery requires an existing, non-redirected original database file.");
        }

        if (Directory.Exists(stagingDirectory) &&
            (File.GetAttributes(stagingDirectory) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The restore-staging directory is redirected.");
        }

        // Never replace an unresolved normal or emergency restore journal.
        if (File.Exists(manifestPath) ||
            Directory.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                "An unfinished restore already exists; its journal must be resolved first.");
        }

        _paths.EnsureRestoreStagingDirectory();

        var operationId = Guid.NewGuid().ToString("N");
        var stagedName = $"pending-{operationId}.db";
        var stagedPath = Path.Combine(stagingDirectory, stagedName);

        var temporaryPath = Path.Combine(
            stagingDirectory,
            $".emergency-snapshot-{Guid.NewGuid():N}.tmp.db");

        var stagedCreated = false;
        var manifestPublished = false;

        try
        {
            // Creates a consistent standalone SQLite snapshot, including
            // committed WAL content from the selected backup when present.
            // The source backup is never modified by this operation.
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                source,
                temporaryPath);

            cancellationToken.ThrowIfCancellationRequested();

            RequireValidSnapshot(temporaryPath);

            var checksum =
                PendingRestoreStateStore.GetSha256(temporaryPath);

            // Publish staged bytes before publishing their journal.
            File.Move(temporaryPath, stagedPath);
            stagedCreated = true;

            if (!string.Equals(
                    PendingRestoreStateStore.GetSha256(stagedPath),
                    checksum,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The staged emergency backup changed after publication.");
            }

            SqliteSnapshotIO.RequireNoSidecars(stagedPath);
            cancellationToken.ThrowIfCancellationRequested();

            var state = new PendingRestoreState(
                operationId,
                stagedName,
                checksum,
                PendingRestorePhase.Prepared,
                Mode: PendingRestoreMode.Emergency);

            PendingRestoreStateStore.Create(manifestPath, state);
            manifestPublished = true;
        }
        finally
        {
            DeletePrivateSnapshot(temporaryPath);

            // Never delete staged bytes if a journal already references
            // them. Only our unreferenced private staging file is removed.
            if (stagedCreated &&
                !manifestPublished &&
                !File.Exists(manifestPath) &&
                !Directory.Exists(manifestPath))
            {
                DeletePrivateSnapshot(stagedPath);
            }
        }
    }

    private string GetSafeSourcePath(string selectedBackupPath)
    {
        if (string.IsNullOrWhiteSpace(selectedBackupPath))
        {
            throw new ArgumentException(
                "A backup path is required.",
                nameof(selectedBackupPath));
        }

        var selected = Path.GetFullPath(selectedBackupPath);

        if (!File.Exists(selected))
        {
            throw new FileNotFoundException(
                "The selected backup does not exist.",
                selected);
        }

        // Reject a symbolic link or reparse point AS THE BACKUP FILE.
        // Parent directories are handled separately: macOS commonly
        // exposes /var through the system link /var -> /private/var.
        if ((File.GetAttributes(selected) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The selected backup file is a symbolic link or reparse point.");
        }

        // Resolve directory links in every relevant path before comparison.
        // This prevents using a directory alias to select daycord.db itself.
        var source = ResolveFilePathThroughDirectoryLinks(selected);

        var databasePath = ResolveFilePathThroughDirectoryLinks(
            _paths.DatabasePath);

        var stagingDirectory = ResolveDirectoryPathThroughLinks(
            _paths.RestoreStagingDirectory);

        // Use conservative case-insensitive comparison on macOS, where
        // default APFS volumes are commonly case-insensitive.
        var comparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (string.Equals(source, databasePath, comparison) ||
            string.Equals(source, databasePath + "-wal", comparison) ||
            string.Equals(source, databasePath + "-shm", comparison) ||
            string.Equals(source, databasePath + "-journal", comparison))
        {
            throw new InvalidOperationException(
                "Select an independent backup, not the active database.");
        }

        var stagingPrefix =
            Path.TrimEndingDirectorySeparator(stagingDirectory) +
            Path.DirectorySeparatorChar;

        if (source.StartsWith(stagingPrefix, comparison))
        {
            throw new InvalidOperationException(
                "A file in restore staging cannot be selected as a backup.");
        }

        // Return the resolved file path. Do not pass a path containing
        // unresolved parent-directory links to the snapshot operation.
        return source;
    }

    private static string ResolveFilePathThroughDirectoryLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException(
                "The selected file has no parent directory.");

        return Path.Combine(
            ResolveDirectoryPathThroughLinks(directory),
            Path.GetFileName(fullPath));
    }

    /// <summary>
    /// Resolves existing directory symlinks and junctions component by
    /// component. Unlike Path.GetFullPath, this also resolves physical
    /// aliases such as macOS /var -> /private/var.
    ///
    /// A nonexistent suffix is retained lexically, which allows resolving
    /// the planned RestoreStaging directory before it has been created.
    /// </summary>
    private static string ResolveDirectoryPathThroughLinks(string directory)
    {
        var fullPath = Path.GetFullPath(directory);

        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException(
                "The directory path has no filesystem root.");

        var pending = new Queue<string>(
            SplitPathSegments(fullPath));

        var resolved = root;
        var linksResolved = 0;

        while (pending.TryDequeue(out var segment))
        {
            var candidate = Path.Combine(resolved, segment);

            if (!Directory.Exists(candidate))
            {
                if (File.Exists(candidate))
                {
                    throw new IOException(
                        "A path component is a file rather than a directory.");
                }

                // The remaining suffix cannot contain an existing
                // directory link if this parent does not exist.
                while (pending.TryDequeue(out var remainingSegment))
                {
                    candidate = Path.Combine(
                        candidate,
                        remainingSegment);
                }

                return Path.TrimEndingDirectorySeparator(candidate);
            }

            var attributes = File.GetAttributes(candidate);

            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                resolved = candidate;
                continue;
            }

            // Fail closed on link cycles or unexpectedly deep chains.
            if (++linksResolved > 40)
            {
                throw new IOException(
                    "Too many directory links in the backup path.");
            }

            var target = Directory.ResolveLinkTarget(
                candidate,
                returnFinalTarget: true);

            if (target is not DirectoryInfo || !target.Exists)
            {
                throw new IOException(
                    "A parent-directory link cannot be resolved.");
            }

            var targetPath = Path.GetFullPath(target.FullName);
            var remainder = pending.ToArray();

            // Start again at the resolved target's root, also checking
            // any links within that target's parent directories.
            pending = new Queue<string>(
                SplitPathSegments(targetPath).Concat(remainder));

            resolved = Path.GetPathRoot(targetPath)
                ?? throw new IOException(
                    "The resolved directory has no filesystem root.");
        }

        return Path.TrimEndingDirectorySeparator(resolved);
    }

    private static string[] SplitPathSegments(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException(
                "The filesystem path has no root.");

        return fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private void RequireValidSnapshot(string snapshotPath)
    {
        var result = _validator.Validate(snapshotPath);

        if (!result.IsValid)
        {
            throw new InvalidDataException(
                $"The selected backup failed Daycord validation: {result.Status}.");
        }

        SqliteSnapshotIO.RequireNoSidecars(snapshotPath);
    }

    private static void DeletePrivateSnapshot(string path)
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