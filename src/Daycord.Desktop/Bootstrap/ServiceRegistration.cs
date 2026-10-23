using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;
using Daycord.Desktop.ViewModels;
using Daycord.Desktop.Views;
using Daycord.Infrastructure.Persistence;
using Daycord.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Daycord.Desktop.Bootstrap;

public static class ServiceRegistration
{
    public static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<AppDataPaths>();

        services.AddSingleton<SqliteConnectionFactory>();

        services.AddSingleton<DatabaseBackupService>();

        services.AddSingleton<MigrationRunner>();

        services.AddSingleton<
            IDatabaseInitializer,
            DatabaseInitializer>();

        services.AddTransient<
            ITaskRepository,
            SqliteTaskRepository>();

        services.AddTransient<TaskService>();

        services.AddSingleton<
            INavigationService,
            NavigationService>();

        services.AddSingleton<
            ILocalizationService,
            LocalizationService>();

        services.AddSingleton<PlaceholderPageViewModel>();

        // The Task List is a singleton so that filters,
        // sorting and selection survive navigation.
        services.AddSingleton<TaskListViewModel>(provider =>
        {
            var taskList = ActivatorUtilities
                .CreateInstance<TaskListViewModel>(provider);

            var navigation = provider
                .GetRequiredService<INavigationService>();

            // Open an existing task by its ID.
            taskList.TaskOpenRequested += navigation.OpenTask;

            // Request the full creation editor.
            // No database record is created here.
            taskList.TaskCreationRequested += navigation.OpenNewTask;

            return taskList;
        });

        // Each Quick Task session is created separately.
        // MainWindowViewModel controls its lifetime.
        services.AddSingleton<Func<TerminalViewModel>>(provider =>
            () => ActivatorUtilities.CreateInstance<TerminalViewModel>(
                provider));

        // Each full task editor receives a fresh ViewModel.
        // It is disposed when the editor is closed.
        services.AddSingleton<Func<TaskDetailsViewModel>>(provider =>
            () => ActivatorUtilities.CreateInstance<TaskDetailsViewModel>(
                provider));

        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}