using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    private static GridRange ShiftRangeRowsUp(GridRange range, uint start, uint count)
    {
        // A whole-column range (Start.Row == 1, End.Row == MaxRow) already spans every row on
        // the sheet. Row insert is a perpendicular-axis edit for it: the range must stay "all
        // rows" and not have its endpoints nudged, or it stops being a full-column range.
        if (SelectionRangeService.IsWholeColumnSelection(range))
            return range;

        if (range.End.Row < start)
            return range;

        // Clamp both endpoints to MaxRow so that full-column ranges (End.Row == MaxRow) remain
        // valid after insert, and so a start near the sheet bottom cannot overflow past MaxRow.
        var newStartRow = range.Start.Row >= start
            ? Math.Min(range.Start.Row + count, CellAddress.MaxRow)
            : range.Start.Row;
        var newEndRow = Math.Min(range.End.Row + count, CellAddress.MaxRow);
        return new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
    }

    private static GridRange? ShiftRangeRowsDown(GridRange range, uint start, uint count)
    {
        // A whole-column range spans every row already; deleting rows (a perpendicular-axis
        // edit for it) must leave it untouched instead of eroding its full row extent.
        if (SelectionRangeService.IsWholeColumnSelection(range))
            return range;

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
        // A whole-row range (Start.Col == 1, End.Col == MaxCol) already spans every column on
        // the sheet. Column insert is a perpendicular-axis edit for it: the range must stay
        // "all columns" and not have its endpoints nudged.
        if (SelectionRangeService.IsWholeRowSelection(range))
            return range;

        if (range.End.Col < start)
            return range;

        // Clamp both endpoints to MaxCol so that full-row ranges (End.Col == MaxCol) remain
        // valid after insert, and so a start near the sheet edge cannot overflow past MaxCol.
        var newStartCol = range.Start.Col >= start
            ? Math.Min(range.Start.Col + count, CellAddress.MaxCol)
            : range.Start.Col;
        var newEndCol = Math.Min(range.End.Col + count, CellAddress.MaxCol);
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
    }

    private static GridRange? ShiftRangeColumnsDown(GridRange range, uint start, uint count)
    {
        // A whole-row range spans every column already; deleting columns (a perpendicular-axis
        // edit for it) must leave it untouched instead of eroding its full column extent.
        if (SelectionRangeService.IsWholeRowSelection(range))
            return range;

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
