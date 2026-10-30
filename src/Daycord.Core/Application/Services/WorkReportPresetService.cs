using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Services;

/// <summary>
/// Application-level operations for saved report presets.
/// </summary>
public sealed class WorkReportPresetService
{
    private readonly IWorkReportPresetRepository _repository;

    public WorkReportPresetService(
        IWorkReportPresetRepository repository)
    {
        _repository = repository
            ?? throw new ArgumentNullException(nameof(repository));
    }

    public Task<WorkReportPresetState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        return _repository.GetStateAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a user preset without modifying other presets.
    /// </summary>
    public async Task<WorkReportPresetState> CreatePresetAsync(
        string name,
        IReadOnlyCollection<WorkReportColumn> columns,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var preset = new WorkReportPreset(
            Guid.NewGuid(),
            name,
            false,
            columns);

        await _repository.SavePresetAsync(
            preset,
            cancellationToken);

        return await _repository.GetStateAsync(
            cancellationToken);
    }

    /// <summary>
    /// Saves renamed, reordered or modified columns.
    /// Changes to an active preset persist immediately.
    /// </summary>
    public async Task<WorkReportPresetState> UpdatePresetAsync(
        Guid presetId,
        string name,
        IReadOnlyCollection<WorkReportColumn> columns,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _repository.GetStateAsync(
            cancellationToken);

        var existing = state.Presets.FirstOrDefault(
            preset => preset.Id == presetId);

        if (existing is null)
        {
            throw new KeyNotFoundException(
                $"Report preset '{presetId}' was not found.");
        }

        var updated = new WorkReportPreset(
            existing.Id,
            name,
            existing.IsBuiltIn,
            columns);

        await _repository.SavePresetAsync(
            updated,
            cancellationToken);

        return await _repository.GetStateAsync(
            cancellationToken);
    }

    /// <summary>
    /// Selects a preset and persists that selection.
    /// </summary>
    public async Task<WorkReportPresetState> ActivatePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _repository.SetActivePresetAsync(
            presetId,
            cancellationToken);

        return await _repository.GetStateAsync(
            cancellationToken);
    }

    /// <summary>
    /// Deletes a custom preset. The repository protects
    /// the built-in preset and maintains active selection.
    /// </summary>
    public async Task<WorkReportPresetState> DeletePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (presetId == WorkReportPreset.DefaultPresetId)
        {
            throw new InvalidOperationException(
                "The built-in report preset cannot be deleted.");
        }

        await _repository.DeletePresetAsync(
            presetId,
            cancellationToken);

        return await _repository.GetStateAsync(
            cancellationToken);
    }

    /// <summary>
    /// Restores the standard contract-work report.
    /// Custom presets remain saved for future reuse.
    /// </summary>
    public async Task<WorkReportPresetState> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _repository.ResetToDefaultAsync(
            cancellationToken);

        return await _repository.GetStateAsync(
            cancellationToken);
    }
}