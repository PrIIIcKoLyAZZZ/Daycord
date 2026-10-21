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