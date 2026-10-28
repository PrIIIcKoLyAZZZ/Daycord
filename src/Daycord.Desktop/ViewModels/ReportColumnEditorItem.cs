using CommunityToolkit.Mvvm.ComponentModel;
using Daycord.Core.Application.Models;

namespace Daycord.Desktop.ViewModels;

public sealed class ReportColumnEditorItem : ObservableObject
{
    private string _defaultHeader;
    private string _header;
    private string _storedHeader;
    private bool _isIncluded;

    public ReportColumnEditorItem(
        WorkReportField field,
        string defaultHeader,
        string? header = null,
        bool isIncluded = false,
        string? language = null)
    {
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        Field = field;

        _defaultHeader = string.IsNullOrWhiteSpace(language)
            ? defaultHeader
            : WorkReportColumnLabels.DefaultHeader(
                field,
                language);

        _storedHeader = header
            ?? WorkReportColumnLabels.Token(field);

        _header = WorkReportColumnLabels.Resolve(
            field,
            _storedHeader,
            language);

        _isIncluded = isIncluded;
    }

    public WorkReportField Field { get; }

    /// <summary>
    /// Localized description shown above the text field.
    /// </summary>
    public string DefaultHeader
    {
        get => _defaultHeader;
        private set => SetProperty(ref _defaultHeader, value);
    }

    /// <summary>
    /// Readable text shown in the editor.
    /// A manual edit turns an automatic header into a custom one.
    /// </summary>
    public string Header
    {
        get => _header;
        set
        {
            value ??= string.Empty;

            if (!SetProperty(ref _header, value))
                return;

            _storedHeader = value;
            OnPropertyChanged(nameof(StoredHeader));
        }
    }

    /// <summary>
    /// Persisted value: automatic token or literal custom text.
    /// </summary>
    public string StoredHeader => _storedHeader;

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    /// <summary>
    /// Refreshes standard labels without rewriting the stored
    /// automatic token or changing custom user-defined headers.
    /// </summary>
    public void SetLanguage(string language)
    {
        var translated = WorkReportColumnLabels.DefaultHeader(
            Field,
            language);

        DefaultHeader = translated;

        if (!WorkReportColumnLabels.IsAutomatic(
                Field,
                _storedHeader))
        {
            return;
        }

        if (string.Equals(
                _header,
                translated,
                StringComparison.Ordinal))
        {
            return;
        }

        _header = translated;
        OnPropertyChanged(nameof(Header));
    }
}