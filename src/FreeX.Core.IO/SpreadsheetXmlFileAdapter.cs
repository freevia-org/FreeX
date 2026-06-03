using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter : IFileAdapter
{
    private static readonly XNamespace SpreadsheetNs = "urn:schemas-microsoft-com:office:spreadsheet";
    private static readonly XNamespace OfficeNs = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace ExcelNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XName SpreadsheetIndexAttribute = SpreadsheetNs + "Index";
    private static readonly XName SpreadsheetSpanAttribute = SpreadsheetNs + "Span";
    private static readonly XName SpreadsheetNameAttribute = SpreadsheetNs + "Name";
    private static readonly XName SpreadsheetFormulaAttribute = SpreadsheetNs + "Formula";
    private static readonly XName SpreadsheetTypeAttribute = SpreadsheetNs + "Type";
    private static readonly XName SpreadsheetMergeAcrossAttribute = SpreadsheetNs + "MergeAcross";
    private static readonly XName SpreadsheetMergeDownAttribute = SpreadsheetNs + "MergeDown";
    private static readonly XName SpreadsheetIdAttribute = SpreadsheetNs + "ID";
    private static readonly XName SpreadsheetParentAttribute = SpreadsheetNs + "Parent";
    private static readonly XName SpreadsheetStyleIdAttribute = SpreadsheetNs + "StyleID";
    private static readonly XName SpreadsheetFormatAttribute = SpreadsheetNs + "Format";
    private static readonly XName SpreadsheetHrefAttribute = SpreadsheetNs + "HRef";
    private static readonly XName SpreadsheetHrefScreenTipAttribute = SpreadsheetNs + "HRefScreenTip";
    private static readonly XName SpreadsheetAuthorAttribute = SpreadsheetNs + "Author";
    private static readonly XName SpreadsheetVisibleAttribute = SpreadsheetNs + "Visible";
    private static readonly XName SpreadsheetHeightAttribute = SpreadsheetNs + "Height";
    private static readonly XName SpreadsheetWidthAttribute = SpreadsheetNs + "Width";
    private static readonly XName SpreadsheetHiddenAttribute = SpreadsheetNs + "Hidden";
    private static readonly XName SpreadsheetRefersToAttribute = SpreadsheetNs + "RefersTo";

    public string Extension => ".xml";
    public string FormatName => "XML Spreadsheet 2003";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xml", "XML Spreadsheet 2003", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) =>
        Load(stream, SecureXmlReaderSettings.DefaultMaxCharactersInDocument);

    internal Workbook Load(Stream stream, long maxCharactersInDocument)
    {
        var document = LoadDocument(stream, maxCharactersInDocument);
        if (document.Root?.Name != SpreadsheetNs + "Workbook")
            throw new InvalidDataException("The XML document is not an Excel XML Spreadsheet 2003 workbook.");

        var workbook = new Workbook("XML Spreadsheet");
        var styles = ReadStyles(workbook, document.Root);
        var sheetIndex = 1;
        foreach (var worksheetElement in document.Root.Elements(SpreadsheetNs + "Worksheet"))
        {
            var sheetName = UniqueSheetName(
                workbook,
                worksheetElement.Attribute(SpreadsheetNameAttribute)?.Value,
                sheetIndex++);
            var sheet = workbook.AddSheet(sheetName);
            ReadWorksheetVisibility(sheet, worksheetElement);
            ReadWorksheetOptions(sheet, worksheetElement);
            ReadWorksheet(sheet, worksheetElement, styles);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        ReadNamedRanges(workbook, document.Root);

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        var styleIds = CreateNumberFormatStyleIds(workbook);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(stream, settings);
        WriteWorkbook(writer, workbook, styleIds);
    }

    public static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet)
        => LoadTransformed(sourceXml, stylesheet, XsltWorkbookTransform.DefaultMaxOutputBytes);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        IReadOnlyDictionary<string, string?> parameters)
        => LoadTransformed(
            sourceXml,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            XsltWorkbookTransform.DefaultMaxInputCharacters,
            parameters);

    public static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet, long maxOutputBytes)
        => LoadTransformed(sourceXml, stylesheet, maxOutputBytes, XsltWorkbookTransform.DefaultMaxInputCharacters);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        long maxOutputBytes,
        long maxInputCharacters)
        => LoadTransformed(sourceXml, stylesheet, maxOutputBytes, maxInputCharacters, parameters: null);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        long maxOutputBytes,
        long maxInputCharacters,
        IReadOnlyDictionary<string, string?>? parameters)
    {
        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(
            sourceXml,
            stylesheet,
            maxOutputBytes,
            maxInputCharacters,
            parameters);
        try
        {
            return new SpreadsheetXmlFileAdapter().Load(transformed);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The XSLT transform output could not be read as XML Spreadsheet 2003.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("The XSLT transform output is not a valid Excel XML Spreadsheet 2003 workbook.", ex);
        }
    }

    private static List<uint> BuildSortedRowLayoutIndexes(Sheet sheet)
    {
        if (sheet.RowHeights.Count == 0 && sheet.HiddenRows.Count == 0)
            return [];

        var rows = new List<uint>(sheet.RowHeights.Count + sheet.HiddenRows.Count);
        foreach (var row in sheet.RowHeights.Keys)
        {
            if (IsValidRowLayoutIndex(row))
                rows.Add(row);
        }

        foreach (var row in sheet.HiddenRows)
        {
            if (IsValidRowLayoutIndex(row))
                rows.Add(row);
        }

        rows.Sort();
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < rows.Count; readIndex++)
        {
            if (readIndex > 0 && rows[readIndex] == rows[readIndex - 1])
                continue;

            rows[writeIndex++] = rows[readIndex];
        }

        if (writeIndex < rows.Count)
            rows.RemoveRange(writeIndex, rows.Count - writeIndex);

        return rows;
    }

    private static bool IsValidRowLayoutIndex(uint rowIndex) =>
        rowIndex is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidColumnLayoutIndex(uint columnIndex) =>
        columnIndex is >= 1 and <= CellAddress.MaxCol;

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
