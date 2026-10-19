using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daycord.Desktop.Localization;

public sealed class LocalizationService :
    ObservableObject,
    ILocalizationService
{
    private static readonly CultureInfo EnglishCulture =
        CultureInfo.GetCultureInfo("en");

    private static readonly CultureInfo RussianCulture =
        CultureInfo.GetCultureInfo("ru");

    private static readonly ResourceManager Resources = new(
        "Daycord.Desktop.Localization.Strings",
        typeof(LocalizationService).Assembly);

    private CultureInfo _currentCulture = EnglishCulture;

    public string CurrentLanguage => _currentCulture.Name;

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Resources.GetString(key, _currentCulture)
               ?? Resources.GetString(key, EnglishCulture)
               ?? key;
    }

    public void SetLanguage(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            languageCode);

        var normalizedCode = languageCode
            .Trim()
            .ToLowerInvariant();

        CultureInfo newCulture = normalizedCode switch
        {
            "en" => EnglishCulture,
            "ru" => RussianCulture,

            _ => throw new ArgumentOutOfRangeException(
                nameof(languageCode),
                languageCode,
                "Supported languages are en and ru.")
        };

        if (_currentCulture.Equals(newCulture))
        {
            return;
        }

        _currentCulture = newCulture;

        OnPropertyChanged(nameof(CurrentLanguage));
    }
}