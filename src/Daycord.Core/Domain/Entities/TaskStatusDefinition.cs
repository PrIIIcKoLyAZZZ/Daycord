namespace Daycord.Core.Domain.Entities;

public class TaskStatusDefinition
{
    public static readonly Guid OpenId =
        Guid.Parse("5e8a148e-1a3b-4b17-bba6-251392a01d61");

    public static readonly Guid CompletedId =
        Guid.Parse("b9436dc3-7247-4d9e-a032-2a9cb1b9c7a8");

    public Guid Id { get; set; }

    public string? SystemKey { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public bool IsCompleted { get; set; }

    public int SortOrder { get; set; }
}