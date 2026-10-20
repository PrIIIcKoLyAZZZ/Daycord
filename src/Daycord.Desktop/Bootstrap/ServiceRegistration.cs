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

        // New Task sessions have short lifetimes. ActivatorUtilities
        // creates them using DI without retaining IDisposable instances
        // in the root container after the user leaves the page.
        services.AddSingleton<Func<TerminalViewModel>>(provider =>
            () => ActivatorUtilities.CreateInstance<TerminalViewModel>(
                provider));

        services.AddSingleton<MainWindowViewModel>();

        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}