using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TaskListViewModel
{
    private INavigationService? _navigation;

    /// <summary>
    /// Called once by DI, before MainWindowViewModel subscribes to
    /// CurrentPage. This ensures the filter is applied before the
    /// Tasks view is created and initializes its data load.
    /// </summary>
    public void AttachNavigation(INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TaskListViewModel));
        }

        if (_navigation is not null)
        {
            if (!ReferenceEquals(_navigation, navigation))
            {
                throw new InvalidOperationException(
                    "TaskListViewModel is already attached to navigation.");
            }

            return;
        }

        _navigation = navigation;
        _navigation.PropertyChanged += OnNavigationPageChanged;

        // Also handle a request if navigation was already established.
        ApplyPendingDateFilterIfAvailable();
    }

    private void OnNavigationPageChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_disposed ||
            (!string.IsNullOrEmpty(e.PropertyName) &&
             e.PropertyName != nameof(INavigationService.CurrentPage)))
        {
            return;
        }

        ApplyPendingDateFilterIfAvailable();
    }

    private void ApplyPendingDateFilterIfAvailable()
    {
        if (_navigation is null ||
            !_navigation.TryConsumeTasksDateFilter(out var date))
        {
            return;
        }

        // Invalidates an in-flight query. Do not start a new query here:
        // TaskListView.InitializeAndRestoreAsync() already reloads the
        // list on every attachment. Starting another one would race it.
        ++_loadVersion;
        _loadCancellation?.Cancel();

        var previousSuppression = _suppressReload;
        _suppressReload = true;

        try
        {
            SearchText = string.Empty;
            SelectedStatus = StatusOptions[0];

            // DateOnly represents the user's local calendar day.
            // DateTimeOffset is used only for the existing date controls.
            var localMidnight = new DateTimeOffset(
                date.ToDateTime(TimeOnly.MinValue));

            DateFrom = localMidnight;
            DateTo = localMidnight;

            // The date must be visible and editable immediately.
            AreFiltersExpanded = true;

            // Preserve matching row objects from DC-034, but never show
            // old rows belonging to another TaskDate as Today results.
            if (SelectedTask?.TaskDate != date)
            {
                SelectedTask = null;
            }

            for (var index = _rows.Count - 1; index >= 0; index--)
            {
                if (_rows[index].TaskDate != date)
                {
                    _rows.RemoveAt(index);
                }
            }

            // Existing rows can only be a partial subset of Today if
            // the previous filter had search or status restrictions.
            _hasLoaded = false;
            SetLoadError(null);
        }
        finally
        {
            _suppressReload = previousSuppression;
        }

        NotifyFilterState();
    }

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