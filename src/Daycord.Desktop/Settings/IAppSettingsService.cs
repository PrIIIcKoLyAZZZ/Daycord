using Daycord.Core.Application.Models;

namespace Daycord.Desktop.Settings;

/// <summary>
/// Application-wide source of truth for persisted user preferences.
/// Consumers should subscribe to SettingsChanged when they need
/// to react to runtime changes.
/// </summary>
public interface IAppSettingsService
{
    AppSettings Current { get; }

    bool IsLoaded { get; }

    event EventHandler? SettingsChanged;

    Task LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}