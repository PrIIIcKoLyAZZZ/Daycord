using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Domain.Entities;

namespace Daycord.Desktop.ViewModels;

/// <summary>
/// Presentation model for an individual planning period.
/// Preserves the persisted identity of the period.
/// </summary>
public sealed class PlanningPeriodRowViewModel
{
    private readonly Action<PlanningPeriodRowViewModel>
        _editAction;

    private readonly Action<PlanningPeriodRowViewModel>
        _deleteAction;

    public PlanningPeriodRowViewModel(
        PlanningPeriod period,
        Action<PlanningPeriodRowViewModel> editAction,
        Action<PlanningPeriodRowViewModel> deleteAction)
    {
        ArgumentNullException.ThrowIfNull(period);

        _editAction = editAction
            ?? throw new ArgumentNullException(
                nameof(editAction));

        _deleteAction = deleteAction
            ?? throw new ArgumentNullException(
                nameof(deleteAction));

        if (period.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "Planning period ID must not be empty.",
                nameof(period));
        }

        if (period.TaskId == Guid.Empty)
        {
            throw new ArgumentException(
                "Task ID must not be empty.",
                nameof(period));
        }

        if (period.PlannedDate == default)
        {
            throw new ArgumentException(
                "Planning date must be valid.",
                nameof(period));
        }

        if (period.PlannedMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(period),
                "Planned minutes must be greater than zero.");
        }

        Id = period.Id;
        TaskId = period.TaskId;
        PlannedDate = period.PlannedDate;
        PlannedMinutes = period.PlannedMinutes;

        EditCommand = new RelayCommand(
            () => _editAction(this));

        DeleteCommand = new RelayCommand(
            () => _deleteAction(this));
    }

    public Guid Id { get; }

    public Guid TaskId { get; }

    public DateOnly PlannedDate { get; }

    public int PlannedMinutes { get; }

    public string DateText =>
        PlannedDate.ToString(
            "dd.MM.yyyy",
            CultureInfo.InvariantCulture);

    public string DurationText =>
        PlannedDuration.Format(PlannedMinutes);

    public ICommand EditCommand { get; }

    public ICommand DeleteCommand { get; }
}