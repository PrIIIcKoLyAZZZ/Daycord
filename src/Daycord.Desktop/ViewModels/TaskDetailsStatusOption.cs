using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Core.Domain.Entities;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

public sealed class TaskDetailsStatusOption : ObservableObject
{
    private readonly ILocalizationService _localization;
    private readonly string? _resourceKey;
    private readonly string _name;

    public TaskDetailsStatusOption(
        TaskStatusDefinition status,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(status);

        _localization = localization
                        ?? throw new ArgumentNullException(nameof(localization));

        Id = status.Id;
        _name = status.Name;

        if (status.IsSystem)
        {
            _resourceKey = status.SystemKey?.ToLowerInvariant() switch
            {
                "open" => "TaskListStatusOpen",
                "completed" => "TaskListStatusCompleted",
                _ => null
            };
        }
    }

    public Guid Id { get; }

    public string Label =>
        _resourceKey is null
            ? _name
            : _localization.GetString(_resourceKey);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Label));
    }
}