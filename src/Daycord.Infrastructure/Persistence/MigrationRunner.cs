using System.Globalization;
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

    private const string IsoUtcFormat =
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

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

        await using var connection =
            await _connections.OpenConnectionAsync(cancellationToken);

        // BEGIN IMMEDIATE prevents competing migration writers.
        await using var transaction =
            connection.BeginTransaction(deferred: false);

        try
        {
            var foreignKeysEnabled =
                await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "PRAGMA foreign_keys;",
                        transaction: transaction,
                        cancellationToken: cancellationToken));

            if (foreignKeysEnabled != 1)
            {
                throw new InvalidOperationException(
                    "SQLite foreign key enforcement is disabled.");
            }

            var historyExists =
                await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM sqlite_master " +
                        "WHERE type = 'table' " +
                        "AND name = '__SchemaMigrations';",
                        transaction: transaction,
                        cancellationToken: cancellationToken)) == 1;

            List<AppliedMigration> applied;

            if (historyExists)
            {
                await ValidateMigrationHistoryTableAsync(
                    connection,
                    transaction,
                    cancellationToken);

                applied = (await connection.QueryAsync<AppliedMigration>(
                    new CommandDefinition(
                        """
                        SELECT Version, Name, Checksum
                        FROM __SchemaMigrations
                        ORDER BY Version;
                        """,
                        transaction: transaction,
                        cancellationToken: cancellationToken))).ToList();
            }
            else
            {
                if (databaseExistedBeforeOpen)
                {
                    throw new InvalidOperationException(
                        "The existing database has no recognized " +
                        "migration history. Daycord will not overwrite " +
                        "an unknown schema.");
                }

                var otherObjects =
                    await connection.ExecuteScalarAsync<int>(
                        new CommandDefinition(
                            """
                            SELECT COUNT(*)
                            FROM sqlite_master
                            WHERE name NOT LIKE 'sqlite_%'
                              AND type IN (
                                  'table',
                                  'view',
                                  'index',
                                  'trigger'
                              );
                            """,
                            transaction: transaction,
                            cancellationToken: cancellationToken));

                if (otherObjects != 0)
                {
                    throw new InvalidOperationException(
                        "An unrecognized schema was found " +
                        "in the new database file.");
                }

                applied = [];
            }

            ValidateAppliedMigrations(applied, migrations);

            var pending = migrations
                .Skip(applied.Count)
                .ToList();

            if (pending.Count > 0)
            {
                // Never upgrade an existing recognized database
                // without first creating a consistent recovery copy.
                if (databaseExistedBeforeOpen || applied.Count > 0)
                {
                    _backups.CreateSchemaUpgradeBackup();
                }

                if (!historyExists)
                {
                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            """
                            CREATE TABLE __SchemaMigrations (
                                Version INTEGER NOT NULL PRIMARY KEY,
                                Name TEXT NOT NULL,
                                Checksum TEXT NOT NULL,
                                AppliedAtUtc TEXT NOT NULL
                            );
                            """,
                            transaction: transaction,
                            cancellationToken: cancellationToken));
                }

                foreach (var migration in pending)
                {
                    var sql = migration.Sql
                        .Replace(
                            "{{OPEN_STATUS_ID}}",
                            TaskStatusDefinition.OpenId.ToString("D"),
                            StringComparison.Ordinal)
                        .Replace(
                            "{{COMPLETED_STATUS_ID}}",
                            TaskStatusDefinition.CompletedId.ToString("D"),
                            StringComparison.Ordinal);

                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            sql,
                            transaction: transaction,
                            cancellationToken: cancellationToken));

                    // An explicit string avoids the provider's
                    // culture-independent but timezone-less
                    // DateTime parameter representation.
                    var appliedAtUtc = DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture);

                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            """
                            INSERT INTO __SchemaMigrations (
                                Version,
                                Name,
                                Checksum,
                                AppliedAtUtc
                            )
                            VALUES (
                                @Version,
                                @Name,
                                @Checksum,
                                @AppliedAtUtc
                            );
                            """,
                            new
                            {
                                migration.Version,
                                migration.Name,
                                migration.Checksum,
                                AppliedAtUtc = appliedAtUtc
                            },
                            transaction: transaction,
                            cancellationToken: cancellationToken));
                }
            }

            await ValidateMigrationTimesAsync(
                connection,
                transaction,
                cancellationToken);

            await ValidateDatabaseAsync(
                connection,
                transaction,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
            }
            catch (Exception)
            {
                // Preserve the original migration failure.
                // Connection disposal releases remaining resources.
            }

            throw;
        }

        // WAL is optional. Correctness must not depend on it.
        try
        {
            await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "PRAGMA journal_mode = WAL;",
                    cancellationToken: cancellationToken));
        }
        catch (SqliteException error)
            when (error.SqliteErrorCode is 5 or 8 or 10)
        {
            // BUSY, READONLY or IOERR.
            // The existing journal mode remains usable.
        }
    }

    private static List<Migration> LoadMigrations()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var result = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name =>
                         name.StartsWith(
                             ResourcePrefix,
                             StringComparison.Ordinal) &&
                         name.EndsWith(
                             ".sql",
                             StringComparison.Ordinal)))
        {
            var name = resource[ResourcePrefix.Length..];

            var match = Regex.Match(
                name,
                @"^([0-9]{3})_[A-Za-z0-9_]+\.sql$",
                RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Invalid migration filename: {name}");
            }

            using var stream =
                assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Missing migration resource: {resource}");

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            var sql = reader.ReadToEnd();

            var checksum = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(sql)));

            result.Add(new Migration(
                int.Parse(
                    match.Groups[1].Value,
                    CultureInfo.InvariantCulture),
                name,
                sql,
                checksum));
        }

        result.Sort(
            (left, right) =>
                left.Version.CompareTo(right.Version));

        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                "No embedded SQLite migrations were found.");
        }

        for (var index = 0; index < result.Count; index++)
        {
            if (result[index].Version != index + 1)
            {
                throw new InvalidOperationException(
                    "SQLite migration versions must be consecutive, " +
                    "starting at 001.");
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
                "This database uses a newer or unknown migration " +
                "version. Downgrades are not supported.");
        }

        for (var index = 0; index < applied.Count; index++)
        {
            var actual = applied[index];
            var expected = available[index];

            if (actual.Version != expected.Version ||
                actual.Name != expected.Name)
            {
                throw new InvalidOperationException(
                    $"Unknown or out-of-order SQLite migration " +
                    $"at version {actual.Version}.");
            }

            if (!string.Equals(
                    actual.Checksum,
                    expected.Checksum,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQLite migration {actual.Name} has been " +
                    "changed after being applied.");
            }
        }
    }

    private static bool IsExplicitUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Regex.IsMatch(
                value,
                @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T" +
                @"[0-9]{2}:[0-9]{2}:[0-9]{2}" +
                @"(\.[0-9]{1,7})?Z$",
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        return DateTime.TryParseExact(
            value,
            [
                IsoUtcFormat,
                "yyyy-MM-dd'T'HH:mm:ss'Z'"
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static async Task ValidateMigrationTimesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<MigrationTime>(
            new CommandDefinition(
                """
                SELECT Version, AppliedAtUtc
                FROM __SchemaMigrations
                ORDER BY Version;
                """,
                transaction: transaction,
                cancellationToken: cancellationToken));

        foreach (var row in rows)
        {
            if (!IsExplicitUtc(row.AppliedAtUtc))
            {
                throw new InvalidOperationException(
                    $"Migration {row.Version} has an invalid " +
                    "ISO 8601 UTC timestamp.");
            }
        }
    }

    private static async Task ValidateMigrationHistoryTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var columns =
            (await connection.QueryAsync<MigrationColumn>(
                new CommandDefinition(
                    """
                    SELECT
                        name AS Name,
                        type AS DataType,
                        "notnull" AS IsRequired,
                        pk AS IsPrimaryKey
                    FROM pragma_table_info('__SchemaMigrations');
                    """,
                    transaction: transaction,
                    cancellationToken: cancellationToken)))
            .ToDictionary(
                column => column.Name,
                StringComparer.OrdinalIgnoreCase);

        if (columns.Count != 4 ||
            !Matches("Version", "INTEGER", primaryKey: true) ||
            !Matches("Name", "TEXT") ||
            !Matches("Checksum", "TEXT") ||
            !Matches("AppliedAtUtc", "TEXT"))
        {
            throw new InvalidOperationException(
                "The SQLite migration history table " +
                "has an unexpected structure.");
        }

        bool Matches(
            string name,
            string dataType,
            bool primaryKey = false) =>
            columns.TryGetValue(name, out var column) &&
            string.Equals(
                column.DataType,
                dataType,
                StringComparison.OrdinalIgnoreCase) &&
            column.IsRequired == 1 &&
            column.IsPrimaryKey == (primaryKey ? 1 : 0);
    }

    private static async Task ValidateDatabaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var tables =
            (await connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN (
                          'Tasks',
                          'TaskStatuses',
                          'PlanningPeriods',
                          'WorkEntries'
                      );
                    """,
                    transaction: transaction,
                    cancellationToken: cancellationToken)))
            .ToHashSet();

        if (tables.Count != 4)
        {
            throw new InvalidOperationException(
                "The Daycord database schema is incomplete.");
        }

        var statuses =
            (await connection.QueryAsync<TaskStatusDefinition>(
                new CommandDefinition(
                    """
                    SELECT
                        Id,
                        SystemKey,
                        Name,
                        IsSystem,
                        IsCompleted,
                        SortOrder
                    FROM TaskStatuses
                    WHERE SystemKey IN ('open', 'completed');
                    """,
                    transaction: transaction,
                    cancellationToken: cancellationToken)))
            .ToList();

        if (statuses.Count != 2 ||
            !statuses.Any(s =>
                s.Id == TaskStatusDefinition.OpenId &&
                s.SystemKey == "open" &&
                s.IsSystem &&
                !s.IsCompleted) ||
            !statuses.Any(s =>
                s.Id == TaskStatusDefinition.CompletedId &&
                s.SystemKey == "completed" &&
                s.IsSystem &&
                s.IsCompleted))
        {
            throw new InvalidOperationException(
                "The database has missing or invalid " +
                "built-in task statuses.");
        }

        var integrity =
            await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "PRAGMA integrity_check;",
                    transaction: transaction,
                    cancellationToken: cancellationToken));

        if (integrity != "ok")
        {
            throw new InvalidOperationException(
                "SQLite integrity_check failed: " + integrity);
        }

        var foreignKeyErrors =
            (await connection.QueryAsync(
                new CommandDefinition(
                    "PRAGMA foreign_key_check;",
                    transaction: transaction,
                    cancellationToken: cancellationToken)))
            .Any();

        if (foreignKeyErrors)
        {
            throw new InvalidOperationException(
                "SQLite foreign_key_check failed.");
        }
    }

    private sealed record Migration(
        int Version,
        string Name,
        string Sql,
        string Checksum);

    private sealed class AppliedMigration
    {
        public int Version { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Checksum { get; set; } = string.Empty;
    }

    private sealed class MigrationTime
    {
        public int Version { get; set; }

        public string AppliedAtUtc { get; set; } = string.Empty;
    }

    private sealed class MigrationColumn
    {
        public string Name { get; set; } = string.Empty;

        public string DataType { get; set; } = string.Empty;

        public int IsRequired { get; set; }

        public int IsPrimaryKey { get; set; }
    }
}