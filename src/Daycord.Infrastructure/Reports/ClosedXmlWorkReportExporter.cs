using System.Globalization;
using System.Xml;
using ClosedXML.Excel;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Infrastructure.Reports;

public sealed class ClosedXmlWorkReportExporter : IWorkReportExporter
{
    private const int MaxExcelRows = 1_048_576;
    private const int MaxExcelTextLength = 32_767;
    private const double MinutesPerDay = 1440.0;

    private const string DurationFormat = "[h]:mm";
    private const string DateFormat = "dd.mm.yyyy";

    private static readonly CultureInfo Russian =
        CultureInfo.GetCultureInfo("ru-RU");

    private static readonly CultureInfo English =
        CultureInfo.GetCultureInfo("en-US");

    private static readonly XLColor Graphite =
        XLColor.FromHtml("#363A40");

    private static readonly XLColor Amber =
        XLColor.FromHtml("#E5A64A");

    private static readonly XLColor LightAmber =
        XLColor.FromHtml("#FFF2DE");

    private static readonly XLColor BorderColor =
        XLColor.FromHtml("#BFC3C9");

    private static readonly XLColor White =
        XLColor.FromHtml("#FFFFFF");

    private readonly Func<string> _getLanguage;

    public ClosedXmlWorkReportExporter(Func<string> getLanguage)
    {
        _getLanguage = getLanguage
            ?? throw new ArgumentNullException(nameof(getLanguage));
    }

    private readonly record struct ReportLabels(
        CultureInfo MonthCulture,
        string MonthTotalPrefix,
        string PeriodTotal);

    private static ReportLabels GetLabels(string? language)
    {
        var isRussian = language?.StartsWith(
            "ru",
            StringComparison.OrdinalIgnoreCase) == true;

        return isRussian
            ? new ReportLabels(
                Russian,
                "Итого за ",
                "Итого за период")
            : new ReportLabels(
                English,
                "Total for ",
                "Total for the period");
    }

    public async Task ExportAsync(
        WorkReport report,
        WorkReportPreset preset,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The report destination must be writable.",
                nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Use the application's selected language,
        // not the operating system language.
        var labels = GetLabels(_getLanguage());

        using var buffer = await Task.Run(
            () => BuildWorkbook(
                report,
                preset,
                labels,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        await buffer.CopyToAsync(
            destination,
            81920,
            cancellationToken).ConfigureAwait(false);

        await destination.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static MemoryStream BuildWorkbook(
        WorkReport report,
        WorkReportPreset preset,
        ReportLabels labels,
        CancellationToken cancellationToken)
    {
        var columns = preset.Columns;

        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                "The selected report preset has no columns.");
        }

        var rows = report.GetRowsForColumns(columns)
            .OrderBy(static row => row.Date)
            .ThenBy(
                static row => row.TaskNumber ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                static row => row.TaskTitle,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Source)
            .ThenBy(static row =>
                row.PlanningPeriodId ??
                row.WorkEntryIds.FirstOrDefault())
            .ToArray();

        var isMultiMonth =
            report.From.Year != report.To.Year ||
            report.From.Month != report.To.Month;

        var rowsByMonth = rows
            .GroupBy(static row =>
                new DateOnly(
                    row.Date.Year,
                    row.Date.Month,
                    1))
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

        var monthCount =
            (report.To.Year - report.From.Year) * 12 +
            report.To.Month - report.From.Month + 1;

        var requiredRows =
            2L +
            rows.Length +
            (isMultiMonth ? monthCount : 0);

        if (requiredRows > MaxExcelRows)
        {
            throw new InvalidOperationException(
                "The report exceeds the Excel worksheet row limit.");
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Work Log");

        ConfigureSheet(sheet, columns);
        WriteHeader(sheet, columns);

        var nextRow = 2;
        var subtotalRows = new List<int>();

        var dateColumn = FindColumn(
            columns,
            WorkReportField.Date);

        var month = new DateOnly(
            report.From.Year,
            report.From.Month,
            1);

        for (var monthIndex = 0;
             monthIndex < monthCount;
             monthIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var firstMonthRow = nextRow;

            var monthRows =
                rowsByMonth.TryGetValue(month, out var found)
                    ? found
                    : Array.Empty<WorkReportRow>();

            foreach (var dateGroup in monthRows.GroupBy(
                         static row => row.Date))
            {
                var firstDateRow = nextRow;

                foreach (var entry in dateGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    WriteEntry(
                        sheet,
                        nextRow,
                        columns,
                        entry);

                    nextRow++;
                }

                if (dateColumn > 0 &&
                    nextRow - firstDateRow > 1)
                {
                    var merged = sheet.Range(
                        firstDateRow,
                        dateColumn,
                        nextRow - 1,
                        dateColumn);

                    merged.Merge();

                    merged.Style.Alignment.Vertical =
                        XLAlignmentVerticalValues.Center;

                    merged.Style.Border.OutsideBorder =
                        XLBorderStyleValues.Thin;

                    merged.Style.Border.OutsideBorderColor =
                        BorderColor;
                }
            }

            if (isMultiMonth)
            {
                var lastMonthRow = nextRow - 1;

                var label =
                    labels.MonthTotalPrefix +
                    month.ToString(
                        "MMMM yyyy",
                        labels.MonthCulture);

                WriteTotal(
                    sheet,
                    nextRow,
                    columns,
                    label,
                    monthRows,
                    firstMonthRow,
                    lastMonthRow,
                    sourceSubtotalRows: null);

                subtotalRows.Add(nextRow);
                nextRow++;
            }

            if (monthIndex + 1 < monthCount)
                month = month.AddMonths(1);
        }

        cancellationToken.ThrowIfCancellationRequested();

        WriteTotal(
            sheet,
            nextRow,
            columns,
            labels.PeriodTotal,
            rows,
            firstDataRow: 2,
            lastDataRow: nextRow - 1,
            sourceSubtotalRows:
                isMultiMonth ? subtotalRows : null);

        var buffer = new MemoryStream();

        try
        {
            workbook.SaveAs(
                buffer,
                validate: false,
                evaluateFormulae: true);

            buffer.Position = 0;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static void ConfigureSheet(
        IXLWorksheet sheet,
        IReadOnlyList<WorkReportColumn> columns)
    {
        sheet.ShowGridLines = false;
        sheet.SheetView.FreezeRows(1);

        sheet.Style.Font.FontName = "Aptos";
        sheet.Style.Font.FontSize = 11;

        for (var i = 0; i < columns.Count; i++)
        {
            var field = columns[i].Field;
            var column = sheet.Column(i + 1);

            column.Width = field switch
            {
                WorkReportField.Date => 16,
                WorkReportField.TaskNumber => 25,
                WorkReportField.TaskTitle => 44,
                WorkReportField.PlannedTime => 17,
                WorkReportField.ActualTime => 17,
                WorkReportField.WorkDescription => 57,
                WorkReportField.TaskShortDescription => 46,
                WorkReportField.TaskStatus => 22,
                _ => 24
            };

            if (field == WorkReportField.Date)
            {
                column.Style.NumberFormat.Format =
                    DateFormat;
            }
            else if (IsTimeField(field))
            {
                column.Style.NumberFormat.Format =
                    DurationFormat;
            }
            else
            {
                column.Style.Alignment.WrapText = true;
            }
        }

        if (columns.Count == 1 &&
            IsTimeField(columns[0].Field))
        {
            sheet.Column(1).Width = 34;
        }
    }

    private static void WriteHeader(
        IXLWorksheet sheet,
        IReadOnlyList<WorkReportColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            WriteText(
                sheet.Cell(1, i + 1),
                columns[i].Header);
        }

        var header = sheet.Range(
            1,
            1,
            1,
            columns.Count);

        header.Style.Fill.BackgroundColor = Graphite;
        header.Style.Font.FontColor = White;
        header.Style.Font.Bold = true;

        header.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        header.Style.Border.OutsideBorder =
            XLBorderStyleValues.Thin;

        header.Style.Border.OutsideBorderColor =
            Graphite;

        header.Style.Border.InsideBorder =
            XLBorderStyleValues.Thin;

        header.Style.Border.InsideBorderColor =
            BorderColor;

        header.Style.Border.BottomBorder =
            XLBorderStyleValues.Medium;

        header.Style.Border.BottomBorderColor = Amber;

        sheet.Row(1).Height = 30;
    }

    private static void WriteEntry(
        IXLWorksheet sheet,
        int rowIndex,
        IReadOnlyList<WorkReportColumn> columns,
        WorkReportRow row)
    {
        var cellRange = sheet.Range(
            rowIndex,
            1,
            rowIndex,
            columns.Count);

        cellRange.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Top;

        cellRange.Style.Border.OutsideBorder =
            XLBorderStyleValues.Thin;

        cellRange.Style.Border.OutsideBorderColor =
            BorderColor;

        cellRange.Style.Border.InsideBorder =
            XLBorderStyleValues.Thin;

        cellRange.Style.Border.InsideBorderColor =
            BorderColor;

        var lineCount = 1;

        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(
                rowIndex,
                i + 1);

            var field = columns[i].Field;

            switch (field)
            {
                case WorkReportField.Date:
                    if (row.Date.Year < 1900)
                    {
                        throw new InvalidOperationException(
                            "Excel dates before 1900 are not supported.");
                    }

                    cell.SetValue(
                        row.Date.ToDateTime(
                            TimeOnly.MinValue));

                    cell.Style.NumberFormat.Format =
                        DateFormat;
                    break;

                case WorkReportField.PlannedTime:
                    WriteDuration(
                        cell,
                        row.PlannedMinutes);
                    break;

                case WorkReportField.ActualTime:
                    WriteDuration(
                        cell,
                        row.ActualMinutes);
                    break;

                default:
                    var value = field switch
                    {
                        WorkReportField.TaskNumber =>
                            row.TaskNumber,

                        WorkReportField.TaskTitle =>
                            row.TaskTitle,

                        WorkReportField.WorkDescription =>
                            row.WorkDescription,

                        WorkReportField.TaskShortDescription =>
                            row.TaskShortDescription,

                        WorkReportField.TaskStatus =>
                            row.TaskStatus,

                        _ => throw new InvalidOperationException(
                            $"Unsupported report field: {field}.")
                    };

                    value ??= string.Empty;

                    WriteText(cell, value);

                    lineCount = Math.Max(
                        lineCount,
                        Math.Min(
                            5,
                            value.Split('\n').Length));
                    break;
            }
        }

        sheet.Row(rowIndex).Height =
            21 + 15 * (lineCount - 1);
    }

    private static void WriteTotal(
        IXLWorksheet sheet,
        int rowIndex,
        IReadOnlyList<WorkReportColumn> columns,
        string label,
        IReadOnlyList<WorkReportRow> rows,
        int firstDataRow,
        int lastDataRow,
        IReadOnlyList<int>? sourceSubtotalRows)
    {
        var range = sheet.Range(
            rowIndex,
            1,
            rowIndex,
            columns.Count);

        range.Style.Fill.BackgroundColor = LightAmber;
        range.Style.Font.FontColor = Graphite;
        range.Style.Font.Bold = true;

        range.Style.Border.OutsideBorder =
            XLBorderStyleValues.Thin;

        range.Style.Border.OutsideBorderColor =
            BorderColor;

        range.Style.Border.InsideBorder =
            XLBorderStyleValues.Thin;

        range.Style.Border.InsideBorderColor =
            BorderColor;

        range.Style.Border.TopBorder =
            XLBorderStyleValues.Medium;

        range.Style.Border.TopBorderColor =
            Amber;

        range.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        sheet.Row(rowIndex).Height = 29;

        var labelColumn = 0;

        for (var i = 0; i < columns.Count; i++)
        {
            if (!IsTimeField(columns[i].Field))
            {
                labelColumn = i + 1;
                break;
            }
        }

        if (labelColumn > 0)
        {
            WriteText(
                sheet.Cell(rowIndex, labelColumn),
                label);
        }

        var firstTimeColumn = 0;

        for (var i = 0; i < columns.Count; i++)
        {
            var field = columns[i].Field;

            if (!IsTimeField(field))
                continue;

            var columnNumber = i + 1;
            var cell = sheet.Cell(
                rowIndex,
                columnNumber);

            if (firstTimeColumn == 0)
                firstTimeColumn = columnNumber;

            var totalMinutes = rows.Sum(row =>
                field == WorkReportField.PlannedTime
                    ? row.PlannedMinutes ?? 0L
                    : row.ActualMinutes ?? 0L);

            if (rows.Count == 0)
            {
                cell.SetValue(0d);
            }
            else if (sourceSubtotalRows is not null)
            {
                var addresses = sourceSubtotalRows
                    .Select(row =>
                        sheet.Cell(
                            row,
                            columnNumber)
                        .Address.ToString())
                    .ToArray();

                var formula =
                    "SUM(" +
                    string.Join(",", addresses) +
                    ")";

                if (addresses.Length is > 0 and <= 255 &&
                    formula.Length <= 8192)
                {
                    cell.FormulaA1 = formula;
                }
                else
                {
                    cell.SetValue(
                        totalMinutes / MinutesPerDay);
                }
            }
            else if (firstDataRow <= lastDataRow)
            {
                var startAddress = sheet.Cell(
                    firstDataRow,
                    columnNumber)
                    .Address.ToString();

                var endAddress = sheet.Cell(
                    lastDataRow,
                    columnNumber)
                    .Address.ToString();

                cell.FormulaA1 =
                    $"SUM({startAddress}:{endAddress})";
            }
            else
            {
                cell.SetValue(
                    totalMinutes / MinutesPerDay);
            }

            cell.Style.NumberFormat.Format =
                DurationFormat;
        }

        if (labelColumn == 0 &&
            firstTimeColumn > 0)
        {
            sheet.Cell(rowIndex, firstTimeColumn)
                .Style.NumberFormat.Format =
                    $"\"{label}\" [h]:mm";
        }
    }

    private static void WriteDuration(
        IXLCell cell,
        long? minutes)
    {
        if (!minutes.HasValue)
            return;

        if (minutes.Value < 0)
        {
            throw new InvalidOperationException(
                "Report durations must not be negative.");
        }

        cell.SetValue(
            minutes.Value / MinutesPerDay);

        cell.Style.NumberFormat.Format =
            DurationFormat;
    }

    private static void WriteText(
        IXLCell cell,
        string? value)
    {
        value ??= string.Empty;

        if (value.Length > MaxExcelTextLength)
        {
            throw new InvalidOperationException(
                "A report text value exceeds the Excel cell limit.");
        }

        try
        {
            XmlConvert.VerifyXmlChars(value);
        }
        catch (XmlException error)
        {
            throw new InvalidOperationException(
                "A report text field contains unsupported characters.",
                error);
        }

        if (value.StartsWith('\''))
            value = "'" + value;

        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(value);
    }

    private static bool IsTimeField(
        WorkReportField field) =>
        field is
            WorkReportField.PlannedTime or
            WorkReportField.ActualTime;

    private static int FindColumn(
        IReadOnlyList<WorkReportColumn> columns,
        WorkReportField field)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Field == field)
                return i + 1;
        }

        return 0;
    }
}