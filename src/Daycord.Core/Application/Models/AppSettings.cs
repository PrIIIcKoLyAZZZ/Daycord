namespace Daycord.Core.Application.Models;

/// <summary>
/// Validated, immutable application preferences.
/// </summary>
public sealed class AppSettings
{
    public const int DefaultMinutesPerDay = 1440;
    public const int MaximumMinutesPerDay = 1440;

    /// <summary>
    /// Strongly typed language constructor.
    /// Preferred for all new application code.
    /// </summary>
    public AppSettings(
        AppLanguage language,
        string theme,
        int minutesPerDay)
    {
        // Validate that the enum has a registered language code.
        _ = language.ToCode();

        if (theme is not ("System" or "Light" or "Dark"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(theme),
                "Unsupported application theme.");
        }

        if (minutesPerDay is < 1 or > MaximumMinutesPerDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutesPerDay),
                $"The duration of one day must be between " +
                $"1 and {MaximumMinutesPerDay} minutes.");
        }

        SelectedLanguage = language;
        Theme = theme;
        MinutesPerDay = minutesPerDay;
    }

    /// <summary>
    /// Compatibility constructor for persistence code.
    /// Converts stored language codes into AppLanguage.
    /// </summary>
    public AppSettings(
        string language,
        string theme,
        int minutesPerDay)
        : this(
            AppLanguageCodes.Parse(language),
            theme,
            minutesPerDay)
    {
    }

    /// <summary>
    /// Primary strongly typed application language.
    /// </summary>
    public AppLanguage SelectedLanguage { get; }

    /// <summary>
    /// Compatibility property for existing code that expects
    /// a language code.
    ///
    /// New ViewModels should use SelectedLanguage.
    /// </summary>
    public string Language => SelectedLanguage.ToCode();

    public string Theme { get; }

    public int MinutesPerDay { get; }

    public static AppSettings Default =>
        new(
            AppLanguage.English,
            "System",
            DefaultMinutesPerDay);
}