using System.ComponentModel;

namespace Daycord.Desktop.Navigation;

public interface INavigationService : INotifyPropertyChanged
{
    PageKind CurrentPage { get; }

    Guid? CurrentTaskId { get; }
    
    bool IsTaskOpen { get; }
    
    bool IsCreatingTask { get; }

    void NavigateTo(PageKind page);

    void OpenTask(Guid taskId);
    
    void OpenNewTask();
    
    void CloseTask();
}