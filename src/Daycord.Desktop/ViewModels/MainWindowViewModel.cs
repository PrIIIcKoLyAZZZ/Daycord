using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public partial class MainWindowViewModel :
    ObservableObject,
    IDisposable
{
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly PlaceholderPageViewModel _placeholderPage;
    private readonly Func<TerminalViewModel> _createTerminalPage;
    private TerminalViewModel? _terminalPage;

    private string? _databaseErrorDetails;
    private bool _disposed;

    [ObservableProperty]
    private bool _isDatabaseReady;

    [ObservableProperty]
    private bool _isDatabaseFailed;

    public MainWindowViewModel(
        INavigationService navigation,
        ILocalizationService localization,
        PlaceholderPageViewModel placeholderPage,
        Func<TerminalViewModel> createTerminalPage)
    {
        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _placeholderPage = placeholderPage
            ?? throw new ArgumentNullException(nameof(placeholderPage));

        _createTerminalPage = createTerminalPage
            ?? throw new ArgumentNullException(nameof(createTerminalPage));

        _placeholderPage.SetPage(_navigation.CurrentPage);

        if (_navigation.CurrentPage == PageKind.NewTask)
        {
            _terminalPage = _createTerminalPage();
        }

        _navigation.PropertyChanged += OnNavigationChanged;
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    // Application

    public string AppName =>
        _localization.GetString("AppName");

    public string AppWordmark =>
        _localization.GetString("AppWordmark");

    public string WelcomeMessage =>
        _localization.GetString("WelcomeMessage");

    public string NavigationHeading =>
        _localization.GetString("NavigationHeading");

    // Navigation labels

    public string NavHomeLabel =>
        _localization.GetString("NavHome");

    public string NavCalendarLabel =>
        _localization.GetString("NavCalendar");

    public string NavTasksLabel =>
        _localization.GetString("NavTasks");

    public string NavNewTaskLabel =>
        _localization.GetString("NavNewTask");

    public string NavReportsLabel =>
        _localization.GetString("NavReports");

    public string NavSettingsLabel =>
        _localization.GetString("NavSettings");

    // Current page

    public PageKind CurrentPage => _navigation.CurrentPage;

    public string CurrentPageTitle =>
        _placeholderPage.PageTitle;

    public object CurrentPageContent =>
        _terminalPage ?? (object)_placeholderPage;

    // Active navigation state

    public bool IsHomeSelected =>
        CurrentPage == PageKind.Home;

    public bool IsCalendarSelected =>
        CurrentPage == PageKind.Calendar;

    public bool IsTasksSelected =>
        CurrentPage == PageKind.Tasks;

    public bool IsNewTaskSelected =>
        CurrentPage == PageKind.NewTask;

    public bool IsReportsSelected =>
        CurrentPage == PageKind.Reports;

    public bool IsSettingsSelected =>
        CurrentPage == PageKind.Settings;

    // Database initialization

    public bool IsDatabaseInitializing =>
        !IsDatabaseReady && !IsDatabaseFailed;

    public string DatabaseStatusMessage
    {
        get
        {
            if (IsDatabaseReady)
            {
                return _localization.GetString("DatabaseReady");
            }

            if (IsDatabaseFailed)
            {
                string errorMessage = string.Format(
                    _localization.GetString(
                        "DatabaseInitializationFailed"),
                    _databaseErrorDetails ?? string.Empty);

                return errorMessage
                    + Environment.NewLine
                    + Environment.NewLine
                    + _localization.GetString(
                        "DatabaseUnavailable");
            }

            return _localization.GetString(
                "DatabaseInitializing");
        }
    }

    public void MarkDatabaseReady()
    {
        _databaseErrorDetails = null;

        IsDatabaseFailed = false;
        IsDatabaseReady = true;

        OnPropertyChanged(nameof(DatabaseStatusMessage));
    }

    public void MarkDatabaseFailed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _databaseErrorDetails = error.Message;

        IsDatabaseReady = false;
        IsDatabaseFailed = true;

        OnPropertyChanged(nameof(DatabaseStatusMessage));
    }

    partial void OnIsDatabaseReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDatabaseInitializing));
        OnPropertyChanged(nameof(DatabaseStatusMessage));
    }

    partial void OnIsDatabaseFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDatabaseInitializing));
        OnPropertyChanged(nameof(DatabaseStatusMessage));
    }

    // Navigation commands

    [RelayCommand]
    private void NavigateHome()
    {
        NavigateTo(PageKind.Home);
    }

    [RelayCommand]
    private void NavigateCalendar()
    {
        NavigateTo(PageKind.Calendar);
    }

    [RelayCommand]
    private void NavigateTasks()
    {
        NavigateTo(PageKind.Tasks);
    }

    [RelayCommand]
    private void NavigateNewTask()
    {
        NavigateTo(PageKind.NewTask);
    }

    [RelayCommand]
    private void NavigateReports()
    {
        NavigateTo(PageKind.Reports);
    }

    [RelayCommand]
    private void NavigateSettings()
    {
        NavigateTo(PageKind.Settings);
    }

    private void NavigateTo(PageKind page)
    {
        if (!IsDatabaseReady || _disposed)
        {
            return;
        }

        _navigation.NavigateTo(page);
    }

    // Navigation notifications

    private void OnNavigationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)
            && e.PropertyName !=
                nameof(INavigationService.CurrentPage))
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        var page = _navigation.CurrentPage;
        _placeholderPage.SetPage(page);

        if (page == PageKind.NewTask)
        {
            // Repeated selection does not create a new session.
            _terminalPage ??= _createTerminalPage();
        }
        else if (_terminalPage is { } previousSession)
        {
            _terminalPage = null;
            previousSession.Dispose();
        }

        OnPropertyChanged(nameof(CurrentPageContent));
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(CurrentPageTitle));

        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(IsCalendarSelected));
        OnPropertyChanged(nameof(IsTasksSelected));
        OnPropertyChanged(nameof(IsNewTaskSelected));
        OnPropertyChanged(nameof(IsReportsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    // Localization notifications

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName)
            && e.PropertyName !=
                nameof(ILocalizationService.CurrentLanguage))
        {
            return;
        }

        OnPropertyChanged(nameof(AppName));
        OnPropertyChanged(nameof(AppWordmark));
        OnPropertyChanged(nameof(WelcomeMessage));
        OnPropertyChanged(nameof(NavigationHeading));

        OnPropertyChanged(nameof(NavHomeLabel));
        OnPropertyChanged(nameof(NavCalendarLabel));
        OnPropertyChanged(nameof(NavTasksLabel));
        OnPropertyChanged(nameof(NavNewTaskLabel));
        OnPropertyChanged(nameof(NavReportsLabel));
        OnPropertyChanged(nameof(NavSettingsLabel));

        _placeholderPage.RefreshLocalization();

        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(DatabaseStatusMessage));
    }

    // Cleanup

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _navigation.PropertyChanged -= OnNavigationChanged;
        _localization.PropertyChanged -= OnLocalizationChanged;
        _terminalPage?.Dispose();
        _terminalPage = null;
    }
}