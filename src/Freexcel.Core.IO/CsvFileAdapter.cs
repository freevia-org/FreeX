using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// CSV file adapter with RFC 4180 quoting support.
/// </summary>
public sealed class CsvFileAdapter : IFileAdapter
{
    private const int DelimiterBufferLength = 256;
    private static readonly string DelimiterBuffer = new(',', DelimiterBufferLength);

    public string Extension => ".csv";
    public string FormatName => "CSV (Comma-separated values)";

    public Workbook Load(Stream stream) => DelimitedTextWorkbookReader.Load(stream, ',', allowSeparatorDirective: true);

    public void Save(Workbook workbook, Stream stream)
    {
        if (workbook.Sheets.Count == 0) return;
        var sheet = workbook.Sheets[0];
        var rowLookup = new Dictionary<uint, CsvRowBucket>();
        var rows = new List<CsvRowBucket>();
        var endRow = 0u;
        var endCol = 0u;
        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            if (!IsValidCsvCellAddress(address.Row, address.Col))
                continue;

            if (!rowLookup.TryGetValue(address.Row, out var row))
            {
                row = new CsvRowBucket(address.Row);
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
                WriteBlankCsvRow(writer, endCol);
                nextRow++;
            }

            WriteCsvRow(writer, row.Cells, endCol);
            nextRow = row.Row + 1;
        }

        while (nextRow <= endRow)
        {
            WriteBlankCsvRow(writer, endCol);
            nextRow++;
        }
    }

    private static bool IsValidCsvCellAddress(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        col is >= 1 and <= CellAddress.MaxCol;

    private sealed class CsvRowBucket(uint row)
    {
        public uint Row { get; } = row;

        public List<(uint Col, Cell Cell)> Cells { get; } = [];
    }

    private static void WriteCsvRow(TextWriter writer, List<(uint Col, Cell Cell)> cells, uint endCol)
    {
        var previousCol = 0u;
        foreach (var (col, cell) in cells)
        {
            WriteCsvDelimiters(writer, previousCol == 0 ? col - 1 : col - previousCol);
            WriteCsvField(writer, FormatCell(cell), cell.Value is TextValue);
            previousCol = col;
        }

        WriteCsvDelimiters(writer, endCol - previousCol);
        writer.Write("\r\n");
    }

    private static void WriteBlankCsvRow(TextWriter writer, uint endCol)
    {
        if (endCol > 0)
            WriteCsvDelimiters(writer, endCol - 1);

        writer.Write("\r\n");
    }

    private static void WriteCsvDelimiters(TextWriter writer, uint count)
    {
        if (count == 1)
        {
            writer.Write(',');
            return;
        }

        while (count >= DelimiterBufferLength)
        {
            writer.Write(DelimiterBuffer);
            count -= DelimiterBufferLength;
        }

        if (count > 0)
            writer.Write(DelimiterBuffer.AsSpan(0, (int)count));
    }

    private static void WriteCsvField(TextWriter writer, string value, bool isTextValue)
    {
        if (value.Length == 0) return;
        if (!ShouldQuoteCsvField(value, isTextValue))
        {
            writer.Write(value);
            return;
        }

        writer.Write('"');
        foreach (var ch in value)
        {
            if (ch == '"')
                writer.Write("\"\"");
            else
                writer.Write(ch);
        }
        writer.Write('"');
    }

    private static bool ShouldQuoteCsvField(string value, bool isTextValue)
    {
        if (isTextValue && IsCoercionLikeText(value))
            return true;

        foreach (var ch in value)
        {
            if (ch is ',' or '"' or '\n' or '\r')
                return true;
        }

        return false;
    }

    private static bool IsCoercionLikeText(string value) =>
        value[0] is '=' or '+' or '-' or '@' ||
        IsErrorLikeText(value);

    private static bool IsErrorLikeText(string value) =>
        value.Equals("#DIV/0!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#VALUE!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#REF!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#NAME?", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#NULL!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#N/A", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#NUM!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#CIRCULAR!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#SPILL!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#CALC!", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("#GETTING_DATA", StringComparison.OrdinalIgnoreCase);

    private static string FormatCell(Cell cell) =>
        cell.FormulaText is { } formulaText
            ? $"={formulaText}"
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
