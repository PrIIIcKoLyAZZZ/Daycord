using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;
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

        // Task data

        services.AddTransient<
            ITaskRepository,
            SqliteTaskRepository>();

        services.AddTransient<TaskService>();

        // Shared desktop services

        services.AddSingleton<
            INavigationService,
            NavigationService>();

        services.AddSingleton<
            ILocalizationService,
            LocalizationService>();

        // Legacy monthly export

        services.AddSingleton<MonthlyWorkReportService>();

        services.AddSingleton<
            IMonthlyReportExporter,
            ClosedXmlMonthlyReportExporter>();

        // Configurable reports

        services.AddTransient<
            IWorkReportPresetRepository,
            SqliteWorkReportPresetRepository>();

        services.AddSingleton<WorkReportPresetService>();

        services.AddSingleton<WorkReportService>();

        // The localization delegate is evaluated on EVERY export.
        // Therefore changing the Daycord language does not
        // require restarting or recreating ReportsViewModel.

        services.AddSingleton<IWorkReportExporter>(provider =>
        {
            var localization = provider
                .GetRequiredService<ILocalizationService>();

            return new ClosedXmlWorkReportExporter(
                () => localization.CurrentLanguage);
        });

        services.AddSingleton<ReportsViewModel>();

        // Views and ViewModels

        services.AddSingleton<PlaceholderPageViewModel>();

        // Preserve selected month and date during navigation.
        services.AddSingleton<CalendarPageViewModel>();

        services.AddSingleton<HomeDashboardViewModel>();

        // Preserve task filters, sorting and selection.
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

        // Separate Quick Task sessions.
        services.AddSingleton<Func<TerminalViewModel>>(provider =>
            () => ActivatorUtilities
                .CreateInstance<TerminalViewModel>(provider));

        // Fresh full task editor for each opening.
        services.AddSingleton<Func<TaskDetailsViewModel>>(provider =>
            () => ActivatorUtilities
                .CreateInstance<TaskDetailsViewModel>(provider));

        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}