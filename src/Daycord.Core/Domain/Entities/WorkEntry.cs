namespace Daycord.Core.Domain.Entities;

public class WorkEntry
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }

    public DateOnly WorkDate { get; set; }

    public int ActualMinutes { get; set; }

    public string? WorkDescription { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}