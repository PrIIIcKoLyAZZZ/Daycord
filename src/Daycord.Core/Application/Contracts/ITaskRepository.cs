using Daycord.Core.Domain.Entities;

namespace Daycord.Core.Application.Contracts;

public interface ITaskRepository
{
    Task CreateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default);

    Task<TaskItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        TaskItem task,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Daycord.Core.Application.Models.TaskListItem>>
        ListAsync(
            Daycord.Core.Application.Models.TaskQuery query,
            CancellationToken cancellationToken = default);

    Task<TaskStatusDefinition?> GetStatusByIdAsync(
        Guid statusId,
        CancellationToken cancellationToken = default);

    Task<TaskStatusDefinition?> GetOpenStatusAsync(
        CancellationToken cancellationToken = default);
}