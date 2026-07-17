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
    public void AuthoredBounds_SurviveFloatingWordArtRoundTrip()
    {
        var wordArt = new WordArt("FreeW", WordArtStyle.GlowBlue, 30)
        {
            WidthPt = 93,
            HeightPt = 48,
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront }
        };

        var read = RoundTrippedWordArt(DocumentWith(wordArt));

        read.WidthPt.Should().BeApproximately(93, 0.01);
        read.HeightPt.Should().BeApproximately(48, 0.01);
    }

    [Fact]
    public void FillBlue_EmitsSolidFillOnShapeProperties()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Solid", WordArtStyle.FillBlue)));

        // The wps namespace is declared on the document root and the WordArt is a wps:wsp text box.
        xml.Root!.Attribute(XNamespace.Xmlns + "wps")!.Value.Should().Be(Wps.NamespaceName);
        var spPr = xml.Descendants(Wps + "wsp").Single().Element(Wps + "spPr")!;
        spPr.Element(A + "solidFill").Should().NotBeNull();
        spPr.Element(A + "gradFill").Should().BeNull();
    }

    [Fact]
    public void GradientFill_EmitsGradFillOnShapeProperties()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Gradient", WordArtStyle.GradientFill)));

        var spPr = xml.Descendants(Wps + "spPr").Single();
        spPr.Element(A + "gradFill").Should().NotBeNull();
        spPr.Descendants(A + "gs").Should().HaveCount(2);
    }

    [Fact]
    public void Outline_EmitsLineOnShapeProperties()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Outline", WordArtStyle.Outline)));

        var spPr = xml.Descendants(Wps + "spPr").Single();
        spPr.Element(A + "ln").Should().NotBeNull();
        spPr.Element(A + "solidFill").Should().NotBeNull();
    }

    [Fact]
    public void Shadow_EmitsEffectListWithOuterShadow()
    {
        var xml = WriteDocumentXml(DocumentWith(WordArt.Create("Shadow", WordArtStyle.Shadow)));

        var spPr = xml.Descendants(Wps + "spPr").Single();
        spPr.Element(A + "effectLst")!.Element(A + "outerShdw").Should().NotBeNull();
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

    // ── W24: Extended WordArt styles round-trip ───────────────────────────────────────────────────

    [Theory]
    [InlineData(WordArtStyle.FillGold)]
    [InlineData(WordArtStyle.FillWhite)]
    [InlineData(WordArtStyle.GradFillMulti)]
    [InlineData(WordArtStyle.ChromeOne)]
    [InlineData(WordArtStyle.ChromeTwo)]
    [InlineData(WordArtStyle.ShadowOrange)]
    [InlineData(WordArtStyle.GlowBlue)]
    [InlineData(WordArtStyle.GlowGold)]
    [InlineData(WordArtStyle.Reflection)]
    [InlineData(WordArtStyle.Bevel)]
    [InlineData(WordArtStyle.PatternFill)]
    public void ExtendedWordArtStyle_SurvivesRoundTrip(WordArtStyle style)
    {
        var wordArt = new WordArt { Text = "Test", Style = style, FontSizePt = 36 };
        var read = RoundTrip(DocumentWith(wordArt));

        var wa = read.Paragraphs.Single().Runs.Single(r => r.WordArt is not null).WordArt!;
        wa.Style.Should().Be(style, $"style {style} must survive round-trip");
        wa.Text.Should().Be("Test");
    }

    // ── W24: WordArt Warp round-trip ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(WordArtWarp.ArchUp)]
    [InlineData(WordArtWarp.ArchDown)]
    [InlineData(WordArtWarp.Circle)]
    [InlineData(WordArtWarp.Wave1)]
    [InlineData(WordArtWarp.Wave2)]
    [InlineData(WordArtWarp.Inflate)]
    [InlineData(WordArtWarp.Deflate)]
    [InlineData(WordArtWarp.ChevronUp)]
    [InlineData(WordArtWarp.ChevronDown)]
    [InlineData(WordArtWarp.FadeRight)]
    [InlineData(WordArtWarp.FadeLeft)]
    [InlineData(WordArtWarp.SlantUp)]
    [InlineData(WordArtWarp.SlantDown)]
    public void WordArtWarp_SurvivesRoundTrip(WordArtWarp warp)
    {
        var wordArt = new WordArt { Text = "Warp", Style = WordArtStyle.GradientFill, FontSizePt = 36, Warp = warp };
        var read = RoundTrip(DocumentWith(wordArt));

        var wa = read.Paragraphs.Single().Runs.Single(r => r.WordArt is not null).WordArt!;
        wa.Warp.Should().Be(warp, $"warp {warp} must survive round-trip");
    }

    [Fact]
    public void WordArtWarp_None_ProducesNoPrstTxWarp()
    {
        var wordArt = new WordArt { Text = "Plain", Style = WordArtStyle.FillBlue, FontSizePt = 36, Warp = WordArtWarp.None };
        var xml = WriteDocumentXml(DocumentWith(wordArt));
        xml.Descendants(A + "prstTxWarp").Should().BeEmpty("Warp=None must not emit a:prstTxWarp");
    }

    [Fact]
    public void WordArtWarp_ArchUp_EmitsPrstTxWarpWithCorrectToken()
    {
        var wordArt = new WordArt { Text = "Arch", Style = WordArtStyle.FillBlue, FontSizePt = 36, Warp = WordArtWarp.ArchUp };
        var xml = WriteDocumentXml(DocumentWith(wordArt));
        var warpEl = xml.Descendants(A + "prstTxWarp").FirstOrDefault();
        warpEl.Should().NotBeNull("Warp=ArchUp must emit a:prstTxWarp");
        warpEl!.Attribute("prst")!.Value.Should().Be("textArchUp");
    }

    [Fact]
    public void WordArtWarp_None_DefaultValuePreservesExistingStyle()
    {
        // A round-tripped WordArt with no warp set should remain Warp.None
        var wordArt = new WordArt { Text = "NoWarp", Style = WordArtStyle.ShadowOrange, FontSizePt = 24 };
        var read = RoundTrip(DocumentWith(wordArt));
        read.Paragraphs.Single().Runs.Single(r => r.WordArt is not null).WordArt!.Warp.Should().Be(WordArtWarp.None);
    }

    /// <summary>A minimal valid 1×1 PNG, used to exercise the image path alongside WordArt.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
