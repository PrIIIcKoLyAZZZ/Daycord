using ClosedXML.Excel;
using Daycord.Core.Application.Contracts;
using Daycord.Core.Application.Models;

namespace Daycord.Infrastructure.Reports;

/// <summary>
/// Generates monthly work log XLSX files using ClosedXML.
/// One saved WorkEntry corresponds to one report row.
/// </summary>
public sealed class ClosedXmlMonthlyReportExporter
    : IMonthlyReportExporter
{
    private const int HeaderRow = 1;
    private const int FirstDataRow = 2;
    private const int ColumnCount = 5;

    private const int MaxExcelRows = 1_048_576;
    private const int MaxExcelTextLength = 32_767;

    private const double MinutesPerDay = 1440.0;

    private const string DateFormat = "dd.mm.yyyy";
    private const string DurationFormat = "[h]:mm";

    private static readonly XLColor Graphite =
        XLColor.FromHtml("#363A40");

    private static readonly XLColor Amber =
        XLColor.FromHtml("#E5A64A");

    private static readonly XLColor LightAmber =
        XLColor.FromHtml("#FFF2DE");

    private static readonly XLColor DarkText =
        XLColor.FromHtml("#292D33");

    private static readonly XLColor White =
        XLColor.FromHtml("#FFFFFF");

    public async Task ExportAsync(
        MonthlyWorkReport report,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The destination stream must be writable.",
                nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ClosedXML generates workbooks synchronously.
        // Building on a worker thread keeps the UI responsive.
        using var buffer = await Task.Run(
            () => GenerateWorkbook(report, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // The destination does not have to support seeking.
        await buffer.CopyToAsync(
            destination,
            81920,
            cancellationToken).ConfigureAwait(false);

        await destination.FlushAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private static MemoryStream GenerateWorkbook(
        MonthlyWorkReport report,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // One row for headers and one for the total.
        if (report.Rows.Count > MaxExcelRows - 2)
        {
            throw new InvalidOperationException(
                "The report contains too many work entries " +
                "for a single Excel worksheet.");
        }

        using var workbook = new XLWorkbook();

        var worksheet = workbook.Worksheets.Add("Work Log");

        ConfigureWorksheet(worksheet);
        WriteHeaders(worksheet);

        var rowIndex = FirstDataRow;

        foreach (var entry in report.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WriteWorkEntry(
                worksheet,
                rowIndex,
                report,
                entry);

            rowIndex++;
        }

        var lastDataRow = rowIndex - 1;
        var totalRow = rowIndex;

        WriteTotal(
            worksheet,
            totalRow,
            lastDataRow,
            report.Rows.Count);

        if (report.Rows.Count > 0)
        {
            // The total row must not be part of the filter.
            worksheet.Range(
                HeaderRow,
                1,
                lastDataRow,
                ColumnCount).SetAutoFilter();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var buffer = new MemoryStream();

        try
        {
            // Evaluate the SUM result before saving so that
            // compatible viewers can display its cached value.
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

    private static void ConfigureWorksheet(
        IXLWorksheet worksheet)
    {
        worksheet.ShowGridLines = false;

        worksheet.Style.Font.FontName = "Aptos";
        worksheet.Style.Font.FontSize = 11;
        worksheet.Style.Font.FontColor = DarkText;

        worksheet.Column(1).Width = 16;
        worksheet.Column(2).Width = 42;
        worksheet.Column(3).Width = 65;
        worksheet.Column(4).Width = 16;
        worksheet.Column(5).Width = 42;

        worksheet.Column(1)
            .Style.NumberFormat.Format = DateFormat;

        worksheet.Column(4)
            .Style.NumberFormat.Format = DurationFormat;

        worksheet.Column(1)
            .Style.Alignment.Horizontal =
                XLAlignmentHorizontalValues.Center;

        worksheet.Column(4)
            .Style.Alignment.Horizontal =
                XLAlignmentHorizontalValues.Center;

        worksheet.Column(3)
            .Style.Alignment.WrapText = true;

        worksheet.Column(5)
            .Style.Alignment.WrapText = true;

        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteHeaders(
        IXLWorksheet worksheet)
    {
        worksheet.Cell(1, 1).SetValue("Дата");
        worksheet.Cell(1, 2).SetValue("Задача");
        worksheet.Cell(1, 3).SetValue("Что делал");
        worksheet.Cell(1, 4).SetValue("Время");
        worksheet.Cell(1, 5).SetValue("Пометка");

        var header = worksheet.Range(
            1,
            1,
            1,
            ColumnCount);

        header.Style.Fill.BackgroundColor = Graphite;
        header.Style.Font.FontColor = White;
        header.Style.Font.Bold = true;

        header.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        header.Style.Border.BottomBorder =
            XLBorderStyleValues.Medium;

        header.Style.Border.BottomBorderColor = Amber;

        worksheet.Row(1).Height = 30;
    }

    private static void WriteWorkEntry(
        IXLWorksheet worksheet,
        int rowIndex,
        MonthlyWorkReport report,
        MonthlyWorkReportRow entry)
    {
        if (entry.WorkDate < report.PeriodStart ||
            entry.WorkDate > report.PeriodEnd)
        {
            throw new InvalidOperationException(
                $"Work entry '{entry.WorkEntryId}' " +
                "is outside the reporting period.");
        }

        if (entry.WorkDate.Year < 1900)
        {
            throw new InvalidOperationException(
                $"Work entry '{entry.WorkEntryId}' " +
                "has a date unsupported by Excel.");
        }

        if (entry.ActualMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Work entry '{entry.WorkEntryId}' " +
                "has an invalid actual duration.");
        }

        // The date remains an actual Excel date value.
        worksheet.Cell(rowIndex, 1).SetValue(
            entry.WorkDate.ToDateTime(
                TimeOnly.MinValue));

        WriteText(
            worksheet.Cell(rowIndex, 2),
            entry.TaskName,
            "TaskName",
            entry.WorkEntryId);

        WriteText(
            worksheet.Cell(rowIndex, 3),
            entry.WorkDescription,
            "WorkDescription",
            entry.WorkEntryId);

        // Numeric Excel duration, not a formatted string.
        worksheet.Cell(rowIndex, 4).SetValue(
            entry.ActualMinutes / MinutesPerDay);

        WriteText(
            worksheet.Cell(rowIndex, 5),
            entry.Note,
            "Note",
            entry.WorkEntryId);

        var dataRow = worksheet.Range(
            rowIndex,
            1,
            rowIndex,
            ColumnCount);

        dataRow.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Top;

        dataRow.Style.Border.BottomBorder =
            XLBorderStyleValues.Hair;

        dataRow.Style.Border.BottomBorderColor =
            XLColor.FromHtml("#E7E8EB");
    }

    private static void WriteText(
        IXLCell cell,
        string? value,
        string fieldName,
        Guid workEntryId)
    {
        value ??= string.Empty;

        if (value.Length > MaxExcelTextLength)
        {
            throw new InvalidOperationException(
                $"The field '{fieldName}' in work entry " +
                $"'{workEntryId}' exceeds Excel's " +
                $"{MaxExcelTextLength}-character cell limit.");
        }

        // Explicit text format. SetValue receives a string,
        // never a formula expression.
        // Values beginning with =, +, - or @ remain text.
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(value);
    }

    private static void WriteTotal(
        IXLWorksheet worksheet,
        int totalRow,
        int lastDataRow,
        int workEntryCount)
    {
        worksheet.Cell(totalRow, 3).SetValue("Итого");

        var totalCell = worksheet.Cell(totalRow, 4);

        if (workEntryCount == 0)
        {
            // An empty month must not contain SUM(D2:D1).
            totalCell.SetValue(0.0);
        }
        else
        {
            totalCell.FormulaA1 =
                $"SUM(D{FirstDataRow}:D{lastDataRow})";
        }

        totalCell.Style.NumberFormat.Format = DurationFormat;

        var totalRange = worksheet.Range(
            totalRow,
            1,
            totalRow,
            ColumnCount);

        totalRange.Style.Fill.BackgroundColor = LightAmber;
        totalRange.Style.Font.FontColor = Graphite;
        totalRange.Style.Font.Bold = true;

        totalRange.Style.Border.TopBorder =
            XLBorderStyleValues.Medium;

        totalRange.Style.Border.TopBorderColor = Amber;

        totalRange.Style.Alignment.Vertical =
            XLAlignmentVerticalValues.Center;

        worksheet.Row(totalRow).Height = 28;
    }
}