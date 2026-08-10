using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for round133's fix: Word's classic Left/Center/Right header/footer building block
/// wraps its entire content in a single-row, three-cell w:tbl so the three pieces sit side-by-side. Before
/// the fix, <see cref="DocxReader"/> unconditionally flattened w:hdr/w:ftr content into a linear top-to-
/// bottom <see cref="Paragraph"/> list (see the Descendants(w:p) walk in ReadHeaderFooterPart), destroying
/// the side-by-side layout on read. The fix detects the "whole content is one w:tbl" pattern and preserves
/// it on <see cref="HeaderFooter.Table"/> instead, while still flattening the SAME paragraph instances into
/// <see cref="HeaderFooter.Paragraphs"/> for back-compat consumers.
/// </summary>
public class HeaderFooterLayoutTableRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    /// <summary>
    /// Writes <paramref name="document"/>, then appends a REAL (non-empty) paragraph as a direct sibling of
    /// the w:tbl inside <paramref name="headerPartName"/> (e.g. "word/header1.xml") — simulating a header
    /// whose table is NOT its sole content (a rarer but real Word pattern), and reads the mutated package
    /// back. Used to prove the table-preservation guard does not over-widen to mixed table+paragraph layouts.
    /// </summary>
    private static TextDocument RoundTripWithExtraParagraphAfterTable(TextDocument document, string headerPartName, string extraText)
    {
        using var writeStream = new MemoryStream();
        DocxWriter.Write(document, writeStream);
        writeStream.Position = 0;

        using var outStream = new MemoryStream();
        using (var srcZip = new ZipArchive(writeStream, ZipArchiveMode.Read))
        using (var dstZip = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in srcZip.Entries)
            {
                var newEntry = dstZip.CreateEntry(entry.FullName);
                using var src = entry.Open();
                using var dst = newEntry.Open();
                if (entry.FullName == headerPartName)
                {
                    var xdoc = XDocument.Load(src);
                    var tbl = xdoc.Root!.Element(W + "tbl")!;
                    var extraParagraph = new XElement(W + "p",
                        new XElement(W + "r", new XElement(W + "t", extraText)));
                    tbl.AddAfterSelf(extraParagraph);
                    xdoc.Save(dst);
                }
                else
                {
                    src.CopyTo(dst);
                }
            }
        }
        outStream.Position = 0;
        return DocxReader.Read(outStream);
    }

    private static Table ThreeCellLayoutTable(string left, string center, string right)
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0] = new TableCell(left);
        table.Rows[0].Cells[1] = new TableCell(center);
        table.Rows[0].Cells[2] = new TableCell(right);
        return table;
    }

    /// <summary>
    /// Populates <paramref name="hf"/> with a Left/Center/Right layout table, mirroring the contract every
    /// real producer of <see cref="HeaderFooter.Table"/> follows (see DocxReader.ReadHeaderFooterPart and
    /// PageBox.CloseHeaderFooterPane): the SAME cell-paragraph instances are flattened into
    /// <see cref="HeaderFooter.Paragraphs"/> too, so <see cref="HeaderFooter.IsEmpty"/> (which only inspects
    /// Paragraphs) reports correctly and the writer does not silently drop the part as empty.
    /// </summary>
    private static void SetLayoutTable(HeaderFooter hf, string left, string center, string right)
    {
        var table = ThreeCellLayoutTable(left, center, right);
        hf.Table = table;
        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                foreach (var paragraph in cell.Paragraphs)
                    hf.Paragraphs.Add(paragraph);
    }

    // ── The bug: a header whose sole content is a Left/Center/Right layout table must NOT flatten ──

    [Fact]
    public void HeaderWithSoleLayoutTable_PreservesTableStructure_NotFlattenedToLinearParagraphs()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Header = new HeaderFooter();
        SetLayoutTable(doc.Header, "Left", "Center", "Right");

        var read = RoundTrip(doc);

        read.Header.Should().NotBeNull();
        read.Header!.Table.Should().NotBeNull(
            "a header whose entire content is one w:tbl must preserve the table, not flatten it");
        read.Header.Table!.Rows.Should().HaveCount(1);
        read.Header.Table.Rows[0].Cells.Should().HaveCount(3,
            "the Left/Center/Right layout has three side-by-side cells, not three stacked paragraphs");
        read.Header.Table.Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("Left");
        read.Header.Table.Rows[0].Cells[1].Paragraphs.Single().PlainText.Should().Be("Center");
        read.Header.Table.Rows[0].Cells[2].Paragraphs.Single().PlainText.Should().Be("Right");
    }

    [Fact]
    public void FooterWithSoleLayoutTable_PreservesTableStructure()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Footer = new HeaderFooter();
        SetLayoutTable(doc.Footer, "Confidential", "Page 1", "2026");

        var read = RoundTrip(doc);

        read.Footer!.Table.Should().NotBeNull("footers use the same Left/Center/Right building block as headers");
        read.Footer.Table!.Rows[0].Cells.Should().HaveCount(3);
    }

    // ── Back-compat: the flattened Paragraphs view must still carry every cell's text ──

    [Fact]
    public void HeaderWithSoleLayoutTable_StillFlattensSameParagraphsForBackCompatConsumers()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Header = new HeaderFooter();
        SetLayoutTable(doc.Header, "Left", "Center", "Right");

        var read = RoundTrip(doc);

        // Every paragraph-based consumer (field resolution, spell check, plain-text extraction, ...) must
        // still see all three cells' text via the flattened Paragraphs list, even though Table is now set.
        read.Header!.Paragraphs.Select(p => p.PlainText).Should().BeEquivalentTo(["Left", "Center", "Right"]);
    }

    // ── Sibling / no-regression: a plain (non-table) header must round-trip exactly as before ──

    [Fact]
    public void HeaderWithPlainParagraphs_NoTable_RoundTripsAsFlatParagraphs()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Header = new HeaderFooter("Plain header text");

        var read = RoundTrip(doc);

        read.Header!.Table.Should().BeNull("a plain-paragraph header must not spuriously acquire a table");
        read.Header.PlainText.Should().Be("Plain header text");
    }

    // ── Guard must not over-widen: a table alongside REAL paragraph content stays flattened ──

    [Fact]
    public void HeaderWithTablePlusRealParagraphContent_DoesNotPreserveTable_StaysFlattened()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Header = new HeaderFooter();
        SetLayoutTable(doc.Header, "Left", "Center", "Right");

        // Simulate a header whose w:tbl is NOT its sole content — a real (non-empty) paragraph sits
        // alongside it, a rarer but real Word pattern. The guard must fall back to flattening rather than
        // over-widening to every header that merely happens to contain a table.
        var read = RoundTripWithExtraParagraphAfterTable(doc, "word/header1.xml", "Extra real text");

        read.Header!.Table.Should().BeNull(
            "the table is not the header's sole content, so the guard must fall back to flattening, not " +
            "over-widen to every header that happens to contain a table");
        read.Header.Paragraphs.Select(p => p.PlainText).Should().Contain("Extra real text");
        read.Header.Paragraphs.Select(p => p.PlainText).Should().Contain("Left");
    }
}
