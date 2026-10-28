using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Desktop.Settings;

/// <summary>
/// Maintains the current persisted application settings.
///
/// The SQLite repository is the persistence source of truth.
/// Changes are published only after successful saving.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IAppSettingsRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _isLoaded;

    public AppSettingsService(
        IAppSettingsRepository repository)
    {
        _repository = repository
            ?? throw new ArgumentNullException(nameof(repository));
    }

    public AppSettings Current { get; private set; } =
        AppSettings.Default;

    public bool IsLoaded => _isLoaded;

    public event EventHandler? SettingsChanged;

    /// <summary>
    /// Loads persisted settings after SQLite initialization.
    /// A failed load does not replace the active settings.
    /// </summary>
    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var loaded =
                await _repository.LoadAsync(cancellationToken);

            // Update the in-memory state only after a successful read
            // and domain validation.
            Current = loaded;
            _isLoaded = true;
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves settings atomically and publishes the new state
    /// only after SQLite confirms the update.
    /// </summary>
    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var changed = false;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!_isLoaded)
            {
                throw new InvalidOperationException(
                    "Application settings have not been loaded.");
            }

            if (AreEqual(Current, settings))
            {
                return;
            }

            // If saving fails, Current stays unchanged.
            await _repository.SaveAsync(
                settings,
                cancellationToken);

            Current = settings;
            changed = true;
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool AreEqual(
        AppSettings left,
        AppSettings right)
    {
        return string.Equals(
                   left.Language,
                   right.Language,
                   StringComparison.Ordinal) &&
               string.Equals(
                   left.Theme,
                   right.Theme,
                   StringComparison.Ordinal) &&
               left.MinutesPerDay == right.MinutesPerDay;
    }
}