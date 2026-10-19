using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Daycord.Core.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

public sealed class MigrationRunner
{
    private const string ResourcePrefix =
        "Daycord.Infrastructure.Persistence.Migrations.";

    private readonly SqliteConnectionFactory _connections;
    private readonly DatabaseBackupService _backups;

    public MigrationRunner(
        SqliteConnectionFactory connections,
        DatabaseBackupService backups)
    {
        _connections = connections;
        _backups = backups;
    }

    public async Task RunAsync(
        bool databaseExistedBeforeOpen,
        CancellationToken cancellationToken = default)
    {
        var migrations = LoadMigrations();

        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        // BEGIN IMMEDIATE serializes concurrent initializers across processes.
        await using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            var foreignKeysEnabled = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("PRAGMA foreign_keys;", transaction: transaction,
                    cancellationToken: cancellationToken));
            if (foreignKeysEnabled != 1)
            {
                throw new InvalidOperationException("SQLite foreign key enforcement is disabled.");
            }

            var historyExists = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM sqlite_master " +
                    "WHERE type = 'table' AND name = '__SchemaMigrations';",
                    transaction: transaction, cancellationToken: cancellationToken)) == 1;

            List<AppliedMigration> applied;
            if (historyExists)
            {
                await ValidateMigrationHistoryTableAsync(connection, transaction, cancellationToken);
                applied = (await connection.QueryAsync<AppliedMigration>(new CommandDefinition(
                    "SELECT Version, Name, Checksum FROM __SchemaMigrations ORDER BY Version;",
                    transaction: transaction, cancellationToken: cancellationToken))).ToList();
            }
            else
            {
                if (databaseExistedBeforeOpen)
                {
                    throw new InvalidOperationException(
                        "The existing database has no recognized migration history. " +
                        "Daycord will not overwrite an unknown schema.");
                }

                var otherObjects = await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM sqlite_master " +
                        "WHERE name NOT LIKE 'sqlite_%' " +
                        "AND type IN ('table', 'view', 'index', 'trigger');",
                        transaction: transaction, cancellationToken: cancellationToken));

                if (otherObjects != 0)
                {
                    throw new InvalidOperationException(
                        "An unrecognized schema was found in the new database file.");
                }

                applied = [];
            }

            ValidateAppliedMigrations(applied, migrations);
            var pending = migrations.Skip(applied.Count).ToList();

            if (pending.Count > 0)
            {
                // A previous recognized schema needs a consistent recovery copy.
                // The backup happens before any migration SQL is executed.
                if (databaseExistedBeforeOpen || applied.Count > 0)
                {
                    _backups.CreateSchemaUpgradeBackup();
                }

                if (!historyExists)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        CREATE TABLE __SchemaMigrations (
                            Version INTEGER NOT NULL PRIMARY KEY,
                            Name TEXT NOT NULL,
                            Checksum TEXT NOT NULL,
                            AppliedAtUtc TEXT NOT NULL
                        );
                        """,
                        transaction: transaction, cancellationToken: cancellationToken));
                }

                foreach (var migration in pending)
                {
                    var sql = migration.Sql
                        .Replace("{{OPEN_STATUS_ID}}", TaskStatusDefinition.OpenId.ToString("D"),
                            StringComparison.Ordinal)
                        .Replace("{{COMPLETED_STATUS_ID}}", TaskStatusDefinition.CompletedId.ToString("D"),
                            StringComparison.Ordinal);

                    await connection.ExecuteAsync(new CommandDefinition(
                        sql, transaction: transaction, cancellationToken: cancellationToken));

                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO __SchemaMigrations (Version, Name, Checksum, AppliedAtUtc)
                        VALUES (@Version, @Name, @Checksum, @AppliedAtUtc);
                        """,
                        new
                        {
                            migration.Version,
                            migration.Name,
                            migration.Checksum,
                            AppliedAtUtc = DateTime.UtcNow
                        },
                        transaction: transaction, cancellationToken: cancellationToken));
                }
            }

            await ValidateDatabaseAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // Preserve the original failure; disposing also releases the connection.
            }

            throw;
        }

        // WAL is an optimization, not a requirement for correctness. SQLite
        // automatically retains the previous journal mode on unsupported media.
        try
        {
            await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "PRAGMA journal_mode = WAL;", cancellationToken: cancellationToken));
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 8 or 10)
        {
            // BUSY, READONLY or IOERR: rollback journaling remains available.
        }
    }

    private static List<Migration> LoadMigrations()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var result = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                    && name.EndsWith(".sql", StringComparison.Ordinal)))
        {
            var name = resource[ResourcePrefix.Length..];
            var match = Regex.Match(name, @"^([0-9]{3})_[A-Za-z0-9_]+\.sql$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Invalid migration filename: {name}");
            }

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing migration resource: {resource}");
            using var reader = new StreamReader(stream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var sql = reader.ReadToEnd();
            var checksum = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

            result.Add(new Migration(int.Parse(match.Groups[1].Value), name, sql, checksum));
        }

        result.Sort((left, right) => left.Version.CompareTo(right.Version));
        if (result.Count == 0)
        {
            throw new InvalidOperationException("No embedded SQLite migrations were found.");
        }

        for (var index = 0; index < result.Count; index++)
        {
            if (result[index].Version != index + 1)
            {
                throw new InvalidOperationException(
                    "SQLite migration versions must be consecutive, starting at 001.");
            }
        }

        return result;
    }

    private static void ValidateAppliedMigrations(
        IReadOnlyList<AppliedMigration> applied,
        IReadOnlyList<Migration> available)
    {
        if (applied.Count > available.Count)
        {
            throw new InvalidOperationException(
                "This database uses a newer or unknown migration version. Downgrades are not supported.");
        }

        for (var index = 0; index < applied.Count; index++)
        {
            var actual = applied[index];
            var expected = available[index];
            if (actual.Version != expected.Version || actual.Name != expected.Name)
            {
                throw new InvalidOperationException(
                    $"Unknown or out-of-order SQLite migration at version {actual.Version}.");
            }

            if (!string.Equals(actual.Checksum, expected.Checksum,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite migration {actual.Name} has been changed after being applied.");
            }
        }
    }

    private static async Task ValidateMigrationHistoryTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var columns = (await connection.QueryAsync<MigrationColumn>(new CommandDefinition(
            """
            SELECT name AS Name, type AS DataType,
                   "notnull" AS IsRequired, pk AS IsPrimaryKey
            FROM pragma_table_info('__SchemaMigrations');
            """,
            transaction: transaction, cancellationToken: cancellationToken)))
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);

        if (columns.Count != 4 ||
            !Matches("Version", "INTEGER", primaryKey: true) ||
            !Matches("Name", "TEXT") ||
            !Matches("Checksum", "TEXT") ||
            !Matches("AppliedAtUtc", "TEXT"))
        {
            throw new InvalidOperationException(
                "The SQLite migration history table has an unexpected structure.");
        }

        bool Matches(string name, string dataType, bool primaryKey = false) =>
            columns.TryGetValue(name, out var column) &&
            string.Equals(column.DataType, dataType, StringComparison.OrdinalIgnoreCase) &&
            column.IsRequired == 1 &&
            column.IsPrimaryKey == (primaryKey ? 1 : 0);
    }

    private static async Task ValidateDatabaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tables = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT name FROM sqlite_master WHERE type = 'table'
            AND name IN ('Tasks', 'TaskStatuses', 'PlanningPeriods', 'WorkEntries');
            """,
            transaction: transaction, cancellationToken: cancellationToken))).ToHashSet();

        if (tables.Count != 4)
        {
            throw new InvalidOperationException("The Daycord database schema is incomplete.");
        }

        var statuses = (await connection.QueryAsync<TaskStatusDefinition>(new CommandDefinition(
            """
            SELECT Id, SystemKey, Name, IsSystem, IsCompleted, SortOrder
            FROM TaskStatuses WHERE SystemKey IN ('open', 'completed');
            """,
            transaction: transaction, cancellationToken: cancellationToken))).ToList();

        if (statuses.Count != 2 ||
            !statuses.Any(s => s.Id == TaskStatusDefinition.OpenId &&
                               s.SystemKey == "open" && s.IsSystem && !s.IsCompleted) ||
            !statuses.Any(s => s.Id == TaskStatusDefinition.CompletedId &&
                               s.SystemKey == "completed" && s.IsSystem && s.IsCompleted))
        {
            throw new InvalidOperationException(
                "The database has missing or invalid built-in task statuses.");
        }

        var integrity = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "PRAGMA integrity_check;", transaction: transaction,
            cancellationToken: cancellationToken));
        if (integrity != "ok")
        {
            throw new InvalidOperationException("SQLite integrity_check failed: " + integrity);
        }

        var foreignKeyErrors = (await connection.QueryAsync(new CommandDefinition(
            "PRAGMA foreign_key_check;", transaction: transaction,
            cancellationToken: cancellationToken))).Any();
        if (foreignKeyErrors)
        {
            throw new InvalidOperationException("SQLite foreign_key_check failed.");
        }
    }

    private sealed record Migration(int Version, string Name, string Sql, string Checksum);

    private sealed class AppliedMigration
    {
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
    }

    private sealed class MigrationColumn
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int IsRequired { get; set; }
        public int IsPrimaryKey { get; set; }
    }
}