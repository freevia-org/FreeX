using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// CSV file adapter with full RFC 4180 quoting support.
/// Fields containing commas, double-quotes, or newlines are wrapped in double-quotes,
/// and embedded double-quotes are escaped by doubling ("").
/// </summary>
public sealed class CsvFileAdapter : IFileAdapter
{
    public string Extension => ".csv";
    public string FormatName => "CSV (Comma-separated values)";

    public Workbook Load(Stream stream)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        uint row = 1;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var fields = ParseCsvLine(line);
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field.Length == 0) continue;

                ScalarValue value = double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
                    ? new NumberValue(num)
                    : new TextValue(field);

                sheet.SetCell(new CellAddress(sheet.Id, row, (uint)(i + 1)), value);
            }
            row++;
        }

        return workbook;
    }

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

    // ── RFC 4180 helpers ──────────────────────────────────────────────────────

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i <= line.Length)
        {
            if (i == line.Length) { fields.Add(""); break; }

            if (line[i] == '"')
            {
                // Quoted field
                var sb = new StringBuilder();
                i++; // skip opening quote
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"'); // escaped quote
                            i += 2;
                        }
                        else
                        {
                            i++; // closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i++]);
                    }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                // Unquoted field
                int start = i;
                while (i < line.Length && line[i] != ',') i++;
                fields.Add(line[start..i]);
                if (i < line.Length) i++; // skip comma
            }
        }
        return fields;
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
        BoolValue b   => b.Value ? "TRUE" : "FALSE",
        TextValue t   => t.Value,
        ErrorValue e  => e.Code,
        _             => "",
    };
}
