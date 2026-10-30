using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public sealed class PlaceholderPageViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    private PageKind _page = PageKind.Home;

    public PlaceholderPageViewModel(
        ILocalizationService localization)
    {
        _localization = localization
                        ?? throw new ArgumentNullException(
                            nameof(localization));
    }

    public string PageTitle =>
        _localization.GetString(GetPageResourceKey(_page));

    public string PlaceholderHeading =>
        _localization.GetString("PlaceholderHeading");

    public string PlaceholderDescription =>
        _localization.GetString("PlaceholderDescription");

    public void SetPage(PageKind page)
    {
        if (!Enum.IsDefined(page))
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "Unsupported placeholder page.");
        }

        if (_page == page)
        {
            return;
        }

        _page = page;

        OnPropertyChanged(nameof(PageTitle));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(PageTitle));

        OnPropertyChanged(nameof(PlaceholderHeading));

        OnPropertyChanged(nameof(PlaceholderDescription));
    }

    private static string GetPageResourceKey(PageKind page)
    {
        return page switch
        {
            PageKind.Home => "NavHome",

            PageKind.Calendar => "NavCalendar",

            PageKind.Tasks => "NavTasks",

            PageKind.NewTask => "NavNewTask",

            PageKind.Reports => "NavReports",

            PageKind.Settings => "NavSettings",

            _ => throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "Unsupported placeholder page.")
        };
    }
}