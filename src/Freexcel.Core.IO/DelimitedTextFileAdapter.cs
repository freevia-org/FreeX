using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed class DelimitedTextFileAdapter(string extension, string formatName, char delimiter) : IFileAdapter
{
    public string Extension { get; } = extension;
    public string FormatName { get; } = formatName;

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(extension, formatName, CanOpen: true, CanSave: false)
    ];

    public Workbook Load(Stream stream)
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

                ScalarValue value = double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
                    ? new NumberValue(num)
                    : new TextValue(field);

                sheet.SetCell(new CellAddress(sheet.Id, row, (uint)(i + 1)), value);
            }

            row++;
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException($"{FormatName} is currently open-only. Use Save As Excel Workbook instead.");

    internal static bool TryReadRecord(TextReader reader, char delimiter, out List<string> fields)
    {
        fields = [];
        var current = new StringBuilder();
        var inQuotes = false;

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
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '\r')
                {
                    continue;
                }
                else if (c == '\n')
                {
                    fields.Add(current.ToString());
                    return true;
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0 || fields.Count > 0)
        {
            fields.Add(current.ToString());
            return true;
        }

        return false;
    }
}
