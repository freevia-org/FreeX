using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Services;

public readonly record struct QuickSortRangePlan(GridRange Range, uint SortByColOffset);

public static class QuickSortRangePlanner
{
    public static QuickSortRangePlan Create(Sheet sheet, GridRange selectedRange, CellAddress? activeCell)
    {
        var sortCell = ResolveActiveCell(selectedRange, activeCell);
        var candidateRange = ResolveCandidateRange(sheet, selectedRange, sortCell);
        var sortByColOffset = ResolveSortByColumnOffset(candidateRange, sortCell);
        var sortRange = SortDialogPlanner.ExcludeHeaderRow(candidateRange, HasLikelyHeaderRow(sheet, candidateRange));

        if (sortByColOffset >= sortRange.ColCount)
            sortByColOffset = 0;

        return new QuickSortRangePlan(sortRange, sortByColOffset);
    }

    public static GridRange ResolveCandidateRange(Sheet sheet, GridRange selectedRange, CellAddress? activeCell)
    {
        var sortCell = ResolveActiveCell(selectedRange, activeCell);
        return ResolveCandidateRange(sheet, selectedRange, sortCell);
    }

    /// <summary>
    /// Detects Excel's "Sort Warning" condition: <paramref name="selectedRange"/> is a genuine
    /// multi-cell selection (single-cell selections silently auto-expand via
    /// <see cref="ResolveCandidateRange(Sheet,GridRange,CellAddress?)"/> and never need this prompt)
    /// that is a proper subset of a larger contiguous current-region block -- e.g. selecting only
    /// C2:C6 out of a A2:C6 table. Returns the surrounding block <see cref="WorkbookSession"/> would
    /// sort instead if the host resolves "Expand the selection", or <c>null</c> when there is no
    /// adjacent data to warn about (including when the selection already covers its whole current
    /// region).
    /// </summary>
    public static GridRange? ResolveAdjacentDataExpansion(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.RowCount == 1 && selectedRange.ColCount == 1)
            return null;

        if (SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is not { } region)
            return null;

        // "The selection already covers its whole block, so there is nothing to expand into."
        // That is a question about POSITION, not size: a block can be exactly as wide as the
        // selection and still sit beside it. Selecting whole columns B:D over a table in A1:C6
        // matches on counts (3 and 3) while column A -- the Name column whose pairing the sort
        // would destroy -- lies outside the selection entirely. Comparing counts here returned
        // null before the axis-aware logic below ever ran, which is how the warning stayed broken
        // for this gesture across several rounds of fixing the code underneath it.
        if (region.Start.Row >= selectedRange.Start.Row && region.Start.Col >= selectedRange.Start.Col &&
            region.End.Row <= selectedRange.End.Row && region.End.Col <= selectedRange.End.Col)
        {
            return null;
        }

        // A whole-column or whole-row selection (e.g. clicking a column header, then Sort A-Z --
        // the single most common way to trigger a sort) reaches CellAddress.MaxRow/MaxCol far past
        // any real data. Comparing that raw against a real-data region below would always fail the
        // "region contains the whole selection" check, so the warning would never fire for exactly
        // the gesture it exists to protect. Clamp the selection to the sheet's real data extent
        // before comparing so this reads the same as an ordinary partial-column/row selection.
        var comparisonRange = ClampToUsedRange(sheet, selectedRange);

        // Real Excel never shows the Sort Warning inside a genuine structured Table (ListObject) --
        // the table itself already defines the record boundary, so a Table sort silently operates
        // on the whole table for the chosen column with no prompt. Suppress the warning whenever
        // the (data-clamped) selection sits entirely inside one of the sheet's tables.
        if (IsFullyInsideStructuredTable(sheet, comparisonRange))
            return null;

        // What Excel actually asks is "is there data next to your selection that you did not
        // select?" -- so compare only on the axis the selection does NOT already span in full.
        //
        // For a whole-column selection the row axis is meaningless: the user selected every row, so
        // any stray value anywhere in that column sits inside the selection and can never be the
        // adjacent data we are warning about. Requiring the region to contain the selection on the
        // row axis as well is what let a single leftover cell far below a table suppress the
        // warning and silently scramble the records -- the defect this feature was written to
        // prevent, reintroduced three rounds running by comparing the wrong axis. The only question
        // for a whole-column selection is whether the block reaches into columns the user left out.
        if (SelectionRangeService.IsWholeColumnSelection(selectedRange))
        {
            return region.Start.Col < comparisonRange.Start.Col || region.End.Col > comparisonRange.End.Col
                ? region
                : null;
        }

        if (SelectionRangeService.IsWholeRowSelection(selectedRange))
        {
            return region.Start.Row < comparisonRange.Start.Row || region.End.Row > comparisonRange.End.Row
                ? region
                : null;
        }

        // An ordinary bounded selection genuinely has to sit inside the block on BOTH axes before
        // we can offer to expand to it -- GetCurrentRegion floods outward from a single anchor, so
        // a selection reaching past the block in any direction is not a subset of it.
        if (region.Start.Row > comparisonRange.Start.Row || region.Start.Col > comparisonRange.Start.Col ||
            region.End.Row < comparisonRange.End.Row || region.End.Col < comparisonRange.End.Col)
        {
            return null;
        }

        return region;
    }

    /// <summary>
    /// Shrinks <paramref name="selectedRange"/>'s end corner to the used-range extent of the
    /// columns (or rows) actually selected, when it reaches past real data -- the case for a
    /// whole-column/whole-row selection, whose End.Row/End.Col sit at
    /// <see cref="CellAddress.MaxRow"/>/<see cref="CellAddress.MaxCol"/>. The start corner is left
    /// untouched -- it is always inside the real sheet for the selections this planner is asked
    /// about. Returns <paramref name="selectedRange"/> unchanged when it isn't a whole-column/row
    /// selection, when the sheet has no data in the selected band at all, or when the selection is
    /// already within the band's real extent.
    /// </summary>
    /// <remarks>
    /// This is scoped to just the selected columns/rows deliberately: a whole-sheet
    /// <see cref="Sheet.GetUsedRange"/> query would be inflated by a stray cell sitting in any OTHER,
    /// unselected column (or row), which would falsely widen the clamp far past the selected data
    /// and make the "region contains the whole selection" check below always fail -- silently
    /// re-enabling the very Sort Warning bypass this clamp exists to close.
    /// </remarks>
    private static GridRange ClampToUsedRange(Sheet sheet, GridRange selectedRange)
    {
        if (SelectionRangeService.IsWholeColumnSelection(selectedRange))
            return ClampEndRow(selectedRange, sheet.GetUsedRangeInColumns(selectedRange.Start.Col, selectedRange.End.Col));

        if (SelectionRangeService.IsWholeRowSelection(selectedRange))
            return ClampEndCol(selectedRange, sheet.GetUsedRangeInRows(selectedRange.Start.Row, selectedRange.End.Row));

        // An ordinary (non-whole-column/row) selection already has real Start/End bounds supplied
        // by the caller -- nothing to clamp.
        return selectedRange;
    }

    private static GridRange ClampEndRow(GridRange selectedRange, GridRange? usedRange)
    {
        if (usedRange is not { } used)
            return selectedRange;

        var endRow = Math.Min(selectedRange.End.Row, Math.Max(used.End.Row, selectedRange.Start.Row));
        if (endRow == selectedRange.End.Row)
            return selectedRange;

        return new GridRange(selectedRange.Start, new CellAddress(selectedRange.Start.Sheet, endRow, selectedRange.End.Col));
    }

    private static GridRange ClampEndCol(GridRange selectedRange, GridRange? usedRange)
    {
        if (usedRange is not { } used)
            return selectedRange;

        var endCol = Math.Min(selectedRange.End.Col, Math.Max(used.End.Col, selectedRange.Start.Col));
        if (endCol == selectedRange.End.Col)
            return selectedRange;

        return new GridRange(selectedRange.Start, new CellAddress(selectedRange.Start.Sheet, selectedRange.End.Row, endCol));
    }

    /// <summary>True when <paramref name="range"/> sits entirely inside one of the sheet's structured (ListObject) tables.</summary>
    private static bool IsFullyInsideStructuredTable(Sheet sheet, GridRange range)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.Range.Contains(range))
                return true;
        }

        return false;
    }

    private static CellAddress ResolveActiveCell(GridRange selectedRange, CellAddress? activeCell)
    {
        if (activeCell is { } cell &&
            cell.Sheet == selectedRange.Start.Sheet &&
            selectedRange.Contains(cell))
        {
            return cell;
        }

        return selectedRange.Start;
    }

    private static GridRange ResolveCandidateRange(Sheet sheet, GridRange selectedRange, CellAddress sortCell)
    {
        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, sortCell) is { } currentRegion &&
            currentRegion.RowCount > 1)
        {
            return currentRegion;
        }

        // A whole-column/row selection reaches CellAddress.MaxRow/MaxCol, and SortCommand sizes its
        // working lists from the range it is handed -- so passing the raw selection through made an
        // ordinary column-header sort allocate and iterate over a million rows to move a handful of
        // cells (measured at roughly 500x the clamped cost). The prompt path already clamps to the
        // selected band's real data extent; the path taken when there is nothing to prompt about
        // has to do the same, or the commonest sort gesture is the slowest one.
        return ClampToUsedRange(sheet, selectedRange);
    }

    private static uint ResolveSortByColumnOffset(GridRange range, CellAddress sortCell)
    {
        if (sortCell.Col < range.Start.Col || sortCell.Col > range.End.Col)
            return 0;

        return sortCell.Col - range.Start.Col;
    }

    public static bool HasLikelyHeaderRow(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return QuickAnalysisSelectionReader.HasHeaderRow(sheet, range);
    }
}
