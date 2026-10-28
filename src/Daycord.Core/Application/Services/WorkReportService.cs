using Daycord.Core.Application.Models;

namespace Daycord.Core.Application.Services;

/// <summary>
/// Builds reporting data from persisted PlanningPeriods
/// and WorkEntries using the existing Calendar API.
/// </summary>
public sealed class WorkReportService
{
    private readonly TaskService _taskService;

    public WorkReportService(TaskService taskService)
    {
        _taskService = taskService
            ?? throw new ArgumentNullException(nameof(taskService));
    }

    public async Task<WorkReport> GetReportAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<WorkReportField>? requestedFields = null,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new ArgumentException(
                "The reporting period is invalid.",
                nameof(from));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var calendar = await _taskService.GetCalendarDataAsync(
            from,
            to,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var planned = calendar.PlannedWork
            .Where(item =>
                item.PlannedDate >= from &&
                item.PlannedDate <= to)
            .OrderBy(static item => item.PlannedDate)
            .ThenBy(
                static item => item.TaskTitle,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Id)
            .ToArray();

        var actual = calendar.ActualWork
            .Where(item =>
                item.WorkDate >= from &&
                item.WorkDate <= to)
            .OrderBy(static item => item.WorkDate)
            .ThenBy(
                static item => item.TaskTitle,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Id)
            .ToArray();

        // ShortDescription is not exposed by CalendarData.
        // Load it only when the selected columns require it.
        var shortDescriptions = new Dictionary<Guid, string?>();

        if (requestedFields?.Contains(
                WorkReportField.TaskShortDescription) == true)
        {
            var taskIds = planned
                .Select(static item => item.TaskId)
                .Concat(actual.Select(
                    static item => item.TaskId))
                .Distinct()
                .OrderBy(static id => id);

            foreach (var taskId in taskIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var task = await _taskService.GetByIdAsync(
                    taskId,
                    cancellationToken).ConfigureAwait(false);

                shortDescriptions[taskId] =
                    task?.ShortDescription;
            }
        }

        string? GetShortDescription(Guid taskId)
        {
            return shortDescriptions.TryGetValue(
                taskId,
                out var description)
                ? description
                : null;
        }

        // Actual work grouped by task and calendar date.
        // This prevents one WorkEntry from being assigned
        // to multiple PlanningPeriods.
        var actualByTaskAndDate = actual
            .GroupBy(
                static item => (
                    item.TaskId,
                    item.WorkDate))
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

        // Identify task/date pairs with planning.
        var plannedKeys = planned
            .Select(
                static item => (
                    item.TaskId,
                    Date: item.PlannedDate))
            .ToHashSet();

        var allocatedActualKeys =
            new HashSet<(Guid TaskId, DateOnly Date)>();

        var rows = new List<WorkReportRow>(
            planned.Length + actual.Length);

        // Every PlanningPeriod becomes one row.
        foreach (var period in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (period.PlannedMinutes <= 0)
            {
                throw new InvalidOperationException(
                    $"Planning period '{period.Id}' " +
                    "has an invalid duration.");
            }

            var key = (
                period.TaskId,
                Date: period.PlannedDate);

            // Only the first PlanningPeriod for a given
            // task and date receives the actual duration.
            var firstForTaskAndDate =
                allocatedActualKeys.Add(key);

            var matchingWork =
                firstForTaskAndDate &&
                actualByTaskAndDate.TryGetValue(
                    key,
                    out var entries)
                    ? entries
                    : [];

            long? actualMinutes = matchingWork.Length > 0
                ? matchingWork.Sum(
                    static entry => (long)entry.ActualMinutes)
                : null;

            var workDescription = string.Join(
                Environment.NewLine,
                matchingWork
                    .Where(static entry =>
                        !string.IsNullOrEmpty(
                            entry.WorkDescription))
                    .Select(static entry =>
                        entry.WorkDescription!));

            rows.Add(new WorkReportRow(
                Date: period.PlannedDate,
                TaskId: period.TaskId,
                PlanningPeriodId: period.Id,
                WorkEntryIds: Array.AsReadOnly(
                    matchingWork
                        .Select(static entry => entry.Id)
                        .ToArray()),
                Source: WorkReportRowSource.PlanningPeriod,
                TaskNumber: period.TaskNumber,
                TaskTitle: period.TaskTitle,
                TaskShortDescription:
                    GetShortDescription(period.TaskId),
                TaskStatus: period.StatusName,
                PlannedMinutes: period.PlannedMinutes,
                ActualMinutes: actualMinutes,
                WorkDescription: workDescription));
        }

        // WorkEntries without a PlanningPeriod on the
        // same task/date are represented independently.
        foreach (var entry in actual)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.ActualMinutes <= 0)
            {
                throw new InvalidOperationException(
                    $"Work entry '{entry.Id}' " +
                    "has an invalid duration.");
            }

            var key = (
                entry.TaskId,
                Date: entry.WorkDate);

            if (plannedKeys.Contains(key))
            {
                // Already included in the planned row.
                continue;
            }

            rows.Add(new WorkReportRow(
                Date: entry.WorkDate,
                TaskId: entry.TaskId,
                PlanningPeriodId: null,
                WorkEntryIds: Array.AsReadOnly(
                    new[] { entry.Id }),
                Source:
                    WorkReportRowSource.ActualWithoutPlanning,
                TaskNumber: entry.TaskNumber,
                TaskTitle: entry.TaskTitle,
                TaskShortDescription:
                    GetShortDescription(entry.TaskId),
                TaskStatus: entry.StatusName,
                PlannedMinutes: null,
                ActualMinutes: entry.ActualMinutes,
                WorkDescription:
                    entry.WorkDescription ?? string.Empty));
        }

        // Deterministic ordering across repeated exports.
        var sorted = rows
            .OrderBy(static row => row.Date)
            .ThenBy(
                static row => row.TaskNumber ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                static row => row.TaskTitle,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Source)
            .ThenBy(
                static row =>
                    row.PlanningPeriodId ??
                    row.WorkEntryIds[0])
            .ToArray();

        return new WorkReport(from, to, sorted);
    }
}