using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Creates a self-contained SQLite file using the online backup API.
/// The source is never modified; destination WAL state is normalized to DELETE.
/// </summary>
internal static class SqliteSnapshotIO
{
    public static void CreateStandaloneSnapshot(
        string sourcePath,
        string destinationPath)
    {
        if (File.Exists(destinationPath))
            throw new IOException(
                "The snapshot destination already exists.");

        var sourceOptions = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 15
        };

        var destinationOptions = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 15
        };

        using (var source =
               new SqliteConnection(sourceOptions.ToString()))
        using (var destination =
               new SqliteConnection(destinationOptions.ToString()))
        {
            source.Open();
            destination.Open();

            source.BackupDatabase(destination);

            // Online backup preserves the source journal mode.
            // Converting the snapshot to DELETE ensures it remains
            // valid as a single .db file.
            using var command = destination.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE;";

            var result = Convert.ToString(
                command.ExecuteScalar());

            if (!string.Equals(
                    result,
                    "delete",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The SQLite snapshot could not be finalized " +
                    "as a standalone file.");
            }
        }

        RequireNoSidecars(destinationPath);
    }

    public static void CopyDurably(
        string sourcePath,
        string destinationPath)
    {
        using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.WriteThrough);

        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    public static void RequireNoSidecars(string databasePath)
    {
        foreach (var extension in new[]
                 {
                     "-wal",
                     "-shm",
                     "-journal"
                 })
        {
            if (File.Exists(databasePath + extension))
            {
                throw new IOException(
                    $"SQLite sidecar {extension} is still present; " +
                    "refusing file replacement.");
            }
        }
    }
}