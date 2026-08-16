using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// End-to-end write tests for the Word 2003 WordprocessingML writer
/// (<see cref="Wordml2003Writer"/> / <see cref="Wordml2003FileAdapter"/>). Each test writes a
/// <see cref="TextDocument"/> to 2003 WordML and then reads it back with
/// <see cref="Wordml2003Reader"/> to verify the round-trip.
/// </summary>
public class Wordml2003WriteTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Writes <paramref name="document"/> to 2003 WordML and reads it back.</summary>
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        Wordml2003Writer.Write(document, stream);
        stream.Position = 0;
        return Wordml2003Reader.Read(stream);
    }

    /// <summary>
    /// Writes <paramref name="document"/> and returns the raw UTF-8 XML string (for structural assertions).
    /// </summary>
    private static string WriteToString(TextDocument document)
    {
        using var stream = new MemoryStream();
        Wordml2003Writer.Write(document, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes <paramref name="document"/> and parses it back as an XDocument.</summary>
    private static XDocument WriteToXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        Wordml2003Writer.Write(document, stream);
        stream.Position = 0;
        return XDocument.Load(stream);
    }

    // -----------------------------------------------------------------------
    // Root / PI assertions
    // -----------------------------------------------------------------------

    [Fact]
    public void Write_ProducesWordDocumentRoot()
    {
        var document = TextDocument.CreateEmpty();
        var xdoc = WriteToXml(document);

        xdoc.Root.Should().NotBeNull();
        xdoc.Root!.Name.Should().Be(Wordml2003Reader.RootName);
    }

    [Fact]
    public void Write_IncludesMsoApplicationProcessingInstruction()
    {
        var document = TextDocument.CreateEmpty();
        var xml = WriteToString(document);

        xml.Should().Contain("mso-application");
        xml.Should().Contain("progid=\"Word.Document\"");
    }

    // -----------------------------------------------------------------------
    // Paragraph / run text
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesSingleParagraphText()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Hello, World!"));

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>()
            .Select(p => p.PlainText)
            .Should().Contain("Hello, World!");
    }

    [Fact]
    public void RoundTrip_PreservesMultipleParagraphs()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("First paragraph"));
        source.Blocks.Add(new Paragraph("Second paragraph"));
        source.Blocks.Add(new Paragraph("Third paragraph"));

        var result = RoundTrip(source);
        var texts = result.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToList();

        texts.Should().Contain("First paragraph");
        texts.Should().Contain("Second paragraph");
        texts.Should().Contain("Third paragraph");
    }

    // -----------------------------------------------------------------------
    // Run formatting
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesBoldRun()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("bold text", new RunFormatting { Bold = true }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        var run = result.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Text.Should().Be("bold text");
        run.Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PreservesItalicRun()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("italic text", new RunFormatting { Italic = true }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PreservesUnderlineRun()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("underlined", new RunFormatting { Underline = true }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.Underline.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PreservesFontSizePt()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("big", new RunFormatting { FontSizePt = 24 }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.FontSizePt.Should().Be(24);
    }

    [Fact]
    public void RoundTrip_PreservesColorHex()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("red", new RunFormatting { ColorHex = "#FF0000" }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.ColorHex.Should().Be("#FF0000");
    }

    [Fact]
    public void RoundTrip_PreservesCombinedRunFormatting()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("styled", new RunFormatting
        {
            Bold = true,
            Italic = true,
            Underline = true,
            FontSizePt = 14,
            ColorHex = "#0070C0",
        }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);
        var fmt = result.Blocks.OfType<Paragraph>().First().Runs.First().Formatting;

        fmt.Bold.Should().BeTrue();
        fmt.Italic.Should().BeTrue();
        fmt.Underline.Should().BeTrue();
        fmt.FontSizePt.Should().Be(14);
        fmt.ColorHex.Should().Be("#0070C0");
    }

    [Fact]
    public void RoundTrip_PreservesExternalHyperlinkAndScreenTip()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("FreeW")
        {
            HyperlinkUrl = "https://freew.dev/docs?q=wordml",
            HyperlinkTooltip = "Open the FreeW docs",
        });
        source.Blocks.Add(paragraph);

        var xml = WriteToXml(source);
        var hlink = xml.Descendants(Wordml2003Reader.W + "hlink").Should().ContainSingle().Subject;
        hlink.Attribute(Wordml2003Reader.W + "dest")!.Value.Should().Be("https://freew.dev/docs?q=wordml");
        hlink.Attribute(Wordml2003Reader.W + "tooltip")!.Value.Should().Be("Open the FreeW docs");

        var run = RoundTrip(source).Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.HyperlinkUrl.Should().Be("https://freew.dev/docs?q=wordml");
        run.HyperlinkAnchor.Should().BeNull();
        run.HyperlinkTooltip.Should().Be("Open the FreeW docs");
    }

    [Fact]
    public void RoundTrip_PreservesInternalHyperlinkAnchor()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Jump to section")
        {
            HyperlinkAnchor = "Summary",
            HyperlinkTooltip = "Go to the summary",
        });
        source.Blocks.Add(paragraph);

        var xml = WriteToXml(source);
        var hlink = xml.Descendants(Wordml2003Reader.W + "hlink").Should().ContainSingle().Subject;
        hlink.Attribute(Wordml2003Reader.W + "bookmark")!.Value.Should().Be("Summary");
        hlink.Attribute(Wordml2003Reader.W + "tooltip")!.Value.Should().Be("Go to the summary");

        var run = RoundTrip(source).Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.HyperlinkUrl.Should().BeNull();
        run.HyperlinkAnchor.Should().Be("Summary");
        run.HyperlinkTooltip.Should().Be("Go to the summary");
    }

    // -----------------------------------------------------------------------
    // Bookmark markers (internal-link targets)
    // -----------------------------------------------------------------------

    [Fact]
    public void Write_EmitsBookmarkMarkerAtTheInternalLinkTargetParagraph()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();

        var target = new Paragraph("Summary section");
        target.BookmarkNames.Add("Summary");
        source.Blocks.Add(target);

        var link = new Paragraph();
        link.Runs.Add(new Run("Jump to section") { HyperlinkAnchor = "Summary" });
        source.Blocks.Add(link);

        var xml = WriteToXml(source);

        // The hyperlink reference (w:hlink w:bookmark="Summary") is only half the story — without a
        // matching w:bookmarkStart/w:bookmarkEnd on the target paragraph the link has nowhere to land.
        var bookmarkStart = xml.Descendants(Wordml2003Reader.W + "bookmarkStart").Should().ContainSingle().Subject;
        bookmarkStart.Attribute(Wordml2003Reader.W + "name")!.Value.Should().Be("Summary");

        var bookmarkEnd = xml.Descendants(Wordml2003Reader.W + "bookmarkEnd").Should().ContainSingle().Subject;
        bookmarkEnd.Attribute(Wordml2003Reader.W + "id")!.Value
            .Should().Be(bookmarkStart.Attribute(Wordml2003Reader.W + "id")!.Value);

        // The marker must sit on the TARGET paragraph specifically, not merely exist somewhere.
        var markedParagraph = xml.Descendants(Wordml2003Reader.W + "p")
            .Single(p => p.Elements(Wordml2003Reader.W + "bookmarkStart").Any());
        markedParagraph.Descendants(Wordml2003Reader.W + "t")
            .Select(t => t.Value).Should().Contain("Summary section");
    }

    [Fact]
    public void Write_OmitsBookmarkMarkersWhenNoParagraphCarriesABookmark()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Plain paragraph, no bookmark"));
        source.Blocks.Add(new Paragraph("Another plain paragraph"));

        var xml = WriteToXml(source);

        xml.Descendants(Wordml2003Reader.W + "bookmarkStart").Should().BeEmpty();
        xml.Descendants(Wordml2003Reader.W + "bookmarkEnd").Should().BeEmpty();
    }

    [Fact]
    public void Write_GivesEachBookmarkMarkerAUniqueIdAcrossParagraphs()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();

        var first = new Paragraph("First target");
        first.BookmarkNames.Add("One");
        source.Blocks.Add(first);

        var second = new Paragraph("Second target");
        second.BookmarkNames.Add("Two");
        source.Blocks.Add(second);

        var xml = WriteToXml(source);

        var ids = xml.Descendants(Wordml2003Reader.W + "bookmarkStart")
            .Select(e => e.Attribute(Wordml2003Reader.W + "id")!.Value)
            .ToList();
        ids.Should().HaveCount(2);
        ids.Should().OnlyHaveUniqueItems();
    }

    // -----------------------------------------------------------------------
    // R18 — super/subscript round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesSuperscriptRun()
    {
        // R18: VerticalAlign.Superscript must survive WordML 2003 writer → reader round-trip.
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("sup", new RunFormatting { VerticalAlign = VerticalAlign.Superscript }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Runs.First()
            .Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
    }

    [Fact]
    public void RoundTrip_PreservesSubscriptRun()
    {
        // R18: VerticalAlign.Subscript must survive WordML 2003 writer → reader round-trip.
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("sub", new RunFormatting { VerticalAlign = VerticalAlign.Subscript }));
        source.Blocks.Add(p);

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Runs.First()
            .Formatting.VerticalAlign.Should().Be(VerticalAlign.Subscript);
    }

    // -----------------------------------------------------------------------
    // Paragraph formatting
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesAlignmentCenter()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("centered")
        {
            Formatting = new ParagraphFormatting { Alignment = TextAlignment.Center }
        });

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Formatting.Alignment
            .Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void RoundTrip_PreservesAlignmentRight()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("right") { Formatting = new ParagraphFormatting { Alignment = TextAlignment.Right } });

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Formatting.Alignment
            .Should().Be(TextAlignment.Right);
    }

    [Fact]
    public void RoundTrip_PreservesAlignmentJustify()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("justified") { Formatting = new ParagraphFormatting { Alignment = TextAlignment.Justify } });

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Formatting.Alignment
            .Should().Be(TextAlignment.Justify);
    }

    [Fact]
    public void RoundTrip_PreservesIndentLeftPt()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("indented") { Formatting = new ParagraphFormatting { IndentLeftPt = 36 } });

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Formatting.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void RoundTrip_PreservesFirstLineIndentPt()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("first-line") { Formatting = new ParagraphFormatting { FirstLineIndentPt = 18 } });

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Formatting.FirstLineIndentPt.Should().Be(18);
    }

    [Fact]
    public void RoundTrip_PreservesHangingIndent()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        // A hanging indent is a negative FirstLineIndentPt.
        source.Blocks.Add(new Paragraph("hanging") { Formatting = new ParagraphFormatting { FirstLineIndentPt = -18 } });

        var result = RoundTrip(source);

        result.Blocks.OfType<Paragraph>().First().Formatting.FirstLineIndentPt.Should().Be(-18);
    }

    // -----------------------------------------------------------------------
    // Table
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesSimpleTable()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();

        var table = new Table();
        var row = new TableRow();
        var cell1 = new TableCell();
        cell1.Paragraphs.Add(new Paragraph("Cell A1"));
        var cell2 = new TableCell();
        cell2.Paragraphs.Add(new Paragraph("Cell B1"));
        row.Cells.Add(cell1);
        row.Cells.Add(cell2);
        table.Rows.Add(row);

        var row2 = new TableRow();
        var cell3 = new TableCell();
        cell3.Paragraphs.Add(new Paragraph("Cell A2"));
        var cell4 = new TableCell();
        cell4.Paragraphs.Add(new Paragraph("Cell B2"));
        row2.Cells.Add(cell3);
        row2.Cells.Add(cell4);
        table.Rows.Add(row2);

        source.Blocks.Add(table);

        var result = RoundTrip(source);

        var resultTable = result.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        resultTable.Rows.Should().HaveCount(2);
        resultTable.Rows[0].Cells.Should().HaveCount(2);
        resultTable.Rows[0].Cells[0].Paragraphs[0].PlainText.Should().Be("Cell A1");
        resultTable.Rows[0].Cells[1].Paragraphs[0].PlainText.Should().Be("Cell B1");
        resultTable.Rows[1].Cells[0].Paragraphs[0].PlainText.Should().Be("Cell A2");
        resultTable.Rows[1].Cells[1].Paragraphs[0].PlainText.Should().Be("Cell B2");
    }

    [Fact]
    public void RoundTrip_TableWithBoldCellContent()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();

        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        var para = new Paragraph();
        para.Runs.Add(new Run("Bold header", new RunFormatting { Bold = true }));
        cell.Paragraphs.Add(para);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        source.Blocks.Add(table);

        var result = RoundTrip(source);

        var resultCell = result.Blocks.OfType<Table>().First().Rows[0].Cells[0];
        resultCell.Paragraphs[0].Runs.First().Formatting.Bold.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Page settings
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesPageSize()
    {
        var source = TextDocument.CreateEmpty();
        source.Page.WidthPt = 595;   // A4
        source.Page.HeightPt = 842;

        var result = RoundTrip(source);

        result.Page.WidthPt.Should().BeApproximately(595, 0.05);
        result.Page.HeightPt.Should().BeApproximately(842, 0.05);
    }

    [Fact]
    public void RoundTrip_PreservesMargins()
    {
        var source = TextDocument.CreateEmpty();
        source.Page.MarginLeftPt = 90;
        source.Page.MarginRightPt = 90;
        source.Page.MarginTopPt = 72;
        source.Page.MarginBottomPt = 72;

        var result = RoundTrip(source);

        result.Page.MarginLeftPt.Should().BeApproximately(90, 0.05);
        result.Page.MarginRightPt.Should().BeApproximately(90, 0.05);
        result.Page.MarginTopPt.Should().BeApproximately(72, 0.05);
        result.Page.MarginBottomPt.Should().BeApproximately(72, 0.05);
    }

    // -----------------------------------------------------------------------
    // Wordml2003FileAdapter factory
    // -----------------------------------------------------------------------

    [Fact]
    public void Adapter_CanOpenAndSave()
    {
        var adapter = Wordml2003FileAdapter.Wordml2003();
        var fmt = adapter.Formats.Should().ContainSingle().Subject;
        fmt.CanOpen.Should().BeTrue();
        fmt.CanSave.Should().BeTrue();
        fmt.Extension.Should().Be(".xml");
        fmt.FormatName.Should().Be("Word 2003 XML Document");
    }

    [Fact]
    public void Adapter_Save_ThenLoad_PreservesContent()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("Via adapter"));

        var adapter = Wordml2003FileAdapter.Wordml2003();
        using var ms = new MemoryStream();
        adapter.Save(source, ms);
        ms.Position = 0;
        var result = adapter.Load(ms);

        result.Blocks.OfType<Paragraph>().Select(p => p.PlainText).Should().Contain("Via adapter");
    }

    [Fact]
    public void Adapter_Save_OutputHasMsoApplicationPi()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("pi test"));

        var adapter = Wordml2003FileAdapter.Wordml2003();
        using var ms = new MemoryStream();
        adapter.Save(source, ms);
        var xml = Encoding.UTF8.GetString(ms.ToArray());

        xml.Should().Contain("mso-application");
        xml.Should().Contain("progid=\"Word.Document\"");
        xml.Should().Contain("w:wordDocument");
    }

    // -----------------------------------------------------------------------
    // Comprehensive round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_FullDocument_ContentSurvives()
    {
        // Build a document with: a plain paragraph, a formatted paragraph, a table.
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();

        // Plain paragraph
        source.Blocks.Add(new Paragraph("Introduction"));

        // Formatted paragraph
        var fmtPara = new Paragraph
        {
            Formatting = new ParagraphFormatting { Alignment = TextAlignment.Center }
        };
        fmtPara.Runs.Add(new Run("Title", new RunFormatting { Bold = true, FontSizePt = 18 }));
        source.Blocks.Add(fmtPara);

        // Table
        var table = new Table();
        var row = new TableRow();
        var cellA = new TableCell(); cellA.Paragraphs.Add(new Paragraph("Name"));
        var cellB = new TableCell(); cellB.Paragraphs.Add(new Paragraph("Value"));
        row.Cells.Add(cellA); row.Cells.Add(cellB);
        table.Rows.Add(row);
        source.Blocks.Add(table);

        var result = RoundTrip(source);

        // Paragraphs
        var paragraphs = result.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Select(p => p.PlainText).Should().Contain("Introduction");

        // Formatted paragraph
        var centered = paragraphs.FirstOrDefault(p => p.Formatting.Alignment == TextAlignment.Center);
        centered.Should().NotBeNull();
        centered!.Runs.Should().ContainSingle().Which.Formatting.Bold.Should().BeTrue();
        centered.Runs.First().Formatting.FontSizePt.Should().Be(18);

        // Table
        result.Blocks.OfType<Table>().Should().ContainSingle();
        var resultTable = result.Blocks.OfType<Table>().First();
        resultTable.Rows[0].Cells[0].Paragraphs[0].PlainText.Should().Be("Name");
        resultTable.Rows[0].Cells[1].Paragraphs[0].PlainText.Should().Be("Value");
    }
}
