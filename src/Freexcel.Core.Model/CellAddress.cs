namespace Freexcel.Core.Model;

/// <summary>
/// Represents a cell address within a specific sheet.
/// Row and Col are 1-based to match Excel's convention.
/// </summary>
public readonly record struct CellAddress(SheetId Sheet, uint Row, uint Col) : IComparable<CellAddress>
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
