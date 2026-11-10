namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Moves the file set currently occupying the active SQLite paths out of the
/// way before a verified safety snapshot is installed. No SQLite connection
/// is opened: this also works if the active database is corrupt or missing.
///
/// A restore journal must already be durably in RollingBack before calling
/// this class. The deterministic destination makes a partially completed
/// quarantine repeatable after a process interruption.
/// </summary>
internal sealed class RollbackFileQuarantine
{
    private static readonly string[] Suffixes = ["", "-wal", "-shm", "-journal"];
    private readonly AppDataPaths _paths;

    public RollbackFileQuarantine(AppDataPaths paths)
    {
        _paths = paths;
    }

    public void MoveActiveFilesAside(string operationId)
    {
        if (!Guid.TryParseExact(operationId, "N", out var parsed) ||
            !string.Equals(parsed.ToString("N"), operationId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid rollback operation ID.");
        }

        _paths.EnsureRawRecoveryBackupDirectory();
        RequirePlainDirectory(_paths.RawRecoveryBackupDirectory);

        var quarantine = Path.Combine(
            _paths.RawRecoveryBackupDirectory,
            $"rollback-replaced-{operationId}");

        Directory.CreateDirectory(quarantine);
        RequirePlainDirectory(quarantine);

        // A pre-existing quarantine is only accepted if it contains the
        // exact SQLite file names that this operation can have moved.
        var allowed = Suffixes
            .Select(suffix => "daycord.db" + suffix)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in Directory.EnumerateFileSystemEntries(quarantine))
        {
            if (!allowed.Contains(Path.GetFileName(entry)))
                throw new InvalidDataException(
                    "The rollback quarantine contains an unexpected file.");

            RequirePlainFile(entry);
        }

        // Move the database first: if the process stops, Daycord cannot
        // mistake the remaining sidecars for a complete active SQLite set.
        // The RollingBack journal prevents normal startup on the next run.
        foreach (var suffix in Suffixes)
        {
            var source = _paths.DatabasePath + suffix;
            var preserved = Path.Combine(quarantine, "daycord.db" + suffix);

            if (File.Exists(preserved))
            {
                RequirePlainFile(preserved);

                // Both copies present is ambiguous. Never overwrite either.
                if (File.Exists(source) || Directory.Exists(source))
                    throw new IOException(
                        "Ambiguous rollback file state; manual recovery required.");

                continue;
            }

            if (Directory.Exists(source))
                throw new IOException(
                    "A database file path unexpectedly refers to a directory.");

            if (!File.Exists(source))
                continue;

            RequirePlainFile(source);

            // Best-effort detection of other holders, notably on Windows.
            // The Daycord instance lock is held by the caller; external
            // SQLite processes are not coordinated by that lock.
            using (var probe = new FileStream(
                       source, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // No modification: verify the path can be accessed alone.
            }

            File.Move(source, preserved);
        }

        // Never install while remnants of the old SQLite file set remain.
        foreach (var suffix in Suffixes)
        {
            if (File.Exists(_paths.DatabasePath + suffix) ||
                Directory.Exists(_paths.DatabasePath + suffix))
            {
                throw new IOException(
                    "The active SQLite file set could not be fully quarantined.");
            }
        }
    }

    private static void RequirePlainDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A recovery directory is redirected or unavailable.");
        }
    }

    private static void RequirePlainFile(string path)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A recovery file is redirected or unavailable.");
        }
    }
}