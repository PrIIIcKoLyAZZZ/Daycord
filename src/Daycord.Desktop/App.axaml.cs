using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Desktop.Bootstrap;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;
using Daycord.Desktop.Settings;
using Daycord.Desktop.ViewModels;
using Daycord.Desktop.Views;
using Daycord.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Daycord.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private AutomaticBackupScheduler? _backupScheduler;

    private bool _databaseInitializationStarted;
    private bool _savingBackupOnClose;
    private bool _allowCloseAfterBackup;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var bootstrapWindow = new DatabaseRecoveryWindow();
            desktop.MainWindow = bootstrapWindow;

            desktop.Exit += (_, _) =>
            {
                _backupScheduler?.Dispose();
                _backupScheduler = null;

                _serviceProvider?.Dispose();
                _serviceProvider = null;
            };

            bootstrapWindow.Opened += async (_, _) =>
            {
                if (_databaseInitializationStarted)
                    return;

                _databaseInitializationStarted = true;

                await InitializeAndOpenMainWindowAsync(
                    desktop,
                    bootstrapWindow);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAndOpenMainWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        DatabaseRecoveryWindow bootstrapWindow)
    {
        var restoreInstalled = false;
        var automaticRecoveryPerformed = false;
        var retryingInterruptedEmergency = false;

        var stage = DatabaseRecoveryStage.Bootstrap;

        DatabaseRecoveryDiagnostics? diagnostics = null;

        try
        {
            var provider =
                ServiceRegistration.CreateServiceProvider();

            _serviceProvider = provider;

            diagnostics = provider
                .GetRequiredService<DatabaseRecoveryDiagnostics>();

            var coordinator = provider
                .GetRequiredService<PendingRestoreCoordinator>();

            var initializer = provider
                .GetRequiredService<IDatabaseInitializer>();

            var settings = provider
                .GetRequiredService<IAppSettingsService>();

            var localization = provider
                .GetRequiredService<ILocalizationService>();

            var paths = provider
                .GetRequiredService<AppDataPaths>();

            stage = DatabaseRecoveryStage.AcquireApplicationLock;

            coordinator.AcquireApplicationLock();

            stage = DatabaseRecoveryStage.ApplyPendingRestore;

            // A previous emergency restore may have failed before
            // capturing the raw original, e.g. due to a locked WAL.
            //
            // Only a provably pre-installation failure may be rearmed.
            // No journal is deleted or replaced with a new operation.
            var retryPreparation =
                new EmergencyRestoreRetryPreparation(
                    paths,
                    coordinator);

            retryingInterruptedEmergency =
                await retryPreparation.TryRearmAsync();

            // Resume an existing restore before opening repositories.
            restoreInstalled =
                await coordinator.ApplyPendingRestoreAsync();

            try
            {
                stage = DatabaseRecoveryStage.InitializeSchema;
                await initializer.InitializeAsync();

                stage = DatabaseRecoveryStage.LoadSettings;
                await settings.LoadAsync();
            }
            catch (Exception initializationError)
            {
                if (restoreInstalled)
                {
                    var failedStage = stage;

                    stage = coordinator.IsEmergencyRestoreInstalled
                        ? DatabaseRecoveryStage.CompleteRestore
                        : DatabaseRecoveryStage.RollbackRestore;

                    await RollbackOrThrowAsync(
                        coordinator,
                        initializationError,
                        diagnostics);

                    stage = failedStage;

                    throw new InvalidOperationException(
                        coordinator.IsEmergencyRestoreInstalled
                            ? "Emergency recovery did not pass database initialization."
                            : "The restored database failed initialization.",
                        initializationError);
                }

                if (stage != DatabaseRecoveryStage.InitializeSchema)
                    throw;

                // Only SQLITE_CORRUPT and SQLITE_NOTADB are eligible
                // for automatic emergency restoration.
                var selector =
                    new AutomaticRecoveryCandidateSelector(paths);

                var preparation =
                    new AutomaticRecoveryPreparation(
                        paths,
                        coordinator,
                        selector);

                var result =
                    await preparation.TryPrepareAsync(
                        initializationError);

                if (result.Status !=
                    AutomaticRecoveryPreparationStatus.Prepared)
                {
                    throw;
                }

                stage = DatabaseRecoveryStage.ApplyPendingRestore;

                restoreInstalled =
                    await coordinator.ApplyPendingRestoreAsync();

                if (!restoreInstalled)
                {
                    throw new InvalidOperationException(
                        "Automatic recovery was not installed.");
                }

                try
                {
                    stage = DatabaseRecoveryStage.InitializeSchema;

                    await initializer.InitializeAsync();

                    stage = DatabaseRecoveryStage.LoadSettings;

                    await settings.LoadAsync();
                }
                catch (Exception recoveredInitializationError)
                {
                    stage = coordinator.IsEmergencyRestoreInstalled
                        ? DatabaseRecoveryStage.CompleteRestore
                        : DatabaseRecoveryStage.RollbackRestore;

                    await RollbackOrThrowAsync(
                        coordinator,
                        recoveredInitializationError,
                        diagnostics);

                    throw new InvalidOperationException(
                        "Automatic recovery did not pass initialization.",
                        recoveredInitializationError);
                }

                automaticRecoveryPerformed = true;
            }

            if (restoreInstalled)
            {
                try
                {
                    stage = DatabaseRecoveryStage.CompleteRestore;

                    await coordinator
                        .CompleteAfterInitializationAsync();
                }
                catch (Exception completionError)
                {
                    stage = coordinator.IsEmergencyRestoreInstalled
                        ? DatabaseRecoveryStage.CompleteRestore
                        : DatabaseRecoveryStage.RollbackRestore;

                    await RollbackOrThrowAsync(
                        coordinator,
                        completionError,
                        diagnostics);

                    throw new InvalidOperationException(
                        "The database restore could not be finalized.",
                        completionError);
                }

                // The initializer skips automatic backups while a
                // pending-restore journal exists. It is completed now.
                try
                {
                    await provider
                        .GetRequiredService<AutomaticBackupService>()
                        .EnsureRecentVerifiedBackupAsync();
                }
                catch (Exception backupError)
                {
                    Trace.TraceWarning(
                        "Post-restore automatic backup failed: {0}",
                        backupError.GetType().Name);
                }
            }

            stage = DatabaseRecoveryStage.ApplyPreferences;

            ApplyStartupPreferences(
                settings.Current,
                localization);

            stage = DatabaseRecoveryStage.Bootstrap;

            if (automaticRecoveryPerformed ||
                (retryingInterruptedEmergency && restoreInstalled))
            {
                // A successful restore has now passed:
                // 1. Snapshot installation
                // 2. Schema initialization
                // 3. Settings initialization
                // 4. Restore journal completion
                //
                // Only here can we show the success message.
                var completedWindow = new RecoveryCompletedWindow(
                    localization.CurrentLanguage);

                completedWindow.Show();
                desktop.MainWindow = completedWindow;

                bootstrapWindow.CloseAfterSuccessfulStartup();
                return;
            }

            var viewModel = provider
                .GetRequiredService<MainWindowViewModel>();

            viewModel.MarkDatabaseReady();

            var mainWindow = provider
                .GetRequiredService<MainWindow>();

            var scheduler = new AutomaticBackupScheduler(
                provider.GetRequiredService<INavigationService>(),
                provider.GetRequiredService<AutomaticBackupService>(),
                provider.GetRequiredService<DatabaseChangeTracker>());

            // MainWindow already handles saving unfinished report
            // changes on Closing. The final backup follows that save.
            mainWindow.Closing += async (_, e) =>
            {
                if (e.Cancel || _allowCloseAfterBackup)
                    return;

                e.Cancel = true;

                if (_savingBackupOnClose)
                    return;

                _savingBackupOnClose = true;

                try
                {
                    await scheduler.StopAndFlushAsync();
                }
                catch (Exception backupError)
                {
                    Trace.TraceWarning(
                        "Final automatic backup failed: {0}",
                        backupError.GetType().Name);
                }
                finally
                {
                    _savingBackupOnClose = false;
                    _allowCloseAfterBackup = true;

                    mainWindow.Close();
                }
            };

            scheduler.Start();
            _backupScheduler = scheduler;

            mainWindow.Show();
            desktop.MainWindow = mainWindow;

            bootstrapWindow.CloseAfterSuccessfulStartup();
        }
        catch (Exception error)
        {
            diagnostics?.TryRecordFailure(stage, error);
            bootstrapWindow.ShowFailure();
        }
    }

    private void ApplyStartupPreferences(
        AppSettings settings,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localization);

        RequestedThemeVariant = settings.Theme switch
        {
            "System" => ThemeVariant.Default,
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => throw new InvalidOperationException(
                "Unsupported application theme.")
        };

        localization.SetLanguage(settings.Language);
    }

    private static async Task RollbackOrThrowAsync(
        PendingRestoreCoordinator coordinator,
        Exception originalError,
        DatabaseRecoveryDiagnostics? diagnostics)
    {
        try
        {
            await coordinator
                .RollbackAfterInitializationFailureAsync();
        }
        catch (Exception rollbackError)
        {
            diagnostics?.TryRecordFailure(
                coordinator.IsEmergencyRestoreInstalled
                    ? DatabaseRecoveryStage.CompleteRestore
                    : DatabaseRecoveryStage.RollbackRestore,
                rollbackError);

            throw new AggregateException(
                "Database recovery could not complete.",
                originalError,
                rollbackError);
        }
    }
}