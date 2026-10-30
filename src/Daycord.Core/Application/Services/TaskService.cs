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
    
    public async Task<PlanningPeriod> AddPlanningPeriodAsync(
        Guid taskId,
        DateOnly plannedDate,
        int plannedMinutes,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidatePlanningInput(plannedDate, plannedMinutes);
        cancellationToken.ThrowIfCancellationRequested();

        await GetRequiredTaskAsync(
            taskId,
            cancellationToken);

        var period = new PlanningPeriod
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            PlannedDate = plannedDate,
            PlannedMinutes = plannedMinutes
        };

        var created = await _repository.AddPlanningPeriodAsync(
            period,
            cancellationToken);

        if (!created)
        {
            throw new KeyNotFoundException(
                $"Task '{taskId}' was not found.");
        }

        return period;
    }
    
    public async Task<PlanningPeriod> UpdatePlanningPeriodAsync(
        Guid taskId,
        Guid periodId,
        DateOnly plannedDate,
        int plannedMinutes,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidatePeriodId(periodId);
        ValidatePlanningInput(plannedDate, plannedMinutes);
        cancellationToken.ThrowIfCancellationRequested();

        var periods = await _repository.ListPlanningPeriodsAsync(
            taskId,
            cancellationToken);

        var existingPeriod = periods.FirstOrDefault(
            period => period.Id == periodId);

        if (existingPeriod is null)
        {
            throw new KeyNotFoundException(
                $"Planning period '{periodId}' was not found for task '{taskId}'.");
        }

        var updatedPeriod = new PlanningPeriod
        {
            Id = existingPeriod.Id,
            TaskId = existingPeriod.TaskId,
            PlannedDate = plannedDate,
            PlannedMinutes = plannedMinutes
        };

        var updated = await _repository.UpdatePlanningPeriodAsync(
            updatedPeriod,
            cancellationToken);

        if (!updated)
        {
            throw new KeyNotFoundException(
                $"Planning period '{periodId}' no longer exists.");
        }

        return updatedPeriod;
    }
    
    public async Task DeletePlanningPeriodAsync(
        Guid taskId,
        Guid periodId,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidatePeriodId(periodId);
        cancellationToken.ThrowIfCancellationRequested();

        var deleted = await _repository.DeletePlanningPeriodAsync(
            taskId,
            periodId,
            cancellationToken);

        if (!deleted)
        {
            throw new KeyNotFoundException(
                $"Planning period '{periodId}' was not found for task '{taskId}'.");
        }
    }
    
    public async Task<IReadOnlyList<WorkEntry>> ListWorkEntriesAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        cancellationToken.ThrowIfCancellationRequested();

        await GetRequiredTaskAsync(taskId, cancellationToken);

        return await _repository.ListWorkEntriesAsync(
            taskId,
            cancellationToken);
    }

    public async Task<WorkEntry> AddWorkEntryAsync(
        Guid taskId,
        DateOnly workDate,
        int actualMinutes,
        string? workDescription,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidateWorkEntryInput(workDate, actualMinutes);
        cancellationToken.ThrowIfCancellationRequested();

        await GetRequiredTaskAsync(taskId, cancellationToken);

        var now = DateTime.UtcNow;
        var entry = new WorkEntry
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            WorkDate = workDate,
            ActualMinutes = actualMinutes,
            WorkDescription = NormalizeOptionalText(workDescription),
            Note = NormalizeOptionalText(note),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (!await _repository.AddWorkEntryAsync(entry, cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Task '{taskId}' no longer exists.");
        }

        return entry;
    }

    public async Task<WorkEntry> UpdateWorkEntryAsync(
        Guid taskId,
        Guid entryId,
        DateOnly workDate,
        int actualMinutes,
        string? workDescription,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidateWorkEntryId(entryId);
        ValidateWorkEntryInput(workDate, actualMinutes);
        cancellationToken.ThrowIfCancellationRequested();

        var entries = await _repository.ListWorkEntriesAsync(
            taskId,
            cancellationToken);

        var existing = entries.FirstOrDefault(
            entry => entry.Id == entryId);

        if (existing is null)
        {
            throw new KeyNotFoundException(
                $"Work entry '{entryId}' was not found for task '{taskId}'.");
        }

        var updatedEntry = new WorkEntry
        {
            Id = existing.Id,
            TaskId = existing.TaskId,
            WorkDate = workDate,
            ActualMinutes = actualMinutes,
            WorkDescription = NormalizeOptionalText(workDescription),
            Note = NormalizeOptionalText(note),
            CreatedAtUtc = existing.CreatedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (!await _repository.UpdateWorkEntryAsync(
                updatedEntry,
                cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Work entry '{entryId}' no longer exists for task '{taskId}'.");
        }

        return updatedEntry;
    }

    public async Task DeleteWorkEntryAsync(
        Guid taskId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        ValidateTaskId(taskId);
        ValidateWorkEntryId(entryId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await _repository.DeleteWorkEntryAsync(
                taskId,
                entryId,
                cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Work entry '{entryId}' was not found for task '{taskId}'.");
        }
    }
    
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
        
        if (request.UpdateTaskNumber)
        {
            task.TaskNumber = NormalizeTaskNumber(
                request.TaskNumber);
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

        return _repository.DeleteAsync(
            taskId,
            cancellationToken);
    }
    
    public Task<CalendarData> GetCalendarDataAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (from > to)
        {
            throw new ArgumentException(
                "The calendar range start cannot be later than its end.",
                nameof(from));
        }

        return _repository.GetCalendarDataAsync(
            from,
            to,
            cancellationToken);
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
            SearchText = string.IsNullOrWhiteSpace(
                query.SearchText)
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

        return _repository.ListStatusesAsync(
            cancellationToken);
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

    private static void ValidatePlannedMinutes(
        int? plannedMinutes)
    {
        if (plannedMinutes.HasValue &&
            plannedMinutes.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedMinutes),
                "Planned minutes must be greater than zero.");
        }
    }

    private static void ValidatePlanningInput(
        DateOnly plannedDate,
        int plannedMinutes)
    {
        if (plannedDate == default)
        {
            throw new ArgumentException(
                "Planning date is required.",
                nameof(plannedDate));
        }

        ValidatePlannedMinutes(plannedMinutes);
    }

    private static void ValidateWorkEntryInput(
        DateOnly workDate,
        int actualMinutes)
    {
        if (workDate == default)
        {
            throw new ArgumentException(
                "Work date is required.",
                nameof(workDate));
        }

        if (actualMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualMinutes),
                "Actual minutes must be greater than zero.");
        }
    }

    private static void ValidateWorkEntryId(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException(
                "Work entry ID must not be empty.",
                nameof(entryId));
        }
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidatePeriodId(Guid periodId)
    {
        if (periodId == Guid.Empty)
        {
            throw new ArgumentException(
                "Planning period ID must not be empty.",
                nameof(periodId));
        }
    }

    private static void ValidateTaskDate(
        DateOnly? taskDate)
    {
        if (taskDate.HasValue &&
            taskDate.Value == default)
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