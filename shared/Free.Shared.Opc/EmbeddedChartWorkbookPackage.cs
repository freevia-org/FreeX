using System.IO.Compression;
using System.Xml.Linq;

namespace Free.Shared.Opc;

/// <summary>
/// The package scaffolding of the tiny spreadsheet that a chart embeds so "Edit Data" has a workbook to
/// open: <c>[Content_Types].xml</c>, <c>_rels/.rels</c>, <c>xl/workbook.xml</c>,
/// <c>xl/_rels/workbook.xml.rels</c>, an optional minimal <c>xl/styles.xml</c> and the caller's single
/// <c>xl/worksheets/sheet1.xml</c>. FreeW (DOCX charts) and FreeP (PPTX charts) each carried a private copy
/// of these part builders; what goes INTO the worksheet (layout, which cells, number formatting) is each
/// app's own and is passed in as the finished sheet document.
///
/// <para>The two apps differ in exactly two explicit, caller-owned ways, so each keeps its current output:
/// whether the workbook has a styles part (<c>includeStyles</c>; FreeP yes, FreeW no) and how a part is
/// serialized (<c>writePart</c>; each app's own writer keeps its XML-illegal-character sanitizing, zip
/// timestamp and indentation policy at its write boundary).</para>
/// </summary>
public static class EmbeddedChartWorkbookPackage
{
    private const string OfficeRelationshipsNamespaceName = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string WorksheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string StylesContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";

    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships = OfficeRelationshipsNamespaceName;

    /// <summary>The spreadsheetml main namespace, for callers that build the worksheet part.</summary>
    public static XNamespace SpreadsheetNamespace => Spreadsheet;

    /// <summary>
    /// Builds the workbook zip. <paramref name="writePart"/> receives the archive, the part path and the part
    /// document and must serialize it (and sanitize XML-illegal characters) the way the calling app writes
    /// every other part. <paramref name="declaration"/>, when not null, is set on every scaffold document
    /// (the caller's <paramref name="sheet"/> is passed through untouched).
    /// </summary>
    public static byte[] Build(
        XDocument sheet,
        string sheetName,
        bool includeStyles,
        XDeclaration? declaration,
        Action<ZipArchive, string, XDocument> writePart)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(sheetName);
        ArgumentNullException.ThrowIfNull(writePart);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            writePart(zip, "[Content_Types].xml", WithDeclaration(ContentTypes(includeStyles), declaration));
            writePart(zip, "_rels/.rels", WithDeclaration(PackageRelationships(), declaration));
            writePart(zip, "xl/workbook.xml", WithDeclaration(WorkbookXml(sheetName), declaration));
            writePart(zip, "xl/_rels/workbook.xml.rels", WithDeclaration(WorkbookRelationships(includeStyles), declaration));
            if (includeStyles)
                writePart(zip, "xl/styles.xml", WithDeclaration(StylesXml(), declaration));
            writePart(zip, "xl/worksheets/sheet1.xml", sheet);
        }

        return buffer.ToArray();
    }

    public static XDocument ContentTypes(bool includeStyles)
    {
        var ct = OpcMediaTypes.ContentTypesNamespace;
        var types = new XElement(ct + "Types",
            new XElement(ct + "Default",
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", OpcMediaTypes.RelationshipsContentType)),
            new XElement(ct + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(ct + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", WorkbookContentType)),
            new XElement(ct + "Override",
                new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                new XAttribute("ContentType", WorksheetContentType)));
        if (includeStyles)
        {
            types.Add(new XElement(ct + "Override",
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", StylesContentType)));
        }

        return new XDocument(types);
    }

    public static XDocument PackageRelationships() =>
        new(OpcRelationships.CreateRoot(
            OpcRelationships.CreateRelationship(
                "rId1",
                OfficeRelationshipsNamespaceName + "/officeDocument",
                "xl/workbook.xml")));

    public static XDocument WorkbookXml(string sheetName) =>
        new(new XElement(Spreadsheet + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", OfficeRelationshipsNamespaceName),
            new XElement(Spreadsheet + "sheets",
                new XElement(Spreadsheet + "sheet",
                    new XAttribute("name", sheetName),
                    new XAttribute("sheetId", "1"),
                    new XAttribute(OfficeRelationships + "id", "rId1")))));

    public static XDocument WorkbookRelationships(bool includeStyles)
    {
        var relationships = OpcRelationships.CreateRoot(
            OpcRelationships.CreateRelationship(
                "rId1",
                OfficeRelationshipsNamespaceName + "/worksheet",
                "worksheets/sheet1.xml"));
        if (includeStyles)
        {
            relationships.Add(OpcRelationships.CreateRelationship(
                "rId2",
                OfficeRelationshipsNamespaceName + "/styles",
                "styles.xml"));
        }

        return new XDocument(relationships);
    }

    /// <summary>The smallest styles part Excel accepts: one font, fill, border, cell style xf and cell xf.</summary>
    public static XDocument StylesXml() =>
        new(new XElement(Spreadsheet + "styleSheet",
            new XElement(Spreadsheet + "fonts",
                new XAttribute("count", "1"),
                new XElement(Spreadsheet + "font")),
            new XElement(Spreadsheet + "fills",
                new XAttribute("count", "1"),
                new XElement(Spreadsheet + "fill")),
            new XElement(Spreadsheet + "borders",
                new XAttribute("count", "1"),
                new XElement(Spreadsheet + "border")),
            new XElement(Spreadsheet + "cellStyleXfs",
                new XAttribute("count", "1"),
                new XElement(Spreadsheet + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0"))),
            new XElement(Spreadsheet + "cellXfs",
                new XAttribute("count", "1"),
                new XElement(Spreadsheet + "xf",
                    new XAttribute("numFmtId", "0"),
                    new XAttribute("fontId", "0"),
                    new XAttribute("fillId", "0"),
                    new XAttribute("borderId", "0"),
                    new XAttribute("xfId", "0")))));

    /// <summary>A worksheet cell holding an inline string (<c>t="inlineStr"</c>), so no shared-strings part is needed.</summary>
    public static XElement InlineStringCell(string reference, string text) =>
        new(Spreadsheet + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", text)));

    /// <summary>Maps a 1-based column number to its spreadsheet letters (1 = A, 26 = Z, 27 = AA).</summary>
    public static string ColumnName(int oneBasedColumn)
    {
        var column = oneBasedColumn;
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }

    private static XDocument WithDeclaration(XDocument document, XDeclaration? declaration)
    {
        if (declaration is not null)
            document.Declaration = new XDeclaration(declaration);
        return document;
    }
}
