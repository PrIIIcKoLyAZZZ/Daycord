using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;
using Daycord.Core.Domain.Entities;

namespace Daycord.Core.Application.Services;

public sealed class TaskService
{
    private readonly ITaskRepository _repository;

    public TaskService(ITaskRepository repository)
    {
        _repository = repository
            ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<TaskItem> CreateAsync(
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateTaskDate(request.TaskDate);
        ValidatePlannedMinutes(request.PlannedMinutes);

        if (request.PlannedMinutes.HasValue &&
            !request.TaskDate.HasValue)
        {
            throw new ArgumentException(
                "A task date is required when planned time is specified.",
                nameof(request));
        }

        var title = NormalizeTitle(request.Title);
        var taskNumber = NormalizeTaskNumber(request.TaskNumber);

        TaskStatusDefinition status;

        if (request.StatusId.HasValue)
        {
            status = await GetRequiredStatusAsync(
                request.StatusId.Value,
                cancellationToken);
        }
        else
        {
            status = await _repository.GetOpenStatusAsync(
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "System Open status was not found.");

            if (status.Id != TaskStatusDefinition.OpenId ||
                status.IsCompleted)
            {
                throw new InvalidOperationException(
                    "System Open status is configured incorrectly.");
            }
        }

        var now = DateTime.UtcNow;

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            TaskDate = request.TaskDate,
            TaskNumber = taskNumber,
            Title = title,
            ShortDescription = request.ShortDescription,
            DescriptionMarkdown = request.DescriptionMarkdown,
            StatusId = status.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = status.IsCompleted ? now : null
        };

        if (request.PlannedMinutes is int plannedMinutes)
        {
            var plannedDate = request.TaskDate
                ?? throw new InvalidOperationException(
                    "Planned time requires a task date.");

            var planningPeriod = new PlanningPeriod
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                PlannedDate = plannedDate,
                PlannedMinutes = plannedMinutes
            };

            await _repository.CreateWithPlanningAsync(
                task,
                planningPeriod,
                cancellationToken);
        }
        else
        {
            await _repository.CreateAsync(
                task,
                cancellationToken);
        }

        return task;
    }

    public async Task<TaskItem?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(id);

        return await _repository.GetByIdAsync(
            id,
            cancellationToken);
    }

    public Task<IReadOnlyList<PlanningPeriod>> ListPlanningPeriodsAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        cancellationToken.ThrowIfCancellationRequested();

        return _repository.ListPlanningPeriodsAsync(
            taskId,
            cancellationToken);
    }

    // Existing callers preserve all planning records.
    public Task<TaskItem> UpdateAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        return UpdateCoreAsync(
            request,
            updatePlannedTime: false,
            plannedMinutes: null,
            cancellationToken: cancellationToken);
    }

    // Used when the full editor changes planned time.
    // Null means clear the single existing planning period.
    public Task<TaskItem> UpdateWithPlannedTimeAsync(
        UpdateTaskRequest request,
        int? plannedMinutes,
        CancellationToken cancellationToken = default)
    {
        return UpdateCoreAsync(
            request,
            updatePlannedTime: true,
            plannedMinutes: plannedMinutes,
            cancellationToken: cancellationToken);
    }

    private async Task<TaskItem> UpdateCoreAsync(
        UpdateTaskRequest request,
        bool updatePlannedTime,
        int? plannedMinutes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateTaskId(request.Id);
        ValidateTaskDate(request.TaskDate);

        if (updatePlannedTime)
        {
            ValidatePlannedMinutes(plannedMinutes);

            if (plannedMinutes.HasValue && !request.TaskDate.HasValue)
            {
                throw new ArgumentException(
                    "A task date is required when planned time is specified.",
                    nameof(request));
            }
        }

        var title = NormalizeTitle(request.Title);

        var task = await GetRequiredTaskAsync(
            request.Id,
            cancellationToken);

        var now = DateTime.UtcNow;

        if (request.StatusId.HasValue)
        {
            await ApplyStatusChangeAsync(
                task,
                request.StatusId.Value,
                now,
                cancellationToken);
        }

        task.TaskDate = request.TaskDate;
        task.Title = title;
        task.ShortDescription = request.ShortDescription;
        task.DescriptionMarkdown = request.DescriptionMarkdown;

        // Preserve TaskNumber for callers that do not edit it.
        if (request.UpdateTaskNumber)
        {
            task.TaskNumber = NormalizeTaskNumber(request.TaskNumber);
        }

        task.UpdatedAtUtc = now;

        var updated = updatePlannedTime
            ? await _repository.UpdateWithPlannedTimeAsync(
                task,
                plannedMinutes,
                cancellationToken)
            : await _repository.UpdateAsync(
                task,
                cancellationToken);

        if (!updated)
        {
            throw new KeyNotFoundException(
                $"Task '{request.Id}' was not found.");
        }

        return task;
    }

    public async Task<TaskItem> ChangeStatusAsync(
        Guid taskId,
        Guid statusId,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidateStatusId(statusId);
        cancellationToken.ThrowIfCancellationRequested();

        var task = await GetRequiredTaskAsync(
            taskId,
            cancellationToken);

        var now = DateTime.UtcNow;

        await ApplyStatusChangeAsync(
            task,
            statusId,
            now,
            cancellationToken);

        task.UpdatedAtUtc = now;

        var updated = await _repository.UpdateAsync(
            task,
            cancellationToken);

        if (!updated)
        {
            throw new KeyNotFoundException(
                $"Task '{taskId}' was not found.");
        }

        return task;
    }

    public Task<bool> DeleteAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        cancellationToken.ThrowIfCancellationRequested();

        return _repository.DeleteAsync(taskId, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskListItem>> ListAsync(
        TaskQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateQuery(query);

        var normalizedQuery = query with
        {
            SearchText = string.IsNullOrWhiteSpace(query.SearchText)
                ? null
                : query.SearchText.Trim()
        };

        return await _repository.ListAsync(
            normalizedQuery,
            cancellationToken);
    }

    public Task<IReadOnlyList<TaskStatusDefinition>> ListStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _repository.ListStatusesAsync(cancellationToken);
    }

    private async Task<TaskItem> GetRequiredTaskAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(
            id,
            cancellationToken);

        return task ?? throw new KeyNotFoundException(
            $"Task '{id}' was not found.");
    }

    private async Task<TaskStatusDefinition> GetRequiredStatusAsync(
        Guid statusId,
        CancellationToken cancellationToken)
    {
        ValidateStatusId(statusId);

        var status = await _repository.GetStatusByIdAsync(
            statusId,
            cancellationToken);

        return status ?? throw new ArgumentException(
            "Task status does not exist.",
            nameof(statusId));
    }

    private async Task ApplyStatusChangeAsync(
        TaskItem task,
        Guid newStatusId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var newStatus = await GetRequiredStatusAsync(
            newStatusId,
            cancellationToken);

        if (task.StatusId == newStatus.Id)
        {
            return;
        }

        var previousStatus = await GetRequiredStatusAsync(
            task.StatusId,
            cancellationToken);

        if (!newStatus.IsCompleted)
        {
            task.CompletedAtUtc = null;
        }
        else if (!previousStatus.IsCompleted)
        {
            task.CompletedAtUtc = now;
        }

        task.StatusId = newStatus.Id;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Task title is required.",
                nameof(title));
        }

        return title.Trim();
    }

    private static string? NormalizeTaskNumber(
        string? taskNumber)
    {
        return string.IsNullOrWhiteSpace(taskNumber)
            ? null
            : taskNumber.Trim();
    }

    private static void ValidatePlannedMinutes(int? plannedMinutes)
    {
        if (plannedMinutes.HasValue &&
            plannedMinutes.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedMinutes),
                "Planned minutes must be greater than zero.");
        }
    }

    private static void ValidateTaskDate(DateOnly? taskDate)
    {
        if (taskDate.HasValue && taskDate.Value == default)
        {
            throw new ArgumentException(
                "Task date is invalid.",
                nameof(taskDate));
        }
    }

    private static void ValidateTaskId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Task ID must not be empty.",
                nameof(id));
        }
    }

    private static void ValidateStatusId(Guid statusId)
    {
        if (statusId == Guid.Empty)
        {
            throw new ArgumentException(
                "Status ID must not be empty.",
                nameof(statusId));
        }
    }

    private static void ValidateQuery(TaskQuery query)
    {
        if (query.StatusId == Guid.Empty)
        {
            throw new ArgumentException(
                "Status ID must not be empty.",
                nameof(query));
        }

        if (query.DateFrom.HasValue &&
            query.DateTo.HasValue &&
            query.DateFrom.Value > query.DateTo.Value)
        {
            throw new ArgumentException(
                "Start date must not be later than end date.",
                nameof(query));
        }

        if (!Enum.IsDefined(query.SortBy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Unsupported task sort field.");
        }

        if (!Enum.IsDefined(query.Direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Unsupported sort direction.");
        }
    }
}