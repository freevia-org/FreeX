namespace Freexcel.Core.Model;

/// <summary>
/// Represents a rectangular range of cells.
/// Start is always the top-left corner; End is always the bottom-right corner.
/// </summary>
public readonly record struct GridRange
{
    public CellAddress Start { get; }
    public CellAddress End { get; }

    public GridRange(CellAddress a, CellAddress b)
    {
        // Normalize so Start is always top-left, End is always bottom-right.
        Start = new CellAddress(a.Sheet, Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col));
        End = new CellAddress(a.Sheet, Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col));
    }

    /// <summary>Number of rows in this range.</summary>
    public uint RowCount => End.Row - Start.Row + 1;

    /// <summary>Number of columns in this range.</summary>
    public uint ColCount => End.Col - Start.Col + 1;

    /// <summary>Total number of cells in this range.</summary>
    public long CellCount => (long)RowCount * ColCount;

    /// <summary>
    /// Enumerate all cell addresses in this range, row by row.
    /// </summary>
    public IEnumerable<CellAddress> AllCells()
    {
        for (var r = Start.Row; r <= End.Row; r++)
        {
            for (var c = Start.Col; c <= End.Col; c++)
            {
                yield return new CellAddress(Start.Sheet, r, c);
            }
        }
    }

    /// <summary>Check if a cell address falls within this range.</summary>
    public bool Contains(CellAddress addr) =>
        addr.Sheet == Start.Sheet &&
        addr.Row >= Start.Row && addr.Row <= End.Row &&
        addr.Col >= Start.Col && addr.Col <= End.Col;

    /// <summary>Check if this range overlaps (shares at least one cell with) another range on the same sheet.</summary>
    public bool Overlaps(GridRange other) =>
        Start.Sheet == other.Start.Sheet &&
        Start.Row <= other.End.Row && End.Row >= other.Start.Row &&
        Start.Col <= other.End.Col && End.Col >= other.Start.Col;

    /// <summary>Parse a range string like "A1:C10" into a GridRange.</summary>
    public static GridRange Parse(string rangeText, SheetId sheet)
    {
        var parts = rangeText.Split(':');
        if (parts.Length != 2)
            throw new FormatException($"Invalid range notation: '{rangeText}'");

        var start = CellAddress.Parse(parts[0], sheet);
        var end = CellAddress.Parse(parts[1], sheet);
        return new GridRange(start, end);
    }

    public override string ToString() => $"{Start.ToA1()}:{End.ToA1()}";
}
