using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for Picture Format &gt; Adjust (Corrections/Color/Transparency) fields:
///   BrightnessPct  → a:blip/a:lum @bright
///   ContrastPct    → a:blip/a:lum @contrast
///   SaturationPct  → a:blip/a:satMod @val
///   TransparencyPct→ a:blip/a:alphaModFix @amt (opacity = 100 - transparencyPct)
/// All four survive DocxWriter → DocxReader with original bytes intact (non-destructive).
/// </summary>
public class ImageAdjustRoundTripTests
{
    private static readonly XNamespace A   = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

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

    // ── Brightness ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BrightnessPct_RoundTrips_ViaLumBrightAttribute()
    {
        var png = MinimalPng();
        var image = new InlineImage(png, 100, 80) { BrightnessPct = 30 };
        var read = ReadBackImage(DocumentWith(image));

        read.BrightnessPct.Should().BeApproximately(30, 0.01);
        // Bytes must be unchanged — non-destructive.
        read.Bytes.Should().Equal(png);
    }

    [Fact]
    public void BrightnessPct_Negative_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { BrightnessPct = -40 };
        var read = ReadBackImage(DocumentWith(image));
        read.BrightnessPct.Should().BeApproximately(-40, 0.01);
    }

    [Fact]
    public void Brightness_EmitsLumElementWithBrightAttribute()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { BrightnessPct = 20 };
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        var lum = blip.Element(A + "lum");
        lum.Should().NotBeNull("a:lum must be a direct child of a:blip");
        lum!.Attribute("bright")!.Value.Should().Be("20000"); // 20 × 1000
    }

    [Fact]
    public void NoBrightness_LumElement_Omitted_WhenContrastAlsoZero()
    {
        var image = new InlineImage(MinimalPng(), 100, 80); // neutral defaults
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        blip.Element(A + "lum").Should().BeNull("a:lum must be absent when both brightness and contrast are 0");
    }

    // ── Contrast ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ContrastPct_RoundTrips_ViaLumContrastAttribute()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ContrastPct = -25 };
        var read = ReadBackImage(DocumentWith(image));
        read.ContrastPct.Should().BeApproximately(-25, 0.01);
    }

    [Fact]
    public void Contrast_EmitsLumElementWithContrastAttribute()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ContrastPct = 15 };
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        var lum = blip.Element(A + "lum");
        lum.Should().NotBeNull();
        lum!.Attribute("contrast")!.Value.Should().Be("15000"); // 15 × 1000
    }

    // ── Saturation ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SaturationPct_RoundTrips_ViaSatModVal()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { SaturationPct = 0 };
        var read = ReadBackImage(DocumentWith(image));
        read.SaturationPct.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void Saturation200_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { SaturationPct = 200 };
        var read = ReadBackImage(DocumentWith(image));
        read.SaturationPct.Should().BeApproximately(200, 0.01);
    }

    [Fact]
    public void Saturation_EmitsSatModWithCorrectVal()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { SaturationPct = 50 };
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        var satMod = blip.Element(A + "satMod");
        satMod.Should().NotBeNull();
        satMod!.Attribute("val")!.Value.Should().Be("50000"); // 50 × 1000
    }

    [Fact]
    public void NeutralSaturation_SatMod_Omitted()
    {
        var image = new InlineImage(MinimalPng(), 100, 80); // SaturationPct = 100 (default)
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        blip.Element(A + "satMod").Should().BeNull("a:satMod must be absent when saturation is neutral (100%)");
    }

    // ── Transparency ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TransparencyPct_RoundTrips_ViaAlphaModFix()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { TransparencyPct = 50 };
        var read = ReadBackImage(DocumentWith(image));
        read.TransparencyPct.Should().BeApproximately(50, 0.01);
    }

    [Fact]
    public void Transparency75_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { TransparencyPct = 75 };
        var read = ReadBackImage(DocumentWith(image));
        read.TransparencyPct.Should().BeApproximately(75, 0.01);
    }

    [Fact]
    public void Transparency_EmitsAlphaModFixWithOpacityPermille()
    {
        // 50 % transparent → opacity = 50 % → amt = 50 000.
        var image = new InlineImage(MinimalPng(), 100, 80) { TransparencyPct = 50 };
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        var alphaFix = blip.Element(A + "alphaModFix");
        alphaFix.Should().NotBeNull();
        alphaFix!.Attribute("amt")!.Value.Should().Be("50000"); // (100-50) × 1000
    }

    [Fact]
    public void ZeroTransparency_AlphaModFix_Omitted()
    {
        var image = new InlineImage(MinimalPng(), 100, 80); // TransparencyPct = 0 (default)
        var xml = WriteDocumentXml(DocumentWith(image));
        var blip = xml.Descendants(A + "blip").Single();
        blip.Element(A + "alphaModFix").Should().BeNull("a:alphaModFix must be absent when transparency is 0");
    }

    // ── All four together ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllAdjustFields_RoundTrip_Together_WithOriginalBytesIntact()
    {
        var png = MinimalPng();
        var image = new InlineImage(png, 100, 80)
        {
            BrightnessPct   = 20,
            ContrastPct     = -10,
            SaturationPct   = 150,
            TransparencyPct = 25
        };
        var read = ReadBackImage(DocumentWith(image));

        read.BrightnessPct.Should().BeApproximately(20,   0.01);
        read.ContrastPct.Should().BeApproximately(-10,    0.01);
        read.SaturationPct.Should().BeApproximately(150,  0.01);
        read.TransparencyPct.Should().BeApproximately(25, 0.01);

        // Non-destructive: the original PNG bytes must be unchanged after a round-trip.
        read.Bytes.Should().Equal(png, "original bytes must survive the round-trip unchanged");
    }

    // ── Combined with existing fields ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AdjustFields_CoexistWith_RotationCropBorder()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            RotationAngle   = 45,
            CropLeft        = 0.1,
            BorderColorHex  = "FF0000",
            BorderWidthPt   = 1.0,
            BrightnessPct   = 15,
            SaturationPct   = 80,
            TransparencyPct = 10
        };
        var read = ReadBackImage(DocumentWith(image));

        read.RotationAngle.Should().BeApproximately(45, 0.01);
        read.CropLeft.Should().BeApproximately(0.1, 0.0001);
        read.BorderColorHex.Should().Be("FF0000");
        read.BrightnessPct.Should().BeApproximately(15, 0.01);
        read.SaturationPct.Should().BeApproximately(80, 0.01);
        read.TransparencyPct.Should().BeApproximately(10, 0.01);
    }
}
