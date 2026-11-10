using Microsoft.Data.Sqlite;

namespace Daycord.Infrastructure.Persistence;

public enum AutomaticRecoveryPreparationStatus
{
    NotApplicable,
    ExistingRecoveryOperation,
    NoValidBackup,
    Prepared
}

public sealed record AutomaticRecoveryPreparationResult(
    AutomaticRecoveryPreparationStatus Status);

/// <summary>
/// Prepares automatic recovery after a SQLite corruption failure.
/// Never replaces the live database or an existing recovery journal.
/// </summary>
public sealed class AutomaticRecoveryPreparation
{
    private readonly AppDataPaths _paths;
    private readonly PendingRestoreCoordinator _coordinator;
    private readonly AutomaticRecoveryCandidateSelector _selector;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public AutomaticRecoveryPreparation(
        AppDataPaths paths,
        PendingRestoreCoordinator coordinator,
        AutomaticRecoveryCandidateSelector selector)
    {
        _paths = paths ??
                 throw new ArgumentNullException(nameof(paths));

        _coordinator = coordinator ??
                       throw new ArgumentNullException(nameof(coordinator));

        _selector = selector ??
                    throw new ArgumentNullException(nameof(selector));
    }

    /// <summary>
    /// Call after failed schema initialization while the application
    /// instance lock is held.
    ///
    /// Prepared means the restore is staged, not installed.
    /// </summary>
    public async Task<AutomaticRecoveryPreparationResult> TryPrepareAsync(
        Exception initializationFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initializationFailure);

        if (!ContainsCorruptionError(initializationFailure))
        {
            return new AutomaticRecoveryPreparationResult(
                AutomaticRecoveryPreparationStatus.NotApplicable);
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Never overwrite an unfinished restore operation.
            if (File.Exists(_paths.PendingRestoreManifestPath) ||
                Directory.Exists(_paths.PendingRestoreManifestPath))
            {
                return new AutomaticRecoveryPreparationResult(
                    AutomaticRecoveryPreparationStatus
                        .ExistingRecoveryOperation);
            }

            var backupPath = await Task.Run(
                () => _selector.FindLatestValid(cancellationToken),
                cancellationToken);

            if (backupPath is null)
            {
                return new AutomaticRecoveryPreparationResult(
                    AutomaticRecoveryPreparationStatus.NoValidBackup);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // The coordinator independently validates and stages
            // the backup while enforcing its application lock.
            await _coordinator.PrepareEmergencyRestoreAsync(
                backupPath);

            return new AutomaticRecoveryPreparationResult(
                AutomaticRecoveryPreparationStatus.Prepared);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ContainsCorruptionError(Exception error)
    {
        if (error is SqliteException sqliteError &&
            sqliteError.SqliteErrorCode is 11 or 26)
        {
            return true;
        }

        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (ContainsCorruptionError(inner))
                    return true;
            }

            return false;
        }

        return error.InnerException is not null &&
               ContainsCorruptionError(error.InnerException);
    }
}