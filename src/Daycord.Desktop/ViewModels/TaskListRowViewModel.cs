using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Core.Application.Models;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

public sealed class TaskListRowViewModel : ObservableObject
{
    private readonly TaskListItem _task;
    private readonly ILocalizationService _localization;

    public TaskListRowViewModel(
        TaskListItem task,
        ILocalizationService localization)
    {
        _task = task
            ?? throw new ArgumentNullException(nameof(task));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
    }

    public Guid Id => _task.Id;

    public string Title => _task.Title;

    public string TaskNumberText =>
        _task.TaskNumber ?? string.Empty;

    public bool HasTaskNumber =>
        !string.IsNullOrWhiteSpace(_task.TaskNumber);

    public string ShortDescriptionText =>
        string.IsNullOrWhiteSpace(_task.ShortDescription)
            ? "—"
            : _task.ShortDescription;

    public string DateText =>
        _task.TaskDate is DateOnly date
            ? date.ToString(
                "d",
                CultureInfo.GetCultureInfo(
                    _localization.CurrentLanguage))
            : "—";

    public string PlannedText =>
        FormatDuration(_task.TotalPlannedMinutes);

    public string ActualText =>
        FormatDuration(_task.TotalActualMinutes);

    public string StatusText =>
        _task.StatusSystemKey switch
        {
            "open" =>
                _localization.GetString("TaskListStatusOpen"),

            "completed" =>
                _localization.GetString("TaskListStatusCompleted"),

            _ => _task.StatusName
        };

    public bool IsCompleted => _task.IsCompleted;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(PlannedText));
        OnPropertyChanged(nameof(ActualText));
        OnPropertyChanged(nameof(StatusText));
    }

    private string FormatDuration(long? totalMinutes)
    {
        if (!totalMinutes.HasValue)
        {
            return "—";
        }

        var hours = totalMinutes.Value / 60;
        var minutes = totalMinutes.Value % 60;

        var hourUnit = _localization.GetString(
            "TaskListHoursUnit");

        var minuteUnit = _localization.GetString(
            "TaskListMinutesUnit");

        if (hours == 0)
        {
            return $"{minutes}{minuteUnit}";
        }

        if (minutes == 0)
        {
            return $"{hours}{hourUnit}";
        }

        return $"{hours}{hourUnit} {minutes}{minuteUnit}";
    }
}