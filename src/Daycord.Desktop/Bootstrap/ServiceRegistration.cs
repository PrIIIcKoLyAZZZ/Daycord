using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;
using Daycord.Desktop.Settings;
using Daycord.Desktop.ViewModels;
using Daycord.Desktop.Views;
using Daycord.Infrastructure.Persistence;
using Daycord.Infrastructure.Persistence.Repositories;
using Daycord.Infrastructure.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Daycord.Desktop.Bootstrap;

public static class ServiceRegistration
{
    public static AppDataPaths ResolveDataPaths()
    {
#if DEBUG
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new IOException(
                "The local application data folder is unavailable.");
        }

        return new AppDataPaths(
            Path.Combine(localApplicationData, "Daycord-test"));
#else
        return new AppDataPaths();
#endif
    }

    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Established Debug / Release profile isolation.
        services.AddSingleton<AppDataPaths>(_ => ResolveDataPaths());

        // Shared database change tracking and automatic backup service.
        services.AddSingleton<DatabaseChangeTracker>();
        services.AddSingleton<AutomaticBackupService>();

        // SQLite infrastructure.
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<DatabaseBackupService>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<DatabaseRecoveryDiagnostics>();

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IUserBackupService, UserBackupService>();
        services.AddSingleton<PendingRestoreCoordinator>();

        // Application settings.
        services.AddSingleton<SqliteAppSettingsRepository>();

        services.AddSingleton<IAppSettingsRepository>(provider =>
            new TrackedAppSettingsRepository(
                provider.GetRequiredService<SqliteAppSettingsRepository>(),
                provider.GetRequiredService<DatabaseChangeTracker>()));

        services.AddSingleton<IAppSettingsService, AppSettingsService>();

        // Tasks, planning periods and actual time.
        services.AddTransient<SqliteTaskRepository>();

        services.AddTransient<ITaskRepository>(provider =>
            new TrackedTaskRepository(
                provider.GetRequiredService<SqliteTaskRepository>(),
                provider.GetRequiredService<DatabaseChangeTracker>()));

        services.AddTransient<TaskService>();

        // Desktop services.
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Report presets.
        services.AddTransient<SqliteWorkReportPresetRepository>();

        services.AddTransient<IWorkReportPresetRepository>(provider =>
            new TrackedWorkReportPresetRepository(
                provider.GetRequiredService<SqliteWorkReportPresetRepository>(),
                provider.GetRequiredService<DatabaseChangeTracker>()));

        services.AddSingleton<WorkReportPresetService>();

        services.AddSingleton<IWorkReportExporter>(provider =>
        {
            var localization = provider
                .GetRequiredService<ILocalizationService>();

            return new ClosedXmlWorkReportExporter(
                () => localization.CurrentLanguage);
        });

        services.AddSingleton<WorkReportService>();
        services.AddSingleton<ReportsViewModel>();

        // Lazy settings ViewModels.
        services.AddSingleton<BackupSettingsViewModel>();

        services.AddSingleton<Func<BackupSettingsViewModel>>(provider =>
            () => provider.GetRequiredService<BackupSettingsViewModel>());

        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<Func<SettingsViewModel>>(provider =>
            () => provider.GetRequiredService<SettingsViewModel>());

        // Existing application views and ViewModels.
        services.AddSingleton<PlaceholderPageViewModel>();
        services.AddSingleton<CalendarPageViewModel>();
        services.AddSingleton<HomeDashboardViewModel>();

        services.AddSingleton<TaskListViewModel>(provider =>
        {
            var taskList = ActivatorUtilities
                .CreateInstance<TaskListViewModel>(provider);

            var navigation = provider
                .GetRequiredService<INavigationService>();

            taskList.TaskOpenRequested += navigation.OpenTask;
            taskList.TaskCreationRequested += navigation.OpenNewTask;
            taskList.AttachNavigation(navigation);

            return taskList;
        });

        services.AddSingleton<Func<TerminalViewModel>>(provider =>
            () => ActivatorUtilities
                .CreateInstance<TerminalViewModel>(provider));

        services.AddSingleton<Func<TaskDetailsViewModel>>(provider =>
            () => ActivatorUtilities
                .CreateInstance<TaskDetailsViewModel>(provider));

        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}