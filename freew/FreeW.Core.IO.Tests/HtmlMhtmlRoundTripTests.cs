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

    [Fact]
    public void Html_SaveDropsUnsupportedFootnoteStoreByDesign()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph("Body");
        paragraph.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(paragraph);
        document.Footnotes[1] = new Footnote(1, "Footnote text");

        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain("Body");
        html.Should().NotContain("Footnote text");
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
        <tr><td>Inner</td></tr>
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
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].PlainText.Should().Contain("Outer");
        table.Rows[0].Cells[0].PlainText.Should().Contain("Inner");
        table.Rows[0].Cells[1].PlainText.Should().Be("Right");
        table.Rows[1].Cells[0].PlainText.Should().Be("Bottom");
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

    private static TextDocument RoundTrip(IDocumentFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
