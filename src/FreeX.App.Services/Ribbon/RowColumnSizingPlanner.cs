using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Framework-neutral planner for the Row Height / Column Width / AutoFit operations behind
/// Home ▸ Cells ▸ Format and the row/column header context menus. It turns a selection into the
/// undoable <see cref="SetRowHeightCommand"/> / <see cref="SetColumnWidthCommand"/> (reused from the
/// Windows host), and — for AutoFit — sizes each row/column from its cell display text via the shared
/// <see cref="AutoFitSizingService"/>. The measurement is a content-based character estimate (no true
/// glyph metrics), which is the only faithful option that runs headlessly on every platform.
///
/// Matching Excel's AutoFit semantics, the content scan excludes: cells that are part of a merged
/// region (Excel never lets a merged cell's content grow row/column sizing), and rows/columns that
/// are effectively hidden (manual hide, AutoFilter, or group-collapse) — a hidden row's long content
/// must not inflate a column's AutoFit width, and vice versa.
/// </summary>
public readonly record struct RowColumnSizePlan(uint Index, double Size);

public static class RowColumnSizingPlanner
{
    /// <summary>
    /// <see cref="Sheet.RowHeights"/>/<see cref="Sheet.DefaultRowHeight"/> are stored in device
    /// pixels at 96 DPI (matching the row-header/grid rendering unit), while Excel's Row Height
    /// dialog and its "0 to 409.5" ceiling are expressed in points -- the same 96/72 conversion
    /// XlsxFileAdapter/XlsxWorksheetRowColumnLayoutReader apply at the XLSX file-I/O boundary.
    /// </summary>
    private const double PixelsPerPoint = 96.0 / 72.0;

    public static double GetRowHeightDialogValue(Sheet? sheet, GridRange range, double fallbackHeight = 20)
    {
        if (sheet is null)
            return fallbackHeight;

        var (startRow, _) = SelectionRangeService.GetRowSpan(range);
        var heightPixels = sheet.RowHeights.TryGetValue(startRow, out var height) ? height : sheet.DefaultRowHeight;
        return heightPixels / PixelsPerPoint;
    }

    public static double GetColumnWidthDialogValue(Sheet? sheet, GridRange range, double fallbackWidth = 8.43)
    {
        if (sheet is null)
            return fallbackWidth;

        var (startCol, _) = SelectionRangeService.GetColumnSpan(range);
        return sheet.ColumnWidths.TryGetValue(startCol, out var width) ? width : sheet.DefaultColumnWidth;
    }

    /// <param name="height">The dialog-entered height in points (Excel's Row Height unit); converted here to the pixel unit <see cref="Sheet.RowHeights"/> stores.</param>
    public static IWorkbookCommand CreateRowHeightCommand(SheetId sheetId, GridRange range, double height)
    {
        var (startRow, endRow) = SelectionRangeService.GetRowSpan(range);
        return new SetRowHeightCommand(sheetId, startRow, endRow, height * PixelsPerPoint);
    }

    public static IWorkbookCommand CreateColumnWidthCommand(SheetId sheetId, GridRange range, double width)
    {
        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(range);
        return new SetColumnWidthCommand(sheetId, startCol, endCol, width);
    }

    public static IWorkbookCommand CreateRowsHiddenCommand(SheetId sheetId, GridRange range, bool hidden)
    {
        var (startRow, endRow) = SelectionRangeService.GetRowSpan(range);
        return new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden);
    }

    public static IWorkbookCommand CreateColumnsHiddenCommand(SheetId sheetId, GridRange range, bool hidden)
    {
        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(range);
        return new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden);
    }

    public static IReadOnlyList<RowColumnSizePlan> PlanAutoFitRowHeights(
        Sheet sheet,
        GridRange selection,
        GridRange? usedRange,
        Func<uint, uint, AutoFitCellText?> getDisplayText,
        double defaultHeight)
    {
        var bounds = GetMeasurementBounds(selection, usedRange, AutoFitAxis.Rows);
        if (bounds is null)
            return [];

        var plans = new List<RowColumnSizePlan>();
        for (var row = bounds.Value.Start.Row; row <= bounds.Value.End.Row; row++)
        {
            var texts = CollectRowTexts(sheet, row, bounds.Value, getDisplayText);
            plans.Add(new RowColumnSizePlan(row, AutoFitSizingService.EstimateRowHeight(texts, defaultHeight)));
        }

        return plans;
    }

    public static IReadOnlyList<RowColumnSizePlan> PlanAutoFitColumnWidths(
        Sheet sheet,
        GridRange selection,
        GridRange? usedRange,
        Func<uint, uint, AutoFitCellText?> getDisplayText,
        double defaultWidth)
    {
        var bounds = GetMeasurementBounds(selection, usedRange, AutoFitAxis.Columns);
        if (bounds is null)
            return [];

        var plans = new List<RowColumnSizePlan>();
        for (var col = bounds.Value.Start.Col; col <= bounds.Value.End.Col; col++)
        {
            var texts = CollectColumnTexts(sheet, col, bounds.Value, getDisplayText);
            plans.Add(new RowColumnSizePlan(col, AutoFitSizingService.EstimateColumnWidth(texts, defaultWidth)));
        }

        return plans;
    }

    public static IWorkbookCommand? CreateAutoFitRowHeightCommand(
        SheetId sheetId,
        IReadOnlyList<RowColumnSizePlan> plans) =>
        CreateAutoFitCommand(plans, "Auto Row Height", plan => new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size));

    public static IWorkbookCommand? CreateAutoFitColumnWidthCommand(
        SheetId sheetId,
        IReadOnlyList<RowColumnSizePlan> plans) =>
        CreateAutoFitCommand(plans, "Auto Column Width", plan => new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size));

    private static IWorkbookCommand? CreateAutoFitCommand(
        IReadOnlyList<RowColumnSizePlan> plans,
        string compositeName,
        Func<RowColumnSizePlan, IWorkbookCommand> createCommand)
    {
        if (plans.Count == 0)
            return null;
        if (plans.Count == 1)
            return createCommand(plans[0]);

        return new CompositeWorkbookCommand(compositeName, plans.Select(createCommand).ToList());
    }

    private static GridRange? GetMeasurementBounds(GridRange selection, GridRange? usedRange, AutoFitAxis axis)
    {
        if (axis == AutoFitAxis.Rows && selection.RowCount == CellAddress.MaxRow)
            return null;

        if (axis == AutoFitAxis.Columns && selection.ColCount == CellAddress.MaxCol)
            return null;

        if (axis == AutoFitAxis.Columns && selection.RowCount == CellAddress.MaxRow)
        {
            if (usedRange is null)
                return null;

            return new GridRange(
                new CellAddress(selection.Start.Sheet, usedRange.Value.Start.Row, selection.Start.Col),
                new CellAddress(selection.Start.Sheet, usedRange.Value.End.Row, selection.End.Col));
        }

        if (axis == AutoFitAxis.Rows && selection.ColCount == CellAddress.MaxCol)
        {
            if (usedRange is null)
                return null;

            return new GridRange(
                new CellAddress(selection.Start.Sheet, selection.Start.Row, usedRange.Value.Start.Col),
                new CellAddress(selection.Start.Sheet, selection.End.Row, usedRange.Value.End.Col));
        }

        return selection;
    }

    private static List<AutoFitCellText> CollectRowTexts(Sheet sheet, uint row, GridRange bounds, Func<uint, uint, AutoFitCellText?> getDisplayText)
    {
        var texts = new List<AutoFitCellText>();
        for (var col = bounds.Start.Col; col <= bounds.End.Col; col++)
        {
            if (sheet.IsColEffectivelyHidden(col))
                continue;
            if (sheet.IsMerged(new CellAddress(bounds.Start.Sheet, row, col)))
                continue;
            if (getDisplayText(row, col) is not { } cellText)
                continue;

            var columnWidth = sheet.ColumnWidths.TryGetValue(col, out var explicitWidth) ? explicitWidth : sheet.DefaultColumnWidth;
            texts.Add(cellText with { ColumnWidth = columnWidth });
        }

        return texts;
    }

    private static List<AutoFitCellText> CollectColumnTexts(Sheet sheet, uint col, GridRange bounds, Func<uint, uint, AutoFitCellText?> getDisplayText)
    {
        var texts = new List<AutoFitCellText>();
        for (var row = bounds.Start.Row; row <= bounds.End.Row; row++)
        {
            if (sheet.IsRowEffectivelyHidden(row))
                continue;
            if (sheet.IsMerged(new CellAddress(bounds.Start.Sheet, row, col)))
                continue;
            if (getDisplayText(row, col) is { } cellText)
                texts.Add(cellText);
        }

        return texts;
    }

    private enum AutoFitAxis
    {
        Rows,
        Columns
    }
}
