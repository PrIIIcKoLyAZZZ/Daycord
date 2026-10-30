using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Contracts;

/// <summary>
/// Reads and persists the singleton application settings in SQLite.
/// The database must be initialized before the first read.
/// </summary>
public interface IAppSettingsRepository
{
    Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}