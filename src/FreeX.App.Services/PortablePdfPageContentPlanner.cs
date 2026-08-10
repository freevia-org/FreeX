using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
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
    bool IsTitleColumn,
    CellColor? ConditionalFillColor = null,
    // R96-render-cf-databar-iconset-1: the resolved data-bar/icon-set conditional format for this
    // cell (first matching rule of each kind, by priority), carried the same way ConditionalFillColor
    // already is, so WorkbookPdfContentBuilder can paint the bar/glyph instead of silently dropping it
    // (see PageContentRenderModelBuilder's identical PageCellBlock.DataBar/IconSet fields, which this
    // mirrors for the PDF export path).
    DataBarLayout? DataBar = null,
    IconSetResult? IconSet = null)
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

        var conditionalFormats = new ConditionalFormatRenderEvaluator(sheet);

        // R112-pdf-width-overflow-1: precompute each page column's character-width budget once
        // (from the sheet's real column width, same source ComputeActualGridSizes already reads for
        // PDF column geometry) so GetDisplayText can reproduce Excel's '#' overflow indicator for an
        // over-wide numeric/date value -- mirroring ViewportService.GetColumnWidthPixels /
        // EstimateCharacterWidth (the interactive grid) and PageContentRenderModelBuilder's identical
        // print-path estimate, which both already pass this into NumberFormatter.FormatWithColor.
        var columnWidthChars = new Dictionary<uint, int>(columns.Count);
        foreach (var column in columns)
            columnWidthChars.TryAdd(column.Column, EstimateCharacterWidth(GetColumnWidthPixels(sheet, column.Column)));

        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var address = new CellAddress(sheet.Id, row.Row, column.Column);
                var cell = sheet.GetCell(address);
                var styleId = cell?.StyleId ??
                    sheet.GetStyleOnly(row.Row, column.Column) ??
                    StyleId.Default;

                var cfResult = conditionalFormats.HasRules
                    ? conditionalFormats.Evaluate(address, cell?.Value ?? BlankValue.Instance)
                    : default;

                cells.Add(new PortablePdfPageCell(
                    row.Row,
                    column.Column,
                    GetDisplayText(workbook, sheet, cell, styleId, columnWidthChars[column.Column]),
                    styleId,
                    row.Role == PortablePdfPageAxisRole.Title,
                    column.Role == PortablePdfPageAxisRole.Title,
                    cfResult.Style?.FillColor,
                    cfResult.DataBar,
                    cfResult.IconSet));
            }
        }

        return cells;
    }

    private static string GetDisplayText(
        Workbook workbook,
        Sheet sheet,
        Cell? cell,
        StyleId styleId,
        int targetWidthCharacters)
    {
        if (cell is null)
            return "";

        if (sheet.ShowFormulas && cell.FormulaText is not null)
            return "=" + cell.FormulaText;

        var style = workbook.GetStyle(styleId);
        // R112-pdf-width-overflow-1: pass the page column's character-width budget (and honor
        // ShrinkToFit the same way ViewportService.GetDisplayText does -- Excel never shows '####'
        // when the cell shrinks its font to fit instead) so an over-wide numeric/date value renders
        // Excel's '#' overflow indicator here too, instead of the raw digits silently overflowing
        // into the neighboring cell on the PDF page.
        var displayText = NumberFormatter.FormatWithColor(
            cell.Value,
            style.NumberFormat,
            targetWidthCharacters,
            workbook.IndexedColors,
            workbook.Theme,
            workbook.Uses1904DateSystem,
            suppressWidthOverflowIndicator: style.ShrinkToFit).Text;

        // N47: honor Page Setup > Sheet > "Cell errors as" (blank/dashes/#N/A) the same way the WPF
        // PrintRenderer path does via PagePrintTextPlanner.FormatPrintedCellText, so error cells print
        // consistently substituted on the Avalonia/portable PDF path too.
        return PagePrintTextPlanner.FormatPrintedCellText(displayText, sheet.PrintErrorValue);
    }

    /// <summary>
    /// The page column's raw pixel width, mirroring <c>ViewportService.GetColumnWidthPixels</c> and
    /// the identical read <c>WorkbookPdfContentBuilder.ComputeActualGridSizes</c> already performs
    /// for PDF column geometry (sheet.ColumnWidths, falling back to DefaultColumnWidth).
    /// </summary>
    private static double GetColumnWidthPixels(Sheet sheet, uint col) =>
        Math.Max(1, ColumnWidthPixelMapper.ColumnWidthToPixels(sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth)));

    /// <summary>
    /// Converts a column's pixel width to an approximate character-width budget, matching
    /// <c>ViewportService.EstimateCharacterWidth</c> / <c>PageContentRenderModelBuilder.EstimateCharacterWidth</c>
    /// (~7 pixels/character above 12px, else pixels/12) so the PDF export's overflow detection agrees
    /// with the interactive grid and the print path.
    /// </summary>
    private static int EstimateCharacterWidth(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= 0)
            return 1;

        var width = pixelWidth <= 12
            ? pixelWidth / 12.0
            : (pixelWidth - 5.0) / 7.0;
        return Math.Max(1, (int)Math.Round(width, MidpointRounding.AwayFromZero));
    }
}
