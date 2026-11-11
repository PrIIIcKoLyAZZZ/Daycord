using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daycord.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daycord.EmergencyRetryChecks;

internal static class Program
{
    private static readonly (string Name, Func<Task> Execute)[] Checks =
    [
        ("No journal leaves database untouched", NoJournalAsync),
        ("Prepared journal is not rearmed", PreparedJournalAsync),
        ("Healthy database is never replaced", HealthyDatabaseAsync),
        ("Tampered staged backup blocks rearm", TamperedStagedBackupAsync),
        ("Possible installation blocks rearm", AmbiguousJournalAsync),
        ("Missing staged backup blocks rearm", MissingStagedBackupAsync),
        ("Safe rearm preserves files and original journal", SafeRearmAsync),
        ("Rearmed emergency restore can complete", CompleteRearmedRestoreAsync)
    ];

    private static async Task<int> Main()
    {
        var passed = 0;

        foreach (var (name, execute) in Checks)
        {
            try
            {
                await execute();
                Console.WriteLine($"PASS  {name}");
                passed++;
            }
            catch (Exception error)
            {
                Console.WriteLine($"FAIL  {name}");
                Console.WriteLine($"      {error.GetType().Name}: {error.Message}");
            }
        }

        Console.WriteLine(
            $"DC-046 Emergency Retry: {passed}/{Checks.Length} checks passed.");

        return passed == Checks.Length ? 0 : 1;
    }

    private static async Task NoJournalAsync()
    {
        using var fixture = await Fixture.CreateAsync();
        var original = Sha256(fixture.Paths.DatabasePath);

        Require(
            !await fixture.Retry.TryRearmAsync(),
            "A retry was prepared without a recovery journal.");

        Require(
            original == Sha256(fixture.Paths.DatabasePath),
            "The active database changed without a journal.");

        Require(
            !File.Exists(fixture.Paths.PendingRestoreManifestPath),
            "A recovery journal appeared unexpectedly.");
    }

    private static async Task PreparedJournalAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.CreateJournal(phase: 0);

        var before = Sha256(
            fixture.Paths.PendingRestoreManifestPath);

        Require(
            !await fixture.Retry.TryRearmAsync(),
            "An already prepared operation was rearmed.");

        Require(
            before == Sha256(fixture.Paths.PendingRestoreManifestPath),
            "The prepared recovery journal was modified.");
    }

    private static async Task HealthyDatabaseAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.CreateJournal(phase: 8);

        var dbHash = Sha256(fixture.Paths.DatabasePath);
        var journalHash = Sha256(
            fixture.Paths.PendingRestoreManifestPath);

        Require(
            !await fixture.Retry.TryRearmAsync(),
            "An already healthy live database was accepted for recovery.");

        Require(
            dbHash == Sha256(fixture.Paths.DatabasePath),
            "The healthy database changed.");

        Require(
            journalHash == Sha256(fixture.Paths.PendingRestoreManifestPath),
            "The recovery journal changed.");
    }

    private static async Task TamperedStagedBackupAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.CreateJournal(phase: 8);
        await fixture.CorruptLiveDatabaseAsync();

        await File.AppendAllTextAsync(
            fixture.StagedPath,
            "tampered");

        var journalHash = Sha256(
            fixture.Paths.PendingRestoreManifestPath);

        await ExpectThrowsAsync<InvalidDataException>(
            () => fixture.Retry.TryRearmAsync());

        Require(
            journalHash == Sha256(fixture.Paths.PendingRestoreManifestPath),
            "A tampered staging file caused a journal rewrite.");
    }

    private static async Task AmbiguousJournalAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.CreateJournal(phase: 8);
        await fixture.CorruptLiveDatabaseAsync();

        // Reach a real installed emergency state with a valid
        // raw-archive reference. Do not guess the archive format.
        Require(
            await fixture.Retry.TryRearmAsync(),
            "Could not prepare the isolated emergency installation.");

        Require(
            await fixture.Coordinator.ApplyPendingRestoreAsync(),
            "Could not install the isolated emergency backup.");

        // Simulate a failure recorded after installation began.
        fixture.SetJournalPhase(8);

        var before = Sha256(
            fixture.Paths.PendingRestoreManifestPath);

        Require(
            !await fixture.Retry.TryRearmAsync(),
            "An operation that reached installation was rearmed.");

        Require(
            before == Sha256(fixture.Paths.PendingRestoreManifestPath),
            "An ambiguous journal was modified.");
    }

    private static async Task MissingStagedBackupAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        fixture.CreateJournal(phase: 8);
        await fixture.CorruptLiveDatabaseAsync();

        File.Delete(fixture.StagedPath);

        var before = Sha256(
            fixture.Paths.PendingRestoreManifestPath);

        await ExpectThrowsAsync<InvalidDataException>(
            () => fixture.Retry.TryRearmAsync());

        Require(
            before == Sha256(fixture.Paths.PendingRestoreManifestPath),
            "The journal changed when the staged database was absent.");
    }

    private static async Task SafeRearmAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        var operationId = fixture.CreateJournal(phase: 8);
        await fixture.CorruptLiveDatabaseAsync();

        var dbHash = Sha256(fixture.Paths.DatabasePath);
        var journalHash = Sha256(
            fixture.Paths.PendingRestoreManifestPath);
        var stagedHash = Sha256(fixture.StagedPath);

        Require(
            await fixture.Retry.TryRearmAsync(),
            "A safely recoverable pre-installation failure was rejected.");

        var archived = Path.Combine(
            fixture.Paths.RestoreStagingDirectory,
            $"retry-origin-{operationId}.json");

        Require(
            File.Exists(archived),
            "The previous failure journal was not preserved.");

        Require(
            Sha256(archived) == journalHash,
            "The saved failure journal differs.");

        Require(
            fixture.JournalPhase() == 0,
            "The retry is not Prepared.");

        Require(
            Sha256(fixture.Paths.DatabasePath) == dbHash,
            "The live database was modified by retry preparation.");

        Require(
            Sha256(fixture.StagedPath) == stagedHash,
            "The staged backup was modified by retry preparation.");

        Require(
            !await fixture.Retry.TryRearmAsync(),
            "The same operation was rearmed twice.");
    }

    private static async Task CompleteRearmedRestoreAsync()
    {
        using var fixture = await Fixture.CreateAsync();

        var operationId = fixture.CreateJournal(phase: 8);
        await fixture.CorruptLiveDatabaseAsync();

        var originalHash = Sha256(
            fixture.Paths.DatabasePath);

        Require(
            await fixture.Retry.TryRearmAsync(),
            "The prepared retry was rejected.");

        Require(
            await fixture.Coordinator.ApplyPendingRestoreAsync(),
            "The emergency backup was not installed.");

        // Repeat normal schema initialization.
        await fixture.Migrations.RunAsync(
            databaseExistedBeforeOpen: true);

        await fixture.Coordinator.CompleteAfterInitializationAsync();

        Require(
            !File.Exists(fixture.Paths.PendingRestoreManifestPath),
            "The successfully completed journal was not cleared.");

        Require(
            await HasExpectedReportSettingsAsync(
                fixture.Paths.DatabasePath),
            "The restored database is missing its normal schema.");

        var rawRoot = Path.Combine(
            fixture.Paths.DataDirectory,
            "Backups",
            "Recovery");

        Require(
            Directory.Exists(rawRoot),
            "The original damaged database was not archived.");

        var rawFiles = Directory.GetFiles(
            rawRoot,
            "*",
            SearchOption.AllDirectories);

        Require(
            rawFiles.Length > 0,
            "The raw archive is empty.");

        Require(
            rawFiles.Any(file => Sha256(file) == originalHash),
            "The original damaged DB bytes were not found in the raw archive.");

        Require(
            File.Exists(Path.Combine(
                fixture.Paths.RestoreStagingDirectory,
                $"retry-origin-{operationId}.json")),
            "The original retry journal was lost.");

        Require(
            originalHash != Sha256(fixture.Paths.DatabasePath),
            "The damaged database appears to remain installed.");
    }

    private static async Task<bool> HasExpectedReportSettingsAsync(
        string path)
    {
        await using var connection = Open(
            path,
            SqliteOpenMode.ReadOnly);

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'ReportSettings';
            """;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync()) == 1;
    }

    private static SqliteConnection Open(
        string path,
        SqliteOpenMode mode)
    {
        return new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
    }

    private static string Sha256(string path)
    {
        using var source = File.OpenRead(path);

        return Convert.ToHexString(
            SHA256.HashData(source));
    }

    private static async Task ExpectThrowsAsync<T>(
        Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected exception: {typeof(T).Name}.");
    }

    private static void Require(
        bool condition,
        string reason)
    {
        if (!condition)
            throw new InvalidOperationException(reason);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private string? _stagedPath;

        private Fixture(
            string root,
            AppDataPaths paths,
            MigrationRunner migrations)
        {
            _root = root;

            Paths = paths;
            Migrations = migrations;

            Coordinator = new PendingRestoreCoordinator(paths);
            Coordinator.AcquireApplicationLock();

            Retry = new EmergencyRestoreRetryPreparation(
                Paths,
                Coordinator);
        }

        public AppDataPaths Paths { get; }
        public MigrationRunner Migrations { get; }
        public PendingRestoreCoordinator Coordinator { get; }
        public EmergencyRestoreRetryPreparation Retry { get; }

        public string StagedPath => _stagedPath ??
            throw new InvalidOperationException(
                "No staged backup has been prepared.");

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "daycord-dc046-emergency-retry-" +
                Guid.NewGuid().ToString("N"));

            var paths = new AppDataPaths(root);
            paths.EnsureDataDirectory();

            try
            {
                var migrations = new MigrationRunner(
                    new SqliteConnectionFactory(paths),
                    new DatabaseBackupService(paths));

                await migrations.RunAsync(
                    databaseExistedBeforeOpen: false);

                return new Fixture(root, paths, migrations);
            }
            catch
            {
                try
                {
                    Directory.Delete(
                        root,
                        recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                throw;
            }
        }

        public string CreateJournal(int phase)
        {
            var id = Guid.NewGuid().ToString("N");

            Paths.EnsureRestoreStagingDirectory();

            _stagedPath = Path.Combine(
                Paths.RestoreStagingDirectory,
                $"pending-{id}.db");

            using (var original = Open(
                       Paths.DatabasePath,
                       SqliteOpenMode.ReadOnly))
            using (var snapshot = Open(
                       _stagedPath,
                       SqliteOpenMode.ReadWriteCreate))
            {
                original.Open();
                snapshot.Open();

                original.BackupDatabase(snapshot);

                using var command = snapshot.CreateCommand();

                command.CommandText =
                    "PRAGMA journal_mode = DELETE;";

                Require(
                    string.Equals(
                        Convert.ToString(command.ExecuteScalar()),
                        "delete",
                        StringComparison.OrdinalIgnoreCase),
                    "The test backup is not standalone.");
            }

            var journal = new
            {
                OperationId = id,
                StagedFileName = $"pending-{id}.db",
                StagedSha256 = Sha256(_stagedPath),
                Phase = phase,
                SafetyBackupFileName = (string?)null,
                SafetyBackupSha256 = (string?)null,
                RawArchiveDirectoryName = (string?)null,
                RawArchiveManifestSha256 = (string?)null,
                Mode = 1
            };

            File.WriteAllText(
                Paths.PendingRestoreManifestPath,
                JsonSerializer.Serialize(journal));

            return id;
        }

        public async Task CorruptLiveDatabaseAsync()
        {
            await using var connection = Open(
                Paths.DatabasePath,
                SqliteOpenMode.ReadWrite);

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText =
                "PRAGMA journal_mode = DELETE;";

            await command.ExecuteNonQueryAsync();

            command.CommandText =
                "DROP TABLE ReportSettings;";

            await command.ExecuteNonQueryAsync();
        }

        public void SetJournalPhase(int phase)
        {
            var node = JsonNode.Parse(
                File.ReadAllText(
                    Paths.PendingRestoreManifestPath))!.AsObject();

            node["Phase"] = phase;

            File.WriteAllText(
                Paths.PendingRestoreManifestPath,
                node.ToJsonString());
        }

        public int JournalPhase()
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(
                    Paths.PendingRestoreManifestPath));

            return document.RootElement
                .GetProperty("Phase")
                .GetInt32();
        }

        public void Dispose()
        {
            Coordinator.Dispose();

            try
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}