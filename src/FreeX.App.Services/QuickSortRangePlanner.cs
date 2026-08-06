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
