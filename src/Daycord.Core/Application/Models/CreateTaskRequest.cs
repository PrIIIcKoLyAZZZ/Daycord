namespace Daycord.Core.Application.Models;

public sealed record CreateTaskRequest(
    DateOnly? TaskDate,
    string Title,
    string? ShortDescription = null,
    string? DescriptionMarkdown = null,
    Guid? StatusId = null,
    string? TaskNumber = null,
    int? PlannedMinutes = null);