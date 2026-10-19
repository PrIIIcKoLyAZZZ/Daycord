using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Daycord.Desktop.Bootstrap;
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
            _serviceProvider = ServiceRegistration.CreateServiceProvider();

            desktop.MainWindow =
                _serviceProvider.GetRequiredService<MainWindow>();

            desktop.Exit += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
