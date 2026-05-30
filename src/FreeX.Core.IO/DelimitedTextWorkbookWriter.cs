using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class DelimitedTextWorkbookWriter
{
    private const int DelimiterBufferLength = 256;
    private static readonly Dictionary<char, string> DelimiterBuffers = new();
    private static readonly HashSet<string> ErrorTextLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "#DIV/0!",
        "#VALUE!",
        "#REF!",
        "#NAME?",
        "#NULL!",
        "#N/A",
        "#NUM!",
        "#CIRCULAR!",
        "#SPILL!",
        "#CALC!",
        "#CONNECT!",
        "#UNKNOWN!",
        "#FIELD!",
        "#BLOCKED!",
        "#GETTING_DATA"
    };

    public static void Save(Workbook workbook, Stream stream, char delimiter)
    {
        if (workbook.Sheets.Count == 0) return;

        var sheet = workbook.Sheets[0];
        var rowLookup = new Dictionary<uint, DelimitedTextRowBucket>();
        var rows = new List<DelimitedTextRowBucket>();
        var endRow = 0u;
        var endCol = 0u;
        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            if (!IsValidCellAddress(address.Row, address.Col))
                continue;

            if (!rowLookup.TryGetValue(address.Row, out var row))
            {
                row = new DelimitedTextRowBucket(address.Row);
                rowLookup[address.Row] = row;
                rows.Add(row);
            }

            row.Cells.Add((address.Col, cell));
            endRow = Math.Max(endRow, address.Row);
            endCol = Math.Max(endCol, address.Col);
        }

        if (rows.Count == 0) return;

        rows.Sort(static (left, right) => left.Row.CompareTo(right.Row));
        foreach (var row in rows)
            row.Cells.Sort(static (left, right) => left.Col.CompareTo(right.Col));

        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        var nextRow = 1u;
        foreach (var row in rows)
        {
            while (nextRow < row.Row)
            {
                WriteBlankRow(writer, delimiter, endCol);
                nextRow++;
            }

            WriteRow(writer, delimiter, row.Cells, endCol);
            nextRow = row.Row + 1;
        }

        while (nextRow <= endRow)
        {
            WriteBlankRow(writer, delimiter, endCol);
            nextRow++;
        }
    }

    private static bool IsValidCellAddress(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        col is >= 1 and <= CellAddress.MaxCol;

    private sealed class DelimitedTextRowBucket(uint row)
    {
        public uint Row { get; } = row;

        public List<(uint Col, Cell Cell)> Cells { get; } = [];
    }

    private static void WriteRow(TextWriter writer, char delimiter, List<(uint Col, Cell Cell)> cells, uint endCol)
    {
        var previousCol = 0u;
        foreach (var (col, cell) in cells)
        {
            WriteDelimiters(writer, delimiter, previousCol == 0 ? col - 1 : col - previousCol);
            WriteField(writer, delimiter, FormatCell(cell), cell.Value is TextValue);
            previousCol = col;
        }

        WriteDelimiters(writer, delimiter, endCol - previousCol);
        writer.Write("\r\n");
    }

    private static void WriteBlankRow(TextWriter writer, char delimiter, uint endCol)
    {
        if (endCol > 0)
            WriteDelimiters(writer, delimiter, endCol - 1);

        writer.Write("\r\n");
    }

    private static void WriteDelimiters(TextWriter writer, char delimiter, uint count)
    {
        if (count == 1)
        {
            writer.Write(delimiter);
            return;
        }

        var delimiterBuffer = GetDelimiterBuffer(delimiter);
        while (count >= DelimiterBufferLength)
        {
            writer.Write(delimiterBuffer);
            count -= DelimiterBufferLength;
        }

        if (count > 0)
            writer.Write(delimiterBuffer.AsSpan(0, (int)count));
    }

    private static string GetDelimiterBuffer(char delimiter)
    {
        lock (DelimiterBuffers)
        {
            if (!DelimiterBuffers.TryGetValue(delimiter, out var buffer))
            {
                buffer = string.Create(
                    DelimiterBufferLength,
                    delimiter,
                    static (chars, value) => chars.Fill(value));
                DelimiterBuffers[delimiter] = buffer;
            }

            return buffer;
        }
    }

    private static void WriteField(TextWriter writer, char delimiter, string value, bool isTextValue)
    {
        if (value.Length == 0)
        {
            if (isTextValue)
                writer.Write("\"\"");
            return;
        }

        if (!ShouldQuoteField(value, delimiter, isTextValue))
        {
            writer.Write(value);
            return;
        }

        var fieldValue = isTextValue && ShouldWriteTextMarker(value)
            ? $"'{value}"
            : value;
        writer.Write('"');
        foreach (var ch in fieldValue)
        {
            if (ch == '"')
                writer.Write("\"\"");
            else
                writer.Write(ch);
        }

        writer.Write('"');
    }

    private static bool ShouldQuoteField(string value, char delimiter, bool isTextValue)
    {
        if (isTextValue && IsCoercionLikeText(value))
            return true;

        foreach (var ch in value)
        {
            if (ch == delimiter || ch is '"' or '\n' or '\r')
                return true;
        }

        return false;
    }

    private static bool IsCoercionLikeText(string value) =>
        value[0] is '=' or '+' or '-' or '@' ||
        IsSeparatorDirectiveLikeText(value) ||
        IsBooleanLikeText(value) ||
        IsDateTimeLikeText(value) ||
        IsUnsignedCurrencyText(value) ||
        IsSignedCurrencyText(value) ||
        IsPercentageText(value) ||
        IsNumericLikeText(value) ||
        IsParenthesizedCurrencyText(value) ||
        IsErrorLikeText(value);

    private static bool ShouldWriteTextMarker(string value) =>
        IsBooleanLikeText(value) ||
        IsDateTimeLikeText(value) ||
        IsUnsignedCurrencyText(value) ||
        IsNumericLikeText(value);

    private static bool IsSeparatorDirectiveLikeText(string value) =>
        value is { Length: 4 } or { Length: 5 } &&
        value.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) &&
        (value.Length == 4 || value[4] is not '\r' and not '\n');

    private static bool IsBooleanLikeText(string value)
    {
        var trimmed = value.Trim();
        return string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDateTimeLikeText(string value)
    {
        var trimmed = value.Trim();
        if (!HasSupportedDateTimeShape(trimmed))
            return false;

        return DateTime.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.NoCurrentDateDefault,
            out _);
    }

    private static bool HasSupportedDateTimeShape(string value)
    {
        var digitRun = 0;
        foreach (var ch in value)
        {
            if (ch == ':' || char.IsLetter(ch))
                return true;

            if (char.IsDigit(ch))
            {
                digitRun++;
                if (digitRun >= 4)
                    return true;
            }
            else
            {
                digitRun = 0;
            }
        }

        return false;
    }

    private static bool IsUnsignedCurrencyText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('$') &&
               double.TryParse(
                   trimmed,
                   NumberStyles.Currency,
                   CultureInfo.GetCultureInfo("en-US"),
                   out _);
    }

    private static bool IsNumericLikeText(string value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

    private static bool IsSignedCurrencyText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 1 &&
               trimmed[0] is '+' or '-' &&
               double.TryParse(
                   trimmed,
                   NumberStyles.Currency,
                   CultureInfo.GetCultureInfo("en-US"),
                   out _);
    }

    private static bool IsPercentageText(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[^1] != '%')
            return false;

        return double.TryParse(trimmed[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsParenthesizedCurrencyText(string value) =>
        value.TrimStart().StartsWith('(') &&
        value.TrimEnd().EndsWith(')') &&
        double.TryParse(
            value,
            NumberStyles.Currency,
            CultureInfo.GetCultureInfo("en-US"),
            out _);

    private static bool IsErrorLikeText(string value) =>
        ErrorTextLiterals.Contains(value.Trim());

    private static string FormatCell(Cell cell) =>
        cell.FormulaText is { } formulaText
            ? formulaText.StartsWith("=", StringComparison.Ordinal) ? formulaText : $"={formulaText}"
            : FormatValue(cell.Value);

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        DateTimeValue dt => FormatDateTimeValue(dt),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        TextValue t => t.Value,
        ErrorValue e => e.Code,
        _ => "",
    };

    private static string FormatDateTimeValue(DateTimeValue value)
    {
        var dateTime = value.ToDateTime();
        var hasFractionalSeconds = dateTime.Ticks % TimeSpan.TicksPerSecond != 0;
        if (dateTime.Date == new DateTime(1899, 12, 30) && dateTime.TimeOfDay != TimeSpan.Zero)
            return dateTime.ToString(hasFractionalSeconds ? "HH:mm:ss.FFFFFFF" : "HH:mm:ss", CultureInfo.InvariantCulture);

        return dateTime.TimeOfDay == TimeSpan.Zero
            ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dateTime.ToString(hasFractionalSeconds ? "yyyy-MM-dd HH:mm:ss.FFFFFFF" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
