using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal readonly record struct QuickSortRangePlan(GridRange Range, uint SortByColOffset);

internal static class QuickSortRangePlanner
{
    public static QuickSortRangePlan Create(Sheet sheet, GridRange selectedRange, CellAddress? activeCell)
    {
        var sortCell = ResolveActiveCell(selectedRange, activeCell);
        var candidateRange = ResolveCandidateRange(sheet, selectedRange, sortCell);
        var sortByColOffset = ResolveSortByColumnOffset(candidateRange, sortCell);
        var sortRange = SortDialog.ExcludeHeaderRow(candidateRange, HasLikelyHeaderRow(sheet, candidateRange));

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
        if (range.RowCount <= 1)
            return false;

        var nonBlankHeaderCells = 0;
        var textHeaderCells = 0;
        var dataColumns = 0;
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var headerCell = sheet.GetCell(range.Start.Row, col);
            if (!HasCellContent(headerCell))
                continue;

            nonBlankHeaderCells++;
            if (headerCell?.Value is TextValue text && !string.IsNullOrWhiteSpace(text.Value))
                textHeaderCells++;

            if (ColumnHasDataBelow(sheet, range, col))
                dataColumns++;
        }

        return nonBlankHeaderCells > 0 &&
               textHeaderCells == nonBlankHeaderCells &&
               dataColumns > 0;
    }

    private static bool ColumnHasDataBelow(Sheet sheet, GridRange range, uint col)
    {
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            if (HasCellContent(sheet.GetCell(row, col)))
                return true;
        }

        return false;
    }

    private static bool HasCellContent(Cell? cell) =>
        cell is not null && (cell.HasFormula || cell.Value is not BlankValue);
}
