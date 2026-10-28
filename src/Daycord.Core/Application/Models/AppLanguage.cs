namespace Daycord.Core.Application.Models;

/// <summary>
/// Supported application interface languages.
/// Add new languages here and register their codes
/// in AppLanguageCodes.
/// </summary>
public enum AppLanguage
{
    English = 0,
    Russian = 1
}

/// <summary>
/// Stable mappings between application languages
/// and their persisted language codes.
/// </summary>
public static class AppLanguageCodes
{
    public static IReadOnlyList<AppLanguage> Available { get; } =
        Array.AsReadOnly(Enum.GetValues<AppLanguage>());

    public static string ToCode(this AppLanguage language)
    {
        return language switch
        {
            AppLanguage.English => "en",
            AppLanguage.Russian => "ru",

            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported application language.")
        };
    }

    public static AppLanguage Parse(string? code)
    {
        return code?.Trim().ToLowerInvariant() switch
        {
            "en" => AppLanguage.English,
            "ru" => AppLanguage.Russian,

            _ => throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "Unsupported application language code.")
        };
    }

    public static string GetNativeName(
        this AppLanguage language)
    {
        return language switch
        {
            AppLanguage.English => "English",
            AppLanguage.Russian => "Русский",

            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unsupported application language.")
        };
    }
}