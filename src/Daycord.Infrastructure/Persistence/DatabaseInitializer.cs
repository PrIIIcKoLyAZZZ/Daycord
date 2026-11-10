using System.Diagnostics;
using Daycord.Core.Application.Contracts;

namespace Daycord.Infrastructure.Persistence;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly AppDataPaths _paths;
    private readonly MigrationRunner _migrations;
    private readonly AutomaticBackupService? _automaticBackups;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    // The optional argument preserves direct construction in the recovery
    // integration checks. Runtime DI supplies the shared singleton instance.
    public DatabaseInitializer(
        AppDataPaths paths,
        MigrationRunner migrations,
        AutomaticBackupService? automaticBackups = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
        _automaticBackups = automaticBackups;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken);

        try
        {
            _paths.EnsureDataDirectory();
            var databaseExisted = File.Exists(_paths.DatabasePath);

            await _migrations.RunAsync(databaseExisted, cancellationToken);

            // A provisional restore must not become an automatic backup.
            if (File.Exists(_paths.PendingRestoreManifestPath) ||
                Directory.Exists(_paths.PendingRestoreManifestPath))
            {
                return;
            }

            try
            {
                if (_automaticBackups is not null)
                {
                    await _automaticBackups.EnsureRecentVerifiedBackupAsync(
                        cancellationToken);
                }
                else
                {
                    // Directly constructed initializers are used by tests;
                    // no second long-lived service is created in the app.
                    using var localBackups = new AutomaticBackupService(_paths);
                    await localBackups.EnsureRecentVerifiedBackupAsync(
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                // An automatic backup failure must not turn a healthy
                // SQLite database into an application startup failure.
                Trace.TraceWarning(
                    "Automatic database backup failed: {0}",
                    error.GetType().Name);
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}