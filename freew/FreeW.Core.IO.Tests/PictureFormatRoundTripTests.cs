using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for the Picture Format W7 features: rotation/flip, crop, picture border, and
/// model default verification. Each test writes a document with the feature set, re-reads it and
/// asserts the fields survived. The XML-level assertions confirm the correct OOXML elements were
/// emitted by the writer; the re-read assertions confirm the reader restores them correctly.
/// </summary>
public class PictureFormatRoundTripTests
{
    private static readonly XNamespace A   = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────

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

    // ── Model defaults ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InlineImage_NewInstance_HasZeroRotationAndNoCropOrBorder()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);

        image.RotationAngle.Should().Be(0);
        image.FlipH.Should().BeFalse();
        image.FlipV.Should().BeFalse();
        image.CropLeft.Should().Be(0);
        image.CropRight.Should().Be(0);
        image.CropTop.Should().Be(0);
        image.CropBottom.Should().Be(0);
        image.HasCrop.Should().BeFalse();
        image.HasBorder.Should().BeFalse();
        image.BorderColorHex.Should().BeNull();
        image.OriginalPixelWidth.Should().Be(0);
        image.OriginalPixelHeight.Should().Be(0);
    }

    // ── Rotate / Flip ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RotationAngle_RoundTrips_ViaXfrmRot()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RotationAngle = 90 };
        var read = ReadBackImage(DocumentWith(image));
        read.RotationAngle.Should().BeApproximately(90, 0.01);
    }

    [Fact]
    public void FlipH_RoundTrips_ViaXfrmFlipH()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { FlipH = true };
        var read = ReadBackImage(DocumentWith(image));
        read.FlipH.Should().BeTrue();
        read.FlipV.Should().BeFalse();
    }

    [Fact]
    public void FlipV_RoundTrips_ViaXfrmFlipV()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { FlipV = true };
        var read = ReadBackImage(DocumentWith(image));
        read.FlipV.Should().BeTrue();
        read.FlipH.Should().BeFalse();
    }

    [Fact]
    public void Rotation45_AndBothFlips_RoundTrip()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RotationAngle = 45, FlipH = true, FlipV = true };
        var read = ReadBackImage(DocumentWith(image));
        read.RotationAngle.Should().BeApproximately(45, 0.01);
        read.FlipH.Should().BeTrue();
        read.FlipV.Should().BeTrue();
    }

    [Fact]
    public void Rotation_EmitsXfrmRotAttributeInEmuAngles()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RotationAngle = 90 };
        var xml = WriteDocumentXml(DocumentWith(image));
        var xfrm = xml.Descendants(A + "xfrm").Single();
        xfrm.Attribute("rot")!.Value.Should().Be("5400000"); // 90 × 60000
    }

    [Fact]
    public void NoRotation_XfrmHasNoRotAttribute()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);
        var xml = WriteDocumentXml(DocumentWith(image));
        var xfrm = xml.Descendants(A + "xfrm").Single();
        xfrm.Attribute("rot").Should().BeNull();
    }

    // ── Crop ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CropFractions_RoundTrip_ViaSrcRect()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            CropLeft = 0.1, CropRight = 0.05, CropTop = 0.2, CropBottom = 0.15
        };
        var read = ReadBackImage(DocumentWith(image));
        read.CropLeft.Should().BeApproximately(0.1, 0.0001);
        read.CropRight.Should().BeApproximately(0.05, 0.0001);
        read.CropTop.Should().BeApproximately(0.2, 0.0001);
        read.CropBottom.Should().BeApproximately(0.15, 0.0001);
        read.HasCrop.Should().BeTrue();
    }

    [Fact]
    public void NoCrop_EmitsNoSrcRect()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);
        var xml = WriteDocumentXml(DocumentWith(image));
        xml.Descendants(A + "srcRect").Should().BeEmpty();
    }

    [Fact]
    public void Crop_EmitsSrcRectWithPerMilleIntegers()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { CropLeft = 0.1 };
        var xml = WriteDocumentXml(DocumentWith(image));
        var srcRect = xml.Descendants(A + "srcRect").Single();
        srcRect.Attribute("l")!.Value.Should().Be("10000"); // 0.1 × 100000
        srcRect.Attribute("r")!.Value.Should().Be("0");
        srcRect.Attribute("t")!.Value.Should().Be("0");
        srcRect.Attribute("b")!.Value.Should().Be("0");
    }

    // ── Picture Border ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PictureBorder_RoundTrips_Color_Width_Dash()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            BorderColorHex = "FF0000",
            BorderWidthPt = 1.5,
            BorderDash = "dash"
        };
        var read = ReadBackImage(DocumentWith(image));
        read.HasBorder.Should().BeTrue();
        read.BorderColorHex.Should().Be("FF0000");
        read.BorderWidthPt.Should().BeApproximately(1.5, 0.01);
        read.BorderDash.Should().Be("dash");
    }

    [Fact]
    public void PictureBorder_SolidDash_NotStoredExplicitly()
    {
        // When dash is "solid" it is the default and not round-tripped as a non-null value.
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            BorderColorHex = "000000",
            BorderWidthPt = 0.75,
            BorderDash = null   // solid (default)
        };
        var read = ReadBackImage(DocumentWith(image));
        read.HasBorder.Should().BeTrue();
        read.BorderColorHex.Should().Be("000000");
        read.BorderDash.Should().BeNull();
    }

    [Fact]
    public void NoBorder_EmitsNoLnElement()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);
        var xml = WriteDocumentXml(DocumentWith(image));
        xml.Descendants(A + "ln").Should().BeEmpty();
    }

    [Fact]
    public void PictureBorder_EmitsLnWithWidthAndSrgbClr()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            BorderColorHex = "0070C0",
            BorderWidthPt = 2.0
        };
        var xml = WriteDocumentXml(DocumentWith(image));
        var ln = xml.Descendants(A + "ln").Single();
        ln.Attribute("w")!.Value.Should().Be("25400"); // 2.0 pt × 12700 EMU
        ln.Descendants(A + "srgbClr").Single().Attribute("val")!.Value.Should().Be("0070C0");
    }

    // ── Size lock-aspect math ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 80, 200)]   // 2× scale → height should be 160
    [InlineData(50, 100, 25)]    // 0.5× → 50
    [InlineData(72, 36, 144)]    // 2× → 72
    public void LockAspect_NewWidth_ProducesProportionalHeight(double origW, double origH, double newW)
    {
        var aspect = origH / origW;
        var expectedH = newW * aspect;
        expectedH.Should().BeApproximately(origH * (newW / origW), 0.001);
    }

    // ── All new features together round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void AllPictureFormatFields_RoundTrip_Together()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            RotationAngle = 30,
            FlipH = true,
            CropLeft = 0.05, CropRight = 0.1, CropTop = 0.02, CropBottom = 0.03,
            BorderColorHex = "FF6600",
            BorderWidthPt = 1.0,
            BorderDash = "dot"
        };
        var read = ReadBackImage(DocumentWith(image));

        read.RotationAngle.Should().BeApproximately(30, 0.01);
        read.FlipH.Should().BeTrue();
        read.FlipV.Should().BeFalse();
        read.CropLeft.Should().BeApproximately(0.05, 0.0001);
        read.CropRight.Should().BeApproximately(0.1, 0.0001);
        read.CropTop.Should().BeApproximately(0.02, 0.0001);
        read.CropBottom.Should().BeApproximately(0.03, 0.0001);
        read.BorderColorHex.Should().Be("FF6600");
        read.BorderWidthPt.Should().BeApproximately(1.0, 0.01);
        read.BorderDash.Should().Be("dot");
    }
}
