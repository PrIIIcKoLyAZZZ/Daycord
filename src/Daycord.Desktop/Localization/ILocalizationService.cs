using System.ComponentModel;

namespace Daycord.Desktop.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }

    string GetString(string key);

    void SetLanguage(string languageCode);
}