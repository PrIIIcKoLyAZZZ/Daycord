namespace Daycord.Core.Application.Models;

/// <summary>
/// A single monthly report row representing one saved WorkEntry.
/// </summary>
public sealed record MonthlyWorkReportRow(
    Guid WorkEntryId,
    DateOnly WorkDate,
    string TaskName,
    string WorkDescription,
    int ActualMinutes,
    string Note);

/// <summary>
/// Read-only monthly work report based on actual work entries.
/// </summary>
public sealed class MonthlyWorkReport
{
    public MonthlyWorkReport(
        int year,
        int month,
        IReadOnlyList<MonthlyWorkReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        PeriodStart = new DateOnly(year, month, 1);

        PeriodEnd = new DateOnly(
            year,
            month,
            DateTime.DaysInMonth(year, month));

        Year = year;
        Month = month;

        // Keep a snapshot independent of the input collection.
        Rows = Array.AsReadOnly(rows.ToArray());

        TotalActualMinutes = Rows.Sum(
            static row => (long)row.ActualMinutes);
    }

    public int Year { get; }

    public int Month { get; }

    public DateOnly PeriodStart { get; }

    public DateOnly PeriodEnd { get; }

    public IReadOnlyList<MonthlyWorkReportRow> Rows { get; }

    public int WorkEntryCount => Rows.Count;

    public long TotalActualMinutes { get; }

    public bool IsEmpty => Rows.Count == 0;
}