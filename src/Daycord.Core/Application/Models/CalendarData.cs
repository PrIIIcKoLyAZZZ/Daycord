namespace Daycord.Core.Application.Models;

/// <summary>
/// Read-only calendar data within an inclusive date range.
/// Each collection follows its own date: TaskDate, PlannedDate or WorkDate.
/// </summary>
public sealed class CalendarData
{
    public IReadOnlyList<CalendarTaskItem> Tasks { get; init; } = [];
    public IReadOnlyList<CalendarPlanningItem> PlannedWork { get; init; } = [];
    public IReadOnlyList<CalendarWorkItem> ActualWork { get; init; } = [];
}

public sealed class CalendarTaskItem
{
    public Guid TaskId { get; set; }
    public DateOnly TaskDate { get; set; }
    public string? TaskNumber { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public Guid StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusSystemKey { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed class CalendarPlanningItem
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public DateOnly PlannedDate { get; set; }
    public int PlannedMinutes { get; set; }
    public string? TaskNumber { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public Guid StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusSystemKey { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed class CalendarWorkItem
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public DateOnly WorkDate { get; set; }
    public int ActualMinutes { get; set; }
    public string? WorkDescription { get; set; }
    public string? Note { get; set; }
    public string? TaskNumber { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public Guid StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StatusSystemKey { get; set; }
    public bool IsCompleted { get; set; }
}