using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class OdtRoundTripTests
{
    private static TextDocument DocOf(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static byte[] Save(TextDocument document)
    {
        using var ms = new MemoryStream();
        OdtFileAdapter.Odt().Save(document, ms);
        return ms.ToArray();
    }

    private static byte[] Save(TextDocument document, OdtFileAdapter adapter)
    {
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    private static TextDocument Load(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return OdtFileAdapter.Odt().Load(ms);
    }

    private static string[] Lines(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToArray();

    [Fact]
    public void RoundTrip_PreservesParagraphText()
    {
        var reloaded = Load(Save(DocOf("First paragraph", "Second", "Third")));
        Lines(reloaded).Should().Contain("First paragraph");
        Lines(reloaded).Should().Contain("Second");
        Lines(reloaded).Should().Contain("Third");
    }

    [Fact]
    public void RoundTrip_PreservesHeading()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Title here") { StyleId = "Heading1" });

        var reloaded = Load(Save(document));
        reloaded.Blocks.OfType<Paragraph>().Should().Contain(p => p.StyleId == "Heading1" && p.PlainText == "Title here");
    }

    [Fact]
    public void RoundTrip_PreservesBoldItalic()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("strong", new RunFormatting { Bold = true, Italic = true }));
        document.Blocks.Add(p);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().SelectMany(x => x.Runs).Single(r => r.Text == "strong");
        run.Formatting.Bold.Should().BeTrue();
        run.Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PreservesDoubleStrikethroughDistinctFromOrdinaryStrikethrough()
    {
        XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("double", new RunFormatting { DoubleStrikethrough = true }));
        paragraph.Runs.Add(new Run("single", new RunFormatting { Strikethrough = true }));
        document.Blocks.Add(paragraph);

        var bytes = Save(document);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        using (var stream = archive.GetEntry("content.xml")!.Open())
        {
            var content = XDocument.Load(stream);
            var doubleProperties = content.Descendants(style + "text-properties")
                .Single(properties => (string?)properties.Attribute(style + "text-line-through-type") == "double");
            doubleProperties.Attribute(style + "text-line-through-style")!.Value.Should().Be("solid");
        }

        var reloadedRuns = Load(bytes).Blocks.OfType<Paragraph>().Single().Runs;
        reloadedRuns[0].Formatting.DoubleStrikethrough.Should().BeTrue();
        reloadedRuns[0].Formatting.Strikethrough.Should().BeFalse();
        reloadedRuns[1].Formatting.Strikethrough.Should().BeTrue();
        reloadedRuns[1].Formatting.DoubleStrikethrough.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_PreservesTable()
    {
        var table = new Table();
        var row = new TableRow();
        var c1 = new TableCell();
        c1.Paragraphs.Add(new Paragraph("A1"));
        var c2 = new TableCell();
        c2.Paragraphs.Add(new Paragraph("B1"));
        row.Cells.Add(c1);
        row.Cells.Add(c2);
        table.Rows.Add(row);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));
        var cells = reloaded.Blocks.OfType<Table>().Single().Rows.Single().Cells;
        cells.Should().HaveCount(2);
        cells[0].Paragraphs.Single().PlainText.Should().Be("A1");
        cells[1].Paragraphs.Single().PlainText.Should().Be("B1");
    }

    [Fact]
    public void RoundTrip_NestedTableInCellStaysNestedAndDoesNotLeakIntoOuterRows()
    {
        var inner = new Table();
        var innerRow = new TableRow();
        var innerA = new TableCell();
        innerA.Paragraphs.Add(new Paragraph("Inner-A"));
        var innerB = new TableCell();
        innerB.Paragraphs.Add(new Paragraph("Inner-B"));
        innerRow.Cells.Add(innerA);
        innerRow.Cells.Add(innerB);
        inner.Rows.Add(innerRow);

        var outer = new Table();
        var outerRow = new TableRow();
        var outerCell = new TableCell();
        outerCell.Paragraphs.Add(new Paragraph("Outer text"));
        outerCell.NestedTables.Add(inner);
        outerRow.Cells.Add(outerCell);
        outer.Rows.Add(outerRow);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(outer);

        var reloaded = Load(Save(document));
        var reloadedOuter = reloaded.Blocks.OfType<Table>().Single();

        // The outer table must still have exactly one row: a nested table's own row must not splice in
        // as a bogus extra outer row (the old reader used table.Descendants("table-row"), which walked
        // into the nested table too).
        reloadedOuter.Rows.Should().HaveCount(1);
        reloadedOuter.Rows[0].Cells.Should().HaveCount(1);
        reloadedOuter.Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("Outer text");

        var reloadedInner = reloadedOuter.Rows[0].Cells[0].NestedTables.Single();
        reloadedInner.Rows.Should().HaveCount(1);
        reloadedInner.Rows[0].Cells.Should().HaveCount(2);
        reloadedInner.Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("Inner-A");
        reloadedInner.Rows[0].Cells[1].Paragraphs.Single().PlainText.Should().Be("Inner-B");
    }

    [Fact]
    public void RoundTrip_PreservesMultiRowTableWithoutMergesOrNesting()
    {
        // Sibling no-regression check for the table-row/covered-table-cell rework: a plain multi-row,
        // multi-column table with no merges and no nesting must still round-trip cell-for-cell.
        var table = new Table();
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 2; c++)
            {
                var cell = new TableCell();
                cell.Paragraphs.Add(new Paragraph($"R{r}C{c}"));
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));
        var reloadedTable = reloaded.Blocks.OfType<Table>().Single();
        reloadedTable.Rows.Should().HaveCount(2);
        reloadedTable.Rows[0].Cells.Select(c => c.Paragraphs.Single().PlainText).Should().Equal("R0C0", "R0C1");
        reloadedTable.Rows[1].Cells.Select(c => c.Paragraphs.Single().PlainText).Should().Equal("R1C0", "R1C1");
    }

    [Fact]
    public void Save_VerticallyMergedCellEmitsNumberRowsSpannedAndCoveredTableCell()
    {
        // Direct check on the WRITER: a Restart/Continue pair must become table:number-rows-spanned on
        // the top cell and a table:covered-table-cell (not another table:table-cell) below it, matching
        // what a real ODF consumer (LibreOffice) expects for a vertical merge.
        var table = new Table();

        var row0 = new TableRow();
        var top = new TableCell();
        top.Paragraphs.Add(new Paragraph("Merged"));
        top.VerticalMerge = VerticalMergeState.Restart;
        row0.Cells.Add(top);
        table.Rows.Add(row0);

        var row1 = new TableRow();
        var continued = new TableCell();
        continued.VerticalMerge = VerticalMergeState.Continue;
        row1.Cells.Add(continued);
        table.Rows.Add(row1);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var bytes = Save(document);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("content.xml")!.Open();
        var content = XDocument.Load(stream);

        XNamespace tableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        var rows = content.Descendants(tableNs + "table-row").ToList();
        rows.Should().HaveCount(2);

        var topCellEl = rows[0].Elements(tableNs + "table-cell").Single();
        topCellEl.Attribute(tableNs + "number-rows-spanned")!.Value.Should().Be("2");

        rows[1].Elements(tableNs + "table-cell").Should().BeEmpty();
        rows[1].Elements(tableNs + "covered-table-cell").Should().HaveCount(1);
    }

    [Fact]
    public void RoundTrip_VerticalMergeKeepsLaterCellInCorrectColumn()
    {
        // The reader must materialise the covered-table-cell as a Continue cell (rather than silently
        // dropping it), otherwise "R1-Right" would shift left into column 0 on read-back.
        var table = new Table();

        var row0 = new TableRow();
        var topMerged = new TableCell();
        topMerged.Paragraphs.Add(new Paragraph("Merged"));
        topMerged.VerticalMerge = VerticalMergeState.Restart;
        var row0Right = new TableCell();
        row0Right.Paragraphs.Add(new Paragraph("R0-Right"));
        row0.Cells.Add(topMerged);
        row0.Cells.Add(row0Right);
        table.Rows.Add(row0);

        var row1 = new TableRow();
        var continued = new TableCell();
        continued.VerticalMerge = VerticalMergeState.Continue;
        var row1Right = new TableCell();
        row1Right.Paragraphs.Add(new Paragraph("R1-Right"));
        row1.Cells.Add(continued);
        row1.Cells.Add(row1Right);
        table.Rows.Add(row1);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));
        var reloadedTable = reloaded.Blocks.OfType<Table>().Single();

        reloadedTable.Rows.Should().HaveCount(2);
        reloadedTable.Rows[0].Cells.Should().HaveCount(2);
        reloadedTable.Rows[1].Cells.Should().HaveCount(2);

        reloadedTable.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        reloadedTable.Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("Merged");
        reloadedTable.Rows[0].Cells[1].Paragraphs.Single().PlainText.Should().Be("R0-Right");

        reloadedTable.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        reloadedTable.Rows[1].Cells[1].Paragraphs.Single().PlainText.Should().Be("R1-Right");
    }

    [Fact]
    public void RoundTrip_CombinedHorizontalAndVerticalMergeKeepsColumnsAligned()
    {
        // A cell spanning 2 columns AND 2 rows: row1 must get a Continue cell with GridSpan=2 (covering
        // both grid columns the restart cell claimed), so the normal cell in column 2 doesn't shift.
        var table = new Table();

        var row0 = new TableRow();
        var topMerged = new TableCell { GridSpan = 2 };
        topMerged.Paragraphs.Add(new Paragraph("Merged"));
        topMerged.VerticalMerge = VerticalMergeState.Restart;
        var row0Col2 = new TableCell();
        row0Col2.Paragraphs.Add(new Paragraph("R0-Col2"));
        row0.Cells.Add(topMerged);
        row0.Cells.Add(row0Col2);
        table.Rows.Add(row0);

        var row1 = new TableRow();
        var continued = new TableCell { GridSpan = 2, VerticalMerge = VerticalMergeState.Continue };
        var row1Col2 = new TableCell();
        row1Col2.Paragraphs.Add(new Paragraph("R1-Col2"));
        row1.Cells.Add(continued);
        row1.Cells.Add(row1Col2);
        table.Rows.Add(row1);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));
        var reloadedTable = reloaded.Blocks.OfType<Table>().Single();

        reloadedTable.Rows.Should().HaveCount(2);
        reloadedTable.Rows[1].Cells.Should().HaveCount(2);

        reloadedTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
        reloadedTable.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        reloadedTable.Rows[1].Cells[0].GridSpan.Should().Be(2);
        reloadedTable.Rows[1].Cells[1].Paragraphs.Single().PlainText.Should().Be("R1-Col2");
    }

    [Fact]
    public void Load_ExternalOdtCoveredTableCellForVerticalMergeMaterialisesContinueCellAtCorrectColumn()
    {
        // Simulates a .odt authored by a real ODF producer (e.g. LibreOffice): the vertically-merged top
        // cell carries table:number-rows-spanned, and the row below uses table:covered-table-cell (NOT a
        // second table:table-cell) at that column. The old reader only ever iterated table:table-cell
        // elements, so it silently dropped the covered placeholder and "R1-Right" landed in column 0.
        const string officeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        const string textNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        const string tableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

        var contentXml =
            $"""
            <office:document-content xmlns:office="{officeNs}" xmlns:text="{textNs}" xmlns:table="{tableNs}" office:version="1.3">
              <office:body>
                <office:text>
                  <table:table table:name="Table1">
                    <table:table-column/>
                    <table:table-column/>
                    <table:table-row>
                      <table:table-cell office:value-type="string" table:number-rows-spanned="2">
                        <text:p>Merged</text:p>
                      </table:table-cell>
                      <table:table-cell office:value-type="string">
                        <text:p>R0-Right</text:p>
                      </table:table-cell>
                    </table:table-row>
                    <table:table-row>
                      <table:covered-table-cell/>
                      <table:table-cell office:value-type="string">
                        <text:p>R1-Right</text:p>
                      </table:table-cell>
                    </table:table-row>
                  </table:table>
                </office:text>
              </office:body>
            </office:document-content>
            """;

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var es = mimeEntry.Open())
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(OdtFileAdapter.MimeType);
                es.Write(bytes, 0, bytes.Length);
            }
            var contentEntry = archive.CreateEntry("content.xml", CompressionLevel.Optimal);
            using (var es = contentEntry.Open())
            using (var writer = new StreamWriter(es))
                writer.Write(contentXml);
        }
        ms.Position = 0;

        var document = OdtFileAdapter.Odt().Load(ms);
        var table = document.Blocks.OfType<Table>().Single();

        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[1].Cells.Should().HaveCount(2);

        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        // The bug: without materialising the covered placeholder, this cell would be missing entirely
        // and "R1-Right" would end up at index 0 instead of index 1.
        table.Rows[1].Cells[1].Paragraphs.Single().PlainText.Should().Be("R1-Right");
    }

    [Fact]
    public void Mimetype_IsFirstEntry_StoredUncompressed_WithExactContent()
    {
        using var za = new ZipArchive(new MemoryStream(Save(DocOf("x"))), ZipArchiveMode.Read);

        za.Entries[0].FullName.Should().Be("mimetype");
        // Stored (uncompressed): the compressed size equals the raw size.
        za.Entries[0].CompressedLength.Should().Be(za.Entries[0].Length);

        using var reader = new StreamReader(za.Entries[0].Open());
        reader.ReadToEnd().Should().Be(OdtFileAdapter.MimeType);
    }

    [Fact]
    public void Save_ProducesPackageWithExpectedParts()
    {
        using var za = new ZipArchive(new MemoryStream(Save(DocOf("x"))), ZipArchiveMode.Read);
        var names = za.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain("content.xml");
        names.Should().Contain("styles.xml");
        names.Should().Contain("META-INF/manifest.xml");
    }

    [Fact]
    public void Ott_IsTemplateDescriptor()
    {
        var format = OdtFileAdapter.Ott().Formats.Single();
        format.Extension.Should().Be(".ott");
        format.OpensAsTemplate.Should().BeTrue();
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, ".odt", false)]
    [InlineData(true, ".ott", true)]
    public void Save_ProducesOdfPackageAndReloadsTextForDocumentAndTemplate(
        bool template,
        string extension,
        bool opensAsTemplate)
    {
        var adapter = template ? OdtFileAdapter.Ott() : OdtFileAdapter.Odt();
        var bytes = Save(DocOf("ODF evidence", "second"), adapter);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.Entries[0].FullName.Should().Be("mimetype");
        archive.Entries.Select(entry => entry.FullName).Should().Contain(new[]
        {
            "content.xml",
            "styles.xml",
            "META-INF/manifest.xml",
        });

        using var stream = new MemoryStream(bytes);
        Lines(adapter.Load(stream)).Should().Contain(new[] { "ODF evidence", "second" });
        adapter.Formats.Single().Should().Match<FileFormatDescriptor>(format =>
            format.Extension == extension &&
            format.OpensAsTemplate == opensAsTemplate &&
            format.CanOpen &&
            format.CanSave);
    }
}
