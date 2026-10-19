using TaskStatus = Daycord.Core.Domain.Enums.TaskStatus;

namespace Daycord.Core.Domain.Entities;

public class TaskItem
{
    public Guid Id { get; set; }

    public DateOnly TaskDate { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? ShortDescription { get; set; }

    public string? DescriptionMarkdown { get; set; }

    public TaskStatus Status { get; set; } = TaskStatus.Open;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}