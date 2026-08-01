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

    // ── Vacated-band format inheritance for whole-row/whole-column insert (R92-render-cellstyle-
    // inheritance-5-2) ────────────────────────────────────────────────────────────────────────
    // Excel's Insert Sheet Rows/Columns default ("Insert Options" smart-tag) copies the formatting
    // of the row above (row insert) / column to the left (column insert) into every new blank cell
    // of the inserted band, instead of leaving it at General/default formatting. This mirrors the
    // identical feature InsertDeleteCellsCommand already implements for the band-scoped Insert
    // Cells command (InheritVacatedFormatShiftRight/Down there) — that fix was never carried over
    // to the far more commonly used whole-row/whole-column insert commands.
    //
    // Must run AFTER ShiftAddressBearingRowsUp/ColumnsUp (which calls ApplyShiftedStyleOnlyEntries,
    // clearing and rebuilding the entire style-only store from the pre-insert snapshot) — otherwise
    // the newly-inherited entries this creates would be wiped out by that rebuild. The neighbor row/
    // column is never touched by the insert itself, so it is safe to read directly off the sheet at
    // any point after the shift.

    /// <summary>
    /// For each newly-inserted row in [<paramref name="beforeRow"/>..<paramref name="beforeRow"/>+
    /// <paramref name="count"/>-1], applies the effective StyleId of the row immediately above
    /// (<paramref name="beforeRow"/>-1) to every blank cell, column-by-column — a no-op if the
    /// insert is at the top of the sheet (no row above) or a given column has no formatting above.
    /// </summary>
    internal static void InheritVacatedRowFormatFromAbove(Sheet sheet, uint beforeRow, uint count)
    {
        if (beforeRow <= 1)
            return;

        var neighborRow = beforeRow - 1;
        HashSet<uint>? columns = null;
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (row != neighborRow || cell.StyleId == StyleId.Default)
                continue;
            (columns ??= []).Add(col);
        }

        if (sheet.HasStyleOnlyCells)
        {
            foreach (var (key, _) in sheet.GetStyleOnlyEntries())
            {
                if (key.Row == neighborRow)
                    (columns ??= []).Add(key.Col);
            }
        }

        if (columns is null)
            return;

        foreach (var col in columns)
        {
            if (GetEffectiveStyleIdForFormatInherit(sheet, neighborRow, col) is not { } style)
                continue;

            for (var row = beforeRow; row < beforeRow + count; row++)
                sheet.SetStyleOnly(row, col, style);
        }
    }

    /// <summary>Column-insert analogue of <see cref="InheritVacatedRowFormatFromAbove"/>: inherits from the column to the left.</summary>
    internal static void InheritVacatedColumnFormatFromLeft(Sheet sheet, uint beforeCol, uint count)
    {
        if (beforeCol <= 1)
            return;

        var neighborCol = beforeCol - 1;
        HashSet<uint>? rows = null;
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (col != neighborCol || cell.StyleId == StyleId.Default)
                continue;
            (rows ??= []).Add(row);
        }

        if (sheet.HasStyleOnlyCells)
        {
            foreach (var (key, _) in sheet.GetStyleOnlyEntries())
            {
                if (key.Col == neighborCol)
                    (rows ??= []).Add(key.Row);
            }
        }

        if (rows is null)
            return;

        foreach (var row in rows)
        {
            if (GetEffectiveStyleIdForFormatInherit(sheet, row, neighborCol) is not { } style)
                continue;

            for (var col = beforeCol; col < beforeCol + count; col++)
                sheet.SetStyleOnly(row, col, style);
        }
    }

    /// <summary>
    /// Returns the effective StyleId of a cell for format-inheritance purposes: its live
    /// Cell.StyleId if occupied (unless that is the default style), else its style-only override
    /// if any, else null (fully default — nothing to propagate). Mirrors
    /// InsertDeleteCellsCommand.GetEffectiveStyleId (the band-scoped Insert Cells equivalent).
    /// </summary>
    private static StyleId? GetEffectiveStyleIdForFormatInherit(Sheet sheet, uint row, uint col)
    {
        var cell = sheet.GetCell(row, col);
        if (cell is not null)
            return cell.StyleId == StyleId.Default ? null : cell.StyleId;

        return sheet.GetStyleOnly(row, col);
    }

    // R111-commands-insert-overflow-metadata-1: Insert Rows/Columns' past-the-boundary overflow
    // guard (InsertRowsCommand.Apply / InsertColumnsCommand.Apply) used to derive its "is there
    // anything down there to overflow" check purely from GetOccupiedCellMap/CellCount -- i.e. only
    // rows/columns holding an actual Cell object. Row/column-level state that lives OUTSIDE the
    // cell dictionary (a style-only formatting band from a whole-row/column header select with no
    // cell value, a RowHeights/ColumnWidths override, a hidden-row/column flag, or an outline/group
    // level) was invisible to it, so such metadata at the sheet's last row/column silently shifted
    // past MaxRow/MaxCol with no error -- and was then dropped on save (Excel itself refuses the
    // insert here: "cannot shift nonblank cells off the worksheet" treats a formatted-but-valueless
    // row/column as non-blank too). These two helpers report the highest row/column that carries
    // ANY of that state (content OR metadata) so the two Apply methods can widen their guard.
    //
    // Sheet.GetUsedRange() already folds style-only entries into the value/spill bounding box, so
    // it alone covers cell values, spills, AND style-only bands; RowHeights/HiddenRows/
    // RowOutlineLevels (or their column counterparts) are the remaining metadata that live wholly
    // outside it and must be checked separately.
    internal static uint HighestFormattedOrOccupiedRow(Sheet sheet)
    {
        var highest = sheet.GetUsedRange()?.End.Row ?? 0;

        foreach (var row in sheet.RowHeights.Keys)
            if (row > highest) highest = row;
        foreach (var row in sheet.HiddenRows)
            if (row > highest) highest = row;
        foreach (var row in sheet.RowOutlineLevels.Keys)
            if (row > highest) highest = row;

        return highest;
    }

    internal static uint HighestFormattedOrOccupiedColumn(Sheet sheet)
    {
        var highest = sheet.GetUsedRange()?.End.Col ?? 0;

        foreach (var col in sheet.ColumnWidths.Keys)
            if (col > highest) highest = col;
        foreach (var col in sheet.HiddenCols)
            if (col > highest) highest = col;
        foreach (var col in sheet.ColOutlineLevels.Keys)
            if (col > highest) highest = col;

        return highest;
    }
}
