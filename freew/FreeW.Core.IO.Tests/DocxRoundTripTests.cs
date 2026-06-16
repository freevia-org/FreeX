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
