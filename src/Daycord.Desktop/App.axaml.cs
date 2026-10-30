using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Desktop.Bootstrap;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Settings;
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
            _serviceProvider =
                ServiceRegistration.CreateServiceProvider();

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
                // Disposing the provider also releases the
                // application-wide database lock owned by
                // PendingRestoreCoordinator.

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

            var settings = provider
                .GetRequiredService<IAppSettingsService>();

            var localization = provider
                .GetRequiredService<ILocalizationService>();

            // DC-014:
            // Hold the application lock for the entire
            // application lifetime.
            //
            // Another Daycord process must not use the same
            // database while restoration or initialization
            // is taking place.

            coordinator.AcquireApplicationLock();

            // DC-014:
            // PendingRestore must be applied before opening
            // the SQLite database through normal services.

            restoreInstalled =
                await coordinator.ApplyPendingRestoreAsync();

            try
            {
                // Apply all pending schema migrations.
                //
                // For an older supported backup, this also
                // applies the new application settings migration.

                await initializer.InitializeAsync();

                // DC-015:
                // Load application settings from the initialized
                // SQLite database.
                //
                // Do not read settings before PendingRestore:
                // the restored database may contain a different
                // Language, Theme and MinutesPerDay.

                await settings.LoadAsync();
            }
            catch (Exception initializationError)
                when (restoreInstalled)
            {
                // A restored database must pass both schema
                // initialization and settings validation.
                //
                // If either fails, restore the verified
                // pre-restore safety backup.

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
                    // Finalize the restore only after:
                    //
                    // 1. Schema initialization succeeded.
                    // 2. Pending migrations were applied.
                    // 3. Application settings loaded successfully.
                    //
                    // The coordinator performs its own final
                    // validation of the installed database.

                    await coordinator.CompleteAfterInitializationAsync();
                }
                catch (Exception completionError)
                {
                    // Never publish an uncertain restore as ready.

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

            // DC-015:
            // Apply persisted preferences before marking the
            // application as ready.
            //
            // English is the default for new installations.
            // The OS locale does not choose the application language.
            //
            // System appearance continues to follow the OS.

            ApplyStartupPreferences(
                settings.Current,
                localization);

            // Reports, Tasks, Calendar and other data-dependent
            // features are activated only after the entire
            // initialization sequence has completed.

            viewModel.MarkDatabaseReady();
        }
        catch (Exception error)
        {
            // Preserve the existing database-error screen.
            // The normal application shell remains unavailable.

            viewModel.MarkDatabaseFailed(error);
        }
    }

    private void ApplyStartupPreferences(
        AppSettings settings,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localization);

        // Apply the requested appearance through Avalonia's
        // native ThemeVariant mechanism.
        //
        // ThemeVariant.Default means following the current
        // operating system appearance.

        RequestedThemeVariant = settings.Theme switch
        {
            "System" => ThemeVariant.Default,
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,

            _ => throw new InvalidOperationException(
                "Unsupported application theme.")
        };

        // Update the existing LocalizationService.
        //
        // Existing ViewModels receive PropertyChanged
        // notifications and can update localized properties
        // without recreating the MainWindow or DI container.

        localization.SetLanguage(settings.Language);
    }

    private static async Task RollbackOrThrowAsync(
        PendingRestoreCoordinator coordinator,
        Exception originalError)
    {
        try
        {
            await coordinator
                .RollbackAfterInitializationFailureAsync();
        }
        catch (Exception rollbackError)
        {
            // Do not hide either failure or allow normal startup.
            //
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