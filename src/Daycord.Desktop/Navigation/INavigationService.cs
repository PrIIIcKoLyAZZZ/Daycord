using System.ComponentModel;

namespace Daycord.Desktop.Navigation;

public interface INavigationService : INotifyPropertyChanged
{
    PageKind CurrentPage { get; }

    void NavigateTo(PageKind page);
}