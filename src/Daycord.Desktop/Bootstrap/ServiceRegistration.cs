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
    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Database

        services.AddSingleton<AppDataPaths>();

        services.AddSingleton<SqliteConnectionFactory>();

        services.AddSingleton<DatabaseBackupService>();

        services.AddSingleton<MigrationRunner>();

        services.AddSingleton<
            IDatabaseInitializer,
            DatabaseInitializer>();

        // Backup & Restore — DC-014

        services.AddSingleton<
            IUserBackupService,
            UserBackupService>();

        services.AddSingleton<PendingRestoreCoordinator>();

        // Application Settings — DC-015

        services.AddSingleton<
            IAppSettingsRepository,
            SqliteAppSettingsRepository>();

        services.AddSingleton<
            IAppSettingsService,
            AppSettingsService>();

        // Task data

        services.AddTransient<
            ITaskRepository,
            SqliteTaskRepository>();

        services.AddTransient<TaskService>();

        // Desktop services

        services.AddSingleton<
            INavigationService,
            NavigationService>();

        services.AddSingleton<
            ILocalizationService,
            LocalizationService>();

        // Reports

        services.AddTransient<
            IWorkReportPresetRepository,
            SqliteWorkReportPresetRepository>();

        services.AddSingleton<WorkReportPresetService>();

        services.AddSingleton<WorkReportService>();

        services.AddSingleton<IWorkReportExporter>(provider =>
        {
            var localization = provider
                .GetRequiredService<ILocalizationService>();

            return new ClosedXmlWorkReportExporter(
                () => localization.CurrentLanguage);
        });

        services.AddSingleton<ReportsViewModel>();

        // Backup & Restore.
        // Keep lazy initialization after PendingRestore.

        services.AddSingleton<BackupSettingsViewModel>();

        services.AddSingleton<Func<BackupSettingsViewModel>>(
            provider =>
                () => provider.GetRequiredService<
                    BackupSettingsViewModel>());

        // Settings.
        // The SettingsViewModel requires loaded AppSettings,
        // so it must not be constructed during initial DI setup.

        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<Func<SettingsViewModel>>(
            provider =>
                () => provider.GetRequiredService<
                    SettingsViewModel>());

        // Existing views and ViewModels

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