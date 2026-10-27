using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Daycord.Desktop.Views;

/// <summary>
/// Read-only monthly calendar view.
/// Month selection, loading and navigation
/// remain in CalendarPageViewModel.
/// </summary>
public partial class CalendarPageView : UserControl
{
    public CalendarPageView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}