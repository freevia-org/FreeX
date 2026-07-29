using System.Buffers;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        // Excel's Current Region is a purely geometric notion -- "the range bounded by any
        // combination of blank rows and blank columns" -- and does NOT require the active cell
        // itself to contain data. A blank cell nested inside a solid data block (a "hole") still
        // expands to the surrounding block, exactly like a filled cell would. Only a sheet with
        // no content anywhere (no used range at all) has no region to expand into.
        var usedRange = sheet.GetUsedRange();
        if (usedRange is null)
            return null;

        var contentIndex = ContentIndex.CreateIfWorthwhile(sheet, usedRange.Value);
        CurrentRegionBounds bounds;
        try
        {
            bounds = ExpandCurrentRegionBounds(sheet, contentIndex, usedRange.Value, activeCell);
        }
        finally
        {
            contentIndex?.Dispose();
        }

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

    private sealed class ContentIndex : IDisposable
    {
        private const long MinimumUsedCells = 4_096;
        private const int SparseAreaPerStoredCell = 4;
        private const int ColumnKeyBits = 15;
        private const int RowKeyBits = 21;
        private readonly ulong[] _rowKeys;
        private readonly ulong[] _columnKeys;
        private readonly int _count;

        private ContentIndex(ulong[] rowKeys, ulong[] columnKeys, int count)
        {
            _rowKeys = rowKeys;
            _columnKeys = columnKeys;
            _count = count;
        }

        public static ContentIndex? CreateIfWorthwhile(Sheet sheet, GridRange usedRange)
        {
            if (usedRange.CellCount < MinimumUsedCells ||
                (long)sheet.CellCount * SparseAreaPerStoredCell > usedRange.CellCount)
            {
                return null;
            }

            var rowKeys = ArrayPool<ulong>.Shared.Rent(sheet.CellCount);
            ulong[]? columnKeys = null;
            try
            {
                columnKeys = ArrayPool<ulong>.Shared.Rent(sheet.CellCount);
                var count = 0;
                foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
                {
                    if (!HasCellContent(cell))
                        continue;

                    rowKeys[count] = CreateRowKey(row, col);
                    columnKeys[count] = CreateColumnKey(row, col);
                    count++;
                }

                if (count == 0)
                {
                    ArrayPool<ulong>.Shared.Return(rowKeys);
                    ArrayPool<ulong>.Shared.Return(columnKeys);
                    return null;
                }

                Array.Sort(rowKeys, 0, count);
                Array.Sort(columnKeys, 0, count);
                return new ContentIndex(rowKeys, columnKeys, count);
            }
            catch
            {
                ArrayPool<ulong>.Shared.Return(rowKeys);
                if (columnKeys is not null)
                    ArrayPool<ulong>.Shared.Return(columnKeys);

                throw;
            }
        }

        public bool RowHasContent(uint row, uint startCol, uint endCol) =>
            HasAnyInRange(_rowKeys, _count, CreateRowKey(row, startCol), CreateRowKey(row, endCol));

        public bool ColumnHasContent(uint col, uint startRow, uint endRow) =>
            HasAnyInRange(_columnKeys, _count, CreateColumnKey(startRow, col), CreateColumnKey(endRow, col));

        private static ulong CreateRowKey(uint row, uint col) =>
            ((ulong)row << ColumnKeyBits) | col;

        private static ulong CreateColumnKey(uint row, uint col) =>
            ((ulong)col << RowKeyBits) | row;

        private static bool HasAnyInRange(ulong[] keys, int count, ulong startKey, ulong endKey)
        {
            var index = Array.BinarySearch(keys, 0, count, startKey);
            if (index < 0)
                index = ~index;

            return index < count && keys[index] <= endKey;
        }

        public void Dispose()
        {
            ArrayPool<ulong>.Shared.Return(_rowKeys);
            ArrayPool<ulong>.Shared.Return(_columnKeys);
        }
    }
}
