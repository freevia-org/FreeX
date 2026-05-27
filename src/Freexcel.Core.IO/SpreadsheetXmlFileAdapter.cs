using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed class SpreadsheetXmlFileAdapter : IFileAdapter
{
    private static readonly XNamespace SpreadsheetNs = "urn:schemas-microsoft-com:office:spreadsheet";
    private static readonly XNamespace OfficeNs = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace ExcelNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XName SpreadsheetIndexAttribute = SpreadsheetNs + "Index";
    private static readonly XName SpreadsheetNameAttribute = SpreadsheetNs + "Name";
    private static readonly XName SpreadsheetFormulaAttribute = SpreadsheetNs + "Formula";
    private static readonly XName SpreadsheetTypeAttribute = SpreadsheetNs + "Type";

    public string Extension => ".xml";
    public string FormatName => "XML Spreadsheet 2003";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xml", "XML Spreadsheet 2003", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream)
    {
        var document = LoadDocument(stream);
        if (document.Root?.Name != SpreadsheetNs + "Workbook")
            throw new InvalidDataException("The XML document is not an Excel XML Spreadsheet 2003 workbook.");

        var workbook = new Workbook("XML Spreadsheet");
        var sheetIndex = 1;
        foreach (var worksheetElement in document.Root.Elements(SpreadsheetNs + "Worksheet"))
        {
            var sheetName = UniqueSheetName(
                workbook,
                worksheetElement.Attribute(SpreadsheetNameAttribute)?.Value,
                sheetIndex++);
            var sheet = workbook.AddSheet(sheetName);
            ReadWorksheet(sheet, worksheetElement);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XProcessingInstruction("mso-application", "progid=\"Excel.Sheet\""),
            new XElement(
                SpreadsheetNs + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", SpreadsheetNs),
                new XAttribute(XNamespace.Xmlns + "o", OfficeNs),
                new XAttribute(XNamespace.Xmlns + "x", ExcelNs),
                workbook.Sheets.Select(ToWorksheetElement)));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    internal static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet)
    {
        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(sourceXml, stylesheet);
        return new SpreadsheetXmlFileAdapter().Load(transformed);
    }

    private static XDocument LoadDocument(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static void ReadWorksheet(Sheet sheet, XElement worksheetElement)
    {
        var tableElement = worksheetElement.Element(SpreadsheetNs + "Table");
        if (tableElement is null)
            return;

        var rowIndex = 1u;
        foreach (var rowElement in tableElement.Elements(SpreadsheetNs + "Row"))
        {
            rowIndex = ReadIndex(rowElement, rowIndex);
            if (rowIndex > CellAddress.MaxRow)
                break;

            var columnIndex = 1u;
            foreach (var cellElement in rowElement.Elements(SpreadsheetNs + "Cell"))
            {
                columnIndex = ReadIndex(cellElement, columnIndex);
                if (columnIndex > CellAddress.MaxCol)
                    break;

                var cell = ReadCell(cellElement);
                if (cell.Value is not BlankValue || cell.FormulaText is not null)
                    sheet.SetCell(new CellAddress(sheet.Id, rowIndex, columnIndex), cell);

                columnIndex++;
            }

            rowIndex++;
        }
    }

    private static Cell ReadCell(XElement cellElement)
    {
        var value = ReadValue(cellElement.Element(SpreadsheetNs + "Data"));
        var formula = cellElement.Attribute(SpreadsheetFormulaAttribute)?.Value;
        if (string.IsNullOrWhiteSpace(formula))
            return Cell.FromValue(value);

        return new Cell
        {
            FormulaText = formula.StartsWith("=", StringComparison.Ordinal) ? formula[1..] : formula,
            Value = value
        };
    }

    private static ScalarValue ReadValue(XElement? dataElement)
    {
        if (dataElement is null)
            return BlankValue.Instance;

        var text = dataElement.Value;
        var type = dataElement.Attribute(SpreadsheetTypeAttribute)?.Value;
        return type switch
        {
            "Number" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) =>
                new NumberValue(number),
            "Boolean" when ReadBoolean(text, out var boolean) =>
                new BoolValue(boolean),
            "DateTime" when DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var dateTime) =>
                DateTimeValue.FromDateTime(dateTime),
            "Error" when text.Length > 0 => new ErrorValue(text),
            _ => new TextValue(text)
        };
    }

    private static bool ReadBoolean(string text, out bool value)
    {
        if (string.Equals(text, "1", StringComparison.Ordinal) ||
            string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(text, "0", StringComparison.Ordinal) ||
            string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static uint ReadIndex(XElement element, uint fallback)
    {
        var indexText = element.Attribute(SpreadsheetIndexAttribute)?.Value;
        return uint.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index > 0
            ? index
            : fallback;
    }

    private static XElement ToWorksheetElement(Sheet sheet) =>
        new(
            SpreadsheetNs + "Worksheet",
            new XAttribute(SpreadsheetNameAttribute, sheet.Name),
            new XElement(
                SpreadsheetNs + "Table",
                sheet.EnumerateCells()
                    .Where(entry => entry.Cell.Value is not BlankValue || entry.Cell.FormulaText is not null)
                    .OrderBy(entry => entry.Address.Row)
                    .ThenBy(entry => entry.Address.Col)
                    .GroupBy(entry => entry.Address.Row)
                    .Select(ToRowElement)));

    private static XElement ToRowElement(IGrouping<uint, (CellAddress Address, Cell Cell)> row)
    {
        var rowElement = new XElement(SpreadsheetNs + "Row", new XAttribute(SpreadsheetIndexAttribute, row.Key));
        foreach (var (address, cell) in row)
            rowElement.Add(ToCellElement(address.Col, cell));

        return rowElement;
    }

    private static XElement ToCellElement(uint column, Cell cell)
    {
        var element = new XElement(SpreadsheetNs + "Cell", new XAttribute(SpreadsheetIndexAttribute, column));
        if (cell.FormulaText is { Length: > 0 } formulaText)
            element.SetAttributeValue(SpreadsheetFormulaAttribute, formulaText.StartsWith("=", StringComparison.Ordinal) ? formulaText : $"={formulaText}");

        if (cell.Value is not BlankValue)
            element.Add(ToDataElement(cell.Value));

        return element;
    }

    private static XElement ToDataElement(ScalarValue value)
    {
        var (type, text) = value switch
        {
            NumberValue number => ("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            DateTimeValue dateTime => ("DateTime", dateTime.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)),
            BoolValue boolean => ("Boolean", boolean.Value ? "1" : "0"),
            ErrorValue error => ("Error", error.Code),
            TextValue textValue => ("String", textValue.Value),
            _ => ("String", "")
        };

        return new XElement(
            SpreadsheetNs + "Data",
            new XAttribute(SpreadsheetTypeAttribute, type),
            text);
    }

    private static string UniqueSheetName(Workbook workbook, string? rawName, int index)
    {
        var baseName = string.IsNullOrWhiteSpace(rawName) ? $"Sheet{index}" : rawName.Trim();
        baseName = SanitizeSheetName(baseName);
        var candidate = baseName;
        var suffix = 1;
        while (workbook.ValidateSheetName(candidate) is not null)
        {
            var marker = $" ({suffix++})";
            candidate = string.Concat(baseName.AsSpan(0, Math.Min(baseName.Length, 31 - marker.Length)), marker);
        }

        return candidate;
    }

    private static string SanitizeSheetName(string value)
    {
        Span<char> invalid = [':', '\\', '/', '?', '*', '[', ']'];
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        var sanitized = builder.ToString().Trim('\'');
        if (sanitized.Length == 0)
            return "Sheet";

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }
}
