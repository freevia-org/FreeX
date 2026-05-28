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

        var index = 0;
        if (!TryReadColumnNumber(value, ref index, out var col) ||
            !TryReadRowNumber(value[index..], out var row))
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
        foreach (var raw in name)
        {
            var c = NormalizeColumnLetter(raw);
            if (c < 'A' || c > 'Z') return 0; // non-letter would underflow uint arithmetic
            if (result > MaxCol) return result; // already beyond valid range - avoids overflow
            result = result * 26 + (uint)(c - 'A' + 1);
        }
        return result;
    }

    private static bool TryReadColumnNumber(ReadOnlySpan<char> value, ref int index, out uint column)
    {
        column = 0;
        var start = index;

        while (index < value.Length)
        {
            var c = NormalizeColumnLetter(value[index]);
            if (c is < 'A' or > 'Z')
                break;

            column = column * 26 + (uint)(c - 'A' + 1);
            if (column > MaxCol)
                return false;

            index++;
        }

        return index > start;
    }

    private static bool TryReadRowNumber(ReadOnlySpan<char> value, out uint row)
    {
        row = 0;
        if (value.IsEmpty)
            return false;

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;

            var digit = (uint)(c - '0');
            if (row > MaxRow / 10 || row == MaxRow / 10 && digit > MaxRow % 10)
                return false;

            row = row * 10 + digit;
            if (row > MaxRow)
                return false;
        }

        return row > 0;
    }

    private static char NormalizeColumnLetter(char c) =>
        c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;

    /// <summary>
    /// Converts a 1-based column number to a column name (e.g. 1 -> "A", 27 -> "AA").
    /// </summary>
    public static string NumberToColumnName(uint col)
    {
        Span<char> buffer = stackalloc char[7];
        var index = buffer.Length;
        while (col > 0)
        {
            col--;
            buffer[--index] = (char)('A' + col % 26);
            col /= 26;
        }
        return new string(buffer[index..]);
    }

    /// <summary>Format as A1 notation (e.g. "B7").</summary>
    public string ToA1()
    {
        var columnLength = GetColumnNameLength(Col);
        var rowLength = GetRowDigitCount(Row);
        return string.Create((int)(columnLength + rowLength), (Col, Row, columnLength), static (buffer, state) =>
        {
            var (col, row, colLength) = state;
            for (var index = (int)colLength - 1; index >= 0; index--)
            {
                col--;
                buffer[index] = (char)('A' + col % 26);
                col /= 26;
            }

            var rowIndex = buffer.Length;
            do
            {
                buffer[--rowIndex] = (char)('0' + row % 10);
                row /= 10;
            }
            while (row > 0);
        });
    }

    private static uint GetColumnNameLength(uint col)
    {
        uint length = 0;
        while (col > 0)
        {
            length++;
            col = (col - 1) / 26;
        }

        return length;
    }

    private static uint GetRowDigitCount(uint row)
    {
        uint length = 1;
        while (row >= 10)
        {
            length++;
            row /= 10;
        }

        return length;
    }

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
