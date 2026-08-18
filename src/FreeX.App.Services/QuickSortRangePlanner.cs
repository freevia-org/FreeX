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

        // Only offer to expand when the current region actually contains the whole selection --
        // GetCurrentRegion floods outward from a single anchor point, so a selection that reaches
        // past the natural block boundary in some other direction would not be a subset of it.
        if (region.Start.Row > selectedRange.Start.Row || region.Start.Col > selectedRange.Start.Col ||
            region.End.Row < selectedRange.End.Row || region.End.Col < selectedRange.End.Col)
        {
            return null;
        }

        return region;
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
