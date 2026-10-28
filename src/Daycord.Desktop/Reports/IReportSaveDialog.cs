namespace Daycord.Desktop.Reports;

/// <summary>
/// UI abstraction for selecting an XLSX destination.
/// A null result means that the user cancelled the dialog.
/// </summary>
public interface IReportSaveDialog
{
    Task<IReportSaveTarget?> PickDestinationAsync(
        string suggestedFileName);
}

/// <summary>
/// A selected destination that can provide a writable stream.
/// No local filesystem path is required.
/// </summary>
public interface IReportSaveTarget
{
    string DisplayName { get; }

    Task<Stream> OpenWriteAsync();
}