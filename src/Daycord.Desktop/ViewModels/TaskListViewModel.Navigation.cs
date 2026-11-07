using CommunityToolkit.Mvvm.Input;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TaskListViewModel
{
    public event Action<Guid>? TaskOpenRequested;

    public event Action? TaskCreationRequested;

    public string CreateFullTaskLabel =>
        _localization.GetString("QuickTaskCreateAction");

    // Do not open old rows while a debounced filter or reload is pending.
    // Row identity is preserved by DC-034, so reference membership is safe.
    public bool CanOpenTaskFromList(TaskListRowViewModel? task)
    {
        return !_disposed &&
               !IsDeleting &&
               !IsDeleteConfirmationOpen &&
               !IsLoading &&
               _loadCancellation is null &&
               !HasDateRangeError &&
               !HasLoadError &&
               task is not null &&
               _rows.Contains(task);
    }

    public void OpenTaskFromList(TaskListRowViewModel? task)
    {
        if (!CanOpenTaskFromList(task))
        {
            return;
        }

        // The guard above confirms task is non-null and still displayed.
        SelectedTask = task;
        TaskOpenRequested?.Invoke(task!.Id);
    }

    [RelayCommand]
    private void CreateFullTask()
    {
        if (_disposed ||
            IsDeleting ||
            IsDeleteConfirmationOpen)
        {
            return;
        }

        TaskCreationRequested?.Invoke();
    }
}