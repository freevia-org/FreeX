using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Sorts the rows of a rectangular range by a specified column, ascending or descending.
/// Stores a snapshot of the original arrangement for undo via Revert.
/// </summary>
public sealed class SortCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _sortByColOffset;   // 0 = first column of the range
    private readonly bool _ascending;

    // Snapshot for undo: list of rows, each row is a list of (address, cell?) pairs
    private List<List<(CellAddress Address, Cell? Cell)>>? _snapshot;

    public string Label => $"Sort {(_ascending ? "Ascending" : "Descending")}";

    public SortCommand(SheetId sheetId, GridRange range, uint sortByColOffset, bool ascending)
    {
        _sheetId = sheetId;
        _range = range;
        _sortByColOffset = sortByColOffset;
        _ascending = ascending;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        uint startRow = _range.Start.Row;
        uint endRow   = _range.End.Row;
        uint startCol = _range.Start.Col;
        uint endCol   = _range.End.Col;
        uint sortCol  = startCol + _sortByColOffset;

        // Clamp sort column to range
        if (sortCol > endCol) sortCol = startCol;

        int rowCount = (int)(endRow - startRow + 1);
        int colCount = (int)(endCol - startCol + 1);

        // Read current state and save snapshot
        _snapshot = new List<List<(CellAddress, Cell?)>>(rowCount);
        var rows = new List<Cell?[]>(rowCount);

        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            var rowCells = new Cell?[colCount];
            var snapRow  = new List<(CellAddress, Cell?)>(colCount);

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col  = startCol + (uint)ci;
                var addr  = new CellAddress(_sheetId, row, col);
                var cell  = sheet.GetCell(addr);
                rowCells[ci] = cell?.Clone();
                snapRow.Add((addr, cell?.Clone()));
            }

            rows.Add(rowCells);
            _snapshot.Add(snapRow);
        }

        // Sort rows by the sort-key column
        int keyColIdx = (int)(sortCol - startCol);
        rows.Sort((a, b) =>
        {
            var va = a[keyColIdx]?.Value ?? BlankValue.Instance;
            var vb = b[keyColIdx]?.Value ?? BlankValue.Instance;
            int cmp = CompareScalar(va, vb);
            return _ascending ? cmp : -cmp;
        });

        // Write sorted rows back
        var affected = new List<CellAddress>(rowCount * colCount);
        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            for (int ci = 0; ci < colCount; ci++)
            {
                uint col  = startCol + (uint)ci;
                var addr  = new CellAddress(_sheetId, row, col);
                var cell  = rows[ri][ci];
                if (cell is null)
                    sheet.ClearCell(addr);
                else
                    sheet.SetCell(addr, cell.Clone());
                affected.Add(addr);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        foreach (var snapRow in _snapshot)
        {
            foreach (var (addr, cell) in snapRow)
            {
                if (cell is null)
                    sheet.ClearCell(addr);
                else
                    sheet.SetCell(addr, cell.Clone());
            }
        }
    }

    /// <summary>
    /// Three-tier comparison: numbers first, then text (case-insensitive), then blank/other.
    /// </summary>
    private static int CompareScalar(ScalarValue a, ScalarValue b)
    {
        return (a, b) switch
        {
            (NumberValue na, NumberValue nb) => na.Value.CompareTo(nb.Value),
            (NumberValue,    _             ) => -1,  // numbers before text/blank
            (_,              NumberValue   ) =>  1,
            (TextValue ta,   TextValue tb  ) => string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase),
            (TextValue,      _             ) => -1,  // text before blank/error
            (_,              TextValue     ) =>  1,
            (BlankValue,     BlankValue    ) =>  0,
            (BlankValue,     _             ) =>  1,  // blanks last
            (_,              BlankValue    ) => -1,
            _                               =>  0,
        };
    }
}
