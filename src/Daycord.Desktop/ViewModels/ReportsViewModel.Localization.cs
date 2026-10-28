using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Desktop.Localization;

namespace Daycord.Desktop.ViewModels;

public sealed partial class ReportsViewModel
{
    private ReportUiLabels? _uiLabels;

    public ReportUiLabels UiLabels =>
        _uiLabels ??= new ReportUiLabels(_localization);

    public CultureInfo CalendarCulture => Culture;
}

public sealed class ReportUiLabels : ObservableObject
{
    private readonly ILocalizationService _localization;

    public ReportUiLabels(
        ILocalizationService localization)
    {
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));

        _localization.PropertyChanged += OnLocalizationChanged;
    }

    private string Text(string key) =>
        _localization.GetString(key);

    private string Label(
        string key,
        string english,
        string russian)
    {
        var value = Text(key);

        if (value != key)
        {
            return value;
        }

        return _localization.CurrentLanguage == "ru"
            ? russian
            : english;
    }

    public string DateFrom => Text("ReportsDateFrom");

    public string DateTo => Text("ReportsDateTo");

    public string ReportRows => Text("ReportsReportRows");

    public string Preset => Text("ReportsPreset");

    public string PresetName => Text("ReportsPresetName");

    public string NewPresetNamePlaceholder =>
        Text("ReportsNewPresetNamePlaceholder");

    public string CreatePreset => Text("ReportsCreatePreset");

    public string DeletePreset => Text("ReportsDeletePreset");

    public string ResetToDefault =>
        Text("ReportsResetToDefault");

    public string ColumnsAndOrder =>
        Text("ReportsColumnsAndOrder");

    public string ColumnsDescription =>
        Text("ReportsColumnsDescription");

    public string MoveUp => Text("ReportsMoveUp");

    public string MoveDown => Text("ReportsMoveDown");

    public string UnsavedChanges =>
        Text("ReportsUnsavedChanges");

    public string SaveColumnSettings =>
        Text("ReportsSaveColumnSettings");

    public string NoReportRows =>
        Text("ReportsNoReportRows");

    // The built-in preset retains its stable name in SQLite.
    // Only its displayed caption is localized.
    public string DefaultPreset =>
        Label(
            "ReportsDefaultPreset",
            "Default",
            "По умолчанию");

    public string ExportSettings =>
        Label(
            "ReportsExportSettings",
            "Export settings",
            "Настройки экспорта");

    public string BackToReport =>
        Label(
            "ReportsBackToReport",
            "Back to report",
            "К отчёту");

    public string CreateNewPreset =>
        Label(
            "ReportsCreateNewPreset",
            "New preset",
            "Новый пресет");

    public string RenamePreset =>
        Label(
            "ReportsRenamePreset",
            "Rename",
            "Переименовать");

    public string SavePresetName =>
        Label(
            "ReportsSavePresetName",
            "Save name",
            "Сохранить название");

    public string Cancel =>
        Label(
            "ReportsCancel",
            "Cancel",
            "Отмена");

    private void OnLocalizationChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName !=
                nameof(ILocalizationService.CurrentLanguage))
        {
            return;
        }

        OnPropertyChanged(nameof(DateFrom));
        OnPropertyChanged(nameof(DateTo));
        OnPropertyChanged(nameof(ReportRows));

        OnPropertyChanged(nameof(Preset));
        OnPropertyChanged(nameof(PresetName));
        OnPropertyChanged(nameof(NewPresetNamePlaceholder));

        OnPropertyChanged(nameof(CreatePreset));
        OnPropertyChanged(nameof(DeletePreset));
        OnPropertyChanged(nameof(ResetToDefault));

        OnPropertyChanged(nameof(ColumnsAndOrder));
        OnPropertyChanged(nameof(ColumnsDescription));

        OnPropertyChanged(nameof(MoveUp));
        OnPropertyChanged(nameof(MoveDown));

        OnPropertyChanged(nameof(UnsavedChanges));
        OnPropertyChanged(nameof(SaveColumnSettings));
        OnPropertyChanged(nameof(NoReportRows));

        OnPropertyChanged(nameof(DefaultPreset));
        OnPropertyChanged(nameof(ExportSettings));
        OnPropertyChanged(nameof(BackToReport));
        OnPropertyChanged(nameof(CreateNewPreset));
        OnPropertyChanged(nameof(RenamePreset));
        OnPropertyChanged(nameof(SavePresetName));
        OnPropertyChanged(nameof(Cancel));
    }
}