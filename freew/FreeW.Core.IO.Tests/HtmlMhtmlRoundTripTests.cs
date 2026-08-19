using System.Text;

namespace FreeW.Core.IO.Tests;

public class HtmlMhtmlRoundTripTests
{
    [Fact]
    public void Html_RoundTripsModelledStructuralSubset()
    {
        var document = BuildStructuralDocument();
        var loaded = RoundTrip(new HtmlFileAdapter(), document);

        loaded.Blocks.Should().HaveCount(5);
        loaded.Blocks[0].Should().BeOfType<Paragraph>().Which.StyleId.Should().Be("Heading1");
        loaded.Blocks[1].Should().BeOfType<Paragraph>().Which.Runs.Should().Contain(r => r.Formatting.Bold && r.Text == "bold");
        loaded.Blocks[1].Should().BeOfType<Paragraph>().Which.Runs.Should().Contain(r => r.Formatting.Italic && r.Text == "italic");
        loaded.Blocks[1].Should().BeOfType<Paragraph>().Which.Runs.Should().Contain(r => r.HyperlinkUrl == "https://example.test/");
        loaded.Blocks[2].Should().BeOfType<Paragraph>().Which.Formatting.ListKind.Should().Be(ListKind.Bullet);
        loaded.Blocks[3].Should().BeOfType<Paragraph>().Which.Formatting.ListKind.Should().Be(ListKind.Bullet);
        loaded.Blocks[4].Should().BeOfType<Table>().Which.Rows[0].Cells[0].PlainText.Should().Be("A1");
    }

    [Theory]
    [InlineData(false, "Web Page, Filtered", false)]
    [InlineData(true, "Web Page", true)]
    public void Html_SaveModesWriteDeterministicMarkupAndReloadSupportedSubset(
        bool fullWebPage,
        string formatName,
        bool expectsOfficeScaffolding)
    {
        var adapter = fullWebPage ? HtmlFileAdapter.WebPage() : HtmlFileAdapter.Filtered();
        var document = BuildStructuralDocument();

        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain("<!doctype html>");
        html.Should().Contain("<table>");
        html.Contains("mso-style-name", StringComparison.Ordinal).Should().Be(expectsOfficeScaffolding);
        adapter.FormatName.Should().Be(formatName);

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        loaded.Blocks.OfType<Paragraph>().Should().Contain(paragraph => paragraph.StyleId == "Heading1");
        loaded.Blocks.OfType<Table>().Should().ContainSingle().Which.Rows[0].Cells[1].PlainText.Should().Be("B1");
    }

    [Fact]
    public void Html_ReadsInlineStyleFormatting()
    {
        const string html = """
<!doctype html><html><body>
<p style="text-align: center"><span style="font-weight: 700; font-style: italic; text-decoration: underline line-through; color: rgb(1, 2, 3); font-size: 16px">Styled</span></p>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);
        var paragraph = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        paragraph.Formatting.Alignment.Should().Be(TextAlignment.Center);
        var run = paragraph.Runs.Should().ContainSingle().Which;
        run.Formatting.Bold.Should().BeTrue();
        run.Formatting.Italic.Should().BeTrue();
        run.Formatting.Underline.Should().BeTrue();
        run.Formatting.Strikethrough.Should().BeTrue();
        run.Formatting.ColorHex.Should().Be("#010203");
        run.Formatting.FontSizePt.Should().Be(12);
    }

    [Fact]
    public void Mhtml_RoundTripsEmbeddedImageThroughCid()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        var image = new InlineImage([0x89, 0x50, 0x4E, 0x47, 0x00], 24, 18, ImageFormat.Png)
        {
            AltText = "Tiny image"
        };
        paragraph.Runs.Add(new Run(string.Empty) { Image = image });
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        var adapter = new MhtmlFileAdapter();
        adapter.Save(document, stream);
        var mhtml = Encoding.UTF8.GetString(stream.ToArray());
        mhtml.Should().Contain("cid:image1@freew.local");

        stream.Position = 0;
        var loaded = adapter.Load(stream);

        var loadedImage = loaded.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<Paragraph>().Which.Runs.Should().ContainSingle().Which.Image;
        loadedImage.Should().NotBeNull();
        loadedImage!.Bytes.Should().Equal(image.Bytes);
        loadedImage.AltText.Should().Be("Tiny image");
    }

    [Fact]
    public void Mhtml_SaveProducesMultipartArchiveAndReloadsTextAndEmbeddedResource()
    {
        var document = BuildStructuralDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage([0x89, 0x50, 0x4E, 0x47, 0x00], 18, 18, ImageFormat.Png)
        {
            AltText = "Evidence image",
        }));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        var adapter = new MhtmlFileAdapter();
        adapter.Save(document, stream);
        var mhtml = Encoding.UTF8.GetString(stream.ToArray());

        mhtml.Should().Contain("multipart/related");
        mhtml.Should().Contain("Content-Type: text/html");
        mhtml.Should().Contain("image1@freew.local");

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        loaded.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).Should().Contain("Title");
        loaded.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.Image?.AltText == "Evidence image")
            .Should().BeTrue();
    }

    [Fact]
    public void Mhtml_LoadResolvesEmbeddedImageByContentLocation()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 };
        var mhtml = $$"""
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="freew-boundary"; type="text/html"

            --freew-boundary
            Content-Type: text/html; charset=utf-8

            <!doctype html><html><body><p><img src="images/picture.png" alt="Located image"></p></body></html>
            --freew-boundary
            Content-Type: image/png
            Content-Transfer-Encoding: base64
            Content-Location: images/picture.png

            {{Convert.ToBase64String(imageBytes)}}
            --freew-boundary--
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(mhtml.ReplaceLineEndings("\r\n")));
        var loaded = new MhtmlFileAdapter().Load(stream);

        var loadedImage = loaded.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<Paragraph>().Which.Runs.Should().ContainSingle().Which.Image;
        loadedImage.Should().NotBeNull();
        loadedImage!.Bytes.Should().Equal(imageBytes);
        loadedImage.AltText.Should().Be("Located image");
    }

    [Fact]
    public void Mhtml_LoadClonesReusedImagePartBeforeApplyingPerUseMetadata()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 };
        var mhtml = $$"""
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="freew-boundary"; type="text/html"

            --freew-boundary
            Content-Type: text/html; charset=utf-8

            <!doctype html><html><body>
            <p><img src="cid:logo" alt="Small logo" width="24" height="18"><img src="cid:logo" alt="Large logo" width="48" height="36"></p>
            </body></html>
            --freew-boundary
            Content-Type: image/png
            Content-ID: <logo>
            Content-Transfer-Encoding: base64

            {{Convert.ToBase64String(imageBytes)}}
            --freew-boundary--
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(mhtml.ReplaceLineEndings("\r\n")));
        var loaded = new MhtmlFileAdapter().Load(stream);

        var images = loaded.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<Paragraph>().Which.Runs
            .Select(run => run.Image)
            .Where(image => image is not null)
            .Select(image => image!)
            .ToList();
        images.Should().HaveCount(2);
        images[0].Should().NotBeSameAs(images[1]);
        images[0].Bytes.Should().Equal(imageBytes);
        images[1].Bytes.Should().Equal(imageBytes);
        images[0].AltText.Should().Be("Small logo");
        images[0].WidthPt.Should().Be(18);
        images[0].HeightPt.Should().Be(13.5);
        images[1].AltText.Should().Be("Large logo");
        images[1].WidthPt.Should().Be(36);
        images[1].HeightPt.Should().Be(27);
    }

    [Theory]
    [InlineData("filtered")]
    [InlineData("full")]
    [InlineData("mhtml")]
    public void HtmlAndMhtml_RoundTripFootnoteAndEndnoteSemantics(string format)
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        var paragraph = new Paragraph("Body ");
        paragraph.Runs.Add(Run.FootnoteReference(1, new RunFormatting
        {
            Bold = true,
            ColorHex = "#C00000",
            VerticalAlign = VerticalAlign.Superscript
        }));
        paragraph.Runs.Add(new Run(" and "));
        paragraph.Runs.Add(Run.EndnoteReference(2));
        document.Blocks.Add(paragraph);

        var footnote = new Footnote(1);
        footnote.Content.Add(new Paragraph
        {
            Runs =
            {
                new Run("Footnote link") { HyperlinkUrl = "https://example.test/footnote" },
                Run.FromImage(new InlineImage([0x89, 0x50, 0x4E, 0x47, 0x00], 12, 9, ImageFormat.Png)
                {
                    AltText = "Footnote image"
                })
            }
        });
        var secondFootnoteParagraph = new Paragraph();
        secondFootnoteParagraph.Runs.Add(new Run("Footnote second paragraph")
        {
            HyperlinkAnchor = "note-target",
            HyperlinkTooltip = "Jump within the document"
        });
        footnote.Content.Add(secondFootnoteParagraph);
        document.Footnotes[1] = footnote;
        document.Endnotes[2] = new Endnote(2, "Endnote text")
        {
            HasAutomaticReferenceMark = false
        };
        document.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;
        document.FootnoteNumbering.StartAt = 3;
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;
        document.EndnoteNumbering.NumberFormat = NoteNumberFormat.UpperLetter;
        document.EndnoteNumbering.StartAt = 2;

        IDocumentFileAdapter adapter = format switch
        {
            "full" => HtmlFileAdapter.WebPage(),
            "mhtml" => new MhtmlFileAdapter(),
            _ => HtmlFileAdapter.Filtered()
        };

        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        var serialized = Encoding.UTF8.GetString(stream.ToArray());
        serialized.Should().Contain("mso-element:footnote");
        serialized.Should().Contain("mso-element:endnote");
        serialized.Should().Contain("Footnote second paragraph");
        serialized.Should().Contain("mso-footnote-numbering-style:roman-lower");
        serialized.Should().Contain("mso-footnote-numbering-start:3");
        serialized.Should().Contain("mso-footnote-numbering-restart:each-section");
        serialized.Should().Contain("mso-endnote-numbering-style:alpha-upper");
        serialized.Should().Contain("mso-endnote-numbering-start:2");
        if (format != "mhtml")
        {
            serialized.Should().Contain("data-freew-note-id=\"1\"><sup>iii</sup>");
            serialized.Should().Contain("data-freew-note-id=\"2\"><sup>B</sup>");
        }

        stream.Position = 0;
        var loaded = adapter.Load(stream);

        loaded.Blocks.Should().ContainSingle();
        var body = loaded.Blocks[0].Should().BeOfType<Paragraph>().Which;
        body.Runs.Should().Contain(run => run.FootnoteId == 1
            && run.Formatting.VerticalAlign == VerticalAlign.Superscript
            && run.Formatting.Bold
            && run.Formatting.ColorHex == "#C00000");
        body.Runs.Should().Contain(run => run.EndnoteId == 2
            && run.Formatting.VerticalAlign == VerticalAlign.Superscript);
        loaded.Footnotes.Should().ContainKey(1);
        loaded.Footnotes[1].Content.Select(noteParagraph => noteParagraph.PlainText)
            .Should().Equal("Footnote link", "Footnote second paragraph");
        loaded.Footnotes[1].Content[0].Runs.Should().Contain(run =>
            run.HyperlinkUrl == "https://example.test/footnote");
        loaded.Footnotes[1].Content[0].Runs.Any(run =>
            run.Image is { AltText: "Footnote image" }).Should().BeTrue();
        loaded.Footnotes[1].Content[1].Runs.Should().Contain(run =>
            run.HyperlinkAnchor == "note-target"
            && run.HyperlinkTooltip == "Jump within the document");
        loaded.Footnotes[1].HasAutomaticReferenceMark.Should().BeTrue();
        loaded.Endnotes.Should().ContainKey(2);
        loaded.Endnotes[2].PlainText.Should().Be("Endnote text");
        loaded.Endnotes[2].HasAutomaticReferenceMark.Should().BeFalse();
        loaded.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerRoman);
        loaded.FootnoteNumbering.StartAt.Should().Be(3);
        loaded.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachSection);
        loaded.EndnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.UpperLetter);
        loaded.EndnoteNumbering.StartAt.Should().Be(2);
    }

    [Fact]
    public void Html_LoadReadsWordFootnoteAndEndnoteMarkupWithoutLeakingStoresIntoBody()
    {
        const string html = """
<!doctype html><html><head><style>
@page { mso-footnote-numbering-style:roman-lower; mso-footnote-numbering-start:3;
        mso-endnote-numbering-style:alpha-upper; mso-endnote-numbering-start:2; }
</style></head><body>
<p>Body<a style="mso-footnote-id:ftn4" href="#_ftn4" name="_ftnref4"><span class="MsoFootnoteReference">iii</span></a> and <a style="mso-footnote-id:edn7" href="#_edn7" name="_ednref7"><span class="MsoEndnoteReference">B</span></a>.</p>
<div style="mso-element:footnote-list">
  <div style="mso-element:footnote" id="ftn4">
    <p class="MsoFootnoteText"><a class="MsoFootnoteReference" href="#_ftnref4" name="_ftn4"><span style="mso-special-character:footnote">iii</span></a>Word footnote.</p>
  </div>
</div>
<div style="mso-element:endnote-list">
  <div style="mso-element:endnote" id="edn7">
    <p class="MsoEndnoteText"><a class="MsoEndnoteReference" href="#_ednref7" name="_edn7"><span style="mso-special-character:endnote">B</span></a>Word endnote.</p>
  </div>
</div>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        loaded.Blocks.Should().ContainSingle();
        loaded.Blocks[0].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Bodyiii and B.");
        loaded.Blocks[0].Should().BeOfType<Paragraph>().Which.Runs.Should().Contain(run => run.FootnoteId == 4);
        loaded.Blocks[0].Should().BeOfType<Paragraph>().Which.Runs.Should().Contain(run => run.EndnoteId == 7);
        loaded.Footnotes[4].PlainText.Should().Be("Word footnote.");
        loaded.Endnotes[7].PlainText.Should().Be("Word endnote.");
        loaded.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerRoman);
        loaded.FootnoteNumbering.StartAt.Should().Be(3);
        loaded.EndnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.UpperLetter);
        loaded.EndnoteNumbering.StartAt.Should().Be(2);

        using var stream = new MemoryStream();
        HtmlFileAdapter.WebPage().Save(loaded, stream);
        var resaved = Encoding.UTF8.GetString(stream.ToArray());
        resaved.Should().Contain("mso-footnote-numbering-style:roman-lower");
        resaved.Should().Contain("data-freew-note-id=\"4\"><sup>iii</sup>");
        resaved.Should().Contain("mso-endnote-numbering-style:alpha-upper");
        resaved.Should().Contain("data-freew-note-id=\"7\"><sup>B</sup>");
    }

    [Fact]
    public void Html_LoadDoesNotTreatCoincidentalNoteNamedAnchorsAsNotes()
    {
        const string html = """
<!doctype html><html><body>
<p><a href="#_ftn5">Ordinary target</a> and <a href="#_ftnref5">ordinary return</a>.</p>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        var paragraph = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        paragraph.PlainText.Should().Be("Ordinary target and ordinary return.");
        paragraph.Runs.Should().NotContain(run => run.FootnoteId.HasValue || run.EndnoteId.HasValue);
        paragraph.Runs.Should().Contain(run => run.HyperlinkAnchor == "_ftn5");
        paragraph.Runs.Should().Contain(run => run.HyperlinkAnchor == "_ftnref5");
    }

    [Fact]
    public void Html_LoadPreservesCustomNoteMarkInsteadOfTreatingItAsAutomaticBacklink()
    {
        const string html = """
<!doctype html><html><body>
<p>Body<a style="mso-footnote-id:ftn4" href="#_ftn4" name="_ftnref4">*</a></p>
<div style="mso-element:footnote-list">
  <div style="mso-element:footnote" id="ftn4">
    <p><a class="MsoFootnoteReference" style="mso-footnote-id:ftn4" href="#_ftnref4" name="_ftn4">*</a>Custom-mark note.</p>
  </div>
</div>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        loaded.Footnotes[4].HasAutomaticReferenceMark.Should().BeFalse();
        loaded.Footnotes[4].PlainText.Should().Be("*Custom-mark note.");
        loaded.Footnotes[4].Content[0].Runs.Should().Contain(run =>
            run.Text == "*" && run.HyperlinkAnchor == "_ftnref4");

        using var stream = new MemoryStream();
        HtmlFileAdapter.Filtered().Save(loaded, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Contain("data-freew-note-id=\"4\"><sup>*</sup>");
    }

    [Fact]
    public void Html_RoundTripNoteOnlyDocumentDoesNotLeakStoreTextIntoBody()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Footnotes[1] = new Footnote(1, "Stored only in footnote");

        var loaded = RoundTrip(HtmlFileAdapter.Filtered(), document);

        loaded.Blocks.Should().BeEmpty();
        loaded.Footnotes[1].PlainText.Should().Be("Stored only in footnote");
    }

    [Fact]
    public void Html_FormatsVisibleNoteMarkersWithStartFormatAndExplicitPageRestart()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(Run.PageBreak());
        paragraph.Runs.Add(Run.FootnoteReference(8));
        document.Blocks.Add(paragraph);
        document.Footnotes[1] = new Footnote(1, "First");
        document.Footnotes[8] = new Footnote(8, "Second");
        document.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;
        document.FootnoteNumbering.StartAt = 3;
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;

        using var stream = new MemoryStream();
        HtmlFileAdapter.Filtered().Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain("data-freew-note-id=\"1\"><sup>iii</sup>");
        html.Should().Contain("data-freew-note-id=\"8\"><sup>iii</sup>");
        html.Should().Contain("mso-footnote-numbering-restart:each-page");
    }

    [Fact]
    public void Html_FormatsVisibleNoteMarkersWithSectionRestart()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        var firstSection = new Paragraph();
        firstSection.Runs.Add(Run.FootnoteReference(1));
        firstSection.SectionBreak = new Section(document.Page);
        var secondSection = new Paragraph();
        secondSection.Runs.Add(Run.FootnoteReference(8));
        document.Blocks.Add(firstSection);
        document.Blocks.Add(secondSection);
        document.Footnotes[1] = new Footnote(1, "First");
        document.Footnotes[8] = new Footnote(8, "Second");
        document.FootnoteNumbering.NumberFormat = NoteNumberFormat.UpperLetter;
        document.FootnoteNumbering.StartAt = 2;
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        using var stream = new MemoryStream();
        HtmlFileAdapter.Filtered().Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain("data-freew-note-id=\"1\"><sup>B</sup>");
        html.Should().Contain("data-freew-note-id=\"8\"><sup>B</sup>");
        html.Should().Contain("mso-footnote-numbering-restart:each-section");
    }

    [Fact]
    public void Html_SaveSerializesFullVerticalMergeRowspan()
    {
        var document = new TextDocument();
        var table = new Table();
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell("Merged") { VerticalMerge = VerticalMergeState.Restart },
                new TableCell("Top")
            }
        });
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell(string.Empty) { VerticalMerge = VerticalMergeState.Continue },
                new TableCell("Middle")
            }
        });
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell(string.Empty) { VerticalMerge = VerticalMergeState.Continue },
                new TableCell("Bottom")
            }
        });
        document.Blocks.Add(table);

        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain("rowspan=\"3\"");
        html.Should().NotContain("rowspan=\"2\"");
    }

    [Fact]
    public void Html_LoadReservesEveryColumnCoveredByRowspanColspan()
    {
        const string html = """
<!doctype html><html><body>
<table>
  <tr><td rowspan="2" colspan="2">Merged</td><td>Right</td></tr>
  <tr><td>After</td></tr>
</table>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        var table = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>().Which;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].GridSpan.Should().Be(2);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        table.Rows[1].Cells[1].PlainText.Should().Be("After");
    }

    [Fact]
    public void Html_LoadDoesNotPromoteNestedTableRowsToOuterTableRows()
    {
        const string html = """
<!doctype html><html><body>
<table>
  <tr>
    <td>Outer
      <table>
        <tr><td>Inner-A</td><td>Inner-B</td></tr>
      </table>
    </td>
    <td>Right</td>
  </tr>
  <tr><td>Bottom</td><td>Done</td></tr>
</table>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        var table = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>().Which;

        // The outer table must still have exactly one row per <tr>: a nested table's own row must not
        // splice in as a bogus extra outer row.
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Should().HaveCount(2);

        var outerCell = table.Rows[0].Cells[0];
        outerCell.PlainText.Should().Contain("Outer");

        // The nested table must survive as a real Table object in NestedTables -- not get read and then
        // discarded in favour of tab/newline-flattened text glued into the outer cell's own paragraphs.
        // (PlainText intentionally does NOT descend into NestedTables, so if this regresses back to the
        // flattening bug, the inner text would land in outerCell.PlainText instead of here.)
        var nestedTable = outerCell.NestedTables.Should().ContainSingle().Which;
        nestedTable.Rows.Should().HaveCount(1);
        nestedTable.Rows[0].Cells.Should().HaveCount(2);
        nestedTable.Rows[0].Cells[0].PlainText.Should().Be("Inner-A");
        nestedTable.Rows[0].Cells[1].PlainText.Should().Be("Inner-B");

        table.Rows[0].Cells[1].PlainText.Should().Be("Right");
        table.Rows[1].Cells[0].PlainText.Should().Be("Bottom");
    }

    [Fact]
    public void Html_LoadKeepsPlainCellsUnaffectedByNestedTableTracking()
    {
        // Sibling no-regression check for the ReadCellParagraphs/NestedTables threading change: a table
        // with no nesting at all -- including a cell whose only content is inline text (so the
        // paragraphs.Count == 0 fallback to raw TextContent still has to fire) -- must still load exactly
        // as before.
        const string html = """
<!doctype html><html><body>
<table>
  <tr><td>Plain</td><td>Second</td></tr>
</table>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        var table = loaded.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>().Which;
        table.Rows.Should().HaveCount(1);
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].NestedTables.Should().BeEmpty();
        table.Rows[0].Cells[0].PlainText.Should().Be("Plain");
        table.Rows[0].Cells[1].NestedTables.Should().BeEmpty();
        table.Rows[0].Cells[1].PlainText.Should().Be("Second");
    }

    [Fact]
    public void Html_LoadKeepsMixedInlineTableCellContentInOneParagraph()
    {
        const string html = """
<!doctype html><html><body>
<table>
  <tr><td>A <strong>B</strong> <em>C</em></td></tr>
</table>
</body></html>
""";

        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        var cell = loaded.Blocks.Should().ContainSingle().Which
            .Should().BeOfType<Table>().Which.Rows.Should().ContainSingle().Which.Cells.Should().ContainSingle().Which;
        var paragraph = cell.Paragraphs.Should().ContainSingle().Which;
        paragraph.PlainText.Should().Be("A B C");
        paragraph.Runs.Should().Contain(run => run.Text == "B" && run.Formatting.Bold);
        paragraph.Runs.Should().Contain(run => run.Text == "C" && run.Formatting.Italic);
    }

    [Fact]
    public void Catalog_RegistersHtmlAndMhtmlFormats()
    {
        var formats = DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats).ToList();
        // Two HTML save variants now share each extension: "Web Page" (full) and "Web Page, Filtered".
        formats.Where(f => f.Extension == ".html" && f.CanOpen && f.CanSave).Should().HaveCount(2);
        formats.Where(f => f.Extension == ".htm" && f.CanOpen && f.CanSave).Should().HaveCount(2);
        formats.Should().ContainSingle(f => f.Extension == ".mhtml" && f.CanOpen && f.CanSave);
        formats.Should().ContainSingle(f => f.Extension == ".mht" && f.CanOpen && f.CanSave);
    }

    private static TextDocument BuildStructuralDocument()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Title") { StyleId = "Heading1" });

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("A "));
        paragraph.Runs.Add(new Run("bold", new RunFormatting { Bold = true }));
        paragraph.Runs.Add(new Run(" and "));
        paragraph.Runs.Add(new Run("italic", new RunFormatting { Italic = true }));
        paragraph.Runs.Add(new Run(" link") { HyperlinkUrl = "https://example.test/" });
        document.Blocks.Add(paragraph);

        document.Blocks.Add(new Paragraph("First item") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });
        document.Blocks.Add(new Paragraph("Second item") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } });

        var table = new Table();
        var row = new TableRow();
        row.Cells.Add(new TableCell("A1"));
        row.Cells.Add(new TableCell("B1"));
        table.Rows.Add(row);
        document.Blocks.Add(table);
        return document;
    }

    /// <summary>
    /// r151 remediation. The reader fix that populates <c>TableCell.NestedTables</c> made the HTML
    /// SAVE path strictly worse until the writer was taught to match: the writer only ever emitted
    /// <c>cell.Paragraphs</c>, so once the nested table stopped being flattened into those
    /// paragraphs, Load-then-Save dropped its content entirely -- no text, no table, no warning.
    /// Before the reader change the same gesture at least kept the text.
    ///
    /// This asserts the two halves AGREE by round-tripping, which is the only shape of test that
    /// could have caught it: every assertion here passes on a reader-only change if you look at
    /// the loaded model alone.
    /// </summary>
    [Fact]
    public void Html_RoundTripsANestedTableInACellWithoutLosingIt()
    {
        const string html = """
<!doctype html><html><body>
<table>
  <tr>
    <td>Outer
      <table>
        <tr><td>Inner-A</td><td>Inner-B</td></tr>
      </table>
    </td>
    <td>Right</td>
  </tr>
</table>
</body></html>
""";

        var adapter = HtmlFileAdapter.Filtered();
        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        using var stream = new MemoryStream();
        adapter.Save(loaded, stream);
        var written = Encoding.UTF8.GetString(stream.ToArray());

        // The content must reach the file at all -- this is the half that regressed.
        written.Should().Contain("Inner-A");
        written.Should().Contain("Inner-B");

        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var outer = reloaded.Blocks.OfType<Table>().Should().ContainSingle().Which;
        outer.Rows.Should().HaveCount(1, "a nested table's row must not splice into the outer table");
        outer.Rows[0].Cells.Should().HaveCount(2);

        var nested = outer.Rows[0].Cells[0].NestedTables.Should().ContainSingle().Which;
        nested.Rows.Should().HaveCount(1);
        nested.Rows[0].Cells.Should().HaveCount(2);
        nested.Rows[0].Cells[0].PlainText.Should().Be("Inner-A");
        nested.Rows[0].Cells[1].PlainText.Should().Be("Inner-B");

        outer.Rows[0].Cells[0].PlainText.Should().Contain("Outer");
        outer.Rows[0].Cells[1].PlainText.Should().Be("Right");
    }

    /// <summary>
    /// Sibling no-regression: a cell holding exactly one paragraph and NO nested table must keep
    /// writing bare runs rather than gaining a paragraph wrapper, since the writer's single-
    /// paragraph shortcut now also tests NestedTables.Count.
    /// </summary>
    [Fact]
    public void Html_RoundTripsAPlainSingleParagraphCellUnchanged()
    {
        const string html = """
<!doctype html><html><body>
<table><tr><td>Solo</td><td>Second</td></tr></table>
</body></html>
""";

        var adapter = HtmlFileAdapter.Filtered();
        var loaded = HtmlFileAdapter.LoadHtml(html, static _ => null);

        using var stream = new MemoryStream();
        adapter.Save(loaded, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var table = reloaded.Blocks.OfType<Table>().Should().ContainSingle().Which;
        table.Rows.Should().HaveCount(1);
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].PlainText.Should().Be("Solo");
        table.Rows[0].Cells[0].NestedTables.Should().BeEmpty();
        table.Rows[0].Cells[1].PlainText.Should().Be("Second");
    }

    private static TextDocument RoundTrip(IDocumentFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
