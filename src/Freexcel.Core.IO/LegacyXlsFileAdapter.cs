using System.Text;
using ExcelDataReader;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed class LegacyXlsFileAdapter : IFileAdapter
{
    public string Extension => ".xls";
    public string FormatName => "Excel 97-2003 Workbook";
    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".xls", "Excel 97-2003 Workbook", CanOpen: true, CanSave: false)
    ];

    public Workbook Load(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var workbook = new Workbook("Untitled");

        do
        {
            var sheet = workbook.AddSheet(string.IsNullOrWhiteSpace(reader.Name) ? $"Sheet{workbook.Sheets.Count + 1}" : reader.Name);
            var row = 1u;
            while (reader.Read())
            {
                for (var col = 0; col < reader.FieldCount; col++)
                {
                    var value = MapValue(reader.GetValue(col));
                    if (value is BlankValue)
                        continue;

                    sheet.SetCell(new CellAddress(sheet.Id, row, (uint)(col + 1)), value);
                }

                row++;
            }
        }
        while (reader.NextResult());

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException("Legacy .xls files are currently open-only. Use Save As Excel Workbook instead.");

    private static ScalarValue MapValue(object? value) =>
        value switch
        {
            null => BlankValue.Instance,
            double number => new NumberValue(number),
            int number => new NumberValue(number),
            decimal number => new NumberValue((double)number),
            bool boolean => new BoolValue(boolean),
            DateTime date => new NumberValue(date.ToOADate()),
            string text when text.Length == 0 => BlankValue.Instance,
            string text => new TextValue(text),
            _ => new TextValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "")
        };
}
