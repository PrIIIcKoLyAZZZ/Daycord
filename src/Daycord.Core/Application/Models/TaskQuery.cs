namespace Daycord.Core.Application.Models;

public enum TaskSortField
{
    TaskDate,
    Title,
    Status
}

public enum TaskSortDirection
{
    Ascending,
    Descending
}

public sealed record TaskQuery
{
    public string? SearchText { get; init; }

    public Guid? StatusId { get; init; }

    public DateOnly? TaskDate { get; init; }

    public DateOnly? DateFrom { get; init; }

    public DateOnly? DateTo { get; init; }

    public TaskSortField SortBy { get; init; } =
        TaskSortField.TaskDate;

    public TaskSortDirection Direction { get; init; } =
        TaskSortDirection.Descending;
}