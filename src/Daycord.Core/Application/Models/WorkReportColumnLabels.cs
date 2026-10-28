namespace Daycord.Core.Application.Models;

/// <summary>
/// Language-independent standard report column headers.
///
/// Presets store a token for an automatic header.
/// A user-edited header is stored as ordinary text.
/// </summary>
public static class WorkReportColumnLabels
{
    public static IReadOnlyList<WorkReportColumn> DefaultColumns { get; } =
        Array.AsReadOnly(
            new[]
            {
                new WorkReportColumn(
                    WorkReportField.Date,
                    Token(WorkReportField.Date)),

                new WorkReportColumn(
                    WorkReportField.TaskNumber,
                    Token(WorkReportField.TaskNumber)),

                new WorkReportColumn(
                    WorkReportField.TaskTitle,
                    Token(WorkReportField.TaskTitle)),

                new WorkReportColumn(
                    WorkReportField.PlannedTime,
                    Token(WorkReportField.PlannedTime))
            });

    public static string Token(WorkReportField field)
    {
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(
                nameof(field));
        }

        return $"@daycord:{field}";
    }

    public static bool IsAutomatic(
        WorkReportField field,
        string? storedHeader)
    {
        return string.Equals(
            storedHeader,
            Token(field),
            StringComparison.Ordinal);
    }

    public static string Resolve(
        WorkReportField field,
        string storedHeader,
        string? language)
    {
        ArgumentNullException.ThrowIfNull(storedHeader);

        return IsAutomatic(field, storedHeader)
            ? DefaultHeader(field, language)
            : storedHeader;
    }

    public static string DefaultHeader(
        WorkReportField field,
        string? language)
    {
        var russian = language?.StartsWith(
            "ru",
            StringComparison.OrdinalIgnoreCase) == true;

        return field switch
        {
            WorkReportField.Date =>
                russian ? "Дата" : "Date",

            WorkReportField.TaskNumber =>
                russian ? "Задача" : "Task",

            WorkReportField.TaskTitle =>
                russian ? "Что делал" : "Work performed",

            WorkReportField.PlannedTime =>
                russian ? "Время" : "Time",

            WorkReportField.ActualTime =>
                russian
                    ? "Фактическое время"
                    : "Actual time",

            WorkReportField.WorkDescription =>
                russian
                    ? "Описание выполненной работы"
                    : "Work description",

            WorkReportField.TaskShortDescription =>
                russian
                    ? "Краткое описание"
                    : "Short description",

            WorkReportField.TaskStatus =>
                russian ? "Статус" : "Status",

            _ => throw new ArgumentOutOfRangeException(
                nameof(field))
        };
    }
}