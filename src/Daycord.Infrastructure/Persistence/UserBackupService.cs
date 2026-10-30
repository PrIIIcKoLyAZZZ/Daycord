using Daycord.Core.Application.Contracts;
using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Creates and validates complete Daycord SQLite backups and prepares a
/// deferred restore. Never replaces the active database during application use.
/// </summary>
public sealed class UserBackupService : IUserBackupService
{
    private readonly AppDataPaths _paths;
    private readonly SqliteSnapshotValidator _validator = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public UserBackupService(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task CreateBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        await EnterOperationAsync(cancellationToken);
        try
        {
            await Task.Run(
                () => CreateBackup(destinationPath, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<BackupValidationResult> ValidateBackupAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await EnterOperationAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => ValidateBackup(sourcePath, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task PrepareRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await EnterOperationAsync(cancellationToken);
        try
        {
            await Task.Run(
                () => PrepareRestore(sourcePath, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task EnterOperationAsync(CancellationToken cancellationToken)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException(
                "A backup or restore operation is already in progress.");
        }
    }

    private void CreateBackup(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destination = NormalizeLocalPath(destinationPath);
        EnsureNotActiveDatabase(destination);

        var directory = Path.GetDirectoryName(destination)
            ?? throw new IOException("Invalid backup destination.");

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The selected backup directory does not exist.");
        }

        // This API does not overwrite existing files. The Desktop layer
        // separately handles user-confirmed atomic replacement via a picker.
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("The backup destination already exists.");
        }

        var temporary = Path.Combine(
            directory, $".daycord-backup-{Guid.NewGuid():N}.tmp.db");

        try
        {
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                _paths.DatabasePath, temporary);

            RequireValidSnapshot(temporary);
            cancellationToken.ThrowIfCancellationRequested();

            // Never expose an incomplete backup as the destination.
            // Source and destination belong to the same directory.
            File.Move(temporary, destination);
        }
        finally
        {
            DeleteGeneratedSnapshot(temporary);
        }
    }

    private BackupValidationResult ValidateBackup(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourcePath))
            return new BackupValidationResult(BackupValidationStatus.InvalidFile);

        var source = NormalizeLocalPath(sourcePath);
        if (!File.Exists(source))
            return new BackupValidationResult(BackupValidationStatus.InvalidFile);

        _paths.EnsureRestoreStagingDirectory();
        var temporary = NewTemporaryStagingPath();

        try
        {
            // Do not run checks or change journal mode on the user-selected
            // file. Both operations act on this independent snapshot.
            SqliteSnapshotIO.CreateStandaloneSnapshot(source, temporary);
            cancellationToken.ThrowIfCancellationRequested();

            return _validator.Validate(temporary);
        }
        catch (SqliteException exception)
            when (exception.SqliteErrorCode is 11 or 26)
        {
            // SQLITE_CORRUPT / SQLITE_NOTADB.
            return new BackupValidationResult(BackupValidationStatus.Corrupted);
        }
        finally
        {
            DeleteGeneratedSnapshot(temporary);
        }
    }

    private void PrepareRestore(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = NormalizeLocalPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "The selected backup does not exist.", source);
        }

        _paths.EnsureRestoreStagingDirectory();
        var manifestPath = _paths.PendingRestoreManifestPath;

        if (File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                "A restore is already scheduled. Restart Daycord first.");
        }

        var operationId = Guid.NewGuid().ToString("N");
        var stagedName = $"pending-{operationId}.db";
        var stagedPath = Path.Combine(_paths.RestoreStagingDirectory, stagedName);
        var temporary = NewTemporaryStagingPath();
        var manifestPublished = false;

        try
        {
            // Re-copy and revalidate: the selected source may have changed
            // after the initial verification in the Settings screen.
            SqliteSnapshotIO.CreateStandaloneSnapshot(source, temporary);
            RequireValidSnapshot(temporary);
            cancellationToken.ThrowIfCancellationRequested();

            var checksum = PendingRestoreStateStore.GetSha256(temporary);
            File.Move(temporary, stagedPath);

            var state = new PendingRestoreState(
                operationId,
                stagedName,
                checksum,
                PendingRestorePhase.Prepared);

            cancellationToken.ThrowIfCancellationRequested();
            PendingRestoreStateStore.Create(manifestPath, state);
            manifestPublished = true;
        }
        finally
        {
            DeleteGeneratedSnapshot(temporary);

            if (!manifestPublished)
            {
                // A staged file is only retained if its journal was saved.
                DeleteGeneratedSnapshot(stagedPath);
            }
        }
    }

    private void RequireValidSnapshot(string snapshotPath)
    {
        var result = _validator.Validate(snapshotPath);
        if (!result.IsValid)
        {
            throw new InvalidDataException(
                $"The SQLite backup failed validation: {result.Status}.");
        }

        SqliteSnapshotIO.RequireNoSidecars(snapshotPath);
    }

    private void EnsureNotActiveDatabase(string destination)
    {
        var database = Path.GetFullPath(_paths.DatabasePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var protectedFile in new[]
                 {
                     database,
                     database + "-wal",
                     database + "-shm",
                     database + "-journal",
                     Path.GetFullPath(_paths.PendingRestoreManifestPath)
                 })
        {
            if (string.Equals(destination, protectedFile, comparison))
            {
                throw new InvalidOperationException(
                    "An active Daycord data file cannot be a backup destination.");
            }
        }
    }

    private static string NormalizeLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A local file path is required.", nameof(path));

        return Path.GetFullPath(path);
    }

    private string NewTemporaryStagingPath() => Path.Combine(
        _paths.RestoreStagingDirectory,
        $".snapshot-{Guid.NewGuid():N}.tmp.db");

    private static void DeleteGeneratedSnapshot(string path)
    {
        // Only call for privately generated paths after SQLite handles close.
        foreach (var name in new[]
                 {
                     path,
                     path + "-wal",
                     path + "-shm",
                     path + "-journal"
                 })
        {
            try
            {
                if (File.Exists(name))
                    File.Delete(name);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}