namespace Daycord.Core.Application.Models;

/// <summary>
/// Stable field identifiers for report presets.
/// Do not renumber: these values will be persisted in SQLite.
/// </summary>
public enum WorkReportField
{
    Date = 1,
    TaskNumber = 2,
    TaskTitle = 3,
    PlannedTime = 4,
    ActualTime = 5,
    WorkDescription = 6,
    TaskShortDescription = 7,
    TaskStatus = 8
}

/// <summary>
/// A selected report field and its custom Excel header.
/// The order of columns in a preset determines Excel column order.
/// </summary>
public sealed record WorkReportColumn(
    WorkReportField Field,
    string Header);

/// <summary>
/// The built-in report layout for contract work.
/// </summary>
public static class WorkReportDefaults
{
    public static IReadOnlyList<WorkReportColumn> Columns { get; } =
        Array.AsReadOnly(
            new[]
            {
                new WorkReportColumn(
                    WorkReportField.Date,
                    "Дата"),

                new WorkReportColumn(
                    WorkReportField.TaskNumber,
                    "Задача"),

                new WorkReportColumn(
                    WorkReportField.TaskTitle,
                    "Что делал"),

                new WorkReportColumn(
                    WorkReportField.PlannedTime,
                    "Время")
            });
}

public enum WorkReportRowSource
{
    PlanningPeriod = 1,
    ActualWithoutPlanning = 2
}

/// <summary>
/// A prepared reporting row.
///
/// An ordinary row represents one PlanningPeriod.
/// Actual time for the same task and date is assigned
/// to one planning row only.
///
/// WorkEntries without planning on the same date are
/// represented separately.
/// </summary>
public sealed record WorkReportRow(
    DateOnly Date,
    Guid TaskId,
    Guid? PlanningPeriodId,
    IReadOnlyList<Guid> WorkEntryIds,
    WorkReportRowSource Source,
    string? TaskNumber,
    string TaskTitle,
    string? TaskShortDescription,
    string TaskStatus,
    long? PlannedMinutes,
    long? ActualMinutes,
    string WorkDescription);

/// <summary>
/// Aggregated totals for one calendar month.
/// </summary>
public sealed record WorkReportMonthTotal(
    DateOnly Month,
    long PlannedMinutes,
    long ActualMinutes);

/// <summary>
/// Read-only report data for an inclusive date range.
/// The report itself does not depend on a preset or Excel.
/// </summary>
public sealed class WorkReport
{
    public WorkReport(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<WorkReportRow> rows)
    {
        if (from > to)
        {
            throw new ArgumentException(
                "The report start date cannot be later " +
                "than its end date.",
                nameof(from));
        }

        ArgumentNullException.ThrowIfNull(rows);

        From = from;
        To = to;

        var snapshot = rows.ToArray();

        foreach (var row in snapshot)
        {
            if (row.Date < from || row.Date > to)
            {
                throw new ArgumentException(
                    "A report row is outside the selected range.",
                    nameof(rows));
            }

            if (row.PlannedMinutes is < 0 ||
                row.ActualMinutes is < 0)
            {
                throw new ArgumentException(
                    "Report durations cannot be negative.",
                    nameof(rows));
            }
        }

        Rows = Array.AsReadOnly(snapshot);

        TotalPlannedMinutes = Rows.Sum(
            static row => row.PlannedMinutes ?? 0L);

        TotalActualMinutes = Rows.Sum(
            static row => row.ActualMinutes ?? 0L);

        PlanningPeriodCount = Rows.Count(
            static row =>
                row.Source == WorkReportRowSource.PlanningPeriod);

        WorkEntryCount = Rows.Sum(
            static row => row.WorkEntryIds.Count);

        var monthTotals = Rows
            .GroupBy(
                static row => new DateOnly(
                    row.Date.Year,
                    row.Date.Month,
                    1))
            .OrderBy(static group => group.Key)
            .Select(group => new WorkReportMonthTotal(
                group.Key,
                group.Sum(
                    static row => row.PlannedMinutes ?? 0L),
                group.Sum(
                    static row => row.ActualMinutes ?? 0L)))
            .ToArray();

        MonthTotals = Array.AsReadOnly(monthTotals);
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    /// <summary>
    /// All prepared rows, including actual-only rows.
    /// </summary>
    public IReadOnlyList<WorkReportRow> Rows { get; }

    public IReadOnlyList<WorkReportMonthTotal> MonthTotals { get; }

    public int PlanningPeriodCount { get; }

    public int WorkEntryCount { get; }

    public long TotalPlannedMinutes { get; }

    public long TotalActualMinutes { get; }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Returns rows appropriate for the selected columns.
    ///
    /// Actual-only rows are included when the report exposes
    /// actual time or descriptions of recorded work.
    ///
    /// The standard planned-time report contains only
    /// PlanningPeriod rows.
    /// </summary>
    public IReadOnlyList<WorkReportRow> GetRowsForColumns(
        IReadOnlyCollection<WorkReportColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var includesActualData = columns.Any(
            static column =>
                column.Field is
                    WorkReportField.ActualTime or
                    WorkReportField.WorkDescription);

        if (includesActualData)
        {
            return Rows;
        }

        return Rows
            .Where(
                static row =>
                    row.Source ==
                    WorkReportRowSource.PlanningPeriod)
            .ToArray();
    }
}