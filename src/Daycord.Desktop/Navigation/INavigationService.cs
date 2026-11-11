using System.ComponentModel;

namespace Daycord.Desktop.Navigation;

public interface INavigationService : INotifyPropertyChanged
{
    PageKind CurrentPage { get; }

    Guid? CurrentTaskId { get; }

    bool IsTaskOpen { get; }

    bool IsCreatingTask { get; }

    void NavigateTo(PageKind page);

    /// <summary>
    /// Opens the existing Tasks page with a one-time
    /// TaskDate filter request.
    /// </summary>
    void NavigateToTasksForDate(DateOnly date);

    /// <summary>
    /// Consumes the pending TaskDate filter request.
    /// Returns false when no request is available.
    /// </summary>
    bool TryConsumeTasksDateFilter(out DateOnly date);

    void OpenTask(Guid taskId);

    void OpenNewTask();

    void CloseTask();
}