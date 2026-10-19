using CommunityToolkit.Mvvm.ComponentModel;

namespace Daycord.Desktop.Navigation;

public sealed class NavigationService :
    ObservableObject,
    INavigationService
{
    private PageKind _currentPage = PageKind.Home;

    public PageKind CurrentPage
    {
        get => _currentPage;

        private set => SetProperty(
            ref _currentPage,
            value);
    }

    public void NavigateTo(PageKind page)
    {
        if (!Enum.IsDefined(page))
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "Unsupported navigation page.");
        }

        if (CurrentPage == page)
        {
            return;
        }

        CurrentPage = page;
    }
}