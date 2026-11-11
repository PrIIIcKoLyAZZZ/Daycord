using System.Text.Json;
using Daycord.Infrastructure.Persistence;

namespace Daycord.RecoveryChecks;

/// <summary>
/// DC-047 targeted regressions. These use isolated temporary databases
/// created by the existing RecoveryChecks sandbox; no user profile is touched.
/// They validate the current backup/restore implementation, not a historical
/// binary fixture from the published 0.1.0 release.
/// </summary>
internal static partial class Program
{
    private static async Task CompleteUserDataRestore(Sandbox s)
    {
        await Initialize(s);
        SeedUserData(s.Paths.DatabasePath);

        var expected = CaptureData(s.Paths.DatabasePath);
        var backup = await MakeBackup(s);
        AssertDataEqual(expected, CaptureData(backup), "created user backup");

        using (var connection = Connect(s.Paths.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE Tasks SET Title = 'changed after backup'
                WHERE Id = '11111111-1111-1111-1111-111111111111';
                UPDATE WorkEntries SET ActualMinutes = 1;
                DELETE FROM PlanningPeriods;
                UPDATE AppSettings SET Language = 'en', Theme = 'Light', MinutesPerDay = 1440;
                UPDATE ReportPresets SET Name = 'changed preset'
                WHERE Id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
                """;
            command.ExecuteNonQuery();
        }

        Assert(!DataEqual(expected, CaptureData(s.Paths.DatabasePath)),
            "The test did not modify the live data before restore.");

        await new UserBackupService(s.Paths).PrepareRestoreAsync(backup);
        using (var coordinator = new PendingRestoreCoordinator(s.Paths))
        {
            coordinator.AcquireApplicationLock();
            Assert(await coordinator.ApplyPendingRestoreAsync(),
                "The user backup was not installed.");
            await Initialize(s);
            await coordinator.CompleteAfterInitializationAsync();
        }

        AssertDataEqual(expected, CaptureData(s.Paths.DatabasePath), "restore");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "The completed restore left its journal behind.");
        VerifySqlite(s.Paths.DatabasePath);

        // Run initialization again to verify that restoring the same migration
        // history does not introduce duplicate or altered user records.
        await Initialize(s);
        AssertDataEqual(expected, CaptureData(s.Paths.DatabasePath), "relaunch");
    }

    private static async Task IncompatibleUserRestore(Sandbox s)
    {
        await Initialize(s);
        SeedUserData(s.Paths.DatabasePath);
        var expected = CaptureData(s.Paths.DatabasePath);
        var backup = await MakeBackup(s);

        using (var connection = Connect(backup))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE __SchemaMigrations
                SET Checksum = @BadChecksum
                WHERE Version = 1;
                """;
            command.Parameters.AddWithValue("@BadChecksum", new string('0', 64));
            Assert(command.ExecuteNonQuery() == 1,
                "Could not alter the backup migration checksum.");
        }

        var userBackups = new UserBackupService(s.Paths);
        var validation = await userBackups.ValidateBackupAsync(backup);
        Assert(validation.Status ==
               Daycord.Core.Application.Contracts.BackupValidationStatus.IncompatibleSchema,
            $"Expected IncompatibleSchema, got {validation.Status}.");

        await ExpectFailure(
            () => userBackups.PrepareRestoreAsync(backup),
            error => error is InvalidDataException ex &&
                     ex.Message.Contains("IncompatibleSchema", StringComparison.Ordinal),
            "user restore from a backup with an altered migration checksum");

        AssertDataEqual(expected, CaptureData(s.Paths.DatabasePath),
            "rejected user restore");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "A rejected user backup created a restore journal.");
        Assert(!Directory.EnumerateFiles(s.Paths.RestoreStagingDirectory,
                "pending-*.db").Any(),
            "A rejected user backup left a staged restore.");
        VerifySqlite(s.Paths.DatabasePath);
    }

    private static void SeedUserData(string database)
    {
        using var connection = Connect(database);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TaskStatuses (Id, SystemKey, Name, IsSystem, IsCompleted, SortOrder)
            VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc', NULL, 'На проверке', 0, 0, 2);

            INSERT INTO Tasks
                (Id, TaskDate, TaskNumber, Title, ShortDescription,
                 DescriptionMarkdown, StatusId, CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc)
            VALUES
                ('11111111-1111-1111-1111-111111111111', '2026-12-31', 'DC-047',
                 'Отчёт & <проверка>', 'Русский, English, ☃', '**Примечание**',
                 'cccccccc-cccc-cccc-cccc-cccccccccccc',
                 '2026-11-01T09:00:00Z', '2026-11-02T10:00:00Z', NULL),
                ('22222222-2222-2222-2222-222222222222', NULL, NULL,
                 'Without date', NULL, NULL,
                 (SELECT Id FROM TaskStatuses WHERE SystemKey = 'open'),
                 '2026-11-01T09:00:00Z', '2026-11-01T09:00:00Z', NULL),
                ('33333333-3333-3333-3333-333333333333', '2027-01-01', 'DONE-1',
                 'Completed task', 'done', NULL,
                 (SELECT Id FROM TaskStatuses WHERE SystemKey = 'completed'),
                 '2026-11-01T09:00:00Z', '2026-11-02T09:00:00Z',
                 '2026-11-02T09:00:00Z');

            INSERT INTO PlanningPeriods (Id, TaskId, PlannedDate, PlannedMinutes)
            VALUES
                ('44444444-4444-4444-4444-444444444444',
                 '11111111-1111-1111-1111-111111111111', '2026-12-31', 1500),
                ('55555555-5555-5555-5555-555555555555',
                 '11111111-1111-1111-1111-111111111111', '2027-01-01', 45);

            INSERT INTO WorkEntries
                (Id, TaskId, WorkDate, ActualMinutes, WorkDescription, Note,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('66666666-6666-6666-6666-666666666666',
                 '11111111-1111-1111-1111-111111111111',
                 '2026-12-31', 125, 'Код и тесты', 'первая запись',
                 '2026-11-01T09:00:00Z', '2026-11-02T10:00:00Z'),
                ('77777777-7777-7777-7777-777777777777',
                 '11111111-1111-1111-1111-111111111111',
                 '2027-01-01', 75, 'Review', NULL,
                 '2026-11-02T09:00:00Z', '2026-11-03T10:00:00Z');

            UPDATE AppSettings
            SET Language = 'ru', Theme = 'Dark', MinutesPerDay = 480
            WHERE SingletonId = 1;

            INSERT INTO ReportPresets
                (Id, Name, IsBuiltIn, ColumnsJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Мой отчёт', 0,
                 '[{"Field":3,"Header":"Название"},{"Field":1,"Header":"Date"}]',
                 '2026-11-01T09:00:00Z', '2026-11-02T10:00:00Z');

            UPDATE ReportSettings
            SET ActivePresetId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
            WHERE SingletonId = 1;
            """;
        command.ExecuteNonQuery();
        VerifySqlite(database);
    }

    private static Dictionary<string, string> CaptureData(string database)
    {
        string[] tables =
        [
            "Tasks", "TaskStatuses", "PlanningPeriods", "WorkEntries",
            "AppSettings", "ReportPresets", "ReportSettings", "__SchemaMigrations"
        ];

        using var connection = Connect(database);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\";";
            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                var cells = new string[reader.FieldCount];
                for (var i = 0; i < cells.Length; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        cells[i] = "null";
                        continue;
                    }

                    var value = reader.GetValue(i);
                    cells[i] = value is byte[] bytes
                        ? "blob:" + Convert.ToHexString(bytes)
                        : reader.GetDataTypeName(i) + ":" +
                          Convert.ToString(value,
                              System.Globalization.CultureInfo.InvariantCulture);
                }

                rows.Add(JsonSerializer.Serialize(cells));
            }

            rows.Sort(StringComparer.Ordinal);
            result[table] = JsonSerializer.Serialize(rows);
        }

        return result;
    }

    private static bool DataEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual) =>
        expected.All(pair => actual.TryGetValue(pair.Key, out var value) &&
                             string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static void AssertDataEqual(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        string phase)
    {
        foreach (var pair in expected)
        {
            Assert(actual.TryGetValue(pair.Key, out var value) &&
                   string.Equals(pair.Value, value, StringComparison.Ordinal),
                $"Table {pair.Key} changed during {phase}.");
        }
    }
}