using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

/// <summary>A task reference in one of the three selected-day sections.</summary>
public sealed class CalendarEntryViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly long? _durationMinutes;
    private readonly string _statusName;
    private readonly string? _statusSystemKey;

    private CalendarEntryViewModel(
        Guid taskId,
        string? taskNumber,
        string taskTitle,
        string statusName,
        string? statusSystemKey,
        long? durationMinutes,
        string? workDescription,
        string? note,
        ILocalizationService localization,
        Action<Guid> openTask)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task ID is required.", nameof(taskId));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        ArgumentNullException.ThrowIfNull(openTask);

        TaskId = taskId;
        TaskNumber = taskNumber ?? string.Empty;
        TaskTitle = taskTitle;
        _statusName = statusName;
        _statusSystemKey = statusSystemKey;
        _durationMinutes = durationMinutes;
        WorkDescription = workDescription ?? string.Empty;
        Note = note ?? string.Empty;
        OpenTaskCommand = new RelayCommand(() => openTask(TaskId));
    }

    public static CalendarEntryViewModel FromTask(
        CalendarTaskItem item,
        ILocalizationService localization,
        Action<Guid> openTask) => new(
            item.TaskId, item.TaskNumber, item.TaskTitle,
            item.StatusName, item.StatusSystemKey, null, null, null,
            localization, openTask);

    public static CalendarEntryViewModel FromPlanning(
        CalendarPlanningItem item,
        ILocalizationService localization,
        Action<Guid> openTask) => new(
            item.TaskId, item.TaskNumber, item.TaskTitle,
            item.StatusName, item.StatusSystemKey, item.PlannedMinutes,
            null, null, localization, openTask);

    public static CalendarEntryViewModel FromWork(
        CalendarWorkItem item,
        ILocalizationService localization,
        Action<Guid> openTask) => new(
            item.TaskId, item.TaskNumber, item.TaskTitle,
            item.StatusName, item.StatusSystemKey, item.ActualMinutes,
            item.WorkDescription, item.Note, localization, openTask);

    public Guid TaskId { get; }
    public string TaskNumber { get; }
    public string TaskTitle { get; }
    public string WorkDescription { get; }
    public string Note { get; }

    public bool HasTaskNumber => !string.IsNullOrWhiteSpace(TaskNumber);
    public bool HasDuration => _durationMinutes.HasValue;
    public bool HasDescription => !string.IsNullOrWhiteSpace(WorkDescription);
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public string DurationText => _durationMinutes.HasValue
        ? CalendarDurationText.Format(_durationMinutes.Value, _localization)
        : string.Empty;

    public string StatusText => _statusSystemKey switch
    {
        "open" => _localization.GetString("TaskListStatusOpen"),
        "completed" => _localization.GetString("TaskListStatusCompleted"),
        _ => _statusName
    };

    public ICommand OpenTaskCommand { get; }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationText));
    }
}

/// <summary>Match the unit names already used in the Tasks table.</summary>
internal static class CalendarDurationText
{
    public static string Format(long minutes, ILocalizationService localization)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes));

        var hours = minutes / 60;
        var remaining = minutes % 60;
        var h = localization.GetString("TaskListHoursUnit");
        var m = localization.GetString("TaskListMinutesUnit");

        if (hours == 0)
            return $"{remaining.ToString(CultureInfo.InvariantCulture)}{m}";
        if (remaining == 0)
            return $"{hours.ToString(CultureInfo.InvariantCulture)}{h}";

        return $"{hours.ToString(CultureInfo.InvariantCulture)}{h} " +
               $"{remaining.ToString(CultureInfo.InvariantCulture)}{m}";
    }
}