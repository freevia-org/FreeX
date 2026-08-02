using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for Picture Effects (a:effectLst: shadow/glow/reflection/softEdge/bevel),
/// Recolor (grayscale/sepia/washout/black-and-white), and Color Temperature.
/// All fields survive DocxWriter → DocxReader with original bytes intact (non-destructive).
/// </summary>
public class ImageEffectsRoundTripTests
{
    private static readonly XNamespace A    = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic  = "http://schemas.openxmlformats.org/drawingml/2006/picture";

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

    private static InlineImage ReadBackImageWithoutEffectAlpha(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("word/document.xml")!;
            XDocument xml;
            using (var input = entry.Open())
                xml = XDocument.Load(input);

            xml.Descendants(A + "alpha").Remove();
            entry.Delete();

            var replacement = zip.CreateEntry("word/document.xml");
            using var output = replacement.Open();
            xml.Save(output);
        }

        stream.Position = 0;
        return DocxReader.Read(stream).Paragraphs.First().Runs.Single(r => r.Image is not null).Image!;
    }

    // ── Shadow ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShadowPreset1_RoundTrips_ViaOuterShdwInEffectLst()
    {
        var png  = MinimalPng();
        var image = new InlineImage(png, 100, 80) { ShadowPreset = 1 };
        var read  = ReadBackImage(DocumentWith(image));

        read.ShadowPreset.Should().Be(1);
        read.Bytes.Should().Equal(png); // non-destructive
    }

    [Fact]
    public void ShadowPreset4_BottomDirection_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ShadowPreset = 4 };
        var read  = ReadBackImage(DocumentWith(image));
        read.ShadowPreset.Should().Be(4);
    }

    [Fact]
    public void Shadow_Emits_EffectLstOuterShdw_InSpPr()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ShadowPreset = 2 };
        var xml   = WriteDocumentXml(DocumentWith(image));
        var effectLst = xml.Descendants(A + "effectLst").FirstOrDefault();
        effectLst.Should().NotBeNull("a:effectLst must be written inside pic:spPr");
        effectLst!.Element(A + "outerShdw").Should().NotBeNull("a:outerShdw must be a child of a:effectLst");
    }

    [Fact]
    public void ShadowPreset0_Omits_EffectLst_WhenNoOtherEffects()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ShadowPreset = 0 };
        var xml   = WriteDocumentXml(DocumentWith(image));
        xml.Descendants(A + "effectLst").Should().BeEmpty("effectLst must be omitted when HasEffects is false");
    }

    // ── Glow ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GlowSizePt_RoundTrips_ViaEffectLstGlow()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { GlowSizePt = 8, GlowColorHex = "4472C4" };
        var read  = ReadBackImage(DocumentWith(image));
        read.GlowSizePt.Should().BeApproximately(8.0, 0.1);
        read.GlowColorHex.Should().Be("4472C4");
    }

    [Fact]
    public void Glow_Emits_EffectLstGlowElement()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { GlowSizePt = 5 };
        var xml   = WriteDocumentXml(DocumentWith(image));
        xml.Descendants(A + "glow").Should().ContainSingle();
    }

    // ── Reflection ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReflectionPreset1_RoundTrips_ViaEffectLstReflection()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ReflectionPreset = 1 };
        var read  = ReadBackImage(DocumentWith(image));
        read.ReflectionPreset.Should().Be(1);
    }

    [Fact]
    public void ReflectionPreset4_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ReflectionPreset = 4 };
        var read  = ReadBackImage(DocumentWith(image));
        read.ReflectionPreset.Should().Be(4);
    }

    [Fact]
    public void ImportedEffects_PreserveExactDrawingMlPayload()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowBlurRad = 76200,
                ShadowDist = 63500,
                ShadowDir = 18900000,
                ShadowColorHex = "102030",
                ShadowAlpha = 55000,
                HasGlow = true,
                GlowRad = 63500,
                GlowColorHex = "5B9BD5",
                GlowAlpha = 60000,
                HasReflection = true,
                ReflectionBlurRad = 6350,
                ReflectionStartAlpha = 50000,
                ReflectionStartPosition = 0,
                ReflectionEndAlpha = 0,
                ReflectionEndPosition = 100000,
                ReflectionDist = 0,
                ReflectionDir = 5400000,
                ReflectionFadeDir = 5400000,
                ReflectionScaleX = 100000,
                ReflectionScaleY = -100000,
                ReflectionAlignment = "bl"
            }
        };

        var xml = WriteDocumentXml(DocumentWith(image));
        var effectLst = xml.Descendants(A + "effectLst").Single();
        effectLst.Element(A + "outerShdw")!.Attribute("blurRad")!.Value.Should().Be("76200");
        effectLst.Element(A + "outerShdw")!.Attribute("dir")!.Value.Should().Be("18900000");
        effectLst.Element(A + "glow")!.Attribute("rad")!.Value.Should().Be("63500");
        var reflection = effectLst.Element(A + "reflection")!;
        reflection.Attribute("stA")!.Value.Should().Be("50000");
        reflection.Attribute("endPos")!.Value.Should().Be("100000");
        reflection.Attribute("sy")!.Value.Should().Be("-100000");

        var read = ReadBackImage(DocumentWith(image));
        read.ImportedEffects.Should().NotBeNull();
        read.ImportedEffects!.ShadowBlurRad.Should().Be(76200);
        read.ImportedEffects.GlowRad.Should().Be(63500);
        read.ImportedEffects.ReflectionStartAlpha.Should().Be(50000);
        read.ImportedEffects.ReflectionScaleY.Should().Be(-100000);
    }

    [Fact]
    public void ImportedEffects_WithoutAlphaTransforms_DefaultToOpaque()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowAlpha = 25000,
                HasGlow = true,
                GlowAlpha = 25000,
            },
        };

        var read = ReadBackImageWithoutEffectAlpha(DocumentWith(image));

        read.ImportedEffects.Should().NotBeNull();
        read.ImportedEffects!.ShadowAlpha.Should().Be(100000);
        read.ImportedEffects.GlowAlpha.Should().Be(100000);
    }

    // ── Soft Edge ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SoftEdgePt_RoundTrips_ViaEffectLstSoftEdge()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { SoftEdgePt = 5.0 };
        var read  = ReadBackImage(DocumentWith(image));
        read.SoftEdgePt.Should().BeApproximately(5.0, 0.1);
    }

    // ── Bevel ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BevelPreset1_RoundTrips_ViaEffectLstInnerShdw()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { BevelPreset = 1 };
        var read  = ReadBackImage(DocumentWith(image));
        read.BevelPreset.Should().Be(1);
    }

    [Fact]
    public void BevelPreset3_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { BevelPreset = 3 };
        var read  = ReadBackImage(DocumentWith(image));
        read.BevelPreset.Should().Be(3);
    }

    // ── Recolor ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Grayscale_RoundTrips_ViaGrayscl()
    {
        var png   = MinimalPng();
        var image = new InlineImage(png, 100, 80) { RecolorMode = ImageRecolorMode.Grayscale };
        var read  = ReadBackImage(DocumentWith(image));

        read.RecolorMode.Should().Be(ImageRecolorMode.Grayscale);
        read.Bytes.Should().Equal(png); // non-destructive
    }

    [Fact]
    public void Grayscale_Emits_GrayscLElement()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RecolorMode = ImageRecolorMode.Grayscale };
        var xml   = WriteDocumentXml(DocumentWith(image));
        xml.Descendants(A + "grayscl").Should().ContainSingle("a:grayscl must appear in a:blip");
    }

    [Fact]
    public void Sepia_RoundTrips_ViaDuotone()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RecolorMode = ImageRecolorMode.Sepia };
        var read  = ReadBackImage(DocumentWith(image));
        read.RecolorMode.Should().Be(ImageRecolorMode.Sepia);
    }

    [Fact]
    public void Sepia_Emits_DuotoneWithBrownAndWhite()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RecolorMode = ImageRecolorMode.Sepia };
        var xml   = WriteDocumentXml(DocumentWith(image));
        var duotone = xml.Descendants(A + "duotone").FirstOrDefault();
        duotone.Should().NotBeNull("a:duotone must be present for sepia");
        var firstHex = duotone!.Elements(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
        firstHex.Should().Be("7B4012", "the first duotone colour must be the brown anchor");
    }

    [Fact]
    public void Washout_RoundTrips_ViaLumAndAlpha()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RecolorMode = ImageRecolorMode.Washout };
        var read  = ReadBackImage(DocumentWith(image));
        read.RecolorMode.Should().Be(ImageRecolorMode.Washout);
    }

    [Fact]
    public void BlackWhite_RoundTrips_ViaGrayscLAndContrast()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RecolorMode = ImageRecolorMode.BlackWhite };
        var read  = ReadBackImage(DocumentWith(image));
        read.RecolorMode.Should().Be(ImageRecolorMode.BlackWhite);
    }

    [Fact]
    public void RecolorNone_ProducesNoGrayscLOrDuotone()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RecolorMode = ImageRecolorMode.None };
        var xml   = WriteDocumentXml(DocumentWith(image));
        xml.Descendants(A + "grayscl").Should().BeEmpty();
        xml.Descendants(A + "duotone").Should().BeEmpty();
    }

    // ── Color Temperature ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ColorTemperature_Warm_RoundTrips_ViaFreeWExtAttr()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ColorTemperature = 60 };
        var read  = ReadBackImage(DocumentWith(image));
        read.ColorTemperature.Should().BeApproximately(60.0, 0.01);
    }

    [Fact]
    public void ColorTemperature_Cool_RoundTrips()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ColorTemperature = -60 };
        var read  = ReadBackImage(DocumentWith(image));
        read.ColorTemperature.Should().BeApproximately(-60.0, 0.01);
    }

    [Fact]
    public void ColorTemperature_Zero_NotEmitted()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ColorTemperature = 0 };
        var xml   = WriteDocumentXml(DocumentWith(image));
        var blip  = xml.Descendants(A + "blip").Single();
        blip.Attributes()
            .Where(a => a.Name.LocalName == "colorTemp")
            .Should().BeEmpty("colorTemp attr must be omitted when zero");
    }

    // ── Non-destructive invariant ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void OriginalBytes_ArePreserved_AcrossAllEffects()
    {
        var png = MinimalPng();
        var image = new InlineImage(png, 100, 80)
        {
            ShadowPreset     = 3,
            GlowSizePt       = 8,
            GlowColorHex     = "FF0000",
            ReflectionPreset = 2,
            SoftEdgePt       = 5,
            BevelPreset      = 2,
            RecolorMode      = ImageRecolorMode.Sepia,
            ColorTemperature = 30
        };
        // Only the first applicable fields actually write (sepia overrides colorTemp, etc.),
        // but the original bytes must never be touched.
        var read = ReadBackImage(DocumentWith(image));
        read.Bytes.Should().Equal(png, "original image bytes must be unchanged after any effect round-trip");
    }
}
