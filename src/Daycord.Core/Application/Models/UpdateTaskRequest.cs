namespace Daycord.Core.Application.Models;

public sealed record UpdateTaskRequest(
    Guid Id,
    DateOnly TaskDate,
    string Title,
    string? ShortDescription = null,
    string? DescriptionMarkdown = null,
    Guid? StatusId = null);