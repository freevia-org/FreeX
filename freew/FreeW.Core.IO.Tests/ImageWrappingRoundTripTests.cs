using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for floating images + text wrapping (roadmap item X3): a floating
/// <see cref="InlineImage"/> (square / tight / top-and-bottom / behind / in-front) must serialise as a
/// <c>wp:anchor</c> carrying the matching wrap element, position offsets and anchors, and read back with
/// those preserved. A plain inline image must keep serialising as <c>wp:inline</c> (never <c>wp:anchor</c>).
/// </summary>
public class ImageWrappingRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

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

    private static TextDocument DocumentWith(InlineImage image)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static InlineImage ReadBackImage(TextDocument document) =>
        RoundTrip(document).Paragraphs.First().Runs.Single(r => r.Image is not null).Image!;

    [Fact]
    public void SquareWrapped_WithOffset_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), widthPt: 100, heightPt: 80)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Margin,
            HorizontalOffsetPt = 36,
            VerticalAnchor = VerticalAnchor.Paragraph,
            VerticalOffsetPt = 18,
        };

        var read = ReadBackImage(DocumentWith(image));

        read.Wrapping.Should().Be(ImageWrapping.Square);
        read.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        read.HorizontalOffsetPt.Should().BeApproximately(36, 0.01);
        read.VerticalAnchor.Should().Be(VerticalAnchor.Paragraph);
        read.VerticalOffsetPt.Should().BeApproximately(18, 0.01);
        read.WidthPt.Should().BeApproximately(100, 0.01);
        read.HeightPt.Should().BeApproximately(80, 0.01);
    }

    [Fact]
    public void BehindText_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.Behind,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalOffsetPt = 200,
        };

        var read = ReadBackImage(DocumentWith(image));

        read.Wrapping.Should().Be(ImageWrapping.Behind);
        read.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        read.VerticalOffsetPt.Should().BeApproximately(200, 0.01);

        // Behind-text images serialise as wp:anchor with behindDoc="1" and a wp:wrapNone wrap element.
        var anchor = WriteDocumentXml(DocumentWith(image)).Descendants(Wp + "anchor").Single();
        anchor.Attribute("behindDoc")!.Value.Should().Be("1");
        anchor.Element(Wp + "wrapNone").Should().NotBeNull();
    }

    [Fact]
    public void InFront_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.InFront,
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalOffsetPt = 144,
        };

        var read = ReadBackImage(DocumentWith(image));

        read.Wrapping.Should().Be(ImageWrapping.InFront);
        read.HorizontalAnchor.Should().Be(HorizontalAnchor.Page);
        read.HorizontalOffsetPt.Should().BeApproximately(144, 0.01);

        // In-front images serialise as wp:anchor with behindDoc="0" and a wp:wrapNone wrap element.
        var anchor = WriteDocumentXml(DocumentWith(image)).Descendants(Wp + "anchor").Single();
        anchor.Attribute("behindDoc")!.Value.Should().Be("0");
        anchor.Element(Wp + "wrapNone").Should().NotBeNull();
    }

    [Fact]
    public void TopAndBottom_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 120, 90)
        {
            Wrapping = ImageWrapping.TopAndBottom,
        };

        var read = ReadBackImage(DocumentWith(image));

        read.Wrapping.Should().Be(ImageWrapping.TopAndBottom);

        WriteDocumentXml(DocumentWith(image))
            .Descendants(Wp + "anchor").Single()
            .Element(Wp + "wrapTopAndBottom").Should().NotBeNull();
    }

    [Fact]
    public void Tight_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 70, 70)
        {
            Wrapping = ImageWrapping.Tight,
        };

        var read = ReadBackImage(DocumentWith(image));

        read.Wrapping.Should().Be(ImageWrapping.Tight);

        // Tight wrap is emitted as a plain wp:wrapTight (no wrapPolygon — a deliberate simplification).
        var wrapTight = WriteDocumentXml(DocumentWith(image))
            .Descendants(Wp + "anchor").Single()
            .Element(Wp + "wrapTight");
        wrapTight.Should().NotBeNull();
        var polygon = wrapTight!.Element(Wp + "wrapPolygon");
        polygon.Should().NotBeNull("wp:wrapTight requires a wrapPolygon for Word to open the document without repair");
        polygon!.Elements(Wp + "lineTo").Should().HaveCount(4);
    }

    [Fact]
    public void InlineImage_StillSerialisesAsInline_NotAnchor()
    {
        // Regression: a plain image (default Wrapping=Inline) must keep emitting wp:inline, never wp:anchor.
        var doc = DocumentWith(new InlineImage(MinimalPng(), 50, 50));

        var xml = WriteDocumentXml(doc);
        xml.Descendants(Wp + "inline").Should().ContainSingle();
        xml.Descendants(Wp + "anchor").Should().BeEmpty();

        ReadBackImage(doc).Wrapping.Should().Be(ImageWrapping.Inline);
    }

    [Fact]
    public void InlineImage_PositionFieldsDefault_AfterRoundTrip()
    {
        var read = ReadBackImage(DocumentWith(new InlineImage(MinimalPng(), 50, 50)));

        read.Wrapping.Should().Be(ImageWrapping.Inline);
        read.IsFloating.Should().BeFalse();
        read.HorizontalOffsetPt.Should().Be(0);
        read.VerticalOffsetPt.Should().Be(0);
        read.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        read.VerticalAnchor.Should().Be(VerticalAnchor.Paragraph);
    }

    [Fact]
    public void AltText_SurvivesOnFloatingImage()
    {
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.Square,
            AltText = "Floating logo",
        };

        ReadBackImage(DocumentWith(image)).AltText.Should().Be("Floating logo");
    }

    // ── ZOrderIndex round-trip (Phase 1) ─────────────────────────────────────────────────────────

    [Fact]
    public void ZOrderIndex_WrittenAsRelativeHeight()
    {
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.Square,
            ZOrderIndex = 7,
        };
        var xml = WriteDocumentXml(DocumentWith(image));

        var relH = xml.Descendants(Wp + "anchor")
            .Single()
            .Attribute("relativeHeight")?.Value;
        relH.Should().Be("7");
    }

    [Fact]
    public void ZOrderIndex_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.InFront,
            ZOrderIndex = 12,
        };

        ReadBackImage(DocumentWith(image)).ZOrderIndex.Should().Be(12);
    }

    [Fact]
    public void ZOrderIndex_DefaultZero_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.Square,
            ZOrderIndex = 0,
        };

        ReadBackImage(DocumentWith(image)).ZOrderIndex.Should().Be(0);
    }

    [Fact]
    public void ZOrderIndex_InlineImage_WritesInlineElement_NotAnchor()
    {
        // Inline images must still serialize as wp:inline — ZOrderIndex is irrelevant there.
        var image = new InlineImage(MinimalPng(), 60, 60)
        {
            Wrapping = ImageWrapping.Inline,
            ZOrderIndex = 5, // set but must be ignored for inline
        };
        var xml = WriteDocumentXml(DocumentWith(image));

        xml.Descendants(Wp + "inline").Should().HaveCount(1, "inline image serialises as wp:inline");
        xml.Descendants(Wp + "anchor").Should().BeEmpty("inline image must not produce a wp:anchor");
    }

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
}
