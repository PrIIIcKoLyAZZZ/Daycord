using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Contracts;

/// <summary>
/// Writes a date-range report using the selected column preset.
/// The stream is supplied and owned by the caller.
/// </summary>
public interface IWorkReportExporter
{
    Task ExportAsync(
        WorkReport report,
        WorkReportPreset preset,
        Stream destination,
        CancellationToken cancellationToken = default);
}