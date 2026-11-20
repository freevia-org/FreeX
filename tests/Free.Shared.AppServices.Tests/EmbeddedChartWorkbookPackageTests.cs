using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Golden coverage for the shared chart-data workbook scaffold. The expected XML below is the part content
/// FreeW's DOCX writer (no styles part) and FreeP's PPTX writer (with a styles part) each produced from
/// their private copies before the scaffold was shared; the two app writers pass their own variant flags.
/// </summary>
public sealed class EmbeddedChartWorkbookPackageTests
{
    private const string ContentTypesNoStyles =
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\" />"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" />"
        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />"
        + "</Types>";

    private const string ContentTypesWithStyles =
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\" />"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\" />"
        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\" />"
        + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\" />"
        + "</Types>";

    private const string PackageRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\" />"
        + "</Relationships>";

    private const string WorkbookRelsNoStyles =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" />"
        + "</Relationships>";

    private const string WorkbookRelsWithStyles =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\" />"
        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\" />"
        + "</Relationships>";

    private const string WorkbookXml =
        "<workbook xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
        + "<sheets><sheet name=\"ChartData\" sheetId=\"1\" r:id=\"rId1\" /></sheets>"
        + "</workbook>";

    private const string StylesXml =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
        + "<fonts count=\"1\"><font /></fonts>"
        + "<fills count=\"1\"><fill /></fills>"
        + "<borders count=\"1\"><border /></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" /></cellStyleXfs>"
        + "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" /></cellXfs>"
        + "</styleSheet>";

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static Dictionary<string, XDocument> BuildParts(bool includeStyles, XDeclaration? declaration, out List<string> order)
    {
        var sheet = new XDocument(new XElement(Main + "worksheet", new XElement(Main + "sheetData")));
        var captured = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        var names = new List<string>();
        var bytes = EmbeddedChartWorkbookPackage.Build(
            sheet,
            "ChartData",
            includeStyles,
            declaration,
            (archive, path, document) =>
            {
                names.Add(path);
                captured[path] = new XDocument(document);
                var entry = archive.CreateEntry(path);
                using var stream = entry.Open();
                document.Save(stream);
            });

        // The result really is a readable zip holding every captured part.
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.Entries.Select(entry => entry.FullName).Should().Equal(names);

        order = names;
        return captured;
    }

    private static XElement Strip(XElement element)
    {
        var clone = new XElement(element);
        foreach (var e in clone.DescendantsAndSelf())
            e.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
        return clone;
    }

    private static void AssertSame(XDocument actual, string expectedXml, string because) =>
        XNode.DeepEquals(Strip(actual.Root!), Strip(XElement.Parse(expectedXml))).Should().BeTrue(because + "\nactual: " + actual.Root);

    [Fact]
    public void WithoutStylesMatchesTheFreeWPackage()
    {
        var parts = BuildParts(includeStyles: false, declaration: null, out var order);

        order.Should().Equal(
            "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml");
        AssertSame(parts["[Content_Types].xml"], ContentTypesNoStyles, "content types");
        AssertSame(parts["_rels/.rels"], PackageRels, "package rels");
        AssertSame(parts["xl/workbook.xml"], WorkbookXml, "workbook");
        AssertSame(parts["xl/_rels/workbook.xml.rels"], WorkbookRelsNoStyles, "workbook rels");
        parts.Values.Where(document => document.Declaration is not null)
            .Should().BeEmpty("FreeW's part writer supplies the declaration itself");
    }

    [Fact]
    public void WithStylesMatchesTheFreePPackage()
    {
        var parts = BuildParts(includeStyles: true, new XDeclaration("1.0", "UTF-8", "yes"), out var order);

        order.Should().Equal(
            "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml");
        AssertSame(parts["[Content_Types].xml"], ContentTypesWithStyles, "content types");
        AssertSame(parts["_rels/.rels"], PackageRels, "package rels");
        AssertSame(parts["xl/workbook.xml"], WorkbookXml, "workbook");
        AssertSame(parts["xl/_rels/workbook.xml.rels"], WorkbookRelsWithStyles, "workbook rels");
        AssertSame(parts["xl/styles.xml"], StylesXml, "styles");
        foreach (var path in order.Where(path => path != "xl/worksheets/sheet1.xml"))
        {
            parts[path].Declaration.Should().NotBeNull(path);
            parts[path].Declaration!.ToString().Should().Be("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        }

        parts["xl/worksheets/sheet1.xml"].Declaration.Should().BeNull("the caller's sheet document is passed through untouched");
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(2, "B")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(702, "ZZ")]
    [InlineData(703, "AAA")]
    [InlineData(0, "")]
    public void ColumnNameIsOneBasedBijectiveBase26(int column, string expected) =>
        EmbeddedChartWorkbookPackage.ColumnName(column).Should().Be(expected);

    [Fact]
    public void InlineStringCellCarriesTheTextWithoutASharedStringsPart()
    {
        var cell = EmbeddedChartWorkbookPackage.InlineStringCell("B1", "Series");

        XNode.DeepEquals(Strip(cell), Strip(XElement.Parse(
            "<c xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" r=\"B1\" t=\"inlineStr\"><is><t>Series</t></is></c>")))
            .Should().BeTrue();
    }
}
