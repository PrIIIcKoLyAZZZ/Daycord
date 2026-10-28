using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Services;

/// <summary>
/// Prepares monthly work reports using the existing Calendar API.
/// Only saved actual work entries are included.
/// </summary>
public sealed class MonthlyWorkReportService
{
    private readonly TaskService _taskService;

    public MonthlyWorkReportService(TaskService taskService)
    {
        _taskService = taskService
            ?? throw new ArgumentNullException(nameof(taskService));
    }

    public async Task<MonthlyWorkReport> GetMonthlyReportAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var periodStart = new DateOnly(year, month, 1);

        var periodEnd = new DateOnly(
            year,
            month,
            DateTime.DaysInMonth(year, month));

        var calendarData = await _taskService
            .GetCalendarDataAsync(
                periodStart,
                periodEnd,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var rows = new List<MonthlyWorkReportRow>();

        foreach (var entry in calendarData.ActualWork)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (entry.WorkDate < periodStart ||
                entry.WorkDate > periodEnd)
            {
                continue;
            }

            if (entry.ActualMinutes <= 0)
            {
                throw new InvalidOperationException(
                    $"Work entry '{entry.Id}' has an invalid actual duration.");
            }

            var taskName = BuildTaskName(
                entry.TaskNumber,
                entry.TaskTitle);

            rows.Add(new MonthlyWorkReportRow(
                WorkEntryId: entry.Id,
                WorkDate: entry.WorkDate,
                TaskName: taskName,
                WorkDescription: entry.WorkDescription
                    ?? string.Empty,
                ActualMinutes: entry.ActualMinutes,
                Note: entry.Note ?? string.Empty));
        }
        
        var sortedRows = rows
            .OrderBy(static row => row.WorkDate)
            .ThenBy(
                static row => row.TaskName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.WorkEntryId)
            .ToArray();

        return new MonthlyWorkReport(
            year,
            month,
            sortedRows);
    }

    private static string BuildTaskName(
        string? taskNumber,
        string taskTitle)
    {
        if (string.IsNullOrWhiteSpace(taskNumber))
        {
            return taskTitle;
        }

        return $"{taskNumber.Trim()} — {taskTitle}";
    }
}