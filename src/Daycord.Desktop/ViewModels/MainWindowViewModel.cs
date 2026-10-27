using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public partial class MainWindowViewModel :
    ObservableObject,
    IDisposable
{
    private readonly TaskService _taskService;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly PlaceholderPageViewModel _placeholderPage;
    private readonly TaskListViewModel _taskListPage;
    private readonly CalendarPageViewModel _calendarPage;
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
        TaskService taskService,
        INavigationService navigation,
        ILocalizationService localization,
        PlaceholderPageViewModel placeholderPage,
        TaskListViewModel taskListPage,
        CalendarPageViewModel calendarPage,
        Func<TerminalViewModel> createTerminalPage,
        Func<TaskDetailsViewModel> createTaskDetailsPage)
    {
        _taskService = taskService
            ?? throw new ArgumentNullException(nameof(taskService));

        _navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _placeholderPage = placeholderPage
            ?? throw new ArgumentNullException(nameof(placeholderPage));

        _taskListPage = taskListPage
            ?? throw new ArgumentNullException(nameof(taskListPage));

        _calendarPage = calendarPage
            ?? throw new ArgumentNullException(nameof(calendarPage));

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
                PageKind.Calendar => _calendarPage,

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

        if (_navigation.CurrentPage == PageKind.Calendar &&
            !_navigation.IsTaskOpen)
        {
            _ = _calendarPage.ActivateAsync();
        }
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

        // Keep calendar selection but stop in-flight month requests
        // while the full task editor is visible.
        _calendarPage.Deactivate();

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

        // Refresh when leaving Task Details even if only a planning
        // period was saved. These operations do not raise TaskSaved.
        var session = _terminalPage;
        var closedTaskId = details.TaskId;
        var shouldRefreshHistory = !_disposed &&
            session is not null &&
            _navigation.CurrentPage == PageKind.NewTask &&
            closedTaskId != Guid.Empty &&
            session.SessionTasks.Any(item => item.Id == closedTaskId);

        details.TaskSaved -= OnTaskSaved;
        details.Dispose();

        OnPropertyChanged(nameof(CurrentPageContent));

        // Refresh even if only a PlanningPeriod or WorkEntry changed.
        // Those independent saves do not raise TaskSaved.
        if (!_disposed && IsDatabaseReady &&
            _navigation.CurrentPage == PageKind.Calendar &&
            !_navigation.IsTaskOpen)
        {
            _ = _calendarPage.ActivateAsync();
        }

        if (shouldRefreshHistory && session is not null)
        {
            _ = RefreshSessionHistoryAsync(closedTaskId, session);
        }
    }

    // Reload persisted values after closing the editor.
    // SQLite is the source of truth for the planned duration.

    private async Task RefreshSessionHistoryAsync(
        Guid taskId,
        TerminalViewModel session)
    {
        try
        {
            var task = await _taskService.GetByIdAsync(taskId);

            if (task is null)
                return;

            var periods = await _taskService.ListPlanningPeriodsAsync(
                taskId);

            long? total = periods.Count == 0
                ? null
                : periods.Sum(period => (long)period.PlannedMinutes);

            if (_disposed ||
                !ReferenceEquals(_terminalPage, session) ||
                _navigation.CurrentPage != PageKind.NewTask)
            {
                return;
            }

            var history = session.SessionTasks;

            for (var i = 0; i < history.Count; i++)
            {
                if (history[i].Id != taskId)
                    continue;

                history[i] = new SessionTaskItemViewModel(
                    task,
                    total,
                    _localization);

                break;
            }
        }
        catch (Exception)
        {
            // A failed history refresh must not undo successful edits
            // or interrupt navigation. Task Details reads SQLite
            // again when the user reopens the task.
        }
    }

    // Synchronize Quick Task history after ordinary task saving.

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

            // Preserve the displayed duration until the database
            // refresh replaces it with the current aggregate.
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

        if (page == PageKind.Calendar &&
            IsDatabaseReady &&
            _taskDetailsPage is null)
        {
            _ = _calendarPage.ActivateAsync();
        }
        else
        {
            _calendarPage.Deactivate();
        }

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

        _calendarPage.Deactivate();
        HideTaskDetails();

        _terminalPage?.Dispose();
        _terminalPage = null;
    }
}