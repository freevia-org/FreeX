using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline WordArt decorative text (roadmap item X2): a <see cref="Run.WordArt"/>
/// must survive write→read with its text, chosen style preset and font size, emit a valid inline
/// <c>w:drawing</c> wrapping a <c>wps:wsp</c> text box whose run <c>a:rPr</c> carries the preset's DrawingML
/// text effect (<c>a:solidFill</c>/<c>a:gradFill</c>/<c>a:ln</c>/<c>a:effectLst</c>), and not be mistaken for
/// a plain shape (which carries no such text effects).
/// </summary>
public class WordArtRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument DocumentWith(WordArt wordArt)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromWordArt(wordArt));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static WordArt RoundTrippedWordArt(TextDocument document) =>
        RoundTrip(document).Paragraphs.Single().Runs.Single(r => r.WordArt is not null).WordArt!;

    [Theory]
    [InlineData(WordArtStyle.FillBlue)]
    [InlineData(WordArtStyle.GradientFill)]
    [InlineData(WordArtStyle.Outline)]
    [InlineData(WordArtStyle.Shadow)]
    public void EachPreset_PreservesTextStyleAndSize(WordArtStyle style)
    {
        var read = RoundTrippedWordArt(DocumentWith(WordArt.Create("FreeW WordArt", style, fontSizePt: 44)));

        read.Text.Should().Be("FreeW WordArt");
        read.Style.Should().Be(style);
        read.FontSizePt.Should().Be(44);
    }

    [Fact]
    public void FillBlue_EmitsSolidFillOnRunProperties()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Solid", WordArtStyle.FillBlue)));

        // The wps namespace is declared on the document root and the WordArt is a wps:wsp text box.
        xml.Root!.Attribute(XNamespace.Xmlns + "wps")!.Value.Should().Be(Wps.NamespaceName);
        var rPr = xml.Descendants(Wps + "wsp").Single().Descendants(W + "rPr").Single();
        rPr.Element(A + "solidFill").Should().NotBeNull();
        rPr.Element(A + "gradFill").Should().BeNull();
    }

    [Fact]
    public void GradientFill_EmitsGradFillOnRunProperties()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Gradient", WordArtStyle.GradientFill)));

        var rPr = xml.Descendants(W + "rPr").Single();
        rPr.Element(A + "gradFill").Should().NotBeNull();
        rPr.Descendants(A + "gs").Should().HaveCount(2);
    }

    [Fact]
    public void Outline_EmitsLineOnRunProperties()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Outline", WordArtStyle.Outline)));

        var rPr = xml.Descendants(W + "rPr").Single();
        rPr.Element(A + "ln").Should().NotBeNull();
        rPr.Element(A + "solidFill").Should().NotBeNull();
    }

    [Fact]
    public void Shadow_EmitsEffectListWithOuterShadow()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Shadow", WordArtStyle.Shadow)));

        var rPr = xml.Descendants(W + "rPr").Single();
        rPr.Element(A + "effectLst")!.Element(A + "outerShdw").Should().NotBeNull();
    }

    [Fact]
    public void WordArt_SerialisesAsInlineWspTextBox()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Box", WordArtStyle.FillBlue)));

        var inline = xml.Descendants(Wp + "inline").Single();
        inline.Descendants(A + "graphicData").Single().Attribute("uri")!.Value.Should().Be(Wps.NamespaceName);
        var txbxContent = inline.Descendants(W + "txbxContent").Single();
        string.Concat(txbxContent.Descendants(W + "t").Select(t => t.Value)).Should().Be("Box");
    }

    [Fact]
    public void WordArt_RoundTripsInsideTableCell()
    {
        // WordArt is an inline run mark, so it must flow through table cells like any other run.
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.FromWordArt(WordArt.Create("Cell art", WordArtStyle.Outline)));
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc);

        var cellParagraph = ((Table)read.Blocks.Single()).Rows[0].Cells[0].Paragraphs.Single();
        var wordArt = cellParagraph.Runs.Single(r => r.WordArt is not null).WordArt!;
        wordArt.Text.Should().Be("Cell art");
        wordArt.Style.Should().Be(WordArtStyle.Outline);
    }

    [Fact]
    public void PlainShape_IsNotMisreadAsWordArt()
    {
        // A plain text-box shape carries no DrawingML text effects on its run a:rPr, so it must round-trip as
        // a Shape, never as WordArt.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(Shape.TextBoxWith("Just a box", widthPt: 200, heightPt: 80)));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var runs = read.Paragraphs.Single().Runs;
        runs.Count(r => r.Shape is not null).Should().Be(1);
        runs.Count(r => r.WordArt is not null).Should().Be(0);
    }

    [Fact]
    public void WordArtAndImage_CoexistWithUniqueDocPrIds()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x") { Image = new InlineImage(OnePixelPng(), 10, 10) });
        paragraph.Runs.Add(Run.FromWordArt(WordArt.Create("Art", WordArtStyle.GradientFill)));
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        var ids = xml.Descendants(Wp + "docPr").Select(d => d.Attribute("id")!.Value).ToList();
        ids.Should().OnlyHaveUniqueItems();

        var read = RoundTrip(doc);
        var runs = read.Paragraphs.Single().Runs;
        runs.Count(r => r.Image is not null).Should().Be(1);
        runs.Count(r => r.WordArt is not null).Should().Be(1);
    }

    [Fact]
    public void WordArt_WithAltText_SurvivesRoundTrip()
    {
        var wordArt = WordArt.Create("FreeW", WordArtStyle.Shadow);
        wordArt.AltText = "FreeW logo WordArt";

        var read = RoundTrip(DocumentWith(wordArt)).Paragraphs.Single()
            .Runs.Single(r => r.WordArt is not null).WordArt!;

        read.AltText.Should().Be("FreeW logo WordArt");
        read.Style.Should().Be(WordArtStyle.Shadow);
    }

    /// <summary>A minimal valid 1×1 PNG, used to exercise the image path alongside WordArt.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
