using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

/// <summary>
/// Finds the newest valid automatic backup.
/// Validates candidates only when recovery is required.
/// </summary>
public sealed class AutomaticRecoveryCandidateSelector
{
    private static readonly Regex BackupNamePattern = new(
        @"^daycord-auto-[0-9]{8}T[0-9]{13}Z-[0-9a-f]{32}\.db$",
        RegexOptions.CultureInvariant);

    private readonly AppDataPaths _paths;
    private readonly SqliteSnapshotValidator _validator = new();

    public AutomaticRecoveryCandidateSelector(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string? FindLatestValid(
        CancellationToken cancellationToken = default)
    {
        var backupsRoot = Path.Combine(
            _paths.DataDirectory,
            "Backups");

        var directory = Path.Combine(
            backupsRoot,
            "Automatic");

        if (!Directory.Exists(directory))
            return null;

        RequireOrdinaryDirectory(_paths.DataDirectory);
        RequireOrdinaryDirectory(backupsRoot);
        RequireOrdinaryDirectory(directory);

        var candidates = Directory
            .EnumerateFiles(
                directory,
                "daycord-auto-*.db",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
                BackupNamePattern.IsMatch(Path.GetFileName(path)))
            .OrderByDescending(
                Path.GetFileName,
                StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsUsableCandidate(candidate))
                return candidate;
        }

        return null;
    }

    private bool IsUsableCandidate(string path)
    {
        if (!File.Exists(path))
            return false;

        var attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
        {
            return false;
        }

        // Published backups must be standalone SQLite databases.
        foreach (var suffix in new[]
                 {
                     "-wal",
                     "-shm",
                     "-journal"
                 })
        {
            if (File.Exists(path + suffix) ||
                Directory.Exists(path + suffix))
            {
                return false;
            }
        }

        try
        {
            return _validator.Validate(path).IsValid;
        }
        catch (SqliteException error)
            when (error.SqliteErrorCode is 11 or 26)
        {
            return false;
        }
    }

    private static void RequireOrdinaryDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                "The automatic backup directory is unavailable.");
        }

        if ((File.GetAttributes(path) &
             FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "A database backup directory is redirected.");
        }
    }
}