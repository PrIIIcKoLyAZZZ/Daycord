using Avalonia.Controls;
using Avalonia.Platform;
using Daycord.Desktop.ViewModels;

namespace Daycord.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Select the appropriate Avalonia window icon.
        // macOS application icon itself comes from the .app bundle.
        var iconFile = OperatingSystem.IsWindows()
            ? "icon.ico"
            : "icon.png";

        var iconUri = new Uri(
            $"avares://Daycord.Desktop/Assets/{iconFile}");

        Icon = new WindowIcon(AssetLoader.Open(iconUri));
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}