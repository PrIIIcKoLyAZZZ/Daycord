using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

public sealed class CalendarDayViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private bool _isSelected;
    private bool _isToday;
    private int _taskCount;
    private long _plannedMinutes;
    private long _actualMinutes;

    public CalendarDayViewModel(
        DateOnly? date,
        DateOnly displayedMonth,
        DateOnly selectedDate,
        DateOnly today,
        int taskCount,
        long plannedMinutes,
        long actualMinutes,
        ILocalizationService localization,
        Action<DateOnly> onSelect)
    {
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        ArgumentNullException.ThrowIfNull(onSelect);

        Date = date;
        IsCurrentMonth = date.HasValue &&
            date.Value.Year == displayedMonth.Year &&
            date.Value.Month == displayedMonth.Month;
        _isToday = date == today;
        _isSelected = date == selectedDate;

        _taskCount = taskCount;
        _plannedMinutes = plannedMinutes;
        _actualMinutes = actualMinutes;

        SelectCommand = new RelayCommand(
            () =>
            {
                if (Date is DateOnly selected)
                    onSelect(selected);
            },
            () => Date.HasValue);
    }

    public DateOnly? Date { get; }
    public bool IsSelectable => Date.HasValue;
    public bool IsCurrentMonth { get; }
    public bool IsToday => _isToday;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public int TaskCount => _taskCount;
    public long PlannedMinutes => _plannedMinutes;
    public long ActualMinutes => _actualMinutes;
    public bool HasTasks => TaskCount > 0;
    public bool HasPlanned => PlannedMinutes > 0;
    public bool HasActual => ActualMinutes > 0;
    public bool HasEvents => HasTasks || HasPlanned || HasActual;

    public string DayNumberText => Date?.Day.ToString(CultureInfo.InvariantCulture)
        ?? string.Empty;
    public string TaskCountText => HasTasks
        ? TaskCount.ToString(CultureInfo.InvariantCulture)
        : string.Empty;
    public string PlannedText => HasPlanned
        ? CalendarDurationText.Format(PlannedMinutes, _localization)
        : string.Empty;
    public string ActualText => HasActual
        ? CalendarDurationText.Format(ActualMinutes, _localization)
        : string.Empty;

    public string SummaryText
    {
        get
        {
            if (Date is not DateOnly date)
                return string.Empty;

            var culture = CultureInfo.GetCultureInfo(_localization.CurrentLanguage);
            var parts = new List<string> { date.ToString("D", culture) };

            if (HasTasks)
                parts.Add($"{_localization.GetString("CalendarTasks")}: {TaskCount}");
            if (HasPlanned)
                parts.Add($"{_localization.GetString("CalendarPlannedWork")}: {PlannedText}");
            if (HasActual)
                parts.Add($"{_localization.GetString("CalendarActualWork")}: {ActualText}");

            return string.Join(" · ", parts);
        }
    }

    public ICommand SelectCommand { get; }

    /// <summary>Refresh indicators without replacing the calendar cell.</summary>
    public void UpdateTotals(int taskCount, long plannedMinutes, long actualMinutes)
    {
        var changed = false;

        if (SetProperty(ref _taskCount, taskCount, nameof(TaskCount)))
        {
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(TaskCountText));
            changed = true;
        }

        if (SetProperty(ref _plannedMinutes, plannedMinutes, nameof(PlannedMinutes)))
        {
            OnPropertyChanged(nameof(HasPlanned));
            OnPropertyChanged(nameof(PlannedText));
            changed = true;
        }

        if (SetProperty(ref _actualMinutes, actualMinutes, nameof(ActualMinutes)))
        {
            OnPropertyChanged(nameof(HasActual));
            OnPropertyChanged(nameof(ActualText));
            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(HasEvents));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public void UpdateToday(DateOnly today)
    {
        SetProperty(ref _isToday, Date == today, nameof(IsToday));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(PlannedText));
        OnPropertyChanged(nameof(ActualText));
        OnPropertyChanged(nameof(SummaryText));
    }
}