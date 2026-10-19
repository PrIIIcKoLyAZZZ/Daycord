using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Daycord.Core.Application.Contracts;
using Daycord.Desktop.Bootstrap;
using Daycord.Desktop.ViewModels;
using Daycord.Desktop.Views;
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

            var viewModel =
                _serviceProvider.GetRequiredService<MainWindowViewModel>();

            var window =
                _serviceProvider.GetRequiredService<MainWindow>();

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
                _serviceProvider?.Dispose();
                _serviceProvider = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeDatabaseAsync(
        MainWindowViewModel viewModel)
    {
        try
        {
            var initializer = _serviceProvider!
                .GetRequiredService<IDatabaseInitializer>();

            await initializer.InitializeAsync();

            viewModel.MarkDatabaseReady();
        }
        catch (Exception error)
        {
            viewModel.MarkDatabaseFailed(error);
        }
    }
}