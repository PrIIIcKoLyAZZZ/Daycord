using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Daycord.Desktop.Views;

public partial class PlaceholderPageView : UserControl
{
    public PlaceholderPageView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}