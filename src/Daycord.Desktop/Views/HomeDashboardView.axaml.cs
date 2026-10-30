using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Daycord.Desktop.Views;

/// <summary>Read-only Home dashboard; interactions live in its ViewModel.</summary>
public partial class HomeDashboardView : UserControl
{
    public HomeDashboardView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}