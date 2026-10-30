using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Core.Application.Models;

namespace Daycord.Desktop.Localization;

public sealed class LocalizationService :
    ObservableObject,
    ILocalizationService
{
    private static readonly CultureInfo EnglishCulture =
        CultureInfo.GetCultureInfo("en");

    private static readonly ResourceManager Resources = new(
        "Daycord.Desktop.Localization.Strings",
        typeof(LocalizationService).Assembly);

    private AppLanguage _currentLanguage = AppLanguage.English;

    public string CurrentLanguage =>
        _currentLanguage.ToCode();

    public AppLanguage CurrentAppLanguage =>
        _currentLanguage;

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var culture = CultureInfo.GetCultureInfo(
            _currentLanguage.ToCode());

        return Resources.GetString(key, culture)
               ?? Resources.GetString(key, EnglishCulture)
               ?? key;
    }

    public void SetLanguage(string languageCode)
    {
        var language = AppLanguageCodes.Parse(languageCode);

        SetLanguage(language);
    }

    public void SetLanguage(AppLanguage language)
    {
        // An enum value is not automatically a supported language.
        // Validate its registration before applying it.
        _ = language.ToCode();

        if (_currentLanguage == language)
        {
            return;
        }

        _currentLanguage = language;

        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(CurrentAppLanguage));
    }
}