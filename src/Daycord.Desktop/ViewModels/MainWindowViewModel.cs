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
    private readonly TaskListViewModel _taskListPage;
    private readonly Func<TerminalViewModel> _createTerminalPage;
    private readonly Func<TaskDetailsViewModel> _createTaskDetailsPage;

    private TerminalViewModel? _terminalPage;
    private TaskDetailsViewModel? _taskDetailsPage;

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
        TaskListViewModel taskListPage,
        Func<TerminalViewModel> createTerminalPage,
        Func<TaskDetailsViewModel> createTaskDetailsPage)
    {
        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _placeholderPage = placeholderPage
            ?? throw new ArgumentNullException(nameof(placeholderPage));

        _taskListPage = taskListPage
            ?? throw new ArgumentNullException(nameof(taskListPage));

        _createTerminalPage = createTerminalPage
            ?? throw new ArgumentNullException(nameof(createTerminalPage));

        _createTaskDetailsPage = createTaskDetailsPage
            ?? throw new ArgumentNullException(nameof(createTaskDetailsPage));

        _placeholderPage.SetPage(_navigation.CurrentPage);

        if (_navigation.CurrentPage == PageKind.NewTask)
        {
            _terminalPage = _createTerminalPage();
        }

        _navigation.PropertyChanged += OnNavigationChanged;
        _localization.PropertyChanged += OnLocalizationChanged;

        if (_navigation.CurrentTaskId is Guid taskId)
        {
            ShowTaskDetails(taskId);
        }
        else if (_navigation.IsCreatingTask)
        {
            ShowNewTaskEditor();
        }
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

    // Sidebar

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

    // Current section and editor state

    public PageKind CurrentPage => _navigation.CurrentPage;

    public bool IsCreatingTask => _navigation.IsCreatingTask;

    public string CurrentPageTitle =>
        _placeholderPage.PageTitle;

    public object CurrentPageContent
    {
        get
        {
            if (_taskDetailsPage is not null)
            {
                return _taskDetailsPage;
            }

            return CurrentPage switch
            {
                PageKind.Tasks => _taskListPage,

                PageKind.NewTask when _terminalPage is not null =>
                    _terminalPage,

                _ => _placeholderPage
            };
        }
    }

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
                var message = string.Format(
                    _localization.GetString(
                        "DatabaseInitializationFailed"),
                    _databaseErrorDetails ?? string.Empty);

                return message
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

    // Sidebar navigation

    [RelayCommand]
    private void NavigateHome() =>
        NavigateTo(PageKind.Home);

    [RelayCommand]
    private void NavigateCalendar() =>
        NavigateTo(PageKind.Calendar);

    [RelayCommand]
    private void NavigateTasks() =>
        NavigateTo(PageKind.Tasks);

    [RelayCommand]
    private void NavigateNewTask() =>
        NavigateTo(PageKind.NewTask);

    [RelayCommand]
    private void NavigateReports() =>
        NavigateTo(PageKind.Reports);

    [RelayCommand]
    private void NavigateSettings() =>
        NavigateTo(PageKind.Settings);

    private void NavigateTo(PageKind page)
    {
        if (_disposed || !IsDatabaseReady)
        {
            return;
        }

        if (_taskDetailsPage is { } details)
        {
            details.RequestLeave(() =>
            {
                if (!_disposed && IsDatabaseReady)
                {
                    _navigation.NavigateTo(page);
                }
            });

            return;
        }

        _navigation.NavigateTo(page);
    }

    // Navigation notifications

    private void OnNavigationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName ==
                nameof(INavigationService.CurrentTaskId))
        {
            if (_navigation.CurrentTaskId is Guid taskId)
            {
                ShowTaskDetails(taskId);
            }
            else if (!_navigation.IsCreatingTask)
            {
                HideTaskDetails();
            }
        }

        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName ==
                nameof(INavigationService.IsCreatingTask))
        {
            OnPropertyChanged(nameof(IsCreatingTask));

            if (_navigation.IsCreatingTask)
            {
                ShowNewTaskEditor();
            }
            else if (!_navigation.CurrentTaskId.HasValue)
            {
                HideTaskDetails();
            }
        }

        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName ==
                nameof(INavigationService.CurrentPage))
        {
            OnMainSectionChanged();
        }
    }

    // Existing task editor

    private void ShowTaskDetails(Guid taskId)
    {
        HideTaskDetails();

        var details = _createTaskDetailsPage();

        _taskDetailsPage = details;
        details.TaskSaved += OnTaskSaved;

        OnPropertyChanged(nameof(CurrentPageContent));

        _ = details.InitializeAsync(taskId);
    }

    // New full task editor (Tasks section)

    private void ShowNewTaskEditor()
    {
        if (_disposed ||
            _navigation.CurrentPage != PageKind.Tasks ||
            !_navigation.IsCreatingTask ||
            _taskDetailsPage is not null)
        {
            return;
        }

        var details = _createTaskDetailsPage();

        _taskDetailsPage = details;
        details.TaskSaved += OnTaskSaved;

        OnPropertyChanged(nameof(CurrentPageContent));

        // No INSERT occurs until the user presses Save.
        _ = details.InitializeForCreateAsync();
    }

    private void HideTaskDetails()
    {
        if (_taskDetailsPage is not { } details)
        {
            return;
        }

        _taskDetailsPage = null;

        details.TaskSaved -= OnTaskSaved;
        details.Dispose();

        OnPropertyChanged(nameof(CurrentPageContent));
    }

    // Synchronize Quick Task history after editing

    private void OnTaskSaved(
        object? sender,
        TaskDetailsSavedEventArgs e)
    {
        if (_disposed || _terminalPage is null)
        {
            return;
        }

        var history = _terminalPage.SessionTasks;

        for (var i = 0; i < history.Count; i++)
        {
            var current = history[i];

            if (current.Id != e.Task.Id)
            {
                continue;
            }

            // Keep the session's planned duration unchanged.
            history[i] = new SessionTaskItemViewModel(
                e.Task,
                current.PlannedMinutes,
                _localization);

            break;
        }

        // TaskListView reloads SQLite when returning to the visual tree.
    }

    // Main section lifecycle

    private void OnMainSectionChanged()
    {
        var page = _navigation.CurrentPage;

        _placeholderPage.SetPage(page);

        if (page == PageKind.NewTask)
        {
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

    // Localization

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_disposed ||
            (!string.IsNullOrEmpty(e.PropertyName) &&
             e.PropertyName !=
                nameof(ILocalizationService.CurrentLanguage)))
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

        HideTaskDetails();

        _terminalPage?.Dispose();
        _terminalPage = null;
    }
}