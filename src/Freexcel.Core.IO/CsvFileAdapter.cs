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
        var sheet    = workbook.AddSheet("Sheet1");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        uint row = 1;
        while (TryReadRecord(reader, out var fields))
        {
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

    private static bool TryReadRecord(TextReader reader, out List<string> fields)
    {
        fields = [];
        var current   = new StringBuilder();
        bool inQuotes = false;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            char c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); current.Append('"'); } // escaped ""
                    else inQuotes = false;
                }
                else
                {
                    current.Append(c); // may be \n — allowed inside quoted fields (RFC 4180)
                }
            }
            else
            {
                switch (c)
                {
                    case '"':  inQuotes = true;  break;
                    case ',':  fields.Add(current.ToString()); current.Clear(); break;
                    case '\r': break; // skip CR; LF below ends the record
                    case '\n': fields.Add(current.ToString()); return true;
                    default:   current.Append(c); break;
                }
            }
        }

        // End of stream — flush the last record if any data remains
        if (current.Length > 0 || fields.Count > 0)
        {
            fields.Add(current.ToString());
            return true;
        }
        return false;
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
