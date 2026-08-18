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

        if (region.ColCount <= selectedRange.ColCount && region.RowCount <= selectedRange.RowCount)
            return null;

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

        // Only offer to expand when the current region actually contains the whole selection --
        // GetCurrentRegion floods outward from a single anchor point, so a selection that reaches
        // past the natural block boundary in some other direction would not be a subset of it.
        if (region.Start.Row > comparisonRange.Start.Row || region.Start.Col > comparisonRange.Start.Col ||
            region.End.Row < comparisonRange.End.Row || region.End.Col < comparisonRange.End.Col)
        {
            return null;
        }

        return region;
    }

    /// <summary>
    /// Shrinks <paramref name="selectedRange"/>'s end corner to the sheet's actual used-range
    /// extent when it reaches past real data (the case for a whole-column/whole-row selection,
    /// whose End.Row/End.Col sit at <see cref="CellAddress.MaxRow"/>/<see cref="CellAddress.MaxCol"/>).
    /// The start corner is left untouched -- it is always inside the real sheet for the selections
    /// this planner is asked about. Returns <paramref name="selectedRange"/> unchanged when the
    /// sheet has no used range at all, or when the selection is already within it.
    /// </summary>
    private static GridRange ClampToUsedRange(Sheet sheet, GridRange selectedRange)
    {
        if (sheet.GetUsedRange() is not { } usedRange)
            return selectedRange;

        var endRow = Math.Min(selectedRange.End.Row, Math.Max(usedRange.End.Row, selectedRange.Start.Row));
        var endCol = Math.Min(selectedRange.End.Col, Math.Max(usedRange.End.Col, selectedRange.Start.Col));
        if (endRow == selectedRange.End.Row && endCol == selectedRange.End.Col)
            return selectedRange;

        return new GridRange(selectedRange.Start, new CellAddress(selectedRange.Start.Sheet, endRow, endCol));
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

        return selectedRange;
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
