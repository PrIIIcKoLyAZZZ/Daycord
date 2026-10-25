using Daycord.Core.Application.Models;
using Daycord.Core.Domain.Entities;

namespace Daycord.Core.Application.Contracts;

public interface ITaskRepository
{
    Task CreateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default);

    Task CreateWithPlanningAsync(
        TaskItem task,
        PlanningPeriod planningPeriod,
        CancellationToken cancellationToken = default);

    Task<TaskItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanningPeriod>> ListPlanningPeriodsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task<bool> AddPlanningPeriodAsync(
        PlanningPeriod period,
        CancellationToken cancellationToken = default);

    Task<bool> UpdatePlanningPeriodAsync(
        PlanningPeriod period,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePlanningPeriodAsync(
        Guid taskId,
        Guid periodId,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateWithPlannedTimeAsync(
        TaskItem task,
        int? plannedMinutes,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskListItem>> ListAsync(
        TaskQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskStatusDefinition>> ListStatusesAsync(
        CancellationToken cancellationToken = default);

    Task<TaskStatusDefinition?> GetStatusByIdAsync(
        Guid statusId,
        CancellationToken cancellationToken = default);

    Task<TaskStatusDefinition?> GetOpenStatusAsync(
        CancellationToken cancellationToken = default);
}