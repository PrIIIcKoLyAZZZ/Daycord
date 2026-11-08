using CommunityToolkit.Mvvm.ComponentModel;

namespace Daycord.Desktop.Navigation;

public sealed class NavigationService :
    ObservableObject,
    INavigationService
{
    private PageKind _currentPage = PageKind.Home;
    private Guid? _currentTaskId;
    private bool _isCreatingTask;

    // A one-time request used when opening Tasks
    // from the Home Dashboard.
    //
    // It must be assigned before CurrentPage changes,
    // because navigation activates the target view.
    private DateOnly? _pendingTasksDateFilter;

    public PageKind CurrentPage
    {
        get => _currentPage;

        private set => SetProperty(ref _currentPage, value);
    }

    public Guid? CurrentTaskId
    {
        get => _currentTaskId;

        private set
        {
            if (SetProperty(ref _currentTaskId, value))
            {
                OnPropertyChanged(nameof(IsTaskOpen));
            }
        }
    }

    public bool IsCreatingTask
    {
        get => _isCreatingTask;

        private set
        {
            if (SetProperty(ref _isCreatingTask, value))
            {
                OnPropertyChanged(nameof(IsTaskOpen));
            }
        }
    }

    public bool IsTaskOpen =>
        CurrentTaskId.HasValue || IsCreatingTask;

    public void OpenTask(Guid taskId)
    {
        if (taskId == Guid.Empty)
        {
            throw new ArgumentException(
                "Task ID must not be empty.",
                nameof(taskId));
        }

        if (CurrentTaskId == taskId)
        {
            return;
        }

        if (IsTaskOpen)
        {
            return;
        }

        // Direct task opening must not leave a pending
        // Tasks filter navigation behind.
        _pendingTasksDateFilter = null;

        CurrentTaskId = taskId;
    }

    public void OpenNewTask()
    {
        if (CurrentPage != PageKind.Tasks || IsTaskOpen)
        {
            return;
        }

        _pendingTasksDateFilter = null;

        IsCreatingTask = true;
    }

    public void CloseTask()
    {
        if (!IsTaskOpen)
        {
            return;
        }

        if (CurrentTaskId.HasValue)
        {
            CurrentTaskId = null;
        }

        if (IsCreatingTask)
        {
            IsCreatingTask = false;
        }
    }

    public void NavigateTo(PageKind page)
    {
        // Ordinary navigation never carries over
        // a previously requested date filter.
        _pendingTasksDateFilter = null;

        NavigateCore(page);
    }

    public void NavigateToTasksForDate(DateOnly date)
    {
        // Store the request before changing CurrentPage.
        // The Tasks view consumes it during initialization.
        _pendingTasksDateFilter = date;

        NavigateCore(PageKind.Tasks);
    }

    public bool TryConsumeTasksDateFilter(
        out DateOnly date)
    {
        if (CurrentPage == PageKind.Tasks &&
            _pendingTasksDateFilter is { } pendingDate)
        {
            // Consume before returning so the same request
            // cannot be applied by a later activation.
            _pendingTasksDateFilter = null;

            date = pendingDate;
            return true;
        }

        date = default;
        return false;
    }

    private void NavigateCore(PageKind page)
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
            CloseTask();
            return;
        }

        CloseTask();
        CurrentPage = page;
    }
}