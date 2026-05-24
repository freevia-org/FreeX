using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
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
