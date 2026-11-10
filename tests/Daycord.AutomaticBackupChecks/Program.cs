using System.Security.Cryptography;
using Daycord.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daycord.AutomaticBackupChecks;

internal static class Program
{
    private static readonly (
        string Name,
        Func<Task> Run)[] Checks =
    [
        (
            "First automatic backup is valid",
            FirstBackupIsValidAsync
        ),
        (
            "Unchanged database causes no backup I/O",
            NoChangesMeansNoBackupAsync
        ),
        (
            "Multiple changes produce one snapshot",
            MultipleChangesProduceOneSnapshotAsync
        ),
        (
            "Changes after captured revision remain dirty",
            LaterChangesRemainDirtyAsync
        ),
        (
            "Automatic backup rotation retains three copies",
            RotationKeepsThreeCopiesAsync
        ),
        (
            "Invalid new snapshot preserves previous backup",
            InvalidSnapshotPreservesPreviousAsync
        ),
        (
            "Startup does not revalidate older snapshots",
            StartupChecksLatestSnapshotAsync
        )
    ];

    private static async Task<int> Main()
    {
        var passed = 0;

        foreach (var check in Checks)
        {
            try
            {
                await check.Run();

                Console.WriteLine($"PASS  {check.Name}");
                passed++;
            }
            catch (Exception error)
            {
                Console.WriteLine($"FAIL  {check.Name}");
                Console.WriteLine(
                    $"      {error.GetType().Name}: {error.Message}");
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            $"DC-046 Automatic Backup: " +
            $"{passed}/{Checks.Length} checks passed.");

        return passed == Checks.Length ? 0 : 1;
    }

    private static async Task FirstBackupIsValidAsync()
    {
        using var fixture = await TestFixture.CreateAsync();

        await fixture.Backups.EnsureRecentVerifiedBackupAsync();

        var backups = fixture.GetPublishedBackups();

        Require(
            backups.Length == 1,
            "Exactly one initial automatic backup was expected.");

        VerifySnapshot(backups[0]);

        Require(
            ReadTaskCount(backups[0]) == 0,
            "The initial snapshot must contain no user tasks.");
    }

    private static async Task NoChangesMeansNoBackupAsync()
    {
        using var fixture = await TestFixture.CreateAsync();

        await fixture.Backups.EnsureRecentVerifiedBackupAsync();

        var backups = fixture.GetPublishedBackups();

        Require(
            backups.Length == 1,
            "The initial automatic snapshot is missing.");

        var originalHash = HashFile(backups[0]);

        // A normal no-change check must not even recreate a missing
        // backup directory. This exercises the in-memory fast path.
        var directory = fixture.AutomaticBackupDirectory;
        var parked = directory + "-parked";

        Directory.Move(directory, parked);

        try
        {
            var created = await fixture.Backups.BackupIfChangedAsync();

            Require(
                !created,
                "No snapshot should be created without changes.");

            Require(
                !Directory.Exists(directory),
                "The no-change fast path touched the backup directory.");
        }
        finally
        {
            Directory.Move(parked, directory);
        }

        Require(
            fixture.GetPublishedBackups().Length == 1,
            "The backup count unexpectedly changed.");

        Require(
            HashFile(backups[0]) == originalHash,
            "The existing backup was modified.");
    }

    private static async Task MultipleChangesProduceOneSnapshotAsync()
    {
        using var fixture = await TestFixture.CreateAsync();

        await fixture.Backups.EnsureRecentVerifiedBackupAsync();

        for (var i = 1; i <= 3; i++)
        {
            await fixture.InsertTaskAsync(i);
        }

        Require(
            fixture.Changes.HasUnbackedChanges,
            "Committed changes were not recorded.");

        // Ordinary navigation should respect the cooldown.
        var throttled = await fixture.Backups.BackupIfChangedAsync();

        Require(
            !throttled,
            "The two-minute cooldown was ignored.");

        Require(
            fixture.GetPublishedBackups().Length == 1,
            "A snapshot was created during cooldown.");

        // Simulate an allowed forced flush, such as shutdown.
        var created = await fixture.Backups.BackupIfChangedAsync(
            bypassCooldown: true);

        Require(
            created,
            "Committed task changes did not produce a snapshot.");

        var backups = fixture.GetPublishedBackups();

        Require(
            backups.Length == 2,
            "Three committed changes must produce one new snapshot.");

        Require(
            ReadTaskCount(backups[0]) == 3,
            "The newest snapshot does not contain all three tasks.");

        Require(
            !fixture.Changes.HasUnbackedChanges,
            "The completed snapshot did not update the revision.");

        var duplicate = await fixture.Backups.BackupIfChangedAsync(
            bypassCooldown: true);

        Require(
            !duplicate,
            "A duplicate snapshot was created without new changes.");

        Require(
            fixture.GetPublishedBackups().Length == 2,
            "The snapshot count increased without changes.");
    }

    private static Task LaterChangesRemainDirtyAsync()
    {
        var changes = new DatabaseChangeTracker();

        changes.MarkCommittedChange();
        changes.MarkCommittedChange();

        var capturedRevision = changes.CaptureRevision();

        Require(
            capturedRevision == 2,
            "The captured revision must be 2.");

        // Simulate a write committed after backup creation started.
        changes.MarkCommittedChange();

        // Only the revision captured before backup is covered.
        changes.MarkBackedUp(capturedRevision);

        Require(
            changes.HasUnbackedChanges,
            "A later committed change was incorrectly marked backed up.");

        Require(
            changes.CaptureRevision() == 3,
            "The later committed revision was lost.");

        changes.MarkBackedUp(3);

        Require(
            !changes.HasUnbackedChanges,
            "All committed revisions should now be backed up.");

        return Task.CompletedTask;
    }

    private static async Task RotationKeepsThreeCopiesAsync()
    {
        using var fixture = await TestFixture.CreateAsync();

        await fixture.Backups.EnsureRecentVerifiedBackupAsync();

        // Create four additional distinct snapshots.
        for (var i = 1; i <= 4; i++)
        {
            await fixture.InsertTaskAsync(i);

            // Keep timestamp ordering deterministic.
            await Task.Delay(35);

            var created = await fixture.Backups.BackupIfChangedAsync(
                bypassCooldown: true);

            Require(
                created,
                $"Snapshot after task {i} was not created.");
        }

        var backups = fixture.GetPublishedBackups();

        Require(
            backups.Length == 3,
            "The retention policy must keep three snapshots.");

        var taskCounts = backups
            .Select(ReadTaskCount)
            .ToArray();

        Require(
            taskCounts.SequenceEqual([4, 3, 2]),
            "The wrong snapshots survived rotation.");

        foreach (var backup in backups)
        {
            VerifySnapshot(backup);
        }

        Require(
            !fixture.Changes.HasUnbackedChanges,
            "The last committed change is not backed up.");
    }

    private static async Task InvalidSnapshotPreservesPreviousAsync()
    {
        using var fixture = await TestFixture.CreateAsync();

        await fixture.Backups.EnsureRecentVerifiedBackupAsync();

        var previous = fixture.GetPublishedBackups().Single();
        var previousHash = HashFile(previous);

        // Deliberately break the schema of the ISOLATED test database.
        // The online backup may succeed as a file operation, but the
        // application's snapshot validator must reject the result.
        await fixture.ExecuteSqlAsync(
            "DROP TABLE ReportSettings;");

        fixture.Changes.MarkCommittedChange();

        await ExpectThrowsAsync<InvalidDataException>(
            async () =>
            {
                await fixture.Backups.BackupIfChangedAsync(
                    bypassCooldown: true);
            });

        var backups = fixture.GetPublishedBackups();

        Require(
            backups.Length == 1,
            "An invalid snapshot displaced an existing backup.");

        Require(
            backups[0] == previous,
            "The original verified backup was replaced.");

        Require(
            HashFile(previous) == previousHash,
            "The original backup was modified.");

        VerifySnapshot(previous);

        Require(
            fixture.Changes.HasUnbackedChanges,
            "A failed snapshot incorrectly cleared pending changes.");

        Require(
            !Directory.EnumerateFiles(
                fixture.AutomaticBackupDirectory,
                ".daycord-auto-*.tmp.db")
                .Any(),
            "An unpublished temporary snapshot was not cleaned up.");
    }

    private static async Task StartupChecksLatestSnapshotAsync()
    {
        using var fixture = await TestFixture.CreateAsync();

        await fixture.Backups.EnsureRecentVerifiedBackupAsync();

        for (var i = 1; i <= 2; i++)
        {
            await fixture.InsertTaskAsync(i);
            await Task.Delay(35);

            await fixture.Backups.BackupIfChangedAsync(
                bypassCooldown: true);
        }

        var before = fixture.GetPublishedBackups();

        Require(
            before.Length == 3,
            "Three initial snapshots are required for this test.");

        var latestHash = HashFile(before[0]);

        // Corrupt the oldest backup. The newest backup remains valid.
        // Startup should not perform a complete scan of the older copies.
        File.WriteAllText(
            before[^1],
            "Intentionally corrupted old test snapshot");

        using var restartedService = new AutomaticBackupService(
            fixture.Paths,
            new DatabaseChangeTracker());

        await restartedService.EnsureRecentVerifiedBackupAsync();

        var after = fixture.GetPublishedBackups();

        Require(
            after.Length == 3,
            "Startup unexpectedly recreated historical snapshots.");

        Require(
            HashFile(after[0]) == latestHash,
            "The newest verified backup changed during startup.");

        Require(
            after.Contains(before[^1]),
            "Startup unexpectedly removed an older backup.");

        VerifySnapshot(after[0]);
    }

    private static void VerifySnapshot(string path)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA integrity_check;";

        Require(
            string.Equals(
                Convert.ToString(command.ExecuteScalar()),
                "ok",
                StringComparison.Ordinal),
            "SQLite integrity_check failed.");

        command.CommandText = "PRAGMA foreign_key_check;";

        using (var reader = command.ExecuteReader())
        {
            Require(
                !reader.Read(),
                "SQLite foreign_key_check failed.");
        }

        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = '__SchemaMigrations';
            """;

        Require(
            Convert.ToInt32(command.ExecuteScalar()) == 1,
            "The snapshot has no migration history.");
    }

    private static int ReadTaskCount(string path)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM Tasks;";

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false
        };

        var connection = new SqliteConnection(
            connectionString.ToString());

        connection.Open();

        return connection;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);

        return Convert.ToHexString(
            SHA256.HashData(stream));
    }

    private static async Task ExpectThrowsAsync<TException>(
        Func<Task> operation)
        where TException : Exception
    {
        try
        {
            await operation();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected exception: {typeof(TException).Name}.");
    }

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly string _root;

        private TestFixture(
            string root,
            AppDataPaths paths)
        {
            _root = root;

            Paths = paths;
            Changes = new DatabaseChangeTracker();

            Backups = new AutomaticBackupService(
                Paths,
                Changes);
        }

        public AppDataPaths Paths { get; }

        public DatabaseChangeTracker Changes { get; }

        public AutomaticBackupService Backups { get; }

        public string AutomaticBackupDirectory =>
            Path.Combine(
                Paths.DataDirectory,
                "Backups",
                "Automatic");

        public static async Task<TestFixture> CreateAsync()
        {
            // Never use Daycord or Daycord-test profiles here.
            var root = Path.Combine(
                Path.GetTempPath(),
                "daycord-dc046-auto-" +
                Guid.NewGuid().ToString("N"));

            var paths = new AppDataPaths(root);

            paths.EnsureDataDirectory();

            try
            {
                var connections = new SqliteConnectionFactory(paths);
                var schemaBackups = new DatabaseBackupService(paths);

                var migrations = new MigrationRunner(
                    connections,
                    schemaBackups);

                await migrations.RunAsync(
                    databaseExistedBeforeOpen: false);

                return new TestFixture(root, paths);
            }
            catch
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                throw;
            }
        }

        public string[] GetPublishedBackups()
        {
            if (!Directory.Exists(AutomaticBackupDirectory))
                return [];

            return Directory
                .EnumerateFiles(
                    AutomaticBackupDirectory,
                    "daycord-auto-*.db",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(
                    Path.GetFileName,
                    StringComparer.Ordinal)
                .ToArray();
        }

        public async Task InsertTaskAsync(int number)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                Pooling = false
            };

            await using var connection = new SqliteConnection(
                connectionString.ToString());

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO Tasks
                (
                    Id,
                    Title,
                    StatusId,
                    CreatedAtUtc,
                    UpdatedAtUtc
                )
                SELECT
                    $id,
                    $title,
                    Id,
                    $timestamp,
                    $timestamp
                FROM TaskStatuses
                WHERE IsSystem = 1
                  AND IsCompleted = 0
                LIMIT 1;
                """;

            var timestamp = DateTime.UtcNow.ToString("O");

            command.Parameters.AddWithValue(
                "$id",
                Guid.NewGuid().ToString("D"));

            command.Parameters.AddWithValue(
                "$title",
                $"Automatic backup test task {number}");

            command.Parameters.AddWithValue(
                "$timestamp",
                timestamp);

            var affected = await command.ExecuteNonQueryAsync();

            Require(
                affected == 1,
                "Could not insert a test task.");

            // The SQL command completed successfully.
            Changes.MarkCommittedChange();
        }

        public async Task ExecuteSqlAsync(string sql)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                Pooling = false
            };

            await using var connection = new SqliteConnection(
                connectionString.ToString());

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            Backups.Dispose();

            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // The test profile is isolated from application data.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}