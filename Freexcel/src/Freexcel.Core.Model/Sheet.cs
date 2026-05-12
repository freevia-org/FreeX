namespace Freexcel.Core.Model;

/// <summary>
/// Represents a worksheet within a workbook.
/// Storage is Dictionary-based (sparse) per the build plan — NOT sparse columnar.
/// </summary>
public sealed class Sheet
{
    private readonly Dictionary<(uint Row, uint Col), Cell> _cells = [];

    /// <summary>Unique identifier for this sheet.</summary>
    public SheetId Id { get; }

    /// <summary>Display name of the sheet (shown on tab).</summary>
    public string Name { get; set; }

    /// <summary>Column widths override (1-based column index → width in characters).</summary>
    public Dictionary<uint, double> ColumnWidths { get; } = [];

    /// <summary>Row heights override (1-based row index → height in pixels).</summary>
    public Dictionary<uint, double> RowHeights { get; } = [];

    /// <summary>Default column width in characters.</summary>
    public double DefaultColumnWidth { get; set; } = 8.43;

    /// <summary>Default row height in pixels.</summary>
    public double DefaultRowHeight { get; set; } = 20.0;

    /// <summary>Number of rows frozen at the top (0 = none).</summary>
    public uint FrozenRows { get; set; } = 0;

    /// <summary>Number of columns frozen at the left (0 = none).</summary>
    public uint FrozenCols { get; set; } = 0;

    public Sheet(SheetId id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Get the cell at the given address, or null if no cell exists there.</summary>
    public Cell? GetCell(uint row, uint col)
    {
        return _cells.GetValueOrDefault((row, col));
    }

    /// <summary>Get the cell at the given address, or null if no cell exists there.</summary>
    public Cell? GetCell(CellAddress address)
    {
        return _cells.GetValueOrDefault((address.Row, address.Col));
    }

    /// <summary>
    /// Set a cell value at the given address. Creates the cell if it doesn't exist.
    /// </summary>
    public void SetCell(CellAddress address, ScalarValue value)
    {
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
        {
            existing.Value = value;
            existing.FormulaText = null;
        }
        else
        {
            _cells[(address.Row, address.Col)] = Cell.FromValue(value);
        }
    }

    /// <summary>
    /// Set a cell with a formula at the given address.
    /// The value should be computed separately by the calc engine.
    /// </summary>
    public void SetFormula(CellAddress address, string formulaText)
    {
        if (_cells.TryGetValue((address.Row, address.Col), out var existing))
        {
            existing.FormulaText = formulaText;
        }
        else
        {
            _cells[(address.Row, address.Col)] = Cell.FromFormula(formulaText);
        }
    }

    /// <summary>Set a cell directly.</summary>
    public void SetCell(CellAddress address, Cell cell)
    {
        _cells[(address.Row, address.Col)] = cell;
    }

    /// <summary>Remove a cell (clear its contents).</summary>
    public void ClearCell(uint row, uint col)
    {
        _cells.Remove((row, col));
    }

    /// <summary>Remove a cell at the given address.</summary>
    public void ClearCell(CellAddress address)
    {
        _cells.Remove((address.Row, address.Col));
    }

    /// <summary>Get the value at a cell address, returning BlankValue if no cell exists.</summary>
    public ScalarValue GetValue(uint row, uint col)
    {
        return _cells.TryGetValue((row, col), out var cell) ? cell.Value : new BlankValue();
    }

    /// <summary>Get the value at a cell address, returning BlankValue if no cell exists.</summary>
    public ScalarValue GetValue(CellAddress address)
    {
        return GetValue(address.Row, address.Col);
    }

    /// <summary>Get all non-empty cell positions.</summary>
    public IReadOnlyCollection<(uint Row, uint Col)> GetOccupiedCells()
    {
        return _cells.Keys;
    }

    /// <summary>Get all cells as address-cell pairs.</summary>
    public IEnumerable<(CellAddress Address, Cell Cell)> EnumerateCells()
    {
        foreach (var ((row, col), cell) in _cells)
        {
            yield return (new CellAddress(Id, row, col), cell);
        }
    }

    /// <summary>Total number of non-empty cells.</summary>
    public int CellCount => _cells.Count;

    /// <summary>Get all non-empty cells as a dictionary keyed by CellAddress.</summary>
    public Dictionary<CellAddress, Cell> GetUsedCells()
    {
        var result = new Dictionary<CellAddress, Cell>(_cells.Count);
        foreach (var ((row, col), cell) in _cells)
            result[new CellAddress(Id, row, col)] = cell;
        return result;
    }

    /// <summary>
    /// Get the bounding range of all non-empty cells, or null if the sheet is empty.
    /// </summary>
    public GridRange? GetUsedRange()
    {
        if (_cells.Count == 0)
            return null;

        uint minRow = uint.MaxValue, maxRow = 0, minCol = uint.MaxValue, maxCol = 0;
        foreach (var (row, col) in _cells.Keys)
        {
            if (row < minRow) minRow = row;
            if (row > maxRow) maxRow = row;
            if (col < minCol) minCol = col;
            if (col > maxCol) maxCol = col;
        }

        return new GridRange(
            new CellAddress(Id, minRow, minCol),
            new CellAddress(Id, maxRow, maxCol));
    }
}
