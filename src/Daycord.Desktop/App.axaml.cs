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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var provider = ServiceRegistration.CreateServiceProvider();
            _serviceProvider = provider;
            var viewModel = provider.GetRequiredService<MainWindowViewModel>();
            var window = provider.GetRequiredService<MainWindow>();

            // The window displays an initializing state, not functional data screens.
            window.Opened += async (_, _) =>
            {
                try
                {
                    var initializer = provider.GetRequiredService<IDatabaseInitializer>();
                    await initializer.InitializeAsync();
                    viewModel.MarkDatabaseReady();
                }
                catch (Exception error)
                {
                    viewModel.MarkDatabaseFailed(error);
                }
            };

            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}