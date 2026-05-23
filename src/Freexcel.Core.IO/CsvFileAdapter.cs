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
        var range = sheet.GetUsedRange();
        if (range is null) return;

        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        for (uint r = range.Value.Start.Row; r <= range.Value.End.Row; r++)
        {
            var parts = new string[range.Value.End.Col - range.Value.Start.Col + 1];
            for (uint c = range.Value.Start.Col; c <= range.Value.End.Col; c++)
            {
                var cell = sheet.GetCell(r, c);
                var raw = cell is null ? "" : FormatValue(cell.Value);
                parts[c - range.Value.Start.Col] = EscapeCsvField(raw);
            }
            writer.Write(string.Join(',', parts));
            writer.Write("\r\n");
        }
    }

    private static string EscapeCsvField(string value)
    {
        if (value.Length == 0) return value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        TextValue t => t.Value,
        ErrorValue e => e.Code,
        _ => "",
    };
}
