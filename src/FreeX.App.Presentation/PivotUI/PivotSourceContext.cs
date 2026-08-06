using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Resolves the data the pivot field pane and header dropdowns need from a worksheet: the active pivot for a
/// given cell, the pivot's source-range column headers (indexed by source field index), and which source
/// columns hold numeric data (so the drag validator defaults a freshly-dropped values field to sum vs.
/// count). Mirrors the desktop host's <c>ReadPivotSourceHeaders</c> while staying UI-free and testable.
/// </summary>
public static class PivotSourceContext
{
    /// <summary>
    /// The pivot whose rendered (or target) range contains <paramref name="activeCell"/>, or null when the
    /// cell is not inside any pivot. Compared by row/column only so a pivot range loaded with a placeholder
    /// sheet id still matches the live active cell (mirrors <c>RibbonContextStateMapper.IsActiveCellInPivot</c>).
    /// </summary>
    public static PivotTableModel? FindActivePivot(Sheet sheet, CellAddress activeCell)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        foreach (var pivot in sheet.PivotTables)
        {
            var range = pivot.LastRenderedRange ?? pivot.TargetRange;
            if (RangeContains(range, activeCell))
                return pivot;
        }

        return null;
    }

    /// <summary>
    /// The header text of each source column, indexed by source field index (column 0 of the source range is
    /// index 0). Returns an empty list when the source sheet is unavailable.
    /// </summary>
    public static IReadOnlyList<string> ReadHeaders(
        Workbook workbook,
        PivotTableModel pivotTable,
        Sheet? fallbackSheet = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(pivotTable);

        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? fallbackSheet;
        if (sourceSheet is null)
            return PivotSourceHeaderResolver.Resolve(workbook, pivotTable, []);

        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
        {
            var caption = SpreadsheetDisplayFormatter
                .FormatCellValue(sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value)
                .Trim();
            headers.Add(string.IsNullOrWhiteSpace(caption) ? $"Column {headers.Count + 1}" : caption);
        }

        return PivotSourceHeaderResolver.Resolve(workbook, pivotTable, headers);
    }

    /// <summary>
    /// True when the source column at <paramref name="sourceFieldIndex"/> holds numeric data in its first
    /// data row (the row immediately below the header). Used as the numeric predicate for
    /// <see cref="Presentation.PivotUI.PivotFieldDragValidator"/>.
    /// </summary>
    public static bool IsNumericSourceColumn(
        Workbook workbook,
        PivotTableModel pivotTable,
        int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(pivotTable);

        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        return sourceSheet is not null &&
               sourceFieldIndex >= 0 &&
               PivotUiPlanner.IsNumericSourceField(sourceSheet, pivotTable, sourceFieldIndex);
    }

    private static bool RangeContains(GridRange range, CellAddress addr) =>
        addr.Row >= range.Start.Row && addr.Row <= range.End.Row &&
        addr.Col >= range.Start.Col && addr.Col <= range.End.Col;
}
