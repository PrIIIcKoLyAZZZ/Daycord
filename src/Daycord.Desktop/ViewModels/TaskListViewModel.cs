using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Core.Application.Services;
using Daycord.Core.Domain.Entities;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

public sealed partial class TaskListViewModel :
    ObservableObject,
    IDisposable
{
    private readonly TaskService _tasks;
    private readonly ILocalizationService _localization;

    private CancellationTokenSource? _loadCancellation;
    private int _loadVersion;

    private bool _suppressReload;
    private bool _statusesLoaded;
    private bool _hasLoaded;
    private bool _disposed;

    private Exception? _loadError;
    private Exception? _deleteError;

    private Guid? _pendingDeleteId;
    private string _pendingDeleteTitle = string.Empty;

    // Never replace the collection bound to the ListBox.
    private readonly ObservableCollection<TaskListRowViewModel> _rows = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TaskListStatusOption? _selectedStatus;

    [ObservableProperty]
    private DateTimeOffset? _dateFrom;

    [ObservableProperty]
    private DateTimeOffset? _dateTo;

    [ObservableProperty]
    private TaskListSortOption? _selectedSort;

    [ObservableProperty]
    private bool _isDescending = true;

    [ObservableProperty]
    private TaskListRowViewModel? _selectedTask;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _areFiltersExpanded;

    [ObservableProperty]
    private bool _isDeleteConfirmationOpen;

    [ObservableProperty]
    private bool _isDeleting;

    public TaskListViewModel(
        TaskService tasks,
        ILocalizationService localization)
    {
        _tasks = tasks
            ?? throw new ArgumentNullException(nameof(tasks));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        Items = new ReadOnlyObservableCollection<TaskListRowViewModel>(_rows);

        _suppressReload = true;

        StatusOptions.Add(
            new TaskListStatusOption(
                null,
                null,
                string.Empty,
                _localization));

        SelectedStatus = StatusOptions[0];

        SortOptions.Add(
            new TaskListSortOption(
                TaskSortField.TaskDate,
                "TaskListSortDate",
                _localization));

        SortOptions.Add(
            new TaskListSortOption(
                TaskSortField.Title,
                "TaskListSortTitle",
                _localization));

        SortOptions.Add(
            new TaskListSortOption(
                TaskSortField.Status,
                "TaskListSortStatus",
                _localization));

        SelectedSort = SortOptions[0];

        _suppressReload = false;

        _localization.PropertyChanged +=
            OnLocalizationChanged;
    }

    // Collections

    public ReadOnlyObservableCollection<TaskListRowViewModel> Items { get; }

    public ObservableCollection<TaskListStatusOption>
        StatusOptions { get; } = new();

    public ObservableCollection<TaskListSortOption>
        SortOptions { get; } = new();

    // Localization

    public string Heading => Text("TaskListHeading");

    public string SearchPlaceholder =>
        Text("TaskListSearchPlaceholder");

    public string StatusFilterLabel =>
        Text("TaskListStatusFilter");

    public string DateFromLabel =>
        Text("TaskListDateFrom");

    public string DateToLabel =>
        Text("TaskListDateTo");

    public string SortLabel =>
        Text("TaskListSortLabel");

    public string DirectionLabel =>
        Text(IsDescending
            ? "TaskListDescending"
            : "TaskListAscending");

    public string FiltersButtonLabel =>
        AreFiltersExpanded
            ? Text("TaskListHideFilters")
            : HasActiveViewOptions
                ? Text("TaskListFiltersActive")
                : Text("TaskListShowFilters");

    public string ResetFiltersLabel =>
        Text("TaskListResetFilters");

    public string RetryLabel =>
        Text("TaskListRefresh");

    public string LoadingMessage =>
        Text("TaskListLoading");

    public string EmptyMessage =>
        Text("TaskListEmpty");

    public string NoResultsMessage =>
        Text("TaskListNoResults");

    public string DateColumn =>
        Text("TaskListColumnDate");

    public string TitleColumn =>
        Text("TaskListColumnTitle");

    public string DescriptionColumn =>
        Text("TaskListColumnDescription");

    public string PlannedColumn =>
        Text("TaskListColumnPlanned");

    public string ActualColumn =>
        Text("TaskListColumnActual");

    public string StatusColumn =>
        Text("TaskListColumnStatus");

    // Filter state

    public bool AreFiltersActive =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        SelectedStatus?.StatusId is not null ||
        DateFrom.HasValue ||
        DateTo.HasValue;

    public bool HasActiveViewOptions =>
        SelectedStatus?.StatusId is not null ||
        DateFrom.HasValue ||
        DateTo.HasValue ||
        SelectedSort?.Field != TaskSortField.TaskDate ||
        !IsDescending;

    public bool HasDateRangeError =>
        DateFrom.HasValue &&
        DateTo.HasValue &&
        DateFrom.Value.Date > DateTo.Value.Date;

    public string DateRangeErrorMessage =>
        HasDateRangeError
            ? Text("TaskListInvalidDateRange")
            : string.Empty;

    // Loading errors

    public bool HasLoadError =>
        _loadError is not null;

    public string LoadErrorMessage =>
        _loadError is null
            ? string.Empty
            : FormatError("TaskListLoadError", _loadError);

    // Empty states

    public bool HasNoTasks =>
        _hasLoaded &&
        !IsLoading &&
        !HasLoadError &&
        !HasDateRangeError &&
        Items.Count == 0 &&
        !AreFiltersActive;

    public bool HasNoResults =>
        _hasLoaded &&
        !IsLoading &&
        !HasLoadError &&
        !HasDateRangeError &&
        Items.Count == 0 &&
        AreFiltersActive;

    // Delete confirmation

    public string DeleteButtonLabel =>
        Text("TaskListDelete");

    public string DeleteConfirmationTitle =>
        Text("TaskListDeleteConfirmationTitle");

    public string DeleteConfirmationMessage =>
        string.Format(
            CultureInfo.GetCultureInfo(
                _localization.CurrentLanguage),
            Text("TaskListDeleteConfirmationMessage"),
            _pendingDeleteTitle);

    public string ConfirmDeleteLabel =>
        Text("TaskListConfirmDelete");

    public string CancelDeleteLabel =>
        Text("TaskListCancelDelete");

    public string DeletingMessage =>
        Text("TaskListDeleting");

    public bool HasDeleteError =>
        _deleteError is not null;

    public string DeleteErrorMessage =>
        _deleteError is null
            ? string.Empty
            : FormatError("TaskListDeleteError", _deleteError);

    // Initialization

    public Task InitializeAsync()
    {
        return LoadAsync(refreshStatuses: true);
    }

    // Reload after a failed database query. The view displays this
    // command next to the load error in the following UI package.
    [RelayCommand]
    private Task RetryAsync()
    {
        return _disposed || IsDeleting
            ? Task.CompletedTask
            : LoadAsync(refreshStatuses: !_statusesLoaded);
    }

    // Filter commands

    [RelayCommand]
    private void ToggleFilters()
    {
        if (_disposed || IsDeleting)
        {
            return;
        }

        AreFiltersExpanded = !AreFiltersExpanded;
    }

    [RelayCommand]
    private void ResetFilters()
    {
        if (_disposed || IsDeleting)
        {
            return;
        }

        _suppressReload = true;

        try
        {
            SearchText = string.Empty;
            SelectedStatus = StatusOptions[0];

            DateFrom = null;
            DateTo = null;

            SelectedSort = SortOptions[0];
            IsDescending = true;
        }
        finally
        {
            _suppressReload = false;
        }

        NotifyFilterState();
        QueueLoad();
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        if (_disposed || IsDeleting)
        {
            return;
        }

        IsDescending = !IsDescending;
    }

    // Delete commands

    private bool CanRequestDelete()
    {
        return !_disposed &&
               !IsDeleting &&
               !IsDeleteConfirmationOpen &&
               SelectedTask is not null;
    }

    [RelayCommand(CanExecute = nameof(CanRequestDelete))]
    private void RequestDelete()
    {
        if (!CanRequestDelete())
        {
            return;
        }

        var task = SelectedTask!;

        // Capture the identity now, not on confirmation.
        // A later selection change must never change
        // which task will be deleted.
        _pendingDeleteId = task.Id;
        _pendingDeleteTitle = task.Title;

        SetDeleteError(null);

        OnPropertyChanged(
            nameof(DeleteConfirmationMessage));

        IsDeleteConfirmationOpen = true;
    }

    private bool CanCancelDelete()
    {
        return !_disposed &&
               IsDeleteConfirmationOpen &&
               !IsDeleting;
    }

    [RelayCommand(CanExecute = nameof(CanCancelDelete))]
    private void CancelDelete()
    {
        if (!CanCancelDelete())
        {
            return;
        }

        CloseDeleteConfirmation();
    }

    private bool CanConfirmDelete()
    {
        return !_disposed &&
               IsDeleteConfirmationOpen &&
               !IsDeleting &&
               _pendingDeleteId.HasValue;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    private async Task ConfirmDeleteAsync()
    {
        if (!CanConfirmDelete())
        {
            return;
        }

        var taskId = _pendingDeleteId!.Value;

        IsDeleting = true;
        SetDeleteError(null);

        // Invalidate outstanding list queries. A query
        // started before deletion must not repopulate
        // the table with the deleted task.
        ++_loadVersion;
        _loadCancellation?.Cancel();
        IsLoading = false;

        try
        {
            // SQLite operations must not block the UI thread.
            // Do not cancel the transaction halfway through
            // when the user navigates to another page.
            var deleted = await Task.Run(
                () => _tasks.DeleteAsync(taskId));

            if (_disposed)
            {
                return;
            }

            if (!deleted)
            {
                throw new KeyNotFoundException(
                    $"Task '{taskId}' no longer exists.");
            }

            CloseDeleteConfirmation();

            // Reload using the currently selected filters.
            await LoadAsync(refreshStatuses: false);
        }
        catch (Exception error)
        {
            if (!_disposed)
            {
                SetDeleteError(error);
            }
        }
        finally
        {
            if (!_disposed)
            {
                IsDeleting = false;
            }
        }
    }

    private void CloseDeleteConfirmation()
    {
        _pendingDeleteId = null;
        _pendingDeleteTitle = string.Empty;

        IsDeleteConfirmationOpen = false;

        SetDeleteError(null);

        OnPropertyChanged(
            nameof(DeleteConfirmationMessage));
    }

    private void SetDeleteError(Exception? error)
    {
        _deleteError = error;

        OnPropertyChanged(nameof(HasDeleteError));
        OnPropertyChanged(nameof(DeleteErrorMessage));
    }

    // Property changes

    partial void OnSearchTextChanged(string value)
    {
        NotifyFilterState();
        QueueLoad(debounce: true);
    }

    partial void OnSelectedStatusChanged(
        TaskListStatusOption? value)
    {
        NotifyFilterState();
        QueueLoad();
    }

    partial void OnDateFromChanged(DateTimeOffset? value)
    {
        NotifyFilterState();
        QueueLoad();
    }

    partial void OnDateToChanged(DateTimeOffset? value)
    {
        NotifyFilterState();
        QueueLoad();
    }

    partial void OnSelectedSortChanged(
        TaskListSortOption? value)
    {
        NotifyFilterState();
        QueueLoad();
    }

    partial void OnIsDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(DirectionLabel));
        NotifyFilterState();
        QueueLoad();
    }

    partial void OnAreFiltersExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(FiltersButtonLabel));
    }

    partial void OnSelectedTaskChanged(
        TaskListRowViewModel? value)
    {
        RequestDeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDeletingChanged(bool value)
    {
        RequestDeleteCommand.NotifyCanExecuteChanged();
        CancelDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDeleteConfirmationOpenChanged(bool value)
    {
        RequestDeleteCommand.NotifyCanExecuteChanged();
        CancelDeleteCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        NotifyEmptyState();
    }

    // List loading

    private void QueueLoad(bool debounce = false)
    {
        if (_disposed || _suppressReload || IsDeleting)
        {
            return;
        }

        _ = LoadAsync(
            refreshStatuses: false,
            debounce: debounce);
    }

    private async Task LoadAsync(
        bool refreshStatuses,
        bool debounce = false)
    {
        if (_disposed)
        {
            return;
        }

        var previous = _loadCancellation;
        var cancellation = new CancellationTokenSource();

        _loadCancellation = cancellation;

        var version = ++_loadVersion;
        var token = cancellation.Token;

        previous?.Cancel();

        try
        {
            if (debounce)
            {
                await Task.Delay(250, token);
            }

            if (!IsCurrent(version, token))
            {
                return;
            }

            IsLoading = true;
            SetLoadError(null);

            if (HasDateRangeError)
            {
                if (_rows.Count > 0)
                {
                    _rows.Clear();
                }

                SelectedTask = null;
                _hasLoaded = true;
                return;
            }

            if (refreshStatuses || !_statusesLoaded)
            {
                var statuses = await Task.Run(
                    () => _tasks.ListStatusesAsync(token),
                    token);

                if (!IsCurrent(version, token))
                {
                    return;
                }

                ReplaceStatuses(statuses);
                _statusesLoaded = true;
            }

            var query = BuildQuery();

            var tasks = await Task.Run(
                () => _tasks.ListAsync(query, token),
                token);

            if (!IsCurrent(version, token))
            {
                return;
            }

            ApplyTasks(tasks);
            _hasLoaded = true;
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // A newer request superseded this one.
        }
        catch (Exception error)
        {
            if (IsCurrent(version, token))
            {
                SetLoadError(error);
                _hasLoaded = true;
            }
        }
        finally
        {
            if (IsCurrent(version, token))
            {
                IsLoading = false;
                NotifyEmptyState();
                _loadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Apply a successful database result on the UI thread in one
    /// synchronous pass. Existing rows and the Items collection survive
    /// refreshes, so unchanged ListBox containers can remain in place.
    /// </summary>
    /// <param name="tasks"></param>
    /// <exception cref="InvalidOperationException"></exception>
    private void ApplyTasks(IReadOnlyList<TaskListItem> tasks)
    {
        var selectedId = SelectedTask?.Id;
        var existing = _rows.ToDictionary(row => row.Id);
        var ids = new HashSet<Guid>();
        var nextRows = new List<TaskListRowViewModel>(tasks.Count);

        foreach (var task in tasks)
        {
            if (!ids.Add(task.Id))
            {
                throw new InvalidOperationException(
                    $"Task query returned duplicate ID '{task.Id}'.");
            }

            if (existing.TryGetValue(task.Id, out var row))
            {
                row.Update(task);
            }
            else
            {
                row = new TaskListRowViewModel(task, _localization);
            }

            nextRows.Add(row);
        }

        // First remove rows no longer matching the query.
        for (var index = _rows.Count - 1; index >= 0; index--)
        {
            if (!ids.Contains(_rows[index].Id))
            {
                _rows.RemoveAt(index);
            }
        }

        // Reorder retained row objects, inserting only genuinely new rows.
        for (var index = 0; index < nextRows.Count; index++)
        {
            var desired = nextRows[index];

            if (index < _rows.Count &&
                ReferenceEquals(_rows[index], desired))
            {
                continue;
            }

            var previousIndex = _rows.IndexOf(desired);

            if (previousIndex >= 0)
            {
                _rows.Move(previousIndex, index);
            }
            else
            {
                _rows.Insert(index, desired);
            }
        }

        // Selection is identified by Guid, never by a former row index.
        // Repair any intermediate selection changes from collection moves.
        SelectedTask = selectedId is Guid id
            ? nextRows.FirstOrDefault(row => row.Id == id)
            : null;

        NotifyEmptyState();
    }

    private TaskQuery BuildQuery()
    {
        return new TaskQuery
        {
            SearchText = SearchText,

            StatusId = SelectedStatus?.StatusId,

            DateFrom = DateFrom.HasValue
                ? DateOnly.FromDateTime(DateFrom.Value.Date)
                : null,

            DateTo = DateTo.HasValue
                ? DateOnly.FromDateTime(DateTo.Value.Date)
                : null,

            SortBy = SelectedSort?.Field
                ?? TaskSortField.TaskDate,

            Direction = IsDescending
                ? TaskSortDirection.Descending
                : TaskSortDirection.Ascending
        };
    }

    private bool IsCurrent(
        int version,
        CancellationToken token)
    {
        return !_disposed &&
               !token.IsCancellationRequested &&
               version == _loadVersion;
    }

    // Status options

    private void ReplaceStatuses(
        IReadOnlyList<TaskStatusDefinition> statuses)
    {
        var previousId = SelectedStatus?.StatusId;

        _suppressReload = true;

        try
        {
            StatusOptions.Clear();

            StatusOptions.Add(
                new TaskListStatusOption(
                    null,
                    null,
                    string.Empty,
                    _localization));

            foreach (var status in statuses)
            {
                StatusOptions.Add(
                    new TaskListStatusOption(
                        status.Id,
                        status.IsSystem
                            ? status.SystemKey
                            : null,
                        status.Name,
                        _localization));
            }

            SelectedStatus =
                StatusOptions.FirstOrDefault(
                    option =>
                        option.StatusId == previousId)
                ?? StatusOptions[0];
        }
        finally
        {
            _suppressReload = false;
        }

        NotifyFilterState();
    }

    // State helpers

    private void SetLoadError(Exception? error)
    {
        _loadError = error;

        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(LoadErrorMessage));

        NotifyEmptyState();
    }

    private void NotifyFilterState()
    {
        OnPropertyChanged(nameof(AreFiltersActive));
        OnPropertyChanged(nameof(HasActiveViewOptions));
        OnPropertyChanged(nameof(FiltersButtonLabel));

        OnPropertyChanged(nameof(HasDateRangeError));
        OnPropertyChanged(nameof(DateRangeErrorMessage));

        NotifyEmptyState();
    }

    private void NotifyEmptyState()
    {
        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private string Text(string key)
    {
        return _localization.GetString(key);
    }

    private string FormatError(string key, Exception error)
    {
        return string.Format(
            CultureInfo.GetCultureInfo(
                _localization.CurrentLanguage),
            Text(key),
            error.Message);
    }

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

        foreach (var row in Items)
        {
            row.RefreshLocalization();
        }

        foreach (var option in StatusOptions)
        {
            option.RefreshLocalization();
        }

        foreach (var option in SortOptions)
        {
            option.RefreshLocalization();
        }

        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(StatusFilterLabel));
        OnPropertyChanged(nameof(DateFromLabel));
        OnPropertyChanged(nameof(DateToLabel));
        OnPropertyChanged(nameof(SortLabel));
        OnPropertyChanged(nameof(DirectionLabel));
        OnPropertyChanged(nameof(FiltersButtonLabel));
        OnPropertyChanged(nameof(ResetFiltersLabel));
        OnPropertyChanged(nameof(RetryLabel));
        OnPropertyChanged(nameof(LoadingMessage));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(NoResultsMessage));

        OnPropertyChanged(nameof(DateColumn));
        OnPropertyChanged(nameof(TitleColumn));
        OnPropertyChanged(nameof(DescriptionColumn));
        OnPropertyChanged(nameof(PlannedColumn));
        OnPropertyChanged(nameof(ActualColumn));
        OnPropertyChanged(nameof(StatusColumn));

        OnPropertyChanged(nameof(DateRangeErrorMessage));
        OnPropertyChanged(nameof(LoadErrorMessage));

        OnPropertyChanged(nameof(DeleteButtonLabel));
        OnPropertyChanged(nameof(DeleteConfirmationTitle));
        OnPropertyChanged(nameof(DeleteConfirmationMessage));
        OnPropertyChanged(nameof(ConfirmDeleteLabel));
        OnPropertyChanged(nameof(CancelDeleteLabel));
        OnPropertyChanged(nameof(DeletingMessage));
        OnPropertyChanged(nameof(DeleteErrorMessage));
    }

    // Cleanup

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _localization.PropertyChanged -=
            OnLocalizationChanged;

        _loadCancellation?.Cancel();
        _loadCancellation = null;
    }
}

public sealed class TaskListStatusOption : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly string? _resourceKey;
    private readonly string _customName;

    public TaskListStatusOption(
        Guid? statusId,
        string? systemKey,
        string name,
        ILocalizationService localization)
    {
        StatusId = statusId;
        _customName = name;
        _localization = localization;

        _resourceKey = statusId is null
            ? "TaskListAllStatuses"
            : systemKey switch
            {
                "open" => "TaskListStatusOpen",
                "completed" => "TaskListStatusCompleted",
                _ => null
            };
    }

    public Guid? StatusId { get; }

    public string Label =>
        _resourceKey is null
            ? _customName
            : _localization.GetString(_resourceKey);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Label));
    }
}

public sealed class TaskListSortOption : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly string _resourceKey;

    public TaskListSortOption(
        TaskSortField field,
        string resourceKey,
        ILocalizationService localization)
    {
        Field = field;
        _resourceKey = resourceKey;
        _localization = localization;
    }

    public TaskSortField Field { get; }

    public string Label =>
        _localization.GetString(_resourceKey);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Label));
    }
}