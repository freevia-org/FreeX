using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static Dictionary<string, NamedRangeSnapshot> CaptureNamedRanges(Workbook workbook) =>
        workbook.NamedRanges.ToDictionary(
            pair => pair.Key,
            pair => new NamedRangeSnapshot(
                pair.Value,
                workbook.TryGetNamedRangeMetadata(pair.Key, out var metadata) ? metadata : NamedRangeMetadata.WorkbookScope),
            StringComparer.OrdinalIgnoreCase);

    internal static void RestoreNamedRanges(Workbook workbook, Dictionary<string, NamedRangeSnapshot>? snapshot)
    {
        if (snapshot is null)
            return;

        workbook.NamedRanges.Clear();
        workbook.NamedRangeMetadataByName.Clear();
        foreach (var (name, namedRange) in snapshot)
            workbook.DefineNamedRange(name, namedRange.Range, namedRange.Metadata);
    }

    internal static void ShiftNamedRangeRowsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeRowsUp(range, start, count);
        }
    }

    internal static void ShiftNamedRangeRowsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeRowsDown(range, start, count);
            if (shifted is null) workbook.RemoveNamedRange(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }
    }

    internal static void ShiftNamedRangeColumnsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeColumnsUp(range, start, count);
        }
    }

    internal static void ShiftNamedRangeColumnsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeColumnsDown(range, start, count);
            if (shifted is null) workbook.RemoveNamedRange(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }
    }

    internal static void ShiftPrintAreaRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeRowsUp(printArea, start, count);
    }

    internal static void ShiftPrintAreaRowsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeRowsDown(printArea, start, count);  // null clears the print area
    }

    internal static void ShiftPrintAreaColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeColumnsUp(printArea, start, count);
    }

    internal static void ShiftPrintAreaColumnsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeColumnsDown(printArea, start, count);  // null clears the print area
    }


    internal static List<GridRange> CaptureChartDataRanges(Sheet sheet) =>
        sheet.Charts.Select(c => c.DataRange).ToList();

    internal static void RestoreChartDataRanges(Sheet sheet, List<GridRange>? snapshot)
    {
        if (snapshot is null) return;
        for (int i = 0; i < sheet.Charts.Count && i < snapshot.Count; i++)
            sheet.Charts[i].DataRange = snapshot[i];
    }

    internal static void ShiftChartRowsUp(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeRowsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartRowsDown(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeRowsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    internal static void ShiftChartColumnsUp(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeColumnsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartColumnsDown(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeColumnsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    private static GridRange ShiftRangeRowsUp(GridRange range, uint start, uint count)
    {
        if (range.End.Row < start)
            return range;

        var newStartRow = range.Start.Row >= start ? range.Start.Row + count : range.Start.Row;
        var newEndRow = range.End.Row + count;
        return new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
    }

    private static GridRange? ShiftRangeRowsDown(GridRange range, uint start, uint count)
    {
        var end = start + count - 1;
        if (range.End.Row < start)
            return range;    // entirely above: unchanged
        if (range.Start.Row > end)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row - count, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row - count, range.End.Col));
        }

        // Overlapping range: compute the surviving portion.
        var newStartRow = range.Start.Row < start ? range.Start.Row : start;
        // If the range end is inside the deletion zone, the last surviving row is start-1.
        // If entirely within the deletion zone (start == newStartRow), nothing survives.
        var newEndRow = range.End.Row > end ? range.End.Row - count : start - 1;
        if (newStartRow == start && newEndRow < start)
            return null;   // range was entirely within the deleted rows
        return new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
    }

    private static GridRange ShiftRangeColumnsUp(GridRange range, uint start, uint count)
    {
        if (range.End.Col < start)
            return range;

        var newStartCol = range.Start.Col >= start ? range.Start.Col + count : range.Start.Col;
        var newEndCol = range.End.Col + count;
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
    }

    private static GridRange? ShiftRangeColumnsDown(GridRange range, uint start, uint count)
    {
        var end = start + count - 1;
        if (range.End.Col < start)
            return range;    // entirely left: unchanged
        if (range.Start.Col > end)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - count),
                new CellAddress(range.End.Sheet, range.End.Row, range.End.Col - count));
        }

        // Overlapping range: compute the surviving portion.
        var newStartCol = range.Start.Col < start ? range.Start.Col : start;
        var newEndCol = range.End.Col > end ? range.End.Col - count : start - 1;
        if (newStartCol == start && newEndCol < start)
            return null;   // range was entirely within the deleted columns
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
    }
}
