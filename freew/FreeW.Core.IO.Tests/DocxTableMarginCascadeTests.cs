using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit.Abstractions;

namespace FreeW.Core.IO.Tests;

public sealed class DocxTableMarginCascadeTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public DocxTableMarginCascadeTests(ITestOutputHelper output)
    {
        var assembly = typeof(DocxReader).Assembly;
        output.WriteLine("Loaded {0}; SHA256={1}; version={2}", assembly.Location,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    [Theory]
    [InlineData(null, null, 12)]
    [InlineData("", null, 12)]
    [InlineData("", "Unknown", 12)]
    [InlineData("", null, 15)]
    public void NoSelectedTableStyleUsesTheWordUnstyledHalfPointFallback(string? styles, string? styleId, int compatibilityMode)
    {
        var document = Read(TableXml(StyleReference(styleId)), styles, compatibilityMode);
        AssertMargins(OnlyTable(document).DefaultCellMargins, 0, 0.5, 0, 0.5);
        OnlyTable(document).Rows[0].Cells[0].Margins.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedMarginlessStyleUsesZeroMarginsRatherThanNativeDefaults(bool isDefault)
    {
        var style = Style("Grid", "", isDefault: isDefault);
        var document = Read(TableXml(isDefault ? "" : StyleReference("Grid")), style);
        AssertMargins(OnlyTable(document).DefaultCellMargins, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void DefaultTableStyleSuppliesMarginsForUnstyledOrUnknownStyleTables(string? styleId)
    {
        var document = Read(TableXml(StyleReference(styleId)), Style("TableNormal", Margins(left: 108, right: 108), isDefault: true));
        AssertMargins(OnlyTable(document).DefaultCellMargins, 0, 5.4, 0, 5.4);
    }

    [Fact]
    public void ResolvableExplicitStyleDoesNotAcquireAnUnrelatedDefaultStyle()
    {
        var document = Read(TableXml(StyleReference("Grid")),
            Style("TableNormal", Margins(left: 108, right: 108), isDefault: true) + Style("Grid", ""));
        AssertMargins(OnlyTable(document).DefaultCellMargins, 0, 0, 0, 0);
    }

    [Fact]
    public void StyleTableRowAndCellResolveEachEdgeInOrderIncludingExplicitZero()
    {
        var styles = Style("Base", Margins(top: 20, left: 40, bottom: 60, right: 80))
            + Style("Child", Margins(left: 0, right: 160), basedOn: "Base");
        var row = Row(Cell(Margins(left: 0)), Cell(), rowMargins: Margins(top: 140, left: 180));
        var table = OnlyTable(Read(TableXml(StyleReference("Child") + Margins(bottom: 0, right: 120), row + Row(Cell())), styles));
        AssertMargins(table.DefaultCellMargins, 1, 0, 0, 6);
        AssertMargins(table.Rows[0].Cells[0].Margins, 7, 0, 0, 6);
        AssertMargins(table.Rows[0].Cells[1].Margins, 7, 9, 0, 6);
        table.Rows[1].Cells[0].Margins.Should().BeNull();
        var reopened = OnlyTable(RoundTrip(Document(table)));
        AssertMargins(reopened.DefaultCellMargins, 1, 0, 0, 6);
        AssertMargins(reopened.Rows[0].Cells[0].Margins, 7, 0, 0, 6);
        AssertMargins(reopened.Rows[0].Cells[1].Margins, 7, 9, 0, 6);
    }

    [Fact]
    public void PartialCellMarginsInheritTheResolvedTableInsteadOfGlobalFivePointFour()
    {
        var table = OnlyTable(Read(TableXml(StyleReference("Custom"), Row(Cell(Margins(top: 200)))),
            Style("Custom", Margins(top: 20, left: 40, bottom: 60, right: 80))));
        AssertMargins(table.Rows[0].Cells[0].Margins, 10, 2, 3, 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmittedEdgesAndEmptyMarginContainersKeepTheResolvedFallback(bool empty)
    {
        var table = OnlyTable(Read(TableXml(empty ? "<w:tblCellMar/>" : Margins(left: 0)), ""));
        AssertMargins(table.DefaultCellMargins, 0, empty ? 0.5 : 0, 0, 0.5);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("missing")]
    [InlineData("wrong-type")]
    public void BrokenOrCyclicStyleChainsTerminateAndRetainKnownEdges(string chain)
    {
        var styles = Style("A", Margins(left: 40), basedOn: "B") + (chain switch
        {
            "cycle" => Style("B", Margins(top: 60), basedOn: "A"),
            "wrong-type" => "<w:style w:type=\"paragraph\" w:styleId=\"B\"><w:name w:val=\"B\"/><w:tblPr>"
                + Margins(top: 600) + "</w:tblPr></w:style>",
            _ => "",
        });
        var table = OnlyTable(Read(TableXml(StyleReference("A")), styles));
        AssertMargins(table.DefaultCellMargins, chain == "cycle" ? 3 : 0, 2, 0, 0);
    }

    [Fact]
    public void ConditionalStyleMarginsAreNotAppliedToEveryCell()
    {
        var styles = Style("Base", Margins(left: 60, right: 80)).Replace("</w:style>",
            "<w:tblStylePr w:type=\"firstRow\"><w:tblPr>" + Margins(left: 1800)
            + "</w:tblPr></w:tblStylePr></w:style>", StringComparison.Ordinal);
        var table = OnlyTable(Read(TableXml(StyleReference("Base")), styles));
        AssertMargins(table.DefaultCellMargins, 0, 3, 0, 4);
    }

    [Fact]
    public void NestedTableResolvesItsOwnMarginsWithoutInheritingOuterCellPadding()
    {
        var nested = TableXml("");
        var outer = TableXml(Margins(left: 160, right: 160), Row(Cell(nested: nested)));
        var table = OnlyTable(Read(outer, ""));
        AssertMargins(table.DefaultCellMargins, 0, 8, 0, 8);
        AssertMargins(table.Rows[0].Cells[0].NestedTables.Single().DefaultCellMargins, 0, 0.5, 0, 0.5);
    }

    [Theory]
    [InlineData("header", false)]
    [InlineData("footer", false)]
    [InlineData("header", true)]
    public void HeaderAndFooterTablesUseTheSameCascadeAndKeepExplicitPadding(string story, bool explicitMargins)
    {
        var document = Read(TableXml(StyleReference("Normal") + (explicitMargins ? Margins(left: 115, right: 115) : "")),
            Style("Normal", Margins(left: 60, right: 80)), story: story);
        var table = story == "header" ? document.Header!.Table! : document.Footer!.Table!;
        AssertMargins(table.DefaultCellMargins, 0, explicitMargins ? 5.75 : 3, 0, explicitMargins ? 5.75 : 4);
        var reopened = RoundTrip(document);
        table = story == "header" ? reopened.Header!.Table! : reopened.Footer!.Table!;
        AssertMargins(table.DefaultCellMargins, 0, explicitMargins ? 5.75 : 3, 0, explicitMargins ? 5.75 : 4);
    }

    [Theory]
    [InlineData("unstyled")]
    [InlineData("zero-style")]
    [InlineData("custom-style")]
    public void ImportedResolvedMarginsRemainStableAcrossTwoSaves(string context)
    {
        var styles = context switch
        {
            "zero-style" => Style("Selected", ""),
            "custom-style" => Style("Selected", Margins(left: 30, right: 50)),
            _ => "",
        };
        var document = Read(TableXml(context == "unstyled" ? "" : StyleReference("Selected")), styles);
        var left = context == "unstyled" ? 0.5 : context == "zero-style" ? 0 : 1.5;
        var right = context == "unstyled" ? 0.5 : context == "zero-style" ? 0 : 2.5;
        for (var index = 0; index < 3; index++)
        {
            AssertMargins(OnlyTable(document).DefaultCellMargins, 0, left, 0, right);
            document = RoundTrip(document);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeNullMarginsAreWrittenAsTheDisplayedFivePointFourPointIntent(bool withImportedStyle)
    {
        var table = Table.Create(1, 1);
        var document = Document(table);
        if (withImportedStyle)
        {
            table.TableStyleId = "Selected";
            document.Styles["Selected"] = new DocumentStyle
            {
                Id = "Selected", Name = "Selected", Type = StyleType.Table,
                PreservedTableStyleXml = "<w:style xmlns:w=\"" + W + "\" w:type=\"table\" w:styleId=\"Selected\">"
                    + "<w:name w:val=\"Selected\"/><w:tblPr>" + Margins(left: 200, right: 200) + "</w:tblPr></w:style>",
            };
        }
        using var saved = Save(document);
        using (var copy = new MemoryStream(saved.ToArray()))
        using (var package = WordprocessingDocument.Open(copy, false))
            new OpenXmlValidator(FileFormatVersions.Office2013).Validate(package).Should().BeEmpty();
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        using (var xml = archive.GetEntry("word/document.xml")!.Open())
        {
            var margins = XDocument.Load(xml).Descendants(W + "tblCellMar").Single();
            margins.Element(W + "left")!.Attribute(W + "w")!.Value.Should().Be("108");
            margins.Element(W + "right")!.Attribute(W + "w")!.Value.Should().Be("108");
            margins.Element(W + "top")!.Attribute(W + "w")!.Value.Should().Be("0");
            margins.Element(W + "bottom")!.Attribute(W + "w")!.Value.Should().Be("0");
        }
        table.DefaultCellMargins.Should().BeNull("saving must not mutate the in-memory table");
        saved.Position = 0;
        AssertMargins(OnlyTable(DocxReader.Read(saved)).DefaultCellMargins, 0, 5.4, 0, 5.4);
    }

    private static Table OnlyTable(TextDocument document) => document.Blocks.OfType<Table>().Single();
    private static TextDocument Document(Table table)
    {
        var document = new TextDocument(); document.Blocks.Add(table); return document;
    }
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var saved = Save(document); return DocxReader.Read(saved);
    }
    private static MemoryStream Save(TextDocument document)
    {
        var output = new MemoryStream(); DocxWriter.Write(document, output); output.Position = 0; return output;
    }
    private static void AssertMargins(TableCellMargins? margins, double top, double left, double bottom, double right) =>
        margins.Should().Be(new TableCellMargins(top, left, bottom, right));

    private static string StyleReference(string? id) => id is null ? "" : $"<w:tblStyle w:val=\"{id}\"/>";
    private static string Style(string id, string margins, string? basedOn = null, bool isDefault = false) =>
        $"<w:style w:type=\"table\" w:styleId=\"{id}\"" + (isDefault ? " w:default=\"1\"" : "") + ">"
        + $"<w:name w:val=\"{id}\"/>" + (basedOn is null ? "" : $"<w:basedOn w:val=\"{basedOn}\"/>")
        + "<w:tblPr>" + margins + "</w:tblPr></w:style>";
    private static string Margins(int? top = null, int? left = null, int? bottom = null, int? right = null) =>
        "<w:tblCellMar>" + Edge("top", top) + Edge("left", left) + Edge("bottom", bottom) + Edge("right", right) + "</w:tblCellMar>";
    private static string Edge(string edge, int? value) => value is null ? "" : $"<w:{edge} w:w=\"{value}\" w:type=\"dxa\"/>";
    private static string Cell(string? margins = null, string? nested = null) => "<w:tc>"
        + (margins is null ? "" : "<w:tcPr>" + margins.Replace("tblCellMar", "tcMar", StringComparison.Ordinal) + "</w:tcPr>")
        + (nested ?? "") + "<w:p><w:r><w:t>Sample</w:t></w:r></w:p></w:tc>";
    private static string Row(string cell, string? secondCell = null, string? rowMargins = null) => "<w:tr>"
        + (rowMargins is null ? "" : "<w:tblPrEx>" + rowMargins + "</w:tblPrEx>") + cell + secondCell + "</w:tr>";
    private static string TableXml(string properties, string? rows = null) =>
        "<w:tbl><w:tblPr>" + properties + "</w:tblPr><w:tblGrid><w:gridCol w:w=\"2400\"/></w:tblGrid>"
        + (rows ?? Row(Cell())) + "</w:tbl>";

    private static TextDocument Read(string table, string? styles, int mode = 12, string? story = null)
    {
        const string r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        using var input = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string name, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(xml);
            }
            Part("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + (styles is null ? "" : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>")
                + (story is null ? "" : $"<Override PartName=\"/word/{story}1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.{story}+xml\"/>") + "</Types>");
            Part("_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"main\" Type=\"{r}/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Part("word/_rels/document.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + (styles is null ? "" : $"<Relationship Id=\"styles\" Type=\"{r}/styles\" Target=\"styles.xml\"/>")
                + $"<Relationship Id=\"settings\" Type=\"{r}/settings\" Target=\"settings.xml\"/>"
                + (story is null ? "" : $"<Relationship Id=\"story\" Type=\"{r}/{story}\" Target=\"{story}1.xml\"/>") + "</Relationships>");
            Part("word/document.xml", $"<w:document xmlns:w=\"{W}\" xmlns:r=\"{r}\"><w:body>"
                + (story is null ? table : "<w:p/>") + "<w:sectPr>"
                + (story is null ? "" : $"<w:{story}Reference w:type=\"default\" r:id=\"story\"/>") + "</w:sectPr></w:body></w:document>");
            Part("word/settings.xml", $"<w:settings xmlns:w=\"{W}\"><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"{mode}\"/></w:compat></w:settings>");
            if (styles is not null) Part("word/styles.xml", $"<w:styles xmlns:w=\"{W}\">{styles}</w:styles>");
            if (story is not null) Part($"word/{story}1.xml", $"<w:{(story == "header" ? "hdr" : "ftr")} xmlns:w=\"{W}\">{table}</w:{(story == "header" ? "hdr" : "ftr")}>");
        }
        input.Position = 0; return DocxReader.Read(input);
    }
}
