using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum PortablePdfPageContentPlanStatus
{
    Ready,
    PageRequestUnavailable,
    SheetUnavailable
}

public enum PortablePdfPageAxisRole
{
    Title,
    Body
}

public sealed record PortablePdfPageRow(uint Row, PortablePdfPageAxisRole Role);

public sealed record PortablePdfPageColumn(uint Column, PortablePdfPageAxisRole Role);

public sealed record PortablePdfPageCell(
    uint Row,
    uint Column,
    string DisplayText,
    StyleId StyleId,
    bool IsTitleRow,
    bool IsTitleColumn)
{
    public bool IsTitle => IsTitleRow || IsTitleColumn;
    public bool IsBody => !IsTitle;
}

public sealed record PortablePdfPageContentPlan(
    PortablePdfPageContentPlanStatus Status,
    string StatusText,
    PortablePdfExportPageRequest? PageRequest,
    IReadOnlyList<PortablePdfPageRow> Rows,
    IReadOnlyList<PortablePdfPageColumn> Columns,
    IReadOnlyList<PortablePdfPageCell> Cells)
{
    public bool IsReady => Status == PortablePdfPageContentPlanStatus.Ready;
    public int RowCount => Rows.Count;
    public int ColumnCount => Columns.Count;
}

public static class PortablePdfPageContentPlanner
{
    public static PortablePdfPageContentPlan CreatePlan(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        int exportPageNumber)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);

        PortablePdfExportPageRequest? pageRequest = null;
        foreach (var request in exportPlan.PageRequests)
        {
            if (request.ExportPageNumber != exportPageNumber)
                continue;

            pageRequest = request;
            break;
        }

        return pageRequest is null
            ? PageRequestUnavailable(exportPageNumber)
            : CreatePlan(workbook, pageRequest);
    }

    public static PortablePdfPageContentPlan CreatePlan(
        Workbook workbook,
        PortablePdfExportPageRequest pageRequest)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(pageRequest);

        var sheet = workbook.GetSheet(pageRequest.PrintRange.Start.Sheet);
        if (sheet is null)
        {
            return new PortablePdfPageContentPlan(
                PortablePdfPageContentPlanStatus.SheetUnavailable,
                $"Portable PDF page {pageRequest.ExportPageNumber} references a worksheet that is not available in the workbook.",
                pageRequest,
                [],
                [],
                []);
        }

        var rows = BuildRows(pageRequest.PageSpans);
        var columns = BuildColumns(pageRequest.PageSpans);
        var cells = BuildCells(workbook, sheet, rows, columns);

        return new PortablePdfPageContentPlan(
            PortablePdfPageContentPlanStatus.Ready,
            $"Ready to render portable PDF page {pageRequest.ExportPageNumber}: {rows.Count} rows, {columns.Count} columns, {cells.Count} cells.",
            pageRequest,
            rows,
            columns,
            cells);
    }

    private static PortablePdfPageContentPlan PageRequestUnavailable(int exportPageNumber) =>
        new(
            PortablePdfPageContentPlanStatus.PageRequestUnavailable,
            $"Portable PDF page {exportPageNumber} is not present in the export plan.",
            null,
            [],
            [],
            []);

    private static IReadOnlyList<PortablePdfPageRow> BuildRows(PortablePdfExportPageSpans spans) =>
        spans.TitleRows.Select(row => new PortablePdfPageRow(row, PortablePdfPageAxisRole.Title))
            .Concat(spans.BodyRows.Select(row => new PortablePdfPageRow(row, PortablePdfPageAxisRole.Body)))
            .ToArray();

    private static IReadOnlyList<PortablePdfPageColumn> BuildColumns(PortablePdfExportPageSpans spans) =>
        spans.TitleColumns.Select(column => new PortablePdfPageColumn(column, PortablePdfPageAxisRole.Title))
            .Concat(spans.BodyColumns.Select(column => new PortablePdfPageColumn(column, PortablePdfPageAxisRole.Body)))
            .ToArray();

    private static IReadOnlyList<PortablePdfPageCell> BuildCells(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<PortablePdfPageRow> rows,
        IReadOnlyList<PortablePdfPageColumn> columns)
    {
        var cells = new List<PortablePdfPageCell>(rows.Count * columns.Count);
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var address = new CellAddress(sheet.Id, row.Row, column.Column);
                var cell = sheet.GetCell(address);
                var styleId = cell?.StyleId ??
                    sheet.GetStyleOnly(row.Row, column.Column) ??
                    StyleId.Default;

                cells.Add(new PortablePdfPageCell(
                    row.Row,
                    column.Column,
                    GetDisplayText(workbook, sheet, cell, styleId),
                    styleId,
                    row.Role == PortablePdfPageAxisRole.Title,
                    column.Role == PortablePdfPageAxisRole.Title));
            }
        }

        return cells;
    }

    private static string GetDisplayText(
        Workbook workbook,
        Sheet sheet,
        Cell? cell,
        StyleId styleId)
    {
        if (cell is null)
            return "";

        if (sheet.ShowFormulas && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        var style = workbook.GetStyle(styleId);
        var displayText = NumberFormatter.FormatWithColor(
            cell.Value,
            style.NumberFormat,
            workbook.IndexedColors,
            workbook.Theme,
            workbook.Uses1904DateSystem).Text;

        // N47: honor Page Setup > Sheet > "Cell errors as" (blank/dashes/#N/A) the same way the WPF
        // PrintRenderer path does via PagePrintTextPlanner.FormatPrintedCellText, so error cells print
        // consistently substituted on the Avalonia/portable PDF path too.
        return PagePrintTextPlanner.FormatPrintedCellText(displayText, sheet.PrintErrorValue);
    }
}
