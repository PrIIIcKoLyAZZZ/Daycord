using System.Diagnostics;
using System.Security.Cryptography;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Maintains up to three published automatic SQLite snapshots.
/// Navigation checks only in-memory revisions. Existing backups are
/// inspected at startup, not on every navigation event.
/// </summary>
public sealed class AutomaticBackupService : IDisposable
{
    private const int RetainedCopies = 3;

    private static readonly TimeSpan MinimumInterval =
        TimeSpan.FromMinutes(2);

    private readonly AppDataPaths _paths;
    private readonly DatabaseChangeTracker _changes;
    private readonly SqliteSnapshotValidator _validator = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile bool _initialized;
    private volatile bool _needsBackup = true;

    private string? _latestBackupPath;
    private DateTime _latestBackupUtc;
    private bool _disposed;

    public AutomaticBackupService(
        AppDataPaths paths,
        DatabaseChangeTracker? changes = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _changes = changes ?? new DatabaseChangeTracker();
    }

    /// <summary>
    /// Called once after normal schema initialization. Checks the newest
    /// published backup and whether the live database appears newer.
    /// It does not revalidate every historical copy.
    /// </summary>
    public async Task EnsureRecentVerifiedBackupAsync(
        CancellationToken cancellationToken = default)
    {
        await BackupIfChangedAsync(
            bypassCooldown: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns false without touching disk when the cached startup state
    /// is clean and no application writes have been committed.
    /// </summary>
    public Task<bool> BackupIfChangedAsync(
        bool bypassCooldown = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        if (_initialized &&
            !_needsBackup &&
            !_changes.HasUnbackedChanges)
        {
            return Task.FromResult(false);
        }

        return RunBackupAsync(
            bypassCooldown,
            cancellationToken);
    }

    private async Task<bool> RunBackupAsync(
        bool bypassCooldown,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            // A previous caller may already have completed the backup.
            if (_initialized &&
                !_needsBackup &&
                !_changes.HasUnbackedChanges)
            {
                return false;
            }

            return await Task.Run(
                () => BackupCore(bypassCooldown, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool BackupCore(
        bool bypassCooldown,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException(
                "The active database is missing.",
                _paths.DatabasePath);
        }

        // Never publish data from an unfinished restore.
        if (File.Exists(_paths.PendingRestoreManifestPath) ||
            Directory.Exists(_paths.PendingRestoreManifestPath))
        {
            return false;
        }

        var directory = Path.Combine(
            _paths.DataDirectory, "Backups", "Automatic");

        if (Directory.Exists(directory) &&
            (File.GetAttributes(directory) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "The automatic backup directory is redirected.");
        }

        Directory.CreateDirectory(directory);

        if (!_initialized)
        {
            InitializeBackupState(directory, cancellationToken);
        }

        if (!_needsBackup && !_changes.HasUnbackedChanges)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var elapsed = now - _latestBackupUtc;

        if (!bypassCooldown &&
            _latestBackupPath is not null &&
            elapsed >= TimeSpan.Zero &&
            elapsed < MinimumInterval)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Capture before snapshot creation. Writes committed later
        // must remain dirty, even if they overlap the backup.
        var capturedRevision = _changes.CaptureRevision();

        var filename =
            $"daycord-auto-{now:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.db";

        var published = Path.Combine(directory, filename);
        var temporary = Path.Combine(
            directory,
            $".daycord-auto-{Guid.NewGuid():N}.tmp.db");

        try
        {
            SqliteSnapshotIO.CreateStandaloneSnapshot(
                _paths.DatabasePath,
                temporary);

            cancellationToken.ThrowIfCancellationRequested();

            if (!IsVerified(temporary))
            {
                throw new InvalidDataException(
                    "The new automatic backup failed validation.");
            }

            // A write can be a no-op at the SQLite file level.
            // Avoid publishing a duplicate snapshot.
            if (_latestBackupPath is not null &&
                SameContentIfAvailable(
                    temporary,
                    _latestBackupPath))
            {
                _needsBackup = false;
                _changes.MarkBackedUp(capturedRevision);
                return false;
            }

            using (var stream = new FileStream(
                       temporary,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(temporary, published);

            // The new backup is validated again after publication.
            // Existing copies are not reopened.
            if (!IsVerified(published))
            {
                throw new InvalidDataException(
                    "The published automatic backup failed validation.");
            }

            _latestBackupPath = published;
            _latestBackupUtc = File.GetLastWriteTimeUtc(published);

            _needsBackup = false;
            _changes.MarkBackedUp(capturedRevision);

            // Rotation only uses file metadata, not full SQLite validation.
            RotatePublishedBackups(directory);

            return true;
        }
        finally
        {
            DeletePrivateTemporary(temporary);
        }
    }

    private void InitializeBackupState(
        string directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Select only the most recently published snapshot.
        // Older snapshots were validated at publication and are
        // revalidated only if needed for actual recovery.
        var latest = GetPublishedBackups(directory)
            .FirstOrDefault();

        _latestBackupPath = null;
        _latestBackupUtc = default;
        _needsBackup = true;

        if (latest is not null && IsVerified(latest))
        {
            _latestBackupPath = latest;
            _latestBackupUtc = File.GetLastWriteTimeUtc(latest);

            _needsBackup = SourceTouchedSince(_latestBackupUtc);
        }

        _initialized = true;
    }

    private bool SourceTouchedSince(DateTime backupUtc)
    {
        foreach (var suffix in new[] { "", "-wal", "-journal" })
        {
            var file = _paths.DatabasePath + suffix;

            if (File.Exists(file) &&
                File.GetLastWriteTimeUtc(file) > backupUtc)
            {
                return true;
            }
        }

        return false;
    }

    private void RotatePublishedBackups(string directory)
    {
        // Never delete backups during an unfinished restore.
        if (File.Exists(_paths.PendingRestoreManifestPath) ||
            Directory.Exists(_paths.PendingRestoreManifestPath))
        {
            return;
        }

        var published = GetPublishedBackups(directory);

        foreach (var old in published.Skip(RetainedCopies))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException error)
            {
                Trace.TraceWarning(
                    "Automatic backup rotation failed: {0}",
                    error.GetType().Name);
            }
            catch (UnauthorizedAccessException error)
            {
                Trace.TraceWarning(
                    "Automatic backup rotation failed: {0}",
                    error.GetType().Name);
            }
        }
    }

    private static List<string> GetPublishedBackups(
        string directory)
    {
        return Directory
            .EnumerateFiles(
                directory,
                "daycord-auto-*.db",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
                !Path.GetFileName(path).StartsWith('.') &&
                IsOrdinaryFile(path))
            .OrderByDescending(
                Path.GetFileName,
                StringComparer.Ordinal)
            .ToList();
    }

    private bool IsVerified(string path)
    {
        try
        {
            if (!IsOrdinaryFile(path))
                return false;

            SqliteSnapshotIO.RequireNoSidecars(path);
            return _validator.Validate(path).IsValid;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
        catch (FormatException) { return false; }
    }

    private static bool SameContentIfAvailable(
        string first,
        string second)
    {
        if (!IsOrdinaryFile(second))
            return false;

        try
        {
            using var firstStream = File.OpenRead(first);
            using var secondStream = File.OpenRead(second);

            var firstHash = SHA256.HashData(firstStream);
            var secondHash = SHA256.HashData(secondStream);

            return firstHash.AsSpan().SequenceEqual(secondHash);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsOrdinaryFile(string path)
    {
        return File.Exists(path) &&
               (File.GetAttributes(path) &
                FileAttributes.ReparsePoint) == 0;
    }

    private static void DeletePrivateTemporary(string path)
    {
        foreach (var suffix in new[]
                 {
                     "", "-wal", "-shm", "-journal"
                 })
        {
            try
            {
                if (File.Exists(path + suffix))
                    File.Delete(path + suffix);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gate.Dispose();
    }
}