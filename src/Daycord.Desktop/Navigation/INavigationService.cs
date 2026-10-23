using System.ComponentModel;

namespace Daycord.Desktop.Navigation;

public interface INavigationService : INotifyPropertyChanged
{
    PageKind CurrentPage { get; }

    Guid? CurrentTaskId { get; }

    // True for editing an existing task or creating a new one.
    bool IsTaskOpen { get; }

    // True only while the full new-task editor is active.
    bool IsCreatingTask { get; }

    void NavigateTo(PageKind page);

    void OpenTask(Guid taskId);

    // Open the full editor for a new task from Tasks.
    void OpenNewTask();

    // Return to the underlying main section.
    void CloseTask();
}