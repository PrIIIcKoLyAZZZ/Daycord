using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Daycord.Core.Application.Contracts;
using Daycord.Desktop.Bootstrap;
using Daycord.Desktop.ViewModels;
using Daycord.Desktop.Views;
using Daycord.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Daycord.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private bool _databaseInitializationStarted;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = ServiceRegistration.CreateServiceProvider();

            var viewModel = _serviceProvider
                .GetRequiredService<MainWindowViewModel>();

            var window = _serviceProvider
                .GetRequiredService<MainWindow>();

            window.Opened += async (_, _) =>
            {
                if (_databaseInitializationStarted)
                {
                    return;
                }

                _databaseInitializationStarted = true;

                await InitializeDatabaseAsync(viewModel);
            };

            desktop.MainWindow = window;

            desktop.Exit += (_, _) =>
            {
                // Disposing the provider also releases the application-wide
                // database lock owned by PendingRestoreCoordinator.
                _serviceProvider?.Dispose();
                _serviceProvider = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeDatabaseAsync(
        MainWindowViewModel viewModel)
    {
        var restoreInstalled = false;

        try
        {
            var provider = _serviceProvider
                ?? throw new InvalidOperationException(
                    "Daycord services are unavailable.");

            var coordinator = provider
                .GetRequiredService<PendingRestoreCoordinator>();

            var initializer = provider
                .GetRequiredService<IDatabaseInitializer>();

            // The lock is held for the entire application lifetime.
            // Another DC-014-aware process must not use the same database.
            coordinator.AcquireApplicationLock();

            // This must happen before DatabaseInitializer, before
            // migration checks, and before any feature loads user data.
            restoreInstalled =
                await coordinator.ApplyPendingRestoreAsync();

            try
            {
                await initializer.InitializeAsync();
            }
            catch (Exception initializationError)
                when (restoreInstalled)
            {
                // If the restored database fails initialization, recover
                // the verified pre-restore safety backup.
                await RollbackOrThrowAsync(
                    coordinator,
                    initializationError);

                throw new InvalidOperationException(
                    "The restored database did not pass initialization. " +
                    "The previous database has been restored. " +
                    "Restart Daycord to continue.",
                    initializationError);
            }

            if (restoreInstalled)
            {
                try
                {
                    // Only the regular database initializer can confirm
                    // that the installed backup is usable by Daycord.
                    await coordinator.CompleteAfterInitializationAsync();
                }
                catch (Exception completionError)
                {
                    // Never publish the restored database as ready while
                    // its completion journal is in an uncertain state.
                    await RollbackOrThrowAsync(
                        coordinator,
                        completionError);

                    throw new InvalidOperationException(
                        "The database restore could not be finalized. " +
                        "Recovery has been attempted and startup was blocked. " +
                        "Restart Daycord after checking the safety backup.",
                        completionError);
                }
            }

            // Reports, Tasks, Calendar and other data-dependent
            // features are activated only after the entire sequence
            // has completed successfully.
            viewModel.MarkDatabaseReady();
        }
        catch (Exception error)
        {
            // This preserves the existing database-error screen.
            // The normal application shell remains unavailable.
            viewModel.MarkDatabaseFailed(error);
        }
    }

    private static async Task RollbackOrThrowAsync(
        PendingRestoreCoordinator coordinator,
        Exception originalError)
    {
        try
        {
            await coordinator.RollbackAfterInitializationFailureAsync();
        }
        catch (Exception rollbackError)
        {
            // Do not hide either failure or allow normal startup.
            // The safety backup and restore journal are retained
            // by the coordinator when rollback cannot complete.
            throw new AggregateException(
                "Database restoration and automatic rollback failed. " +
                "Daycord startup was blocked to protect user data. " +
                "Do not replace or delete the existing database files.",
                originalError,
                rollbackError);
        }
    }
}