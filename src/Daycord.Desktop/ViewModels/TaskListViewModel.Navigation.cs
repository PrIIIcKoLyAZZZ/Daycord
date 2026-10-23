using CommunityToolkit.Mvvm.Input;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TaskListViewModel
{
    public event Action<Guid>? TaskOpenRequested;

    public event Action? TaskCreationRequested;

    public string CreateFullTaskLabel =>
        _localization.GetString("QuickTaskCreateAction");

    public void OpenTaskFromList(TaskListRowViewModel? task)
    {
        if (_disposed ||
            IsDeleting ||
            IsDeleteConfirmationOpen ||
            task is null)
        {
            return;
        }

        SelectedTask = task;
        TaskOpenRequested?.Invoke(task.Id);
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