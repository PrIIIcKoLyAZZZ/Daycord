namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Creates a verified, standalone backup of an existing Daycord database
/// before an upgrade changes its schema. Never touches the active database.
/// </summary>
public sealed class DatabaseBackupService
{
    private readonly AppDataPaths _paths;
    private readonly SqliteSnapshotValidator _validator = new();

    public DatabaseBackupService(AppDataPaths paths)
    {
        _paths = paths;
    }

    public void CreateSchemaUpgradeBackup()
    {
        _paths.EnsureSchemaBackupDirectory();

        // Both files are in the same directory, so publishing the completed
        // snapshot does not require a cross-volume copy. The profile name
        // identifies its origin even if the file is later moved elsewhere.
        var profile = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(_paths.DataDirectory));

        var identifier = Guid.NewGuid().ToString("N");
        var createdAtUtc = DateTime.UtcNow;
        var filename =
            $"daycord-pre-upgrade-{profile}-" +
            $"{createdAtUtc:yyyyMMddTHHmmssfffffffZ}-{identifier}.db";

        var publishedPath = Path.Combine(
            _paths.SchemaBackupDirectory, filename);

        var temporaryPath = Path.Combine(
            _paths.SchemaBackupDirectory,
            $".pre-upgrade-{identifier}.tmp.db");

        try
        {
            // SQLite Online Backup API includes committed data in the WAL.
            // The shared helper finalizes the copy as a standalone database.
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                _paths.DatabasePath,
                temporaryPath);

            // A successful SQLite file copy alone is not a usable recovery
            // point. Require the same schema and migration-history checks
            // used for user-created backups before permitting the upgrade.
            var validation = _validator.Validate(temporaryPath);

            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    "The pre-upgrade SQLite snapshot failed validation: " +
                    validation.Status + ". The migration was not started.");
            }

            SqliteSnapshotIO.RequireNoSidecars(temporaryPath);

            // File.Move never overwrites an existing backup. The final name
            // is visible only after the snapshot is complete and verified.
            File.Move(temporaryPath, publishedPath);
        }
        finally
        {
            // Only a privately generated temporary snapshot is cleaned up.
            // Never delete the active database or a published backup.
            DeleteTemporarySnapshot(temporaryPath);
        }
    }

    private static void DeleteTemporarySnapshot(string temporaryPath)
    {
        foreach (var path in new[]
                 {
                     temporaryPath,
                     temporaryPath + "-wal",
                     temporaryPath + "-shm",
                     temporaryPath + "-journal"
                 })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Preserve the original backup/validation failure.
                // Unpublished files are never presented as usable backups.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the original backup/validation failure.
            }
        }
    }
}