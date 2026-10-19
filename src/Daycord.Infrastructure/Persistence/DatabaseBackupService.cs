using Dapper;
using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

public sealed class DatabaseBackupService
{
    private readonly AppDataPaths _paths;

    public DatabaseBackupService(AppDataPaths paths)
    {
        _paths = paths;
    }

    public void CreateSchemaUpgradeBackup()
    {
        _paths.EnsureSchemaBackupDirectory();

        var filename = $"daycord-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.db";
        var backupPath = Path.Combine(_paths.SchemaBackupDirectory, filename);

        // Use a separate read connection: the migration connection holds an
        // IMMEDIATE transaction to prevent competing writers during the backup.
        var sourceOptions = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 15
        };
        var targetOptions = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 15
        };

        try
        {
            using var source = new SqliteConnection(sourceOptions.ToString());
            using var target = new SqliteConnection(targetOptions.ToString());

            source.Open();
            target.Open();
            source.BackupDatabase(target);

            var integrity = target.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (integrity != "ok")
            {
                throw new InvalidDataException(
                    "The schema upgrade backup did not pass the SQLite integrity check.");
            }
        }
        catch
        {
            // A failed backup must never be mistaken for a valid recovery copy.
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
            catch (IOException)
            {
                // Keep the original backup failure as the diagnostic cause.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep the original backup failure as the diagnostic cause.
            }

            throw;
        }
    }
}