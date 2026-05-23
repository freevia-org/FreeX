using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// CSV file adapter with RFC 4180 quoting support.
/// </summary>
public sealed class CsvFileAdapter : IFileAdapter
{
    public string Extension => ".csv";
    public string FormatName => "CSV (Comma-separated values)";

    public Workbook Load(Stream stream) => DelimitedTextWorkbookReader.Load(stream, ',', allowSeparatorDirective: true);

    public void Save(Workbook workbook, Stream stream)
    {
        if (workbook.Sheets.Count == 0) return;
        var sheet = workbook.Sheets[0];
        var usedCells = sheet.GetUsedCells()
            .Where(pair => IsValidCsvCellAddress(pair.Key.Row, pair.Key.Col))
            .ToDictionary(pair => (pair.Key.Row, pair.Key.Col), pair => pair.Value);
        if (usedCells.Count == 0) return;

        var startRow = 1u;
        var endRow = usedCells.Keys.Max(key => key.Row);
        var startCol = 1u;
        var endCol = usedCells.Keys.Max(key => key.Col);

        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        for (uint r = startRow; r <= endRow; r++)
        {
            var parts = new string[endCol - startCol + 1];
            for (uint c = startCol; c <= endCol; c++)
            {
                usedCells.TryGetValue((r, c), out var cell);
                var raw = cell is null ? "" : FormatCell(cell);
                parts[c - startCol] = EscapeCsvField(raw, cell?.Value is TextValue);
            }
            writer.Write(string.Join(',', parts));
            writer.Write("\r\n");
        }
    }

    private static bool IsValidCsvCellAddress(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        col is >= 1 and <= CellAddress.MaxCol;

    private static string EscapeCsvField(string value, bool isTextValue)
    {
        if (value.Length == 0) return value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r') ||
            (isTextValue && IsFormulaLikeText(value)))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static bool IsFormulaLikeText(string value) =>
        value[0] is '=' or '+' or '-' or '@';

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
        if (dateTime.Date == new DateTime(1899, 12, 30) && dateTime.TimeOfDay != TimeSpan.Zero)
            return dateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        return dateTime.TimeOfDay == TimeSpan.Zero
            ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
