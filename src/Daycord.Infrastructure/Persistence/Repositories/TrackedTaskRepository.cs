using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Core.Domain.Entities;

namespace Daycord.Infrastructure.Persistence.Repositories;

/// <summary>
/// Records successful task-related writes for automatic backup scheduling.
/// The underlying repository owns transaction boundaries; only its
/// successfully completed calls may advance the in-memory revision.
/// </summary>
public sealed class TrackedTaskRepository : ITaskRepository
{
    private readonly SqliteTaskRepository _inner;
    private readonly DatabaseChangeTracker _changes;

    public TrackedTaskRepository(
        SqliteTaskRepository inner,
        DatabaseChangeTracker changes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    public async Task CreateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default)
    {
        await _inner.CreateAsync(task, cancellationToken);
        _changes.MarkCommittedChange();
    }

    public async Task CreateWithPlanningAsync(
        TaskItem task,
        PlanningPeriod planningPeriod,
        CancellationToken cancellationToken = default)
    {
        await _inner.CreateWithPlanningAsync(task, planningPeriod, cancellationToken);
        _changes.MarkCommittedChange();
    }

    public Task<TaskItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _inner.GetByIdAsync(id, cancellationToken);

    public Task<bool> UpdateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.UpdateAsync(task, cancellationToken));

    public Task<IReadOnlyList<PlanningPeriod>> ListPlanningPeriodsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        _inner.ListPlanningPeriodsAsync(taskId, cancellationToken);

    public Task<bool> AddPlanningPeriodAsync(
        PlanningPeriod period,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.AddPlanningPeriodAsync(period, cancellationToken));

    public Task<bool> UpdatePlanningPeriodAsync(
        PlanningPeriod period,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.UpdatePlanningPeriodAsync(period, cancellationToken));

    public Task<bool> DeletePlanningPeriodAsync(
        Guid taskId,
        Guid periodId,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.DeletePlanningPeriodAsync(taskId, periodId, cancellationToken));

    public Task<IReadOnlyList<WorkEntry>> ListWorkEntriesAsync(
        Guid taskId,
        CancellationToken cancellationToken = default) =>
        _inner.ListWorkEntriesAsync(taskId, cancellationToken);

    public Task<bool> AddWorkEntryAsync(
        WorkEntry entry,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.AddWorkEntryAsync(entry, cancellationToken));

    public Task<bool> UpdateWorkEntryAsync(
        WorkEntry entry,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.UpdateWorkEntryAsync(entry, cancellationToken));

    public Task<bool> DeleteWorkEntryAsync(
        Guid taskId,
        Guid entryId,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.DeleteWorkEntryAsync(taskId, entryId, cancellationToken));

    public Task<bool> UpdateWithPlannedTimeAsync(
        TaskItem task,
        int? plannedMinutes,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.UpdateWithPlannedTimeAsync(
            task, plannedMinutes, cancellationToken));

    public Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        MarkIfChangedAsync(_inner.DeleteAsync(id, cancellationToken));

    public Task<CalendarData> GetCalendarDataAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        _inner.GetCalendarDataAsync(from, to, cancellationToken);

    public Task<IReadOnlyList<TaskListItem>> ListAsync(
        TaskQuery query,
        CancellationToken cancellationToken = default) =>
        _inner.ListAsync(query, cancellationToken);

    public Task<IReadOnlyList<TaskStatusDefinition>> ListStatusesAsync(
        CancellationToken cancellationToken = default) =>
        _inner.ListStatusesAsync(cancellationToken);

    public Task<TaskStatusDefinition?> GetStatusByIdAsync(
        Guid statusId,
        CancellationToken cancellationToken = default) =>
        _inner.GetStatusByIdAsync(statusId, cancellationToken);

    public Task<TaskStatusDefinition?> GetOpenStatusAsync(
        CancellationToken cancellationToken = default) =>
        _inner.GetOpenStatusAsync(cancellationToken);

    private async Task<bool> MarkIfChangedAsync(Task<bool> operation)
    {
        var changed = await operation;

        if (changed)
            _changes.MarkCommittedChange();

        return changed;
    }
}