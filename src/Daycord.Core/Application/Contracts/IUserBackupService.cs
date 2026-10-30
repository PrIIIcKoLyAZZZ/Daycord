namespace Daycord.Core.Application.Contracts;

public enum BackupValidationStatus
{
    Valid,
    InvalidFile,
    Corrupted,
    IncompatibleSchema
}

public sealed record BackupValidationResult(BackupValidationStatus Status)
{
    public bool IsValid => Status == BackupValidationStatus.Valid;
}

/// <summary>
/// User-initiated full-database backup and deferred restore.
/// Paths must identify local files; the implementation owns staging files.
/// </summary>
public interface IUserBackupService
{
    /// <summary>
    /// Creates a consistent, validated SQLite backup at the chosen path.
    /// The active database remains unchanged.
    /// </summary>
    Task CreateBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a backup on a separate staging snapshot without changing
    /// either the selected file or the current database.
    /// </summary>
    Task<BackupValidationResult> ValidateBackupAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revalidates and stages a backup for restoration on the next launch.
    /// Does not replace the active database in the running process.
    /// The caller must obtain the user's confirmation first.
    /// </summary>
    Task PrepareRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}