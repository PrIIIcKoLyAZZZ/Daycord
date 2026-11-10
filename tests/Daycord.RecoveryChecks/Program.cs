using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daycord.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daycord.RecoveryChecks;

/// <summary>
/// DC-046/DC-047 integration checks. ALL mutable data lives under a fresh random
/// directory in Path.GetTempPath(); no default AppDataPaths is ever created.
/// Run: dotnet run --project tests/Daycord.RecoveryChecks --configuration Debug
/// </summary>
internal static partial class Program
{
    private static async Task<int> Main()
    {
        var checks = new (string Name, Func<Sandbox, Task> Execute)[]
        {
            ("First launch does not create pre-upgrade backup", FirstLaunch),
            ("Online backup includes committed WAL data", WalBackup),
            ("Normal restore retains pre-restore data", NormalRestore),
            ("Backup and restore preserve all user records", CompleteUserDataRestore),
            ("User restore rejects changed migration checksum", IncompatibleUserRestore),
            ("Invalid backup cannot schedule emergency restore", InvalidBackup),
            ("Incompatible migration history is rejected", FutureHistory),
            ("Emergency preparation requires instance lock", RequiresInstanceLock),
            ("Active database cannot be chosen as backup", ActiveDatabaseSelection),
            ("RestoreStaging cannot be chosen as backup", StagingSelection),
            ("Emergency restore preserves corrupt original", EmergencyRestore),
            ("Tampered staged backup blocks emergency startup", TamperedStage),
            ("Incomplete raw archive blocks installation", IncompleteArchive),
            ("Existing restore journal cannot be replaced", ExistingJournal),
            ("Interrupted normal installation rolls back", InterruptedInstall),
            ("Interrupted quarantine resumes normal rollback", InterruptedQuarantine)
        };

        var failed = 0;
        foreach (var check in checks)
        {
            using var sandbox = new Sandbox();
            try
            {
                await check.Execute(sandbox);
                Console.WriteLine($"PASS  {check.Name}");
            }
            catch (Exception error)
            {
                failed++;
                sandbox.KeepArtifacts();
                Console.Error.WriteLine($"FAIL  {check.Name}");
                Console.Error.WriteLine($"      {error.GetType().Name}: {error.Message}");
                Console.Error.WriteLine($"      Test files retained: {sandbox.Root}");
            }
        }

        Console.WriteLine($"Recovery: {checks.Length - failed}/{checks.Length} checks passed (DC-046 + DC-047).");
        return failed == 0 ? 0 : 1;
    }

    private static async Task FirstLaunch(Sandbox s)
    {
        await Initialize(s);
        Assert(File.Exists(s.Paths.DatabasePath), "New SQLite file was not created.");
        Assert(!Directory.Exists(s.Paths.SchemaBackupDirectory) ||
               !Directory.EnumerateFiles(s.Paths.SchemaBackupDirectory, "*.db").Any(),
            "First launch unexpectedly created a pre-upgrade snapshot.");
        VerifySqlite(s.Paths.DatabasePath);
    }

    private static async Task WalBackup(Sandbox s)
    {
        await Initialize(s);
        using (var active = Connect(s.Paths.DatabasePath))
        {
            using (var command = active.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                Assert(string.Equals(Convert.ToString(command.ExecuteScalar()), "wal",
                    StringComparison.OrdinalIgnoreCase), "Could not enable WAL in test DB.");
            }

            InsertTask(active, "wal-committed");
            Assert(File.Exists(s.Paths.DatabasePath + "-wal"),
                "WAL file not present during the test; WAL case was not exercised.");

            var backup = s.File("wal-backup.db");
            await new UserBackupService(s.Paths).CreateBackupAsync(backup);
            Assert(TaskCount(backup, "wal-committed") == 1,
                "Committed WAL transaction is missing from the snapshot.");
            VerifySqlite(backup);
        }
    }

    private static async Task NormalRestore(Sandbox s)
    {
        await Initialize(s);
        InsertTask(s.Paths.DatabasePath, "before-backup");
        var backup = await MakeBackup(s);
        InsertTask(s.Paths.DatabasePath, "after-backup");
        var service = new UserBackupService(s.Paths);
        await service.PrepareRestoreAsync(backup);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();
        Assert(await coordinator.ApplyPendingRestoreAsync(), "Normal restore was not installed.");
        Assert(!coordinator.IsEmergencyRestoreInstalled,
            "Normal restore incorrectly selected emergency policy.");
        await Initialize(s);
        await coordinator.CompleteAfterInitializationAsync();

        Assert(TaskCount(s.Paths.DatabasePath, "before-backup") == 1, "Backup task missing.");
        Assert(TaskCount(s.Paths.DatabasePath, "after-backup") == 0, "Post-backup task remained.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath), "Completed journal was not cleared.");
        Assert(Directory.EnumerateFiles(s.Paths.BeforeRestoreBackupDirectory, "*.db").Any(),
            "Normal restore did not retain the verified safety copy.");
        VerifySqlite(s.Paths.DatabasePath);
    }

    private static async Task InvalidBackup(Sandbox s)
    {
        await Initialize(s);
        InsertTask(s.Paths.DatabasePath, "unchanged");
        var originalHash = Sha(s.Paths.DatabasePath);
        var invalid = s.File("bad.db");
        File.WriteAllText(invalid, "not a SQLite database");

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();

        var status = await new UserBackupService(s.Paths).ValidateBackupAsync(invalid);
        Assert(status.Status == Daycord.Core.Application.Contracts.BackupValidationStatus.Corrupted,
            $"Expected Corrupted backup status; got {status.Status}.");

        await ExpectFailure(
            () => coordinator.PrepareEmergencyRestoreAsync(invalid),
            error => error is SqliteException { SqliteErrorCode: 11 or 26 } ||
                     error is InvalidDataException ex &&
                     ex.Message.Contains("Corrupted", StringComparison.OrdinalIgnoreCase),
            "invalid SQLite backup (SQLITE_CORRUPT/NOTADB)");

        Assert(Sha(s.Paths.DatabasePath) == originalHash, "Live database changed.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath), "Invalid backup published a journal.");
    }

    private static async Task FutureHistory(Sandbox s)
    {
        await Initialize(s);
        var backup = await MakeBackup(s);
        using (var connection = Connect(backup))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE __SchemaMigrations SET Checksum = @Bad WHERE Version = 1;";
            command.Parameters.AddWithValue("@Bad", new string('0', 64));
            Assert(command.ExecuteNonQuery() == 1, "Could not corrupt test migration history.");
        }

        var originalHash = Sha(s.Paths.DatabasePath);
        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();

        var status = await new UserBackupService(s.Paths).ValidateBackupAsync(backup);
        Assert(status.Status == Daycord.Core.Application.Contracts.BackupValidationStatus.IncompatibleSchema,
            $"Expected IncompatibleSchema backup status; got {status.Status}.");

        await ExpectFailure(
            () => coordinator.PrepareEmergencyRestoreAsync(backup),
            error => error is InvalidDataException ex &&
                     ex.Message.Contains("IncompatibleSchema", StringComparison.Ordinal),
            "incompatible migration history");

        Assert(Sha(s.Paths.DatabasePath) == originalHash, "Live database changed.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "Incompatible migration history was accepted.");
    }

    private static async Task RequiresInstanceLock(Sandbox s)
    {
        await Initialize(s);
        var backup = await MakeBackup(s);
        var originalHash = Sha(s.Paths.DatabasePath);
        using var coordinator = new PendingRestoreCoordinator(s.Paths);

        await ExpectFailure(
            () => coordinator.PrepareEmergencyRestoreAsync(backup),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("instance lock has not been acquired",
                         StringComparison.Ordinal),
            "missing instance lock");

        Assert(Sha(s.Paths.DatabasePath) == originalHash,
            "Preparation without a lock changed the live database.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "Preparation without a lock published a restore journal.");
    }

    private static async Task ActiveDatabaseSelection(Sandbox s)
    {
        await Initialize(s);
        var originalHash = Sha(s.Paths.DatabasePath);
        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();

        // Include a lexical alias so the test cannot pass through a naïve
        // comparison of the exact original strings.
        var alias = Path.Combine(s.Paths.DataDirectory, "unused", "..", "daycord.db");

        await ExpectFailure(
            () => coordinator.PrepareEmergencyRestoreAsync(alias),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("active database", StringComparison.Ordinal),
            "active database selected as backup");

        Assert(Sha(s.Paths.DatabasePath) == originalHash,
            "Selecting the active database modified it.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "Selecting the active database published a journal.");
    }

    private static async Task StagingSelection(Sandbox s)
    {
        await Initialize(s);
        var backup = await MakeBackup(s);
        s.Paths.EnsureRestoreStagingDirectory();
        var staged = Path.Combine(s.Paths.RestoreStagingDirectory, "other-backup.db");
        File.Copy(backup, staged);

        var originalHash = Sha(s.Paths.DatabasePath);
        var stagedHash = Sha(staged);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();

        await ExpectFailure(
            () => coordinator.PrepareEmergencyRestoreAsync(staged),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("restore staging", StringComparison.Ordinal),
            "RestoreStaging file selected as backup");

        Assert(Sha(s.Paths.DatabasePath) == originalHash,
            "Selecting a staged file modified the active database.");
        Assert(Sha(staged) == stagedHash,
            "Selecting a staged file modified the rejected source.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "Selecting a staged file published a journal.");
    }

    private static async Task EmergencyRestore(Sandbox s)
    {
        await Initialize(s);
        InsertTask(s.Paths.DatabasePath, "from-backup");
        var backup = await MakeBackup(s);
        InsertTask(s.Paths.DatabasePath, "lost-after-backup");

        File.WriteAllBytes(s.Paths.DatabasePath, "corrupted-original-db"u8.ToArray());
        var damagedHash = Sha(s.Paths.DatabasePath);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();
        await coordinator.PrepareEmergencyRestoreAsync(backup);

        Assert(Sha(s.Paths.DatabasePath) == damagedHash,
            "Preparing an emergency restore touched the active DB.");
        Assert(ReadPhase(s.Paths.PendingRestoreManifestPath) == 0 &&
               ReadMode(s.Paths.PendingRestoreManifestPath) == 1,
            "Emergency journal was not prepared correctly.");

        Assert(await coordinator.ApplyPendingRestoreAsync(),
            "Emergency snapshot was not installed.");
        Assert(coordinator.IsEmergencyRestoreInstalled,
            "Emergency policy was not selected.");

        await Initialize(s);
        await coordinator.CompleteAfterInitializationAsync();

        Assert(TaskCount(s.Paths.DatabasePath, "from-backup") == 1,
            "Restored task was not found.");
        Assert(TaskCount(s.Paths.DatabasePath, "lost-after-backup") == 0,
            "Changes after backup incorrectly survived restore.");
        Assert(!File.Exists(s.Paths.PendingRestoreManifestPath),
            "Completed emergency journal was not removed.");

        var archived = Directory.EnumerateDirectories(s.Paths.RawRecoveryBackupDirectory,
            "raw-before-restore-*").Single();

        Assert(Sha(Path.Combine(archived, "daycord.db")) == damagedHash,
            "Raw preservation did not retain original corrupt bytes.");
        VerifySqlite(s.Paths.DatabasePath);
    }

    private static async Task TamperedStage(Sandbox s)
    {
        await Initialize(s);
        var backup = await MakeBackup(s);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();
        await coordinator.PrepareEmergencyRestoreAsync(backup);

        var journal = s.Paths.PendingRestoreManifestPath;
        var stagedName = ReadString(journal, "StagedFileName");
        File.WriteAllText(Path.Combine(s.Paths.RestoreStagingDirectory, stagedName), "tampered");

        var originalHash = Sha(s.Paths.DatabasePath);

        await ExpectFailure(
            () => coordinator.ApplyPendingRestoreAsync(),
            error => error is InvalidDataException ex &&
                     ex.Message.Contains("Staged emergency backup is missing or altered",
                         StringComparison.Ordinal),
            "tampered staged snapshot");

        Assert(Sha(s.Paths.DatabasePath) == originalHash,
            "Live DB changed after staged tampering.");
        Assert(ReadPhase(journal) == 8,
            "EmergencyNeedsAttention was not recorded.");

        await ExpectFailure(
            () => coordinator.ApplyPendingRestoreAsync(),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("explicit intervention", StringComparison.Ordinal),
            "emergency automatic retry blocked");

        Assert(Sha(s.Paths.DatabasePath) == originalHash,
            "Automatic retry modified the active DB.");
    }

    private static async Task IncompleteArchive(Sandbox s)
    {
        await Initialize(s);
        var backup = await MakeBackup(s);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();
        await coordinator.PrepareEmergencyRestoreAsync(backup);

        var operationId = ReadString(s.Paths.PendingRestoreManifestPath, "OperationId");
        var incomplete = Path.Combine(s.Paths.RawRecoveryBackupDirectory,
            $".raw-before-restore-{operationId}-{Guid.NewGuid():N}.incomplete");
        Directory.CreateDirectory(incomplete);

        var originalHash = Sha(s.Paths.DatabasePath);

        await ExpectFailure(
            () => coordinator.ApplyPendingRestoreAsync(),
            error => error is IOException ex &&
                     ex.Message.Contains("unfinished raw recovery archive",
                         StringComparison.Ordinal),
            "incomplete raw archive");

        Assert(ReadPhase(s.Paths.PendingRestoreManifestPath) == 8,
            "Unfinished raw archive did not block installation.");
        Assert(Sha(s.Paths.DatabasePath) == originalHash,
            "Active DB changed despite incomplete archive.");
        Assert(Directory.Exists(incomplete),
            "Forensic incomplete archive was removed.");
    }

    private static async Task ExistingJournal(Sandbox s)
    {
        await Initialize(s);
        var backup = await MakeBackup(s);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();
        await coordinator.PrepareEmergencyRestoreAsync(backup);

        var original = File.ReadAllBytes(s.Paths.PendingRestoreManifestPath);

        await ExpectFailure(
            () => coordinator.PrepareEmergencyRestoreAsync(backup),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("unfinished restore already exists",
                         StringComparison.Ordinal),
            "existing restore journal");

        Assert(File.ReadAllBytes(s.Paths.PendingRestoreManifestPath).SequenceEqual(original),
            "Existing restore journal was overwritten.");
    }

    private static async Task InterruptedInstall(Sandbox s)
    {
        await PrepareInstalledNormalRestore(s);
        var manifest = s.Paths.PendingRestoreManifestPath;
        SetPhase(manifest, 2); // Installing: simulate process death after installing.

        using var restarted = new PendingRestoreCoordinator(s.Paths);
        restarted.AcquireApplicationLock();

        await ExpectFailure(
            () => restarted.ApplyPendingRestoreAsync(),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("An interrupted restore was rolled back",
                         StringComparison.Ordinal),
            "normal restore interruption");

        Assert(TaskCount(s.Paths.DatabasePath, "before-backup") == 1,
            "Original task missing after rollback.");
        Assert(TaskCount(s.Paths.DatabasePath, "after-backup") == 1,
            "Pre-restore changes were not recovered.");
        Assert(!File.Exists(manifest),
            "Rollback did not clear its completed journal.");
        VerifySqlite(s.Paths.DatabasePath);
    }

    private static async Task InterruptedQuarantine(Sandbox s)
    {
        await PrepareInstalledNormalRestore(s);
        var manifest = s.Paths.PendingRestoreManifestPath;
        var operationId = ReadString(manifest, "OperationId");
        SetPhase(manifest, 4); // RollingBack: simulate interruption during quarantine.

        var quarantined = Path.Combine(s.Paths.RawRecoveryBackupDirectory,
            $"rollback-replaced-{operationId}");
        Directory.CreateDirectory(quarantined);
        File.Move(s.Paths.DatabasePath, Path.Combine(quarantined, "daycord.db"));

        using var restarted = new PendingRestoreCoordinator(s.Paths);
        restarted.AcquireApplicationLock();

        await ExpectFailure(
            () => restarted.ApplyPendingRestoreAsync(),
            error => error is InvalidOperationException ex &&
                     ex.Message.Contains("An interrupted restore was rolled back",
                         StringComparison.Ordinal),
            "interrupted quarantine rollback");

        Assert(TaskCount(s.Paths.DatabasePath, "after-backup") == 1,
            "Interrupted quarantine was not completed by rollback.");
        Assert(File.Exists(Path.Combine(quarantined, "daycord.db")),
            "Quarantined installed database was discarded.");
        Assert(!File.Exists(manifest),
            "Rollback journal is still present.");
        VerifySqlite(s.Paths.DatabasePath);
    }

    private static async Task PrepareInstalledNormalRestore(Sandbox s)
    {
        await Initialize(s);
        InsertTask(s.Paths.DatabasePath, "before-backup");
        var backup = await MakeBackup(s);
        InsertTask(s.Paths.DatabasePath, "after-backup");
        await new UserBackupService(s.Paths).PrepareRestoreAsync(backup);

        using var coordinator = new PendingRestoreCoordinator(s.Paths);
        coordinator.AcquireApplicationLock();
        Assert(await coordinator.ApplyPendingRestoreAsync(),
            "Normal restore did not install.");
        Assert(ReadPhase(s.Paths.PendingRestoreManifestPath) == 3,
            "Fixture did not reach Installed state.");

        // Deliberately do not call CompleteAfterInitializationAsync():
        // the next test simulates the process stopping before completion.
    }

    private static async Task Initialize(Sandbox s)
    {
        var connections = new SqliteConnectionFactory(s.Paths);
        var migrations = new MigrationRunner(
            connections, new DatabaseBackupService(s.Paths));
        await new DatabaseInitializer(s.Paths, migrations).InitializeAsync();
    }

    private static async Task<string> MakeBackup(Sandbox s)
    {
        var file = s.File($"good-{Guid.NewGuid():N}.db");
        await new UserBackupService(s.Paths).CreateBackupAsync(file);
        VerifySqlite(file);
        return file;
    }

    private static SqliteConnection Connect(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void InsertTask(string database, string title)
    {
        using var connection = Connect(database);
        InsertTask(connection, title);
    }

    private static void InsertTask(SqliteConnection connection, string title)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tasks (Id, Title, StatusId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@Id, @Title,
                    (SELECT Id FROM TaskStatuses WHERE SystemKey = 'open'),
                    @Utc, @Utc);
            """;

        command.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("@Title", title);
        command.Parameters.AddWithValue("@Utc", DateTime.UtcNow.ToString("O"));

        Assert(command.ExecuteNonQuery() == 1,
            "Could not insert a fixture task.");
    }

    private static int TaskCount(string database, string title)
    {
        using var connection = Connect(database);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tasks WHERE Title = @Title;";
        command.Parameters.AddWithValue("@Title", title);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void VerifySqlite(string database)
    {
        using var connection = Connect(database);

        using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";

        Assert(string.Equals(
                Convert.ToString(integrity.ExecuteScalar()),
                "ok",
                StringComparison.OrdinalIgnoreCase),
            "SQLite integrity_check failed.");

        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";

        using var rows = foreignKeys.ExecuteReader();
        Assert(!rows.Read(),
            "SQLite foreign_key_check reported violations.");
    }

    private static string Sha(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int ReadPhase(string path) => ReadInt(path, "Phase");
    private static int ReadMode(string path) => ReadInt(path, "Mode");

    private static int ReadInt(string path, string property)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty(property).GetInt32();
    }

    private static string ReadString(string path, string property)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty(property).GetString()
            ?? throw new InvalidDataException($"Journal field {property} is null.");
    }

    private static void SetPhase(string journal, int phase)
    {
        var state = JsonNode.Parse(File.ReadAllText(journal))?.AsObject()
            ?? throw new InvalidDataException("Missing restore journal object.");

        state["Phase"] = phase;
        File.WriteAllText(journal, state.ToJsonString());
    }

    private static async Task ExpectFailure(
        Func<Task> operation,
        Func<Exception, bool> matchesExpectedReason,
        string expectedReason)
    {
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            if (matchesExpectedReason(error))
                return;

            throw new InvalidOperationException(
                $"Expected failure: {expectedReason}. " +
                $"Actual: {error.GetType().Name}: {error.Message}",
                error);
        }

        throw new InvalidOperationException(
            $"Expected failure: {expectedReason}. Operation completed successfully.");
    }

    private static void Assert(bool condition, string error)
    {
        if (!condition)
            throw new InvalidOperationException(error);
    }

    private sealed class Sandbox : IDisposable
    {
        private bool _retain;

        public string Root { get; }
        public AppDataPaths Paths { get; }

        public Sandbox()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"daycord-dc046-checks-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Root);

            Paths = new AppDataPaths(
                Path.Combine(Root, "isolated-profile"));

            Assert(
                Paths.DatabasePath.StartsWith(
                    Root + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal),
                "Test database escaped the temporary sandbox.");
        }

        public string File(string name) => Path.Combine(Root, name);

        public void KeepArtifacts() => _retain = true;

        public void Dispose()
        {
            if (_retain || !Directory.Exists(Root))
                return;

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                Console.Error.WriteLine(
                    $"Sandbox cleanup deferred: {Root}");
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine(
                    $"Sandbox cleanup deferred: {Root}");
            }
        }
    }
}