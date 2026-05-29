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

        var contentIndex = ContentIndex.CreateIfWorthwhile(sheet, usedRange.Value);
        var bounds = ExpandCurrentRegionBounds(sheet, contentIndex, usedRange.Value, activeCell);

        return new GridRange(
            new CellAddress(activeCell.Sheet, bounds.Top, bounds.Left),
            new CellAddress(activeCell.Sheet, bounds.Bottom, bounds.Right));
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

    private static bool RowHasContent(Sheet sheet, ContentIndex? contentIndex, uint row, uint startCol, uint endCol)
    {
        if (contentIndex is not null)
            return contentIndex.RowHasContent(row, startCol, endCol);

        for (var col = startCol; col <= endCol; col++)
        {
            if (HasCellContent(sheet.GetCell(row, col)))
                return true;
        }

        return false;
    }

    private static bool ColumnHasContent(Sheet sheet, ContentIndex? contentIndex, uint col, uint startRow, uint endRow)
    {
        if (contentIndex is not null)
            return contentIndex.ColumnHasContent(col, startRow, endRow);

        for (var row = startRow; row <= endRow; row++)
        {
            if (HasCellContent(sheet.GetCell(row, col)))
                return true;
        }

        return false;
    }

    private static CurrentRegionBounds ExpandCurrentRegionBounds(
        Sheet sheet,
        ContentIndex? contentIndex,
        GridRange usedRange,
        CellAddress activeCell)
    {
        var bounds = new CurrentRegionBounds(activeCell.Row, activeCell.Row, activeCell.Col, activeCell.Col);

        var changed = true;
        while (changed)
        {
            changed = false;
            if (bounds.Top > usedRange.Start.Row && RowHasContent(sheet, contentIndex, bounds.Top - 1, bounds.Left, bounds.Right))
            {
                bounds = bounds with { Top = bounds.Top - 1 };
                changed = true;
            }

            if (bounds.Bottom < usedRange.End.Row && RowHasContent(sheet, contentIndex, bounds.Bottom + 1, bounds.Left, bounds.Right))
            {
                bounds = bounds with { Bottom = bounds.Bottom + 1 };
                changed = true;
            }

            if (bounds.Left > usedRange.Start.Col && ColumnHasContent(sheet, contentIndex, bounds.Left - 1, bounds.Top, bounds.Bottom))
            {
                bounds = bounds with { Left = bounds.Left - 1 };
                changed = true;
            }

            if (bounds.Right < usedRange.End.Col && ColumnHasContent(sheet, contentIndex, bounds.Right + 1, bounds.Top, bounds.Bottom))
            {
                bounds = bounds with { Right = bounds.Right + 1 };
                changed = true;
            }
        }

        return bounds;
    }

    private static bool HasCellContent(Cell? cell) =>
        cell is not null && (cell.HasFormula || cell.Value is not BlankValue);

    private readonly record struct CurrentRegionBounds(uint Top, uint Bottom, uint Left, uint Right);

    private sealed class ContentIndex
    {
        private const long MinimumUsedCells = 4_096;
        private const int SparseAreaPerStoredCell = 4;
        private readonly Dictionary<uint, List<uint>> _colsByRow;
        private readonly Dictionary<uint, List<uint>> _rowsByCol;

        private ContentIndex(Dictionary<uint, List<uint>> colsByRow, Dictionary<uint, List<uint>> rowsByCol)
        {
            _colsByRow = colsByRow;
            _rowsByCol = rowsByCol;
        }

        public static ContentIndex? CreateIfWorthwhile(Sheet sheet, GridRange usedRange)
        {
            if (usedRange.CellCount < MinimumUsedCells ||
                (long)sheet.CellCount * SparseAreaPerStoredCell > usedRange.CellCount)
            {
                return null;
            }

            var colsByRow = new Dictionary<uint, List<uint>>();
            var rowsByCol = new Dictionary<uint, List<uint>>();
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (!HasCellContent(cell))
                    continue;

                Add(colsByRow, address.Row, address.Col);
                Add(rowsByCol, address.Col, address.Row);
            }

            if (colsByRow.Count == 0)
                return null;

            SortValues(colsByRow);
            SortValues(rowsByCol);
            return new ContentIndex(colsByRow, rowsByCol);
        }

        public bool RowHasContent(uint row, uint startCol, uint endCol) =>
            _colsByRow.TryGetValue(row, out var cols) && HasAnyInRange(cols, startCol, endCol);

        public bool ColumnHasContent(uint col, uint startRow, uint endRow) =>
            _rowsByCol.TryGetValue(col, out var rows) && HasAnyInRange(rows, startRow, endRow);

        private static void Add(Dictionary<uint, List<uint>> valuesByKey, uint key, uint value)
        {
            if (!valuesByKey.TryGetValue(key, out var values))
            {
                values = [];
                valuesByKey[key] = values;
            }

            values.Add(value);
        }

        private static void SortValues(Dictionary<uint, List<uint>> valuesByKey)
        {
            foreach (var values in valuesByKey.Values)
                values.Sort();
        }

        private static bool HasAnyInRange(List<uint> sortedValues, uint start, uint end)
        {
            var index = sortedValues.BinarySearch(start);
            if (index < 0)
                index = ~index;

            return index < sortedValues.Count && sortedValues[index] <= end;
        }
    }
}
