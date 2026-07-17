using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// IO round-trip tests for the Table Styles gallery feature:
/// <list type="bullet">
///   <item><c>w:tblStyle w:val</c> is written in <c>w:tblPr</c> for any table with a <see cref="Table.TableStyleId"/>.</item>
///   <item>A corresponding <c>w:style w:type="table"</c> definition is written to <c>word/styles.xml</c>.</item>
///   <item><see cref="DocxReader"/> reads <c>w:tblStyle</c> back into <see cref="Table.TableStyleId"/>.</item>
///   <item>A table with no <see cref="Table.TableStyleId"/> is unaffected (no regression).</item>
/// </list>
/// </summary>
public class TableStyleRoundTripTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument DocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static XDocument StylesXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument MakeDocWithStyledTable(string styleId)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(3, 2);
        table.TableStyleId = styleId;
        table.Formatting = table.Formatting with { HeaderRow = true, BandedRows = true };
        doc.Blocks.Add(table);
        return doc;
    }

    // ── Tests ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Write_TableWithStyleId_EmitsTblStyleElement()
    {
        var doc = MakeDocWithStyledTable("GridTable1Light");

        var xml = DocumentXml(doc);
        var tblStyle = xml.Descendants(W + "tblStyle").FirstOrDefault();

        tblStyle.Should().NotBeNull("w:tblStyle must be written to w:tblPr when TableStyleId is set");
        tblStyle!.Attribute(W + "val")?.Value.Should().Be("GridTable1Light");
    }

    [Fact]
    public void Write_TableWithStyleId_EmitsTableStyleDefinitionInStylesXml()
    {
        var doc = MakeDocWithStyledTable("GridTable1Light");

        var styles = StylesXml(doc);
        var tableStyle = styles.Descendants(W + "style")
            .FirstOrDefault(e => e.Attribute(W + "styleId")?.Value == "GridTable1Light");

        tableStyle.Should().NotBeNull("styles.xml must contain a w:style for the referenced table style");
        tableStyle!.Attribute(W + "type")?.Value.Should().Be("table");
        tableStyle.Element(W + "name")?.Attribute(W + "val")?.Value.Should().Be("Grid Table 1 Light");
    }

    [Fact]
    public void RoundTrip_TableWithStyleId_PreservesTableStyleId()
    {
        var doc = MakeDocWithStyledTable("GridTable1Light");

        var rt = RoundTrip(doc);

        var table = rt.Blocks.OfType<Table>().First();
        table.TableStyleId.Should().Be("GridTable1Light",
            "the catalog style id must survive a write→read cycle");
    }

    [Fact]
    public void RoundTrip_StyledTable_PreservesWordVisibleWriterGeneratedFills()
    {
        var doc = MakeDocWithStyledTable("GridTable1Light");

        var table = RoundTrip(doc).Blocks.OfType<Table>().First();

        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == "#D9E2F3");
        table.Rows[1].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == "#F2F2F2");
    }

    [Fact]
    public void RoundTrip_TableWithNoStyleId_IsUnaffected()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(2, 2);
        // No TableStyleId set.
        table.Formatting = table.Formatting with { Borders = true };
        doc.Blocks.Add(table);

        var rt = RoundTrip(doc);

        var rtTable = rt.Blocks.OfType<Table>().First();
        rtTable.TableStyleId.Should().BeNull("a plain table without a named style must not acquire one");
        rtTable.Formatting.Borders.Should().BeTrue();
    }

    [Fact]
    public void Write_TableWithBorderedStyle_EmitsTblBordersInStyleDefinition()
    {
        var doc = MakeDocWithStyledTable("TableGrid");

        var styles = StylesXml(doc);
        var tableStyle = styles.Descendants(W + "style")
            .First(e => e.Attribute(W + "styleId")?.Value == "TableGrid");

        tableStyle.Descendants(W + "tblBorders").Should().NotBeEmpty(
            "a bordered style (TableGrid) must carry w:tblBorders in its definition");
    }

    [Fact]
    public void Write_TableWithBorderedStyle_StyleBordersInheritedOnRead()
    {
        var doc = MakeDocWithStyledTable("TableGrid");

        var rt = RoundTrip(doc);

        var table = rt.Blocks.OfType<Table>().First();
        // TableGrid is a bordered style; the reader should set Borders=true from the catalog entry.
        table.Formatting.Borders.Should().BeTrue(
            "a table using the TableGrid catalog style must have Borders=true after round-trip");
    }

    [Fact]
    public void Write_MultipleCatalogStyles_OnlyUsedStylesAppearInStylesXml()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var t1 = Table.Create(2, 2);
        t1.TableStyleId = "GridTable1Light";
        var t2 = Table.Create(2, 2);
        t2.TableStyleId = "ListTable1Light";
        doc.Blocks.Add(t1);
        doc.Blocks.Add(t2);

        var styles = StylesXml(doc);
        var tableStyles = styles.Descendants(W + "style")
            .Where(e => e.Attribute(W + "type")?.Value == "table")
            .Select(e => e.Attribute(W + "styleId")?.Value)
            .ToList();

        tableStyles.Should().Contain("GridTable1Light");
        tableStyles.Should().Contain("ListTable1Light");
        // Should not contain unreferenced styles.
        tableStyles.Should().NotContain("TableGrid", "unreferenced catalog styles must not be emitted");
    }

    [Fact]
    public void RoundTrip_TblStyleFirst_IsValidSchemaOrder()
    {
        // w:tblStyle must be the first child of w:tblPr (before w:tblpPr, w:tblW, etc.).
        var doc = MakeDocWithStyledTable("PlainTable1");

        var xml = DocumentXml(doc);
        var tblPr = xml.Descendants(W + "tblPr").First();
        var firstChild = tblPr.Elements().FirstOrDefault();

        firstChild?.Name.Should().Be(W + "tblStyle",
            "w:tblStyle must be the first element in w:tblPr per CT_TblPr schema order");
    }
}
