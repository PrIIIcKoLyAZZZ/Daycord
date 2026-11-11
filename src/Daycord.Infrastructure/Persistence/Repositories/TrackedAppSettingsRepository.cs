using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Infrastructure.Persistence.Repositories;

/// <summary>
/// Tracks successfully persisted application preferences.
/// Read operations do not advance the database revision.
/// </summary>
public sealed class TrackedAppSettingsRepository : IAppSettingsRepository
{
    private readonly SqliteAppSettingsRepository _inner;
    private readonly DatabaseChangeTracker _changes;

    public TrackedAppSettingsRepository(
        SqliteAppSettingsRepository inner,
        DatabaseChangeTracker changes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    public Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return _inner.LoadAsync(cancellationToken);
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        await _inner.SaveAsync(settings, cancellationToken);

        // The SQLite UPDATE completed successfully.
        _changes.MarkCommittedChange();
    }
}