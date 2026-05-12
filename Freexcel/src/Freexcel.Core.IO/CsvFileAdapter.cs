using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// CSV file adapter. Phase 2 limitations: no quoted-field handling on read or write
/// (values containing commas will be split on load; values containing commas will
/// produce structurally invalid CSV on save). Full RFC 4180 quoting deferred to Phase 4.
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
            var fields = line.Split(',');
            for (uint col = 1; col <= (uint)fields.Length; col++)
            {
                var field = fields[col - 1];
                if (field.Length == 0) continue;

                ScalarValue value = double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
                    ? new NumberValue(num)
                    : new TextValue(field);

                var addr = new CellAddress(sheet.Id, row, col);
                sheet.SetCell(addr, value);
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
                parts[c - range.Value.Start.Col] = cell is null
                    ? ""
                    : FormatValue(cell.Value);
            }
            writer.Write(string.Join(',', parts));
            writer.Write("\r\n");
        }
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
