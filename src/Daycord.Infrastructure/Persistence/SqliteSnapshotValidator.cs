using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Core.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Read-only validation of a consistent SQLite snapshot.
/// Never modifies the backup or its migration history.
/// </summary>
internal sealed class SqliteSnapshotValidator
{
    private const string MigrationResourcePrefix =
        "Daycord.Infrastructure.Persistence.Migrations.";

    private const string DefaultPresetId =
        "7b9f3e18-6fc7-449e-9a12-e59063d58101";

    private static readonly string[] RequiredTables =
    [
        "__SchemaMigrations",
        "TaskStatuses",
        "Tasks",
        "PlanningPeriods",
        "WorkEntries",
        "ReportPresets",
        "ReportSettings"
    ];

    private static readonly string[] RequiredIndexes =
    [
        "UX_TaskStatuses_SystemKey",
        "IX_Tasks_TaskDate",
        "IX_Tasks_TaskNumber",
        "IX_Tasks_StatusId",
        "IX_PlanningPeriods_TaskId",
        "IX_PlanningPeriods_PlannedDate",
        "IX_WorkEntries_TaskId",
        "IX_WorkEntries_WorkDate",
        "UX_ReportPresets_Name"
    ];

    public BackupValidationResult Validate(string snapshotPath)
    {
        if (!File.Exists(snapshotPath))
        {
            return new(BackupValidationStatus.InvalidFile);
        }

        var options = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 15
        };

        try
        {
            using var connection =
                new SqliteConnection(options.ToString());

            connection.Open();

            if (connection.ExecuteScalar<string>(
                    "PRAGMA integrity_check;") != "ok")
            {
                return new(BackupValidationStatus.Corrupted);
            }

            if (connection.Query(
                    "PRAGMA foreign_key_check;").Any())
            {
                return new(BackupValidationStatus.Corrupted);
            }

            if (!HasExpectedStructure(connection) ||
                !TryGetCompatibleMigrationCount(
                    connection,
                    out var migrationCount) ||
                !HasValidAppSettings(
                    connection,
                    migrationCount) ||
                !HasValidSystemStatuses(connection) ||
                !HasValidReportSettings(connection))
            {
                return new(
                    BackupValidationStatus.IncompatibleSchema);
            }

            return new(BackupValidationStatus.Valid);
        }
        catch (SqliteException exception)
            when (exception.SqliteErrorCode is 11 or 26)
        {
            return new(BackupValidationStatus.Corrupted);
        }
    }

    private static bool HasExpectedStructure(
        SqliteConnection connection)
    {
        var tables = connection.Query<string>(
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table';
                """)
            .ToHashSet(StringComparer.Ordinal);

        var indexes = connection.Query<string>(
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'index';
                """)
            .ToHashSet(StringComparer.Ordinal);

        if (!RequiredTables.All(tables.Contains) ||
            !RequiredIndexes.All(indexes.Contains))
        {
            return false;
        }

        return HasColumns(
                   connection, "TaskStatuses",
                   "Id", "SystemKey", "Name", "IsSystem",
                   "IsCompleted", "SortOrder") &&
               HasColumns(
                   connection, "Tasks",
                   "Id", "TaskDate", "TaskNumber",
                   "Title", "ShortDescription",
                   "DescriptionMarkdown", "StatusId",
                   "CreatedAtUtc", "UpdatedAtUtc",
                   "CompletedAtUtc") &&
               HasColumns(
                   connection, "PlanningPeriods",
                   "Id", "TaskId", "PlannedDate",
                   "PlannedMinutes") &&
               HasColumns(
                   connection, "WorkEntries",
                   "Id", "TaskId", "WorkDate",
                   "ActualMinutes", "WorkDescription",
                   "Note", "CreatedAtUtc", "UpdatedAtUtc") &&
               HasColumns(
                   connection, "ReportPresets",
                   "Id", "Name", "IsBuiltIn",
                   "ColumnsJson", "CreatedAtUtc",
                   "UpdatedAtUtc") &&
               HasColumns(
                   connection, "ReportSettings",
                   "SingletonId", "ActivePresetId") &&
               HasValidMigrationHistoryColumns(connection);
    }

    private static bool HasColumns(
        SqliteConnection connection,
        string tableName,
        params string[] required)
    {
        var columns = connection.Query<string>(
                """
                SELECT name
                FROM pragma_table_info(@TableName);
                """,
                new { TableName = tableName })
            .ToHashSet(StringComparer.Ordinal);

        return required.All(columns.Contains);
    }

    private static bool HasValidMigrationHistoryColumns(
        SqliteConnection connection)
    {
        var columns = connection.Query<MigrationColumn>(
                """
                SELECT
                    name AS Name,
                    type AS DataType,
                    "notnull" AS IsRequired,
                    pk AS IsPrimaryKey
                FROM pragma_table_info('__SchemaMigrations');
                """)
            .ToDictionary(
                x => x.Name,
                StringComparer.OrdinalIgnoreCase);

        if (columns.Count != 4)
        {
            return false;
        }

        return Matches("Version", "INTEGER", 1) &&
               Matches("Name", "TEXT", 0) &&
               Matches("Checksum", "TEXT", 0) &&
               Matches("AppliedAtUtc", "TEXT", 0);

        bool Matches(
            string name,
            string type,
            int primaryKey) =>
            columns.TryGetValue(name, out var column) &&
            string.Equals(
                column.DataType,
                type,
                StringComparison.OrdinalIgnoreCase) &&
            column.IsRequired == 1 &&
            column.IsPrimaryKey == primaryKey;
    }

    private static bool TryGetCompatibleMigrationCount(
        SqliteConnection connection,
        out int migrationCount)
    {
        migrationCount = 0;

        var history = connection.Query<AppliedMigration>(
                """
                SELECT
                    Version,
                    Name,
                    Checksum,
                    AppliedAtUtc
                FROM __SchemaMigrations
                ORDER BY Version;
                """)
            .ToList();

        var assembly = typeof(MigrationRunner).Assembly;

        var resourceNames = assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(
                    MigrationResourcePrefix,
                    StringComparison.Ordinal) &&
                name.EndsWith(
                    ".sql",
                    StringComparison.Ordinal))
            .OrderBy(
                name => name,
                StringComparer.Ordinal)
            .ToArray();

        // Accept a verified prefix of embedded migrations.
        // Older backups may contain only migration 001.
        // The normal migration runner applies missing
        // migrations after the backup has been restored.

        if (resourceNames.Length == 0 ||
            history.Count == 0 ||
            history.Count > resourceNames.Length)
        {
            return false;
        }

        for (var index = 0;
             index < resourceNames.Length;
             index++)
        {
            var resourceName = resourceNames[index];

            var fileName =
                resourceName[MigrationResourcePrefix.Length..];

            var match = Regex.Match(
                fileName,
                @"^([0-9]{3})_[A-Za-z0-9_]+\.sql$",
                RegexOptions.CultureInvariant);

            if (!match.Success ||
                !int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var version) ||
                version != index + 1)
            {
                return false;
            }

            if (index >= history.Count)
            {
                continue;
            }

            using var stream =
                assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                return false;
            }

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            var checksum = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        reader.ReadToEnd())));

            var applied = history[index];

            if (applied.Version != version ||
                !string.Equals(
                    applied.Name,
                    fileName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    applied.Checksum,
                    checksum,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsValidUtcTimestamp(applied.AppliedAtUtc))
            {
                return false;
            }
        }

        migrationCount = history.Count;
        return true;
    }

    private static bool HasValidAppSettings(
        SqliteConnection connection,
        int migrationCount)
    {
        var exists = connection.ExecuteScalar<long>(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'AppSettings';
            """) == 1;

        // Migration 001 did not contain AppSettings.
        if (migrationCount == 1)
        {
            return !exists;
        }

        if (migrationCount < 2 || !exists)
        {
            return false;
        }

        var columns = connection.Query<MigrationColumn>(
                """
                SELECT
                    name AS Name,
                    type AS DataType,
                    "notnull" AS IsRequired,
                    pk AS IsPrimaryKey
                FROM pragma_table_info('AppSettings');
                """)
            .ToDictionary(
                x => x.Name,
                StringComparer.OrdinalIgnoreCase);

        if (columns.Count != 4 ||
            !Matches("SingletonId", "INTEGER", 1) ||
            !Matches("Language", "TEXT", 0) ||
            !Matches("Theme", "TEXT", 0) ||
            !Matches("MinutesPerDay", "INTEGER", 0))
        {
            return false;
        }

        var rows = connection.Query<StoredAppSettings>(
                """
                SELECT
                    Language,
                    Theme,
                    MinutesPerDay
                FROM AppSettings
                WHERE SingletonId = 1
                  AND typeof(MinutesPerDay) = 'integer';
                """)
            .ToList();

        if (connection.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM AppSettings;") != 1 ||
            rows.Count != 1)
        {
            return false;
        }

        var row = rows[0];

        try
        {
            // AppLanguage is the supported-language catalog.
            // No fixed en/ru restriction is maintained here.

            var language = AppLanguageCodes.Parse(row.Language);

            if (!string.Equals(
                    row.Language,
                    language.ToCode(),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return (row.Theme is "System" or "Light" or "Dark") &&
               row.MinutesPerDay is >= 1 and <= 1440;

        bool Matches(
            string name,
            string type,
            int primaryKey) =>
            columns.TryGetValue(name, out var column) &&
            string.Equals(
                column.DataType,
                type,
                StringComparison.OrdinalIgnoreCase) &&
            column.IsRequired == 1 &&
            column.IsPrimaryKey == primaryKey;
    }

    private static bool IsValidUtcTimestamp(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Regex.IsMatch(
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
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                "yyyy-MM-dd'T'HH:mm:ss'Z'"
            ],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static bool HasValidSystemStatuses(
        SqliteConnection connection)
    {
        var statuses = connection.Query<SystemStatus>(
                """
                SELECT
                    Id,
                    SystemKey,
                    IsSystem,
                    IsCompleted
                FROM TaskStatuses
                WHERE SystemKey IN ('open', 'completed');
                """)
            .ToList();

        return statuses.Count == 2 &&
               statuses.Any(s =>
                   s.SystemKey == "open" &&
                   string.Equals(
                       s.Id,
                       TaskStatusDefinition.OpenId.ToString("D"),
                       StringComparison.OrdinalIgnoreCase) &&
                   s.IsSystem == 1 &&
                   s.IsCompleted == 0) &&
               statuses.Any(s =>
                   s.SystemKey == "completed" &&
                   string.Equals(
                       s.Id,
                       TaskStatusDefinition.CompletedId.ToString("D"),
                       StringComparison.OrdinalIgnoreCase) &&
                   s.IsSystem == 1 &&
                   s.IsCompleted == 1);
    }

    private static bool HasValidReportSettings(
        SqliteConnection connection)
    {
        var defaultCount = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM ReportPresets
            WHERE Id = @Id AND IsBuiltIn = 1;
            """,
            new { Id = DefaultPresetId });

        var totalSettings = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ReportSettings;");

        var validSettings = connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM ReportSettings AS s
            INNER JOIN ReportPresets AS p
                ON p.Id = s.ActivePresetId
            WHERE s.SingletonId = 1;
            """);

        return defaultCount == 1 &&
               totalSettings == 1 &&
               validSettings == 1;
    }

    private sealed class StoredAppSettings
    {
        public string Language { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;
        public int MinutesPerDay { get; set; }
    }

    private sealed class MigrationColumn
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int IsRequired { get; set; }
        public int IsPrimaryKey { get; set; }
    }

    private sealed class AppliedMigration
    {
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string AppliedAtUtc { get; set; } = string.Empty;
    }

    private sealed class SystemStatus
    {
        public string Id { get; set; } = string.Empty;
        public string SystemKey { get; set; } = string.Empty;
        public int IsSystem { get; set; }
        public int IsCompleted { get; set; }
    }
}