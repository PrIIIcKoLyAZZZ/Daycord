using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Core.Application.Services;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Navigation;

namespace Daycord.Desktop.ViewModels;

/// <summary>
/// Today's read-only overview. The three sections use independent calendar dates.
/// The page is reloaded from SQLite whenever Home becomes active.
/// </summary>
public sealed partial class HomeDashboardViewModel : ObservableObject, IDisposable
{
    private readonly TaskService _tasks;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;

    private CancellationTokenSource? _loadCancellation;
    private int _loadVersion;
    private bool _isActive;
    private bool _disposed;
    private bool _hasLoaded;
    private Exception? _loadError;
    private bool _isLoading;

    private DateOnly _today;
    private IReadOnlyList<CalendarEntryViewModel> _todayTasks = [];
    private IReadOnlyList<CalendarEntryViewModel> _plannedWork = [];
    private IReadOnlyList<CalendarEntryViewModel> _actualWork = [];

    // Snapshots let an unchanged refresh retain its existing visual items.
    private IReadOnlyList<CalendarTaskItem> _displayedTasks = [];
    private IReadOnlyList<CalendarPlanningItem> _displayedPlanned = [];
    private IReadOnlyList<CalendarWorkItem> _displayedActual = [];

    private int _completedCount;
    private long _plannedMinutes;
    private long _actualMinutes;

    public HomeDashboardViewModel(
        TaskService tasks,
        INavigationService navigation,
        ILocalizationService localization)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _today = DateOnly.FromDateTime(DateTime.Now);
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public DateOnly Today
    {
        get => _today;
        private set
        {
            if (SetProperty(ref _today, value))
                OnPropertyChanged(nameof(DateText));
        }
    }

    public IReadOnlyList<CalendarEntryViewModel> TodayTasks
    {
        get => _todayTasks;
        private set => SetProperty(ref _todayTasks, value);
    }

    public IReadOnlyList<CalendarEntryViewModel> PlannedWork
    {
        get => _plannedWork;
        private set => SetProperty(ref _plannedWork, value);
    }

    public IReadOnlyList<CalendarEntryViewModel> ActualWork
    {
        get => _actualWork;
        private set => SetProperty(ref _actualWork, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                NotifyLoadState();
        }
    }

    // A refresh of the same day must not hide an already loaded dashboard.
    public bool IsLoaded => _hasLoaded && !HasLoadError;
    public bool HasLoadError => _loadError is not null;
    public bool HasTodayTasks => TodayTasks.Count > 0;
    public bool HasPlannedWork => PlannedWork.Count > 0;
    public bool HasActualWork => ActualWork.Count > 0;
    public bool IsEmpty => IsLoaded &&
        !HasTodayTasks && !HasPlannedWork && !HasActualWork;

    public int TodayTaskCount => TodayTasks.Count;
    public int CompletedCount => _completedCount;
    public long PlannedMinutes => _plannedMinutes;
    public long ActualMinutes => _actualMinutes;

    public string CompletedText => $"{CompletedCount} / {TodayTaskCount}";
    public string PlannedText => CalendarDurationText.Format(
        PlannedMinutes, _localization);
    public string ActualText => CalendarDurationText.Format(
        ActualMinutes, _localization);

    private CultureInfo Culture =>
        CultureInfo.GetCultureInfo(_localization.CurrentLanguage);

    private string Text(string key) => _localization.GetString(key);

    public string DateText => Today.ToDateTime(TimeOnly.MinValue)
        .ToString("D", Culture);
    public string TodayTasksLabel => Text("HomeDashboardTodayTasks");
    public string CompletedLabel => Text("HomeDashboardCompleted");
    public string PlannedTodayLabel => Text("HomeDashboardPlannedToday");
    public string ActualTodayLabel => Text("HomeDashboardActualToday");
    public string PlannedWorkLabel => Text("HomeDashboardPlannedWork");
    public string ActualWorkLabel => Text("HomeDashboardActualWork");
    public string NoTasksLabel => Text("HomeDashboardNoTasks");
    public string NoPlannedLabel => Text("HomeDashboardNoPlanned");
    public string NoActualLabel => Text("HomeDashboardNoActual");
    public string NoEntriesLabel => Text("HomeDashboardNoEntries");
    public string LoadingLabel => Text("HomeDashboardLoading");
    public string RetryLabel => Text("HomeDashboardRetry");
    public string OpenTaskLabel => Text("HomeDashboardOpenTask");
    public string ErrorText => _loadError is null
        ? string.Empty
        : string.Format(Culture, Text("HomeDashboardLoadFailed"),
            _loadError.Message);

    public Task ActivateAsync()
    {
        if (_disposed) return Task.CompletedTask;
        _isActive = true;
        return ReloadAsync();
    }

    public void Deactivate()
    {
        _isActive = false;
        ++_loadVersion;

        var pending = _loadCancellation;
        _loadCancellation = null;
        pending?.Cancel();

        IsLoading = false;
    }

    [RelayCommand]
    private void Retry() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_disposed || !_isActive) return;

        var previous = _loadCancellation;
        var source = new CancellationTokenSource();
        _loadCancellation = source;
        var requestVersion = ++_loadVersion;
        previous?.Cancel();

        var date = DateOnly.FromDateTime(DateTime.Now);

        if (Today != date)
        {
            // Old-day values must never appear beneath the new date label.
            Today = date;
            _hasLoaded = false;
            ClearData();
        }

        // For the same day, keep the last successful data and its visual tree.
        _loadError = null;
        IsLoading = true;
        NotifyLoadState();

        try
        {
            var data = await Task.Run(
                () => _tasks.GetCalendarDataAsync(date, date, source.Token),
                source.Token);

            if (!IsCurrentLoad(source, requestVersion)) return;

            ApplyData(data, date);
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentLoad(source, requestVersion))
            {
                // Do not present a failed refresh as successfully updated.
                _hasLoaded = false;
                ClearData();
                _loadError = ex;
            }
        }
        finally
        {
            if (IsCurrentLoad(source, requestVersion))
            {
                _loadCancellation = null;
                IsLoading = false;
                NotifyLoadState();
            }

            source.Dispose();
        }
    }

    private void ApplyData(CalendarData data, DateOnly date)
    {
        var comparer = StringComparer.Create(Culture, ignoreCase: true);

        var tasks = data.Tasks
            .Where(item => item.TaskDate == date)
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.TaskTitle, comparer)
            .ThenBy(item => item.TaskId)
            .ToArray();

        var planned = data.PlannedWork
            .Where(item => item.PlannedDate == date)
            .OrderBy(item => item.TaskTitle, comparer)
            .ThenBy(item => item.Id)
            .ToArray();

        var actual = data.ActualWork
            .Where(item => item.WorkDate == date)
            .OrderBy(item => item.TaskTitle, comparer)
            .ThenBy(item => item.Id)
            .ToArray();

        if (!SameTasks(_displayedTasks, tasks))
        {
            TodayTasks = tasks.Select(item => CalendarEntryViewModel.FromTask(
                item, _localization, OpenTask)).ToArray();
            _displayedTasks = tasks;
        }

        if (!SamePlanned(_displayedPlanned, planned))
        {
            PlannedWork = planned.Select(item => CalendarEntryViewModel.FromPlanning(
                item, _localization, OpenTask)).ToArray();
            _displayedPlanned = planned;
        }

        if (!SameActual(_displayedActual, actual))
        {
            ActualWork = actual.Select(item => CalendarEntryViewModel.FromWork(
                item, _localization, OpenTask)).ToArray();
            _displayedActual = actual;
        }

        _completedCount = tasks.Count(item => item.IsCompleted);
        _plannedMinutes = planned.Sum(item => (long)item.PlannedMinutes);
        _actualMinutes = actual.Sum(item => (long)item.ActualMinutes);
        NotifyDataChanged();
    }

    // Equality includes everything displayed by CalendarEntryViewModel.
    // Compare the sorted source records, not object references returned by SQLite.
    private static bool SameTasks(
        IReadOnlyList<CalendarTaskItem> left,
        IReadOnlyList<CalendarTaskItem> right) =>
        left.Select(item => (
            item.TaskId, item.TaskDate, item.TaskNumber, item.TaskTitle,
            item.StatusId, item.StatusName, item.StatusSystemKey,
            item.IsCompleted))
        .SequenceEqual(right.Select(item => (
            item.TaskId, item.TaskDate, item.TaskNumber, item.TaskTitle,
            item.StatusId, item.StatusName, item.StatusSystemKey,
            item.IsCompleted)));

    private static bool SamePlanned(
        IReadOnlyList<CalendarPlanningItem> left,
        IReadOnlyList<CalendarPlanningItem> right) =>
        left.Select(item => (
            item.Id, item.TaskId, item.PlannedDate, item.PlannedMinutes,
            item.TaskNumber, item.TaskTitle, item.StatusId,
            item.StatusName, item.StatusSystemKey, item.IsCompleted))
        .SequenceEqual(right.Select(item => (
            item.Id, item.TaskId, item.PlannedDate, item.PlannedMinutes,
            item.TaskNumber, item.TaskTitle, item.StatusId,
            item.StatusName, item.StatusSystemKey, item.IsCompleted)));

    private static bool SameActual(
        IReadOnlyList<CalendarWorkItem> left,
        IReadOnlyList<CalendarWorkItem> right) =>
        left.Select(item => (
            item.Id, item.TaskId, item.WorkDate, item.ActualMinutes,
            item.WorkDescription, item.Note, item.TaskNumber, item.TaskTitle,
            item.StatusId, item.StatusName, item.StatusSystemKey,
            item.IsCompleted))
        .SequenceEqual(right.Select(item => (
            item.Id, item.TaskId, item.WorkDate, item.ActualMinutes,
            item.WorkDescription, item.Note, item.TaskNumber, item.TaskTitle,
            item.StatusId, item.StatusName, item.StatusSystemKey,
            item.IsCompleted)));

    private void ClearData()
    {
        _displayedTasks = [];
        _displayedPlanned = [];
        _displayedActual = [];

        TodayTasks = [];
        PlannedWork = [];
        ActualWork = [];

        _completedCount = 0;
        _plannedMinutes = 0;
        _actualMinutes = 0;
        NotifyDataChanged();
    }

    private void NotifyDataChanged()
    {
        OnPropertyChanged(nameof(TodayTaskCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(CompletedText));
        OnPropertyChanged(nameof(PlannedMinutes));
        OnPropertyChanged(nameof(ActualMinutes));
        OnPropertyChanged(nameof(PlannedText));
        OnPropertyChanged(nameof(ActualText));
        OnPropertyChanged(nameof(HasTodayTasks));
        OnPropertyChanged(nameof(HasPlannedWork));
        OnPropertyChanged(nameof(HasActualWork));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void NotifyLoadState()
    {
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ErrorText));
    }

    private bool IsCurrentLoad(CancellationTokenSource source, int version) =>
        !_disposed && _isActive && _loadVersion == version &&
        ReferenceEquals(_loadCancellation, source);

    private void OpenTask(Guid taskId)
    {
        if (!_disposed && _isActive && taskId != Guid.Empty)
            _navigation.OpenTask(taskId);
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName != nameof(ILocalizationService.CurrentLanguage)))
            return;

        foreach (var item in TodayTasks) item.RefreshLocalization();
        foreach (var item in PlannedWork) item.RefreshLocalization();
        foreach (var item in ActualWork) item.RefreshLocalization();

        string[] names =
        [
            nameof(DateText), nameof(TodayTasksLabel), nameof(CompletedLabel),
            nameof(PlannedTodayLabel), nameof(ActualTodayLabel),
            nameof(PlannedWorkLabel), nameof(ActualWorkLabel),
            nameof(NoTasksLabel), nameof(NoPlannedLabel), nameof(NoActualLabel),
            nameof(NoEntriesLabel), nameof(LoadingLabel), nameof(RetryLabel),
            nameof(OpenTaskLabel), nameof(ErrorText),
            nameof(PlannedText), nameof(ActualText)
        ];

        foreach (var name in names) OnPropertyChanged(name);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isActive = false;
        ++_loadVersion;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        _localization.PropertyChanged -= OnLocalizationChanged;
    }
}