namespace Daycord.Core.Domain.Entities;

public class PlanningPeriod
{
    public Guid Id { get; set; }
    
    public Guid TaskId { get; set; }
    
    public DateOnly PlannedDate { get; set; }
    
    public int PlannedMinutes { get; set; }
}