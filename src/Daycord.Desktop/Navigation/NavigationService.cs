using CommunityToolkit.Mvvm.ComponentModel;

namespace Daycord.Desktop.Navigation;

public sealed class NavigationService :
    ObservableObject,
    INavigationService
{
    private PageKind _currentPage = PageKind.Home;
    private Guid? _currentTaskId;
    private bool _isCreatingTask;

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
        
        CurrentTaskId = taskId;
    }

    public void OpenNewTask()
    {
        if (CurrentPage != PageKind.Tasks || IsTaskOpen)
        {
            return;
        }

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