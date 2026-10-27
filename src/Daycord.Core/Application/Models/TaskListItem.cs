namespace Daycord.Core.Application.Models;

public sealed class TaskListItem
{
    public Guid Id { get; set; }

    public DateOnly? TaskDate { get; set; }

    public string? TaskNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    public Guid StatusId { get; set; }

    public string StatusName { get; set; } = string.Empty;
    
    public string? StatusSystemKey { get; set; }

    public bool IsCompleted { get; set; }
    
    public long? TotalPlannedMinutes { get; set; }
    
    public long? TotalActualMinutes { get; set; }
}