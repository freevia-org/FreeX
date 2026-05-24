namespace Freexcel.Core.Model;

/// <summary>
/// Represents a cell address within a specific sheet.
/// Row and Col are 1-based to match Excel's convention.
/// </summary>
public readonly partial record struct CellAddress(SheetId Sheet, uint Row, uint Col) : IComparable<CellAddress>
{
    /// <summary>Maximum supported columns (16,384 = XFD in Excel).</summary>
    public const uint MaxCol = 16_384;

    /// <summary>Maximum supported rows (1,048,576 in Excel).</summary>
    public const uint MaxRow = 1_048_576;

    /// <summary>
    /// Parse an A1-notation string like "B7" into a CellAddress.
    /// The sheet must be provided separately.
    /// </summary>
    public static CellAddress Parse(string a1, SheetId sheet)
    {
        if (!TryParse(a1, sheet, out var result))
            throw new FormatException($"Invalid A1 notation: '{a1}'");

        return result;
    }

    /// <summary>
    /// Try to parse an A1-notation string. Returns false if the format is invalid.
    /// </summary>
    public static bool TryParse(string a1, SheetId sheet, out CellAddress result)
    {
        var value = a1.AsSpan().Trim();
        if (value.IsEmpty)
        {
            result = default;
            return false;
        }

        uint col = 0;
        var index = 0;
        while (index < value.Length)
        {
            var c = value[index];
            if (c is >= 'a' and <= 'z')
                c = (char)(c - ('a' - 'A'));
            if (c is < 'A' or > 'Z')
                break;

            col = col * 26 + (uint)(c - 'A' + 1);
            if (col > MaxCol)
            {
                result = default;
                return false;
            }

            index++;
        }

        if (index == 0 || index == value.Length)
        {
            result = default;
            return false;
        }

        uint row = 0;
        for (; index < value.Length; index++)
        {
            var c = value[index];
            if (c is < '0' or > '9')
            {
                result = default;
                return false;
            }

            row = row * 10 + (uint)(c - '0');
            if (row > MaxRow)
            {
                result = default;
                return false;
            }
        }

        if (row == 0)
        {
            result = default;
            return false;
        }

        result = new CellAddress(sheet, row, col);
        return true;
    }
    /// <summary>
    /// Converts a column name (e.g. "A", "Z", "AA", "XFD") to a 1-based column number.
    /// </summary>
    public static uint ColumnNameToNumber(string name)
    {
        uint result = 0;
        foreach (var c in name.ToUpperInvariant())
        {
            if (c < 'A' || c > 'Z') return 0; // non-letter would underflow uint arithmetic
            if (result > MaxCol) return result; // already beyond valid range — avoids overflow
            result = result * 26 + (uint)(c - 'A' + 1);
        }
        return result;
    }

    /// <summary>
    /// Converts a 1-based column number to a column name (e.g. 1 → "A", 27 → "AA").
    /// </summary>
    public static string NumberToColumnName(uint col)
    {
        var result = "";
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }

    /// <summary>Format as A1 notation (e.g. "B7").</summary>
    public string ToA1() => $"{NumberToColumnName(Col)}{Row}";

    public override string ToString() => ToA1();

    public int CompareTo(CellAddress other)
    {
        var rowCmp = Row.CompareTo(other.Row);
        return rowCmp != 0 ? rowCmp : Col.CompareTo(other.Col);
    }

}

/// <summary>
/// Represents a rectangular range of cells.
/// Start is always the top-left corner; End is always the bottom-right corner.
/// </summary>
public readonly record struct GridRange
{
    public CellAddress Start { get; }
    public CellAddress End   { get; }

    public GridRange(CellAddress a, CellAddress b)
    {
        // Normalize so Start is always top-left, End is always bottom-right
        Start = new CellAddress(a.Sheet, Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col));
        End   = new CellAddress(a.Sheet, Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col));
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
