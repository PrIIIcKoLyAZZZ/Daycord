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
/// Persistent calendar state; data is reloaded from SQLite on activation.
/// </summary>
public sealed partial class CalendarPageViewModel : ObservableObject, IDisposable
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
    private CalendarData? _data;

    private IReadOnlyList<CalendarTaskItem> _displayedTasks = [];
    private IReadOnlyList<CalendarPlanningItem> _displayedPlanned = [];
    private IReadOnlyList<CalendarWorkItem> _displayedActual = [];

    private DateOnly _displayedMonth;
    private DateOnly _selectedDate;
    private IReadOnlyList<CalendarDayViewModel> _days = [];
    private IReadOnlyList<string> _weekdayLabels = [];
    private IReadOnlyList<CalendarEntryViewModel> _selectedTasks = [];
    private IReadOnlyList<CalendarEntryViewModel> _selectedPlanned = [];
    private IReadOnlyList<CalendarEntryViewModel> _selectedActual = [];
    private long _selectedPlannedMinutes;
    private long _selectedActualMinutes;
    private bool _isLoading;

    public CalendarPageViewModel(
        TaskService tasks,
        INavigationService navigation,
        ILocalizationService localization)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        var today = DateOnly.FromDateTime(DateTime.Today);
        _selectedDate = today;
        _displayedMonth = new DateOnly(today.Year, today.Month, 1);

        _localization.PropertyChanged += OnLocalizationChanged;
        RefreshWeekdayLabels();
        BuildDays();
    }

    public DateOnly DisplayedMonth
    {
        get => _displayedMonth;
        private set
        {
            if (!SetProperty(ref _displayedMonth, value)) return;
            OnPropertyChanged(nameof(MonthTitle));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            PreviousMonthCommand.NotifyCanExecuteChanged();
            NextMonthCommand.NotifyCanExecuteChanged();
        }
    }

    public DateOnly SelectedDate
    {
        get => _selectedDate;
        private set
        {
            if (!SetProperty(ref _selectedDate, value)) return;
            OnPropertyChanged(nameof(SelectedDayTitle));
        }
    }

    public IReadOnlyList<CalendarDayViewModel> Days
    {
        get => _days;
        private set => SetProperty(ref _days, value);
    }

    public IReadOnlyList<string> WeekdayLabels
    {
        get => _weekdayLabels;
        private set => SetProperty(ref _weekdayLabels, value);
    }

    public IReadOnlyList<CalendarEntryViewModel> SelectedTasks
    {
        get => _selectedTasks;
        private set => SetProperty(ref _selectedTasks, value);
    }

    public IReadOnlyList<CalendarEntryViewModel> SelectedPlanned
    {
        get => _selectedPlanned;
        private set => SetProperty(ref _selectedPlanned, value);
    }

    public IReadOnlyList<CalendarEntryViewModel> SelectedActual
    {
        get => _selectedActual;
        private set => SetProperty(ref _selectedActual, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsSelectedDayEmpty));
            OnPropertyChanged(nameof(IsMonthEmpty));
        }
    }

    public bool CanGoPrevious => DisplayedMonth != DateOnly.MinValue;
    public bool CanGoNext => DisplayedMonth.Year != 9999 || DisplayedMonth.Month != 12;
    public bool HasLoadError => _loadError is not null;

    // A same-month refresh must not hide a successfully loaded panel.
    public bool IsLoaded => _hasLoaded && !HasLoadError;

    public bool HasSelectedTasks => SelectedTasks.Count > 0;
    public bool HasSelectedPlanned => SelectedPlanned.Count > 0;
    public bool HasSelectedActual => SelectedActual.Count > 0;

    public bool IsSelectedDayEmpty => IsLoaded &&
        !HasSelectedTasks && !HasSelectedPlanned && !HasSelectedActual;

    public bool IsMonthEmpty => IsLoaded && !Days.Any(day =>
        day.IsCurrentMonth && day.HasEvents);

    public long SelectedPlannedMinutes => _selectedPlannedMinutes;
    public long SelectedActualMinutes => _selectedActualMinutes;

    public string SelectedPlannedText => CalendarDurationText.Format(
        SelectedPlannedMinutes, _localization);
    public string SelectedActualText => CalendarDurationText.Format(
        SelectedActualMinutes, _localization);

    private CultureInfo Culture =>
        CultureInfo.GetCultureInfo(_localization.CurrentLanguage);
    private string Text(string key) => _localization.GetString(key);

    public string TitleLabel => Text("NavCalendar");
    public string MonthTitle => DisplayedMonth.ToString("MMMM yyyy", Culture);
    public string SelectedDayTitle => SelectedDate.ToString("D", Culture);
    public string TodayLabel => Text("CalendarToday");
    public string PreviousMonthLabel => Text("CalendarPreviousMonth");
    public string NextMonthLabel => Text("CalendarNextMonth");
    public string SelectedDayLabel => Text("CalendarSelectedDay");
    public string TasksLabel => Text("CalendarTasks");
    public string PlannedLabel => Text("CalendarPlannedWork");
    public string ActualLabel => Text("CalendarActualWork");
    public string TotalPlannedLabel => Text("CalendarTotalPlanned");
    public string TotalActualLabel => Text("CalendarTotalActual");
    public string LoadingLabel => Text("CalendarLoading");
    public string RetryLabel => Text("CalendarRetry");
    public string NoEntriesLabel => Text("CalendarNoEntries");
    public string NoMonthDataLabel => Text("CalendarNoMonthData");
    public string NoTasksLabel => Text("CalendarNoTasks");
    public string NoPlannedLabel => Text("CalendarNoPlanned");
    public string NoActualLabel => Text("CalendarNoActual");
    public string OpenTaskLabel => Text("CalendarOpenTask");

    public string ErrorText => _loadError is null
        ? string.Empty
        : string.Format(Culture, Text("CalendarLoadFailed"), _loadError.Message);

    /// <summary>
    /// Called when Calendar becomes visible or Task Details closes.
    /// </summary>
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
        _loadCancellation?.Cancel();
        IsLoading = false;
    }

    private void RefreshWeekdayLabels()
    {
        // Both English and Russian use Monday-first on this screen.
        WeekdayLabels = Enumerable.Range(0, 7)
            .Select(index => Culture.DateTimeFormat.GetAbbreviatedDayName(
                (DayOfWeek)((index + 1) % 7)))
            .ToArray();
    }

    // Create cells only when the displayed month changes.
    private void BuildDays()
    {
        var offset = ((int)DisplayedMonth.DayOfWeek + 6) % 7;
        var firstDayNumber = DisplayedMonth.DayNumber - offset;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var days = new List<CalendarDayViewModel>(42);

        for (var index = 0; index < 42; index++)
        {
            var dayNumber = firstDayNumber + index;
            DateOnly? date = dayNumber >= DateOnly.MinValue.DayNumber &&
                             dayNumber <= DateOnly.MaxValue.DayNumber
                ? DateOnly.FromDayNumber(dayNumber)
                : null;

            days.Add(new CalendarDayViewModel(
                date, DisplayedMonth, SelectedDate, today,
                0, 0, 0, _localization, SelectDay));
        }

        Days = days;
        OnPropertyChanged(nameof(IsMonthEmpty));
    }

    // Update indicators without replacing calendar cells.
    private void UpdateDayIndicators(CalendarData? data)
    {
        var totals = new Dictionary<DateOnly, DayTotals>();

        if (data is not null)
        {
            foreach (var item in data.Tasks)
                GetTotals(totals, item.TaskDate).TaskCount++;

            foreach (var item in data.PlannedWork)
            {
                var day = GetTotals(totals, item.PlannedDate);
                day.PlannedMinutes = checked(
                    day.PlannedMinutes + item.PlannedMinutes);
            }

            foreach (var item in data.ActualWork)
            {
                var day = GetTotals(totals, item.WorkDate);
                day.ActualMinutes = checked(
                    day.ActualMinutes + item.ActualMinutes);
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        foreach (var day in Days)
        {
            var values = day.Date is DateOnly date &&
                         totals.TryGetValue(date, out var found)
                ? found
                : null;

            day.UpdateTotals(
                values?.TaskCount ?? 0,
                values?.PlannedMinutes ?? 0,
                values?.ActualMinutes ?? 0);

            day.UpdateToday(today);
        }

        OnPropertyChanged(nameof(IsMonthEmpty));
    }

    private static DayTotals GetTotals(
        Dictionary<DateOnly, DayTotals> totals, DateOnly date)
    {
        if (!totals.TryGetValue(date, out var result))
        {
            result = new DayTotals();
            totals.Add(date, result);
        }

        return result;
    }

    private sealed class DayTotals
    {
        public int TaskCount;
        public long PlannedMinutes;
        public long ActualMinutes;
    }

    private void UpdateSelectedDay()
    {
        foreach (var day in Days)
            day.IsSelected = day.Date == SelectedDate;

        var tasks = _data?.Tasks
            .Where(x => x.TaskDate == SelectedDate).ToArray() ?? [];

        var planned = _data?.PlannedWork
            .Where(x => x.PlannedDate == SelectedDate).ToArray() ?? [];

        var actual = _data?.ActualWork
            .Where(x => x.WorkDate == SelectedDate).ToArray() ?? [];

        if (!SameTasks(_displayedTasks, tasks))
        {
            SelectedTasks = tasks.Select(x =>
                CalendarEntryViewModel.FromTask(
                    x, _localization, OpenTask)).ToArray();

            _displayedTasks = tasks;
            OnPropertyChanged(nameof(HasSelectedTasks));
        }

        if (!SamePlanned(_displayedPlanned, planned))
        {
            SelectedPlanned = planned.Select(x =>
                CalendarEntryViewModel.FromPlanning(
                    x, _localization, OpenTask)).ToArray();

            _displayedPlanned = planned;
            OnPropertyChanged(nameof(HasSelectedPlanned));
        }

        if (!SameActual(_displayedActual, actual))
        {
            SelectedActual = actual.Select(x =>
                CalendarEntryViewModel.FromWork(
                    x, _localization, OpenTask)).ToArray();

            _displayedActual = actual;
            OnPropertyChanged(nameof(HasSelectedActual));
        }

        var plannedMinutes = planned.Sum(x => (long)x.PlannedMinutes);

        if (_selectedPlannedMinutes != plannedMinutes)
        {
            _selectedPlannedMinutes = plannedMinutes;
            OnPropertyChanged(nameof(SelectedPlannedMinutes));
            OnPropertyChanged(nameof(SelectedPlannedText));
        }

        var actualMinutes = actual.Sum(x => (long)x.ActualMinutes);

        if (_selectedActualMinutes != actualMinutes)
        {
            _selectedActualMinutes = actualMinutes;
            OnPropertyChanged(nameof(SelectedActualMinutes));
            OnPropertyChanged(nameof(SelectedActualText));
        }

        OnPropertyChanged(nameof(IsSelectedDayEmpty));
    }

    // Compare actual displayed fields so unchanged sections keep their VMs.
    private static bool SameTasks(
        IReadOnlyList<CalendarTaskItem> left,
        IReadOnlyList<CalendarTaskItem> right) =>
        left.Select(x => (
            x.TaskId, x.TaskDate, x.TaskNumber, x.TaskTitle,
            x.StatusId, x.StatusName, x.StatusSystemKey, x.IsCompleted))
        .SequenceEqual(right.Select(x => (
            x.TaskId, x.TaskDate, x.TaskNumber, x.TaskTitle,
            x.StatusId, x.StatusName, x.StatusSystemKey, x.IsCompleted)));

    private static bool SamePlanned(
        IReadOnlyList<CalendarPlanningItem> left,
        IReadOnlyList<CalendarPlanningItem> right) =>
        left.Select(x => (
            x.Id, x.TaskId, x.PlannedDate, x.PlannedMinutes,
            x.TaskNumber, x.TaskTitle, x.StatusId,
            x.StatusName, x.StatusSystemKey, x.IsCompleted))
        .SequenceEqual(right.Select(x => (
            x.Id, x.TaskId, x.PlannedDate, x.PlannedMinutes,
            x.TaskNumber, x.TaskTitle, x.StatusId,
            x.StatusName, x.StatusSystemKey, x.IsCompleted)));

    private static bool SameActual(
        IReadOnlyList<CalendarWorkItem> left,
        IReadOnlyList<CalendarWorkItem> right) =>
        left.Select(x => (
            x.Id, x.TaskId, x.WorkDate, x.ActualMinutes,
            x.WorkDescription, x.Note, x.TaskNumber, x.TaskTitle,
            x.StatusId, x.StatusName, x.StatusSystemKey, x.IsCompleted))
        .SequenceEqual(right.Select(x => (
            x.Id, x.TaskId, x.WorkDate, x.ActualMinutes,
            x.WorkDescription, x.Note, x.TaskNumber, x.TaskTitle,
            x.StatusId, x.StatusName, x.StatusSystemKey, x.IsCompleted)));

    private void SelectDay(DateOnly date)
    {
        if (_disposed) return;

        if (date.Year != DisplayedMonth.Year ||
            date.Month != DisplayedMonth.Month)
        {
            SwitchMonth(
                new DateOnly(date.Year, date.Month, 1),
                date);
            return;
        }

        if (SelectedDate == date) return;

        SelectedDate = date;
        UpdateSelectedDay();
    }

    private void SwitchMonth(DateOnly month, DateOnly selectedDate)
    {
        // Invalidate pending queries before changing the month.
        ++_loadVersion;
        _loadCancellation?.Cancel();
        IsLoading = false;

        // Never display another month's data under a new heading.
        _data = null;
        _hasLoaded = false;
        _loadError = null;

        DisplayedMonth = month;
        SelectedDate = selectedDate;

        BuildDays();
        UpdateSelectedDay();
        NotifyLoadState();

        _ = ReloadAsync();
    }

    private void OpenTask(Guid taskId)
    {
        if (!_disposed && _isActive && taskId != Guid.Empty)
            _navigation.OpenTask(taskId);
    }

    private bool CanPreviousMonth() => CanGoPrevious;

    [RelayCommand(CanExecute = nameof(CanPreviousMonth))]
    private void PreviousMonth() => MoveMonth(-1);

    private bool CanNextMonth() => CanGoNext;

    [RelayCommand(CanExecute = nameof(CanNextMonth))]
    private void NextMonth() => MoveMonth(1);

    private void MoveMonth(int delta)
    {
        if (_disposed ||
            (delta < 0 && !CanGoPrevious) ||
            (delta > 0 && !CanGoNext))
            return;

        var month = DisplayedMonth.AddMonths(delta);
        var day = Math.Min(
            SelectedDate.Day,
            DateTime.DaysInMonth(month.Year, month.Month));

        SwitchMonth(
            month,
            new DateOnly(month.Year, month.Month, day));
    }

    [RelayCommand]
    private void GoToToday()
    {
        if (_disposed) return;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);

        if (month != DisplayedMonth)
        {
            SwitchMonth(month, today);
            return;
        }

        SelectedDate = today;
        UpdateSelectedDay();

        foreach (var day in Days)
            day.UpdateToday(today);
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

        // Keep already loaded data while refreshing this same month.
        _loadError = null;
        IsLoading = true;
        NotifyLoadState();

        var offset = ((int)DisplayedMonth.DayOfWeek + 6) % 7;
        var firstDayNumber = DisplayedMonth.DayNumber - offset;
        var startDayNumber = Math.Max(
            firstDayNumber,
            DateOnly.MinValue.DayNumber);
        var endDayNumber = Math.Min(
            firstDayNumber + 41,
            DateOnly.MaxValue.DayNumber);

        var from = DateOnly.FromDayNumber(startDayNumber);
        var to = DateOnly.FromDayNumber(endDayNumber);

        try
        {
            var data = await Task.Run(
                () => _tasks.GetCalendarDataAsync(
                    from, to, source.Token),
                source.Token);

            if (!IsCurrentLoad(source, requestVersion))
                return;

            _data = data;

            UpdateDayIndicators(data);
            UpdateSelectedDay();

            _hasLoaded = true;
        }
        catch (OperationCanceledException)
            when (source.IsCancellationRequested)
        {
            // Replaced by a newer query or canceled on navigation.
        }
        catch (Exception ex)
        {
            if (IsCurrentLoad(source, requestVersion))
            {
                _loadError = ex;
                _hasLoaded = false;
                _data = null;

                // Failed refreshes must not masquerade as fresh data.
                UpdateDayIndicators(null);
                UpdateSelectedDay();
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

    private bool IsCurrentLoad(
        CancellationTokenSource source,
        int version) =>
        !_disposed &&
        _isActive &&
        _loadVersion == version &&
        ReferenceEquals(_loadCancellation, source);

    private void NotifyLoadState()
    {
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(IsMonthEmpty));
        OnPropertyChanged(nameof(IsSelectedDayEmpty));
    }

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_disposed ||
            (!string.IsNullOrEmpty(e.PropertyName) &&
             e.PropertyName != nameof(
                 ILocalizationService.CurrentLanguage)))
            return;

        RefreshWeekdayLabels();

        foreach (var day in Days)
            day.RefreshLocalization();

        foreach (var item in SelectedTasks)
            item.RefreshLocalization();

        foreach (var item in SelectedPlanned)
            item.RefreshLocalization();

        foreach (var item in SelectedActual)
            item.RefreshLocalization();

        string[] names =
        [
            nameof(TitleLabel),
            nameof(MonthTitle),
            nameof(SelectedDayTitle),
            nameof(TodayLabel),
            nameof(PreviousMonthLabel),
            nameof(NextMonthLabel),
            nameof(SelectedDayLabel),
            nameof(TasksLabel),
            nameof(PlannedLabel),
            nameof(ActualLabel),
            nameof(TotalPlannedLabel),
            nameof(TotalActualLabel),
            nameof(SelectedPlannedText),
            nameof(SelectedActualText),
            nameof(LoadingLabel),
            nameof(RetryLabel),
            nameof(NoEntriesLabel),
            nameof(NoMonthDataLabel),
            nameof(NoTasksLabel),
            nameof(NoPlannedLabel),
            nameof(NoActualLabel),
            nameof(OpenTaskLabel),
            nameof(ErrorText)
        ];

        foreach (var name in names)
            OnPropertyChanged(name);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _localization.PropertyChanged -= OnLocalizationChanged;

        ++_loadVersion;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
        _isActive = false;
    }
}