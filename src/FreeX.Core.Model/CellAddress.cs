using System.Threading;

namespace FreeX.Core.Model;

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

    private static readonly string?[] ColumnNameCache = new string?[(int)MaxCol + 1];

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
            !TryReadRowNumber(value, ref index, out var row))
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

    private static bool TryReadRowNumber(ReadOnlySpan<char> value, ref int index, out uint row)
    {
        row = 0;
        var start = index;

        while (index < value.Length)
        {
            var c = value[index];
            if (c is < '0' or > '9')
                return false;

            var digit = (uint)(c - '0');
            if (row > MaxRow / 10 || row == MaxRow / 10 && digit > MaxRow % 10)
                return false;

            row = row * 10 + digit;
            index++;
        }

        return index > start && row > 0;
    }

    private static char NormalizeColumnLetter(char c) =>
        c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;

    /// <summary>
    /// Converts a 1-based column number to a column name (e.g. 1 -> "A", 27 -> "AA").
    /// </summary>
    public static string NumberToColumnName(uint col)
    {
        return TryGetCachedColumnName(col) ?? CreateColumnName(col);
    }

    /// <summary>Format as A1 notation (e.g. "B7").</summary>
    public string ToA1()
    {
        var columnName = TryGetCachedColumnName(Col);
        if (columnName is not null)
        {
            var cachedRowLength = GetRowDigitCount(Row);
            return string.Create(columnName.Length + (int)cachedRowLength, (columnName, Row), static (buffer, state) =>
            {
                var (columnName, row) = state;
                columnName.AsSpan().CopyTo(buffer);

                var rowIndex = buffer.Length;
                do
                {
                    buffer[--rowIndex] = (char)('0' + row % 10);
                    row /= 10;
                }
                while (row > 0);
            });
        }

        var columnLength = GetColumnNameLength(Col);
        var rowLength = GetRowDigitCount(Row);
        return string.Create((int)(columnLength + rowLength), (Col, Row, columnLength), static (buffer, state) =>
        {
            var (col, row, colLength) = state;
            WriteColumnName(col, buffer[..(int)colLength]);

            var rowIndex = buffer.Length;
            do
            {
                buffer[--rowIndex] = (char)('0' + row % 10);
                row /= 10;
            }
            while (row > 0);
        });
    }

    private static string? TryGetCachedColumnName(uint col)
    {
        if (col is 0 || col > MaxCol)
            return null;

        var index = (int)col;
        var cached = Volatile.Read(ref ColumnNameCache[index]);
        if (cached is not null)
            return cached;

        var created = CreateColumnName(col);
        var previous = Interlocked.CompareExchange(ref ColumnNameCache[index], created, null);
        return previous ?? created;
    }

    private static string CreateColumnName(uint col)
    {
        var columnLength = GetColumnNameLength(col);
        Span<char> buffer = stackalloc char[(int)columnLength];
        WriteColumnName(col, buffer);
        return new string(buffer);
    }

    private static void WriteColumnName(uint col, Span<char> destination)
    {
        for (var index = destination.Length - 1; index >= 0; index--)
        {
            col--;
            destination[index] = (char)('A' + col % 26);
            col /= 26;
        }
    }

    private static uint GetColumnNameLength(uint col)
    {
        if (col == 0)
            return 0;

        return col <= 26 ? 1u :
            col <= 702 ? 2u :
            col <= 18_278 ? 3u :
            col <= 475_254 ? 4u :
            col <= 12_356_630 ? 5u :
            col <= 321_272_406 ? 6u : 7u;
    }

    private static uint GetRowDigitCount(uint row)
    {
        return row < 10 ? 1u :
            row < 100 ? 2u :
            row < 1_000 ? 3u :
            row < 10_000 ? 4u :
            row < 100_000 ? 5u :
            row < 1_000_000 ? 6u :
            row < 10_000_000 ? 7u :
            row < 100_000_000 ? 8u :
            row < 1_000_000_000 ? 9u : 10u;
    }

    public override string ToString() => ToA1();

    public int CompareTo(CellAddress other)
    {
        var rowCmp = Row.CompareTo(other.Row);
        return rowCmp != 0 ? rowCmp : Col.CompareTo(other.Col);
    }
}
