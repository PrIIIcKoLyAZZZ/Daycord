using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Contracts;

/// <summary>
/// Exports a prepared monthly work report to a writable stream.
/// </summary>
public interface IMonthlyReportExporter
{
    Task ExportAsync(
        MonthlyWorkReport report,
        Stream destination,
        CancellationToken cancellationToken = default);
}