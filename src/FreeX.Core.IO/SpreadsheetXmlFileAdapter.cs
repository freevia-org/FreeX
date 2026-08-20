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

    internal Workbook Load(Stream stream, long maxCharactersInDocument) =>
        LoadWithWarnings(stream, maxCharactersInDocument).Workbook;

    public XlsxLoadResult LoadWithWarnings(Stream stream) =>
        LoadWithWarnings(stream, SecureXmlReaderSettings.DefaultMaxCharactersInDocument);

    /// <summary>
    /// Loads a SpreadsheetML (Excel XML Spreadsheet 2003) stream, also reporting when
    /// <see cref="ReadWorksheet"/> had to break out of its row or column loop because an explicit
    /// <c>ss:Index</c> jumped past this sheet's grid limit -- see that method's doc comment for why
    /// the used range alone cannot be trusted to reveal that afterwards.
    /// </summary>
    internal XlsxLoadResult LoadWithWarnings(Stream stream, long maxCharactersInDocument)
    {
        var document = LoadDocument(stream, maxCharactersInDocument);
        if (document.Root?.Name != SpreadsheetNs + "Workbook")
            throw new InvalidDataException("The XML document is not an Excel XML Spreadsheet 2003 workbook.");

        var workbook = new Workbook("XML Spreadsheet");
        var styles = ReadStyles(workbook, document.Root);
        var sheetIndex = 1;
        List<string>? gridLimitWarnings = null;
        foreach (var worksheetElement in document.Root.Elements(SpreadsheetNs + "Worksheet"))
        {
            var sheetName = UniqueSheetName(
                workbook,
                worksheetElement.Attribute(SpreadsheetNameAttribute)?.Value,
                sheetIndex++);
            var sheet = workbook.AddSheet(sheetName);
            ReadWorksheetVisibility(sheet, worksheetElement);
            ReadWorksheetOptions(sheet, worksheetElement);
            var (rowLimitExceeded, colLimitExceeded) = ReadWorksheet(sheet, worksheetElement, styles);
            if (rowLimitExceeded || colLimitExceeded)
            {
                gridLimitWarnings ??= [];
                gridLimitWarnings.Add(rowLimitExceeded && colLimitExceeded
                    ? $"[grid-limit] Sheet '{sheet.Name}': the source file may contain more rows and columns than this sheet's {CellAddress.MaxRow:N0}-row, {CellAddress.MaxCol:N0}-column limit; anything beyond that limit was not loaded."
                    : rowLimitExceeded
                        ? $"[grid-limit] Sheet '{sheet.Name}': the source file may contain more than {CellAddress.MaxRow:N0} rows; rows beyond that limit were not loaded."
                        : $"[grid-limit] Sheet '{sheet.Name}': the source file may contain more than {CellAddress.MaxCol:N0} columns; columns beyond that limit were not loaded.");
            }
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        ReadNamedRanges(workbook, document.Root);

        return new XlsxLoadResult(workbook, gridLimitWarnings ?? (IReadOnlyList<string>)[]);
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
            // Entitize (not Replace) so a literal CR inside cell text/comments is written as &#xD; and
            // survives the round-trip. XML end-of-line normalization on read collapses a raw CR-LF in
            // text to a single LF, silently rewriting multi-line cell values; entitizing the CR prevents
            // that loss so text values are byte-faithful.
            NewLineHandling = NewLineHandling.Entitize
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
