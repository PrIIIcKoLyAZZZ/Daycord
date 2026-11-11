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
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(source, destination, comparison))
        {
            throw new InvalidOperationException(
                "The snapshot must not replace its source database.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException(
                "The snapshot destination already exists.");
        }

        // An orphaned destination sidecar may belong to an interrupted
        // operation. Never let SQLite silently reuse that state.
        RequireNoSidecars(destination);

        var sourceOptions = new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 15
        };

        var destinationOptions = new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 15
        };

        using (var sourceConnection =
               new SqliteConnection(sourceOptions.ToString()))
        using (var destinationConnection =
               new SqliteConnection(destinationOptions.ToString()))
        {
            sourceConnection.Open();
            destinationConnection.Open();

            // Online Backup API copies a consistent SQLite view, including
            // committed transactions stored in the source WAL.
            sourceConnection.BackupDatabase(destinationConnection);

            // Online backup preserves the source journal mode.
            // Converting the snapshot to DELETE ensures it remains
            // valid as a single .db file.
            using var command = destinationConnection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = DELETE;";

            var result = Convert.ToString(command.ExecuteScalar());

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

        RequireNoSidecars(destination);

        // SQLite handles are closed. Flush the complete single-file copy
        // before callers publish it as a recovery point.
        using var snapshotFile = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        snapshotFile.Flush(flushToDisk: true);
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