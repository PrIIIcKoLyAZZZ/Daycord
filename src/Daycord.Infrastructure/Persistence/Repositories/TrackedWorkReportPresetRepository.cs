using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Infrastructure.Persistence.Repositories;

/// <summary>
/// Tracks completed report-preset mutations without modifying the
/// underlying SQLite persistence or its transaction boundaries.
/// </summary>
public sealed class TrackedWorkReportPresetRepository
    : IWorkReportPresetRepository
{
    private readonly SqliteWorkReportPresetRepository _inner;
    private readonly DatabaseChangeTracker _changes;

    public TrackedWorkReportPresetRepository(
        SqliteWorkReportPresetRepository inner,
        DatabaseChangeTracker changes)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    public Task<WorkReportPresetState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        return _inner.GetStateAsync(cancellationToken);
    }

    public async Task SavePresetAsync(
        WorkReportPreset preset,
        CancellationToken cancellationToken = default)
    {
        await _inner.SavePresetAsync(preset, cancellationToken);
        _changes.MarkCommittedChange();
    }

    public async Task SetActivePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        await _inner.SetActivePresetAsync(presetId, cancellationToken);
        _changes.MarkCommittedChange();
    }

    public async Task DeletePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        await _inner.DeletePresetAsync(presetId, cancellationToken);
        _changes.MarkCommittedChange();
    }

    public async Task ResetToDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        await _inner.ResetToDefaultAsync(cancellationToken);
        _changes.MarkCommittedChange();
    }
}