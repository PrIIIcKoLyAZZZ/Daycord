using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Core.Domain.Entities;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

public sealed class SessionTaskItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    public SessionTaskItemViewModel(
        TaskItem task,
        int? plannedMinutes,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(task);

        _localization = localization
            ?? throw new ArgumentNullException(
                nameof(localization));

        Id = task.Id;
        TaskDate = task.TaskDate;
        TaskNumber = task.TaskNumber;
        Title = task.Title;
        PlannedMinutes = plannedMinutes;
    }

    public Guid Id { get; }

    public DateOnly? TaskDate { get; }

    public string? TaskNumber { get; }

    public string Title { get; }

    public int? PlannedMinutes { get; }

    public bool HasTaskDate =>
        TaskDate.HasValue;

    public bool HasTaskNumber =>
        !string.IsNullOrWhiteSpace(TaskNumber);

    public bool HasPlannedTime =>
        PlannedMinutes.HasValue &&
        PlannedMinutes.Value > 0;

    public string TaskDateText
    {
        get
        {
            if (TaskDate is not DateOnly date)
            {
                return string.Empty;
            }

            var today = DateOnly.FromDateTime(
                DateTime.Today);

            if (date == today)
            {
                return _localization.GetString(
                    "QuickTaskToday");
            }

            if (date == today.AddDays(1))
            {
                return _localization.GetString(
                    "QuickTaskTomorrow");
            }

            return date.ToString(
                "d",
                CultureInfo.GetCultureInfo(
                    _localization.CurrentLanguage));
        }
    }

    public string TaskNumberText =>
        TaskNumber ?? string.Empty;

    public string PlannedTimeText
    {
        get
        {
            if (!HasPlannedTime)
            {
                return string.Empty;
            }

            var totalMinutes = PlannedMinutes!.Value;

            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;

            var isRussian = _localization.CurrentLanguage
                .StartsWith(
                    "ru",
                    StringComparison.OrdinalIgnoreCase);

            var hourSuffix = isRussian ? "ч" : "h";
            var minuteSuffix = isRussian ? "м" : "m";

            if (hours == 0)
            {
                return $"{minutes}{minuteSuffix}";
            }

            if (minutes == 0)
            {
                return $"{hours}{hourSuffix}";
            }

            return
                $"{hours}{hourSuffix} " +
                $"{minutes}{minuteSuffix}";
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(TaskDateText));
        OnPropertyChanged(nameof(PlannedTimeText));
    }
}