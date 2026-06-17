namespace FreeX.Core.Model;

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
        if (a.Sheet != b.Sheet)
            throw new ArgumentException("GridRange endpoints must be on the same sheet.", nameof(b));

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

    /// <summary>Check if this range entirely contains <paramref name="inner"/> (same sheet, all corners within).</summary>
    public bool Contains(GridRange inner) =>
        Start.Sheet == inner.Start.Sheet &&
        Start.Row <= inner.Start.Row &&
        End.Row >= inner.End.Row &&
        Start.Col <= inner.Start.Col &&
        End.Col >= inner.End.Col;

    /// <summary>Check if this range overlaps (shares at least one cell with) another range on the same sheet.</summary>
    public bool Overlaps(GridRange other) =>
        Start.Sheet == other.Start.Sheet &&
        Start.Row <= other.End.Row && End.Row >= other.Start.Row &&
        Start.Col <= other.End.Col && End.Col >= other.Start.Col;

    /// <summary>
    /// Computes the intersection of two ranges.
    /// Returns true and sets <paramref name="intersection"/> when the ranges share at least one cell
    /// on the same sheet; returns false (and sets <paramref name="intersection"/> to default) when
    /// they are disjoint or on different sheets.
    /// </summary>
    public static bool TryIntersect(GridRange a, GridRange b, out GridRange intersection)
    {
        if (!a.Overlaps(b))
        {
            intersection = default;
            return false;
        }

        intersection = new GridRange(
            new CellAddress(
                a.Start.Sheet,
                Math.Max(a.Start.Row, b.Start.Row),
                Math.Max(a.Start.Col, b.Start.Col)),
            new CellAddress(
                a.Start.Sheet,
                Math.Min(a.End.Row, b.End.Row),
                Math.Min(a.End.Col, b.End.Col)));
        return true;
    }

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

    /// <summary>
    /// Parse a reference that may be either a multi-cell range ("A1:C10") or a single cell ("D6").
    /// A single-cell reference yields a degenerate 1x1 range. Use this for OOXML references whose
    /// <c>ref</c> attribute legitimately collapses to one cell (e.g. a pivot table location).
    /// </summary>
    public static GridRange ParseCellOrRange(string rangeText, SheetId sheet)
    {
        var separator = rangeText.IndexOf(':');
        if (separator < 0)
        {
            var cell = CellAddress.Parse(rangeText, sheet);
            return new GridRange(cell, cell);
        }

        return Parse(rangeText, sheet);
    }

    public override string ToString() => $"{Start.ToA1()}:{End.ToA1()}";
}
