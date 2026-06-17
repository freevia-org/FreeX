using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Pivot;

/// <summary>
/// Resolves the data the pivot field pane and header dropdowns need from a worksheet: the active pivot for a
/// given cell, the pivot's source-range column headers (indexed by source field index), and which source
/// columns hold numeric data (so the drag validator defaults a freshly-dropped values field to sum vs.
/// count). Mirrors the desktop host's <c>ReadPivotSourceHeaders</c> while staying UI-free and testable.
/// </summary>
internal static class PivotSourceContext
{
    /// <summary>
    /// The pivot whose rendered (or target) range contains <paramref name="activeCell"/>, or null when the
    /// cell is not inside any pivot. Compared by row/column only so a pivot range loaded with a placeholder
    /// sheet id still matches the live active cell (mirrors <c>RibbonContextStateMapper.IsActiveCellInPivot</c>).
    /// </summary>
    internal static PivotTableModel? FindActivePivot(Sheet sheet, CellAddress activeCell)
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
    internal static IReadOnlyList<string> ReadHeaders(Workbook workbook, PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(pivotTable);

        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return [];

        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
            headers.Add(HeaderText(sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value));

        return headers;
    }

    /// <summary>
    /// True when the source column at <paramref name="sourceFieldIndex"/> holds numeric data in its first
    /// data row (the row immediately below the header). Used as the numeric predicate for
    /// <see cref="Presentation.PivotUI.PivotFieldDragValidator"/>.
    /// </summary>
    internal static bool IsNumericSourceColumn(
        Workbook workbook,
        PivotTableModel pivotTable,
        int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(pivotTable);

        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || sourceFieldIndex < 0)
            return false;

        var col = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        if (col > pivotTable.SourceRange.End.Col)
            return false;

        var firstDataRow = pivotTable.SourceRange.Start.Row + 1;
        if (firstDataRow > pivotTable.SourceRange.End.Row)
            return false;

        var value = sourceSheet.GetCell(firstDataRow, col)?.Value;
        return value is NumberValue or DateTimeValue;
    }

    private static string HeaderText(ScalarValue? value) => value switch
    {
        null or BlankValue => string.Empty,
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        DateTimeValue date => date.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        _ => value.ToString() ?? string.Empty,
    };

    private static bool RangeContains(GridRange range, CellAddress addr) =>
        addr.Row >= range.Start.Row && addr.Row <= range.End.Row &&
        addr.Col >= range.Start.Col && addr.Col <= range.End.Col;
}
