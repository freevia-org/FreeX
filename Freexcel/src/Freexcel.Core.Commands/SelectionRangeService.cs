using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class SelectionRangeService
{
    public static bool IsWholeRowSelection(GridRange range) =>
        range.Start.Col == 1 && range.End.Col == CellAddress.MaxCol;

    public static bool IsWholeColumnSelection(GridRange range) =>
        range.Start.Row == 1 && range.End.Row == CellAddress.MaxRow;

    public static GridRange GetWholeRows(GridRange range) =>
        new(
            new CellAddress(range.Start.Sheet, range.Start.Row, 1),
            new CellAddress(range.Start.Sheet, range.End.Row, CellAddress.MaxCol));

    public static GridRange GetWholeColumns(GridRange range) =>
        new(
            new CellAddress(range.Start.Sheet, 1, range.Start.Col),
            new CellAddress(range.Start.Sheet, CellAddress.MaxRow, range.End.Col));

    public static (uint StartRow, uint EndRow) GetRowSpan(GridRange range) =>
        (range.Start.Row, range.End.Row);

    public static (uint StartCol, uint EndCol) GetColumnSpan(GridRange range) =>
        (range.Start.Col, range.End.Col);

    public static GridRange? GetCurrentRegion(Sheet sheet, CellAddress activeCell)
    {
        if (!HasCellContent(sheet.GetCell(activeCell)))
            return null;

        var usedRange = sheet.GetUsedRange();
        if (usedRange is null)
            return null;

        var top = activeCell.Row;
        var bottom = activeCell.Row;
        var left = activeCell.Col;
        var right = activeCell.Col;

        var changed = true;
        while (changed)
        {
            changed = false;
            if (top > usedRange.Value.Start.Row && RowHasContent(sheet, top - 1, left, right))
            {
                top--;
                changed = true;
            }

            if (bottom < usedRange.Value.End.Row && RowHasContent(sheet, bottom + 1, left, right))
            {
                bottom++;
                changed = true;
            }

            if (left > usedRange.Value.Start.Col && ColumnHasContent(sheet, left - 1, top, bottom))
            {
                left--;
                changed = true;
            }

            if (right < usedRange.Value.End.Col && ColumnHasContent(sheet, right + 1, top, bottom))
            {
                right++;
                changed = true;
            }
        }

        return new GridRange(
            new CellAddress(activeCell.Sheet, top, left),
            new CellAddress(activeCell.Sheet, bottom, right));
    }

    public static IReadOnlyList<GridRange> CompressAddresses(IEnumerable<CellAddress> addresses)
    {
        var sorted = addresses
            .OrderBy(a => a.Sheet.Value)
            .ThenBy(a => a.Row)
            .ThenBy(a => a.Col)
            .ToList();
        if (sorted.Count == 0)
            return [];

        var ranges = new List<GridRange>();
        var runStart = sorted[0];
        var previous = sorted[0];

        foreach (var address in sorted.Skip(1))
        {
            if (address.Sheet == previous.Sheet &&
                address.Row == previous.Row &&
                address.Col == previous.Col + 1)
            {
                previous = address;
                continue;
            }

            ranges.Add(new GridRange(runStart, previous));
            runStart = previous = address;
        }

        ranges.Add(new GridRange(runStart, previous));
        return ranges;
    }

    public static GridRange? GetBoundingRange(IEnumerable<CellAddress> addresses)
    {
        var list = addresses.ToList();
        if (list.Count == 0)
            return null;

        var sheet = list[0].Sheet;
        return new GridRange(
            new CellAddress(sheet, list.Min(a => a.Row), list.Min(a => a.Col)),
            new CellAddress(sheet, list.Max(a => a.Row), list.Max(a => a.Col)));
    }

    private static bool RowHasContent(Sheet sheet, uint row, uint startCol, uint endCol)
    {
        for (var col = startCol; col <= endCol; col++)
        {
            if (HasCellContent(sheet.GetCell(row, col)))
                return true;
        }

        return false;
    }

    private static bool ColumnHasContent(Sheet sheet, uint col, uint startRow, uint endRow)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            if (HasCellContent(sheet.GetCell(row, col)))
                return true;
        }

        return false;
    }

    private static bool HasCellContent(Cell? cell) =>
        cell is not null && (cell.HasFormula || cell.Value is not BlankValue);
}
