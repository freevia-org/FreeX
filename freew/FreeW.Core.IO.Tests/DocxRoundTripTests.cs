using System.IO;
using System.IO.Compression;

namespace FreeW.Core.IO.Tests;

public class DocxRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void Paragraphs_And_Text_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Hello world"));
        doc.Blocks.Add(new Paragraph("Second paragraph"));

        var result = RoundTrip(doc);

        result.Paragraphs.Select(p => p.PlainText).Should().Equal("Hello world", "Second paragraph");
    }

    [Fact]
    public void RunFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("styled", new RunFormatting
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontFamily = "Arial",
            FontSizePt = 14,
            ColorHex = "#C0504D"
        }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.Bold.Should().BeTrue();
        formatting.Italic.Should().BeTrue();
        formatting.Underline.Should().BeTrue();
        formatting.Strikethrough.Should().BeTrue();
        formatting.FontFamily.Should().Be("Arial");
        formatting.FontSizePt.Should().Be(14);
        formatting.ColorHex.Should().Be("#C0504D");
    }

    [Fact]
    public void Superscript_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("2", new RunFormatting { VerticalAlign = VerticalAlign.Superscript }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
    }

    [Fact]
    public void Subscript_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("2", new RunFormatting { VerticalAlign = VerticalAlign.Subscript }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.VerticalAlign.Should().Be(VerticalAlign.Subscript);
    }

    [Fact]
    public void SmallCaps_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("heading", new RunFormatting { SmallCaps = true }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.SmallCaps.Should().BeTrue();
        formatting.AllCaps.Should().BeFalse();
        formatting.VerticalAlign.Should().Be(VerticalAlign.Baseline);
    }

    [Fact]
    public void AllCaps_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("title", new RunFormatting { AllCaps = true }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.AllCaps.Should().BeTrue();
        formatting.SmallCaps.Should().BeFalse();
    }

    [Fact]
    public void RunHighlight_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("highlighted", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.HighlightColorHex.Should().Be("#FFFF00");
    }

    [Fact]
    public void RunForegroundColor_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("coloured", new RunFormatting { ColorHex = "#2F5496" }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.ColorHex.Should().Be("#2F5496");
        formatting.HighlightColorHex.Should().BeNull();
    }

    [Fact]
    public void ParagraphFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("p")
        {
            Formatting = ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Center,
                SpaceBeforePt = 12,
                IndentLeftPt = 36
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Alignment.Should().Be(TextAlignment.Center);
        formatting.SpaceBeforePt.Should().Be(12);
        formatting.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void ParagraphBorder_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bordered")
        {
            Formatting = ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder("#FF0000", 1.5)
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Border.Should().NotBeNull();
        formatting.Border!.ColorHex.Should().Be("#FF0000");
        formatting.Border.WidthPt.Should().BeApproximately(1.5, 0.001);
        formatting.ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void ParagraphShading_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("shaded")
        {
            Formatting = ParagraphFormatting.Default with { ShadingColorHex = "#FFFF00" }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ShadingColorHex.Should().Be("#FFFF00");
        formatting.Border.Should().BeNull();
    }

    [Fact]
    public void ParagraphWithoutBorderOrShading_HasNeither()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Border.Should().BeNull();
        formatting.ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void Styles_And_StyleReference_RoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Title") { StyleId = "Title" });

        var result = RoundTrip(doc);

        result.Styles.Should().ContainKey("Title");
        result.Styles["Title"].Run.Bold.Should().BeTrue();
        result.Paragraphs.First().StyleId.Should().Be("Title");
    }

    [Fact]
    public void Table_RoundTrips_RowsColumnsAndCellText()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Before table"));
        var table = new Table();
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 3; c++)
                row.Cells.Add(new TableCell($"r{r}c{c}"));
            table.Rows.Add(row);
        }
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("After table"));

        var result = RoundTrip(doc);

        result.Blocks.Should().HaveCount(3);
        result.Blocks[0].Should().BeOfType<Paragraph>();
        result.Blocks[2].Should().BeOfType<Paragraph>();

        var readTable = result.Blocks[1].Should().BeOfType<Table>().Subject;
        readTable.RowCount.Should().Be(2);
        readTable.ColumnCount.Should().Be(3);
        readTable.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("r0c0", "r0c1", "r0c2");
        readTable.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("r1c0", "r1c1", "r1c2");
    }

    [Fact]
    public void Table_BorderlessFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        table.Formatting = TableFormatting.Default with { Borders = false };
        table.Rows[0].Cells[0] = new TableCell("x");
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Formatting.Borders.Should().BeFalse();
        readTable.Rows[0].Cells[0].PlainText.Should().Be("x");
    }

    [Fact]
    public void InlineImage_RoundTrips()
    {
        var png = MinimalPng();
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("before "));
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 120, heightPt: 90)));
        paragraph.Runs.Add(new Run(" after"));
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        // Text runs survive on either side of the image run.
        runs.Select(r => r.Text).Should().Equal("before ", string.Empty, " after");

        var imageRun = runs.Single(r => r.Image is not null);
        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image.WidthPt.Should().BeApproximately(120, 0.01);
        imageRun.Image.HeightPt.Should().BeApproximately(90, 0.01);
    }

    [Fact]
    public void InlineImage_AddsPngContentTypeAndMediaPart()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), 50, 50)));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/media/image1.png").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        ctReader.ReadToEnd().Should().Contain("image/png");
    }

    // A 1x1 transparent PNG — valid bytes so decoders accept it, opaque to the writer (stored verbatim).
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    [Fact]
    public void CoreProperties_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Properties.Title = "My Title";
        doc.Properties.Author = "Ada Lovelace";
        doc.Properties.Subject = "Analytical Engine";
        doc.Properties.Keywords = "history; computing";
        doc.Properties.Comments = "First program";
        doc.Properties.LastModifiedBy = "Charles Babbage";
        doc.Properties.Created = new DateTimeOffset(1843, 10, 1, 9, 30, 0, TimeSpan.Zero);
        doc.Properties.Modified = new DateTimeOffset(1843, 10, 15, 14, 0, 0, TimeSpan.Zero);

        var properties = RoundTrip(doc).Properties;

        properties.Title.Should().Be("My Title");
        properties.Author.Should().Be("Ada Lovelace");
        properties.Subject.Should().Be("Analytical Engine");
        properties.Keywords.Should().Be("history; computing");
        properties.Comments.Should().Be("First program");
        properties.LastModifiedBy.Should().Be("Charles Babbage");
        properties.Created.Should().Be(new DateTimeOffset(1843, 10, 1, 9, 30, 0, TimeSpan.Zero));
        properties.Modified.Should().Be(new DateTimeOffset(1843, 10, 15, 14, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void CoreProperties_PackageHasCorePartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Properties.Title = "Has Core";

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("docProps/core.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/docProps/core.xml");
        contentTypes.Should().Contain("application/vnd.openxmlformats-package.core-properties+xml");

        using var relsReader = new StreamReader(zip.GetEntry("_rels/.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("docProps/core.xml");
        rels.Should().Contain("http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
    }

    [Fact]
    public void MissingCorePart_YieldsEmptyProperties()
    {
        // A package without docProps/core.xml (built by hand) must read back with empty properties.
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body><w:p><w:r><w:t>Hi</w:t></w:r></w:p></w:body></w:document>");
        }
        stream.Position = 0;

        var properties = DocxReader.Read(stream).Properties;

        properties.Title.Should().BeNull();
        properties.Author.Should().BeNull();
        properties.Created.Should().BeNull();
    }

    [Fact]
    public void Hyperlink_RoundTrips_WithUrlIntact()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("see "));
        paragraph.Runs.Add(new Run("the docs") { HyperlinkUrl = "https://example.com/docs" });
        paragraph.Runs.Add(new Run(" now"));
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        runs.Select(r => r.Text).Should().Equal("see ", "the docs", " now");
        runs[0].HyperlinkUrl.Should().BeNull();
        runs[1].HyperlinkUrl.Should().Be("https://example.com/docs");
        runs[2].HyperlinkUrl.Should().BeNull();
    }

    [Fact]
    public void Hyperlink_PreservesRunFormatting()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("bold link", new RunFormatting { Bold = true })
        {
            HyperlinkUrl = "https://example.com"
        });
        doc.Blocks.Add(paragraph);

        var run = RoundTrip(doc).Paragraphs.First().Runs.Single();

        run.Text.Should().Be("bold link");
        run.HyperlinkUrl.Should().Be("https://example.com");
        run.Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void Hyperlink_WritesExternalRelationship()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link") { HyperlinkUrl = "https://example.com/page" });
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("https://example.com/page");
        rels.Should().Contain("TargetMode=\"External\"");
        rels.Should().Contain("/hyperlink");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("hyperlink");
    }

    [Fact]
    public void Hyperlink_SharedUrl_UsesSingleRelationship()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("a") { HyperlinkUrl = "https://example.com" });
        paragraph.Runs.Add(new Run("plain"));
        paragraph.Runs.Add(new Run("b") { HyperlinkUrl = "https://example.com" });
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        runs.Where(r => r.HyperlinkUrl is not null)
            .Select(r => r.HyperlinkUrl)
            .Should().AllBe("https://example.com");
        runs.Single(r => r.Text == "plain").HyperlinkUrl.Should().BeNull();
    }

    [Fact]
    public void BulletList_RoundTrips_ListKindAndLevel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bullet item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 1 }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ListKind.Should().Be(ListKind.Bullet);
        formatting.ListLevel.Should().Be(1);
    }

    [Fact]
    public void NumberedList_RoundTrips_ListKindAndLevel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("numbered item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 2 }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ListKind.Should().Be(ListKind.Number);
        formatting.ListLevel.Should().Be(2);
    }

    [Fact]
    public void NonListParagraph_HasNoListKind()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        RoundTrip(doc).Paragraphs.First().Formatting.ListKind.Should().Be(ListKind.None);
    }

    [Fact]
    public void List_WritesNumberingPartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/numbering.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/numbering.xml");
        contentTypes.Should().Contain("application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("numbering.xml");
        rels.Should().Contain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("numPr");
    }

    [Fact]
    public void NoLists_OmitsNumberingPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/numbering.xml").Should().BeNull();
    }

    [Fact]
    public void Header_And_Footer_Text_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter("Confidential Report");
        doc.Footer = new HeaderFooter("Company Inc.");

        var result = RoundTrip(doc);

        result.Header.Should().NotBeNull();
        result.Header!.PlainText.Should().Be("Confidential Report");
        result.Footer.Should().NotBeNull();
        result.Footer!.PlainText.Should().Be("Company Inc.");
    }

    [Fact]
    public void Footer_PageNumberField_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        var footer = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Page "));
        paragraph.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(paragraph);
        doc.Footer = footer;

        var result = RoundTrip(doc);

        result.Footer.Should().NotBeNull();
        var runs = result.Footer!.Paragraphs.Single().Runs;
        runs[0].Text.Should().Be("Page ");
        runs[0].FieldKind.Should().Be(RunFieldKind.None);
        runs[1].FieldKind.Should().Be(RunFieldKind.PageNumber);
    }

    [Fact]
    public void HeaderFooter_RunFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        var header = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold header", new RunFormatting { Bold = true, FontSizePt = 14 }));
        header.Paragraphs.Add(paragraph);
        doc.Header = header;

        var formatting = RoundTrip(doc).Header!.Paragraphs.Single().Runs[0].Formatting;

        formatting.Bold.Should().BeTrue();
        formatting.FontSizePt.Should().Be(14);
    }

    [Fact]
    public void HeaderFooter_Package_HasPartsContentTypesAndRelationships()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter("Header text");
        var footer = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(paragraph);
        doc.Footer = footer;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/header1.xml").Should().NotBeNull();
        zip.GetEntry("word/footer1.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/header1.xml");
        contentTypes.Should().Contain("/word/footer1.xml");
        contentTypes.Should().Contain("wordprocessingml.header+xml");
        contentTypes.Should().Contain("wordprocessingml.footer+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("relationships/header");
        rels.Should().Contain("relationships/footer");
        rels.Should().Contain("header1.xml");
        rels.Should().Contain("footer1.xml");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().Contain("headerReference");
        documentXml.Should().Contain("footerReference");

        using var footerReader = new StreamReader(zip.GetEntry("word/footer1.xml")!.Open());
        var footerXml = footerReader.ReadToEnd();
        footerXml.Should().Contain("fldSimple");
        footerXml.Should().Contain(" PAGE ");
    }

    [Fact]
    public void EmptyHeaderFooter_DoesNotEmitParts()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter();  // no paragraphs => empty
        doc.Footer = null;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/header1.xml").Should().BeNull();
        zip.GetEntry("word/footer1.xml").Should().BeNull();
    }

    [Fact]
    public void NoHeaderFooter_RoundTripsAsNull()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));

        var result = RoundTrip(doc);

        result.Header.Should().BeNull();
        result.Footer.Should().BeNull();
    }

    [Fact]
    public void Read_NonWordZip_Throws()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("not-a-document.txt");
        stream.Position = 0;

        var read = () => DocxReader.Read(stream);
        read.Should().Throw<InvalidDataException>();
    }
}
