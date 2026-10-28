
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daycord.Core.Application.Models;
using Daycord.Desktop.Localization;
using Daycord.Desktop.Settings;

namespace Daycord.Desktop.ViewModels;

public sealed class SettingsLanguageOption
{
    public SettingsLanguageOption(AppLanguage language)
    {
        Language = language;
        DisplayName = language.GetNativeName();
    }

    public AppLanguage Language { get; }

    public string DisplayName { get; }
}

public sealed class SettingsThemeOption : ObservableObject
{
    private readonly ILocalizationService _localization;

    public SettingsThemeOption(
        string value,
        string resourceKey,
        ILocalizationService localization)
    {
        Value = value;
        ResourceKey = resourceKey;
        _localization = localization;
    }

    public string Value { get; }

    public string ResourceKey { get; }

    public string DisplayName =>
        _localization.GetString(ResourceKey);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}

public sealed class SettingsViewModel :
    ObservableObject,
    IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private SettingsLanguageOption? _selectedLanguage;
    private SettingsThemeOption? _selectedTheme;

    private string _durationInput = string.Empty;
    private string? _statusKey;

    private bool _isBusy;
    private bool _isInitializing;
    private bool _isDisposed;

    public SettingsViewModel(
        IAppSettingsService settings,
        ILocalizationService localization)
    {
        _settings = settings
            ?? throw new ArgumentNullException(nameof(settings));

        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        if (!_settings.IsLoaded)
        {
            throw new InvalidOperationException(
                "Application settings must be loaded before " +
                "creating SettingsViewModel.");
        }

        _isInitializing = true;

        Languages = AppLanguageCodes.Available
            .Select(language => new SettingsLanguageOption(language))
            .ToArray();

        Themes =
        [
            new SettingsThemeOption(
                "System",
                "SettingsThemeSystem",
                localization),

            new SettingsThemeOption(
                "Light",
                "SettingsThemeLight",
                localization),

            new SettingsThemeOption(
                "Dark",
                "SettingsThemeDark",
                localization)
        ];

        _selectedLanguage = Languages.First(
            option =>
                option.Language == settings.Current.SelectedLanguage);

        _selectedTheme = Themes.First(
            option =>
                option.Value == settings.Current.Theme);

        _durationInput =
            PlannedDuration.Format(settings.Current.MinutesPerDay);

        SaveDurationCommand = new AsyncRelayCommand(
            SaveDurationAsync);

        _localization.PropertyChanged += OnLocalizationChanged;

        _isInitializing = false;
    }
    
    // Choices

    public IReadOnlyList<SettingsLanguageOption> Languages { get; }

    public IReadOnlyList<SettingsThemeOption> Themes { get; }

    public SettingsLanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_isDisposed ||
                !SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            if (!_isInitializing && value is not null)
                _ = SaveSelectionsAsync();
        }
    }

    public SettingsThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_isDisposed ||
                !SetProperty(ref _selectedTheme, value))
            {
                return;
            }

            if (!_isInitializing && value is not null)
                _ = SaveSelectionsAsync();
        }
    }
    
    // Duration

    public string DurationInput
    {
        get => _durationInput;
        set
        {
            if (_isDisposed)
                return;

            if (SetProperty(
                    ref _durationInput,
                    value ?? string.Empty))
            {
                ClearStatus();
            }
        }
    }

    public string DayEquivalent
    {
        get
        {
            var duration = PlannedDuration.Format(
                _settings.Current.MinutesPerDay);

            return string.Format(
                CultureInfo.GetCultureInfo(
                    _localization.CurrentLanguage),
                L("SettingsDayEquivalent"),
                duration);
        }
    }
    
    // State

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSaveDuration));
                OnPropertyChanged(nameof(CanChangePreferences));
            }
        }
    }

    public bool CanSaveDuration =>
        !_isDisposed && !IsBusy;

    public bool CanChangePreferences =>
        !_isDisposed && !IsBusy;

    // Only errors are displayed.
    // Successful saves do not produce a notification.

    public bool HasStatus =>
        _statusKey is not null;

    public string StatusMessage =>
        _statusKey is null
            ? string.Empty
            : L(_statusKey);

    public IAsyncRelayCommand SaveDurationCommand { get; }
    
    // Localized labels

    private string L(string key) =>
        _localization.GetString(key);

    public string GeneralHeading =>
        L("SettingsGeneralHeading");

    public string LanguageTitle =>
        L("SettingsLanguageTitle");

    public string LanguageDescription =>
        L("SettingsLanguageDescription");

    public string AppearanceTitle =>
        L("SettingsAppearanceTitle");

    public string AppearanceDescription =>
        L("SettingsAppearanceDescription");

    public string TimeTrackingHeading =>
        L("SettingsTimeTrackingHeading");

    public string DayLengthTitle =>
        L("SettingsDayLengthTitle");

    public string DayLengthDescription =>
        L("SettingsDayLengthDescription");

    public string SaveDurationLabel =>
        L("SettingsSaveDuration");

    public string DataHeading =>
        L("SettingsDataHeading");
    
    // Automatic language / theme persistence

    private async Task SaveSelectionsAsync()
    {
        try
        {
            await _saveGate.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_isDisposed)
                return;

            IsBusy = true;

            while (!_isDisposed)
            {
                var language = _selectedLanguage?.Language;
                var theme = _selectedTheme?.Value;

                if (language is null || theme is null)
                    break;

                var current = _settings.Current;

                if (current.SelectedLanguage == language.Value &&
                    current.Theme == theme)
                {
                    break;
                }

                var updated = new AppSettings(
                    language.Value,
                    theme,
                    current.MinutesPerDay);

                try
                {
                    await _settings.SaveAsync(updated);

                    ApplyAppearanceAndLanguage(updated);

                    // Successful change is immediately visible.
                    // No "Settings saved" notification.
                    ClearStatus();
                }
                catch (Exception)
                {
                    RestorePersistedSelections();
                    SetStatus("SettingsSaveFailed");
                    break;
                }
            }
        }
        finally
        {
            IsBusy = false;
            _saveGate.Release();
        }
    }
    
    // Explicit duration persistence

    private async Task SaveDurationAsync()
    {
        if (_isDisposed)
            return;

        await _saveGate.WaitAsync();

        try
        {
            if (_isDisposed)
                return;

            IsBusy = true;

            if (!PlannedDuration.TryParseDayLength(
                    DurationInput,
                    out var minutes))
            {
                SetStatus("SettingsInvalidDuration");
                return;
            }

            var current = _settings.Current;

            var updated = new AppSettings(
                current.SelectedLanguage,
                current.Theme,
                minutes);

            try
            {
                await _settings.SaveAsync(updated);

                DurationInput = PlannedDuration.Format(minutes);

                // The preview below the field confirms
                // the newly persisted day duration.
                OnPropertyChanged(nameof(DayEquivalent));

                // No success notification. Keep only the preview.
                ClearStatus();
            }
            catch (Exception)
            {
                SetStatus("SettingsSaveFailed");
            }
        }
        finally
        {
            IsBusy = false;
            _saveGate.Release();
        }
    }
    
    // Application appearance / localization

    private void ApplyAppearanceAndLanguage(
        AppSettings settings)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = settings.Theme switch
            {
                "System" => ThemeVariant.Default,
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,

                _ => throw new InvalidOperationException(
                    "Unsupported application theme.")
            };
        }

        _localization.SetLanguage(settings.Language);
    }

    private void RestorePersistedSelections()
    {
        var current = _settings.Current;

        _isInitializing = true;

        try
        {
            SelectedLanguage = Languages.First(
                option =>
                    option.Language == current.SelectedLanguage);

            SelectedTheme = Themes.First(
                option =>
                    option.Value == current.Theme);
        }
        finally
        {
            _isInitializing = false;
        }
    }
    
    // Error status

    private void SetStatus(string key)
    {
        _statusKey = key;

        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void ClearStatus()
    {
        if (_statusKey is null)
            return;

        _statusKey = null;

        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(StatusMessage));
    }
    
    // Dynamic localization

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_isDisposed ||
            e.PropertyName is not (
                nameof(ILocalizationService.CurrentLanguage)
                or null
                or ""))
        {
            return;
        }

        foreach (var option in Themes)
            option.RefreshLocalization();

        OnPropertyChanged(nameof(GeneralHeading));
        OnPropertyChanged(nameof(LanguageTitle));
        OnPropertyChanged(nameof(LanguageDescription));
        OnPropertyChanged(nameof(AppearanceTitle));
        OnPropertyChanged(nameof(AppearanceDescription));
        OnPropertyChanged(nameof(TimeTrackingHeading));
        OnPropertyChanged(nameof(DayLengthTitle));
        OnPropertyChanged(nameof(DayLengthDescription));
        OnPropertyChanged(nameof(SaveDurationLabel));
        OnPropertyChanged(nameof(DataHeading));
        OnPropertyChanged(nameof(DayEquivalent));
        OnPropertyChanged(nameof(StatusMessage));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _localization.PropertyChanged -= OnLocalizationChanged;
    }
}