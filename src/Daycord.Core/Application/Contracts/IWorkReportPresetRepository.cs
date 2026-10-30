using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Contracts;

/// <summary>
/// Persistent storage for Excel report configurations.
///
/// Implementations must preserve the built-in preset,
/// maintain a valid active preset, and save changes
/// transactionally where multiple records are affected.
/// </summary>
public interface IWorkReportPresetRepository
{
    /// <summary>
    /// Loads all presets and the active selection.
    /// </summary>
    Task<WorkReportPresetState> GetStateAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new preset or updates an existing one.
    ///
    /// The built-in flag and preset identity cannot
    /// be changed through an update.
    /// </summary>
    Task SavePresetAsync(
        WorkReportPreset preset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the selected preset across app restarts.
    /// The preset must already exist.
    /// </summary>
    Task SetActivePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user-created preset.
    ///
    /// The built-in preset cannot be deleted.
    /// If the deleted preset is active, the default
    /// preset becomes active in the same transaction.
    /// </summary>
    Task DeletePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the built-in preset's original columns
    /// and name, then makes it active.
    ///
    /// User-created presets are preserved.
    /// </summary>
    Task ResetToDefaultAsync(
        CancellationToken cancellationToken = default);
}