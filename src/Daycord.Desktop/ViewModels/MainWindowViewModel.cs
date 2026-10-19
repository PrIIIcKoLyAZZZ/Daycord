using CommunityToolkit.Mvvm.ComponentModel;

namespace Daycord.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public string WelcomeMessage { get; } =
        "Добро пожаловать в Daycord";
}
