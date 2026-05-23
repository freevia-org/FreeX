using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class DelimitedTextWorkbookReader
{
    public static Workbook Load(Stream stream, char delimiter)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        uint row = 1;
        while (TryReadRecord(reader, delimiter, out var fields))
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field.Length == 0)
                    continue;

                sheet.SetCell(new CellAddress(sheet.Id, row, (uint)(i + 1)), CoerceValue(field));
            }

            row++;
        }

        return workbook;
    }

    internal static bool TryReadRecord(TextReader reader, char delimiter, out List<string> fields)
    {
        fields = [];
        var current = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            var c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
                atFieldStart = true;
            }
            else if (c == '\r')
            {
                if (reader.Peek() == '\n')
                    reader.Read();
                fields.Add(current.ToString());
                return true;
            }
            else if (c == '\n')
            {
                fields.Add(current.ToString());
                return true;
            }
            else
            {
                current.Append(c);
                atFieldStart = false;
            }
        }

        if (current.Length > 0 || fields.Count > 0)
        {
            fields.Add(current.ToString());
            return true;
        }

        return false;
    }

    private static ScalarValue CoerceValue(string field)
    {
        if (string.Equals(field, "TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (string.Equals(field, "FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        if (double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return new NumberValue(number);

        return new TextValue(field);
    }
}
