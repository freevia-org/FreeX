using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for <see cref="ImageArtisticEffect"/>:
/// - Native Word a14 picture-effect previews are imported without applying their effect twice.
/// - The lossless FreeW extension retains authored effects and baked-preview provenance.
/// - Original bytes are never modified (non-destructive).
/// - Every distinct effect value round-trips and is distinguishable from None.
/// </summary>
public class ArtisticEffectRoundTripTests
{
    private static readonly XNamespace A       = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace A14     = "http://schemas.microsoft.com/office/drawing/2010/main";
    private static readonly XNamespace Pic     = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace R       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace FreeWExt = XNamespace.Get("http://schemas.freew.app/2024/ext");

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

    private static TextDocument DocumentWith(InlineImage image)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(para);
        return doc;
    }

    private static InlineImage ReadBackImage(InlineImage image)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(DocumentWith(image), stream);
        stream.Position = 0;
        return DocxReader.Read(stream)
            .Paragraphs.First()
            .Runs.Single(r => r.Image is not null)
            .Image!;
    }

    private static XElement? ReadBlip(InlineImage image)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(DocumentWith(image), stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(A + "blip").FirstOrDefault();
    }

    // ── None: no attribute emitted ─────────────────────────────────────────────

    [Fact]
    public void None_WritesNo_ArtisticEffectAttribute()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ArtisticEffect = ImageArtisticEffect.None };
        var blip = ReadBlip(image);
        blip.Should().NotBeNull();
        blip!.Descendants(FreeWExt + "artisticEffect").Should().BeEmpty(
            "None effect must not emit the freew:artisticEffect extension");
        blip.Descendants(A14 + "imgEffect").Should().BeEmpty(
            "None effect must not emit a Word-visible artistic effect");
    }

    [Fact]
    public void None_RoundTrips_AsNone()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);
        var read = ReadBackImage(image);
        read.ArtisticEffect.Should().Be(ImageArtisticEffect.None);
    }

    // ── Non-destructive: bytes unchanged ───────────────────────────────────────

    [Fact]
    public void Blur_OriginalBytes_ArePreserved()
    {
        var png = MinimalPng();
        var image = new InlineImage(png, 100, 80) { ArtisticEffect = ImageArtisticEffect.Blur };
        var read = ReadBackImage(image);
        read.Bytes.Should().Equal(png, "original image bytes must never be mutated by artistic effects");
    }

    // ── All 14 non-None effects round-trip ─────────────────────────────────────

    [Theory]
    [InlineData(ImageArtisticEffect.Blur)]
    [InlineData(ImageArtisticEffect.GlowDiffused)]
    [InlineData(ImageArtisticEffect.GlowEdges)]
    [InlineData(ImageArtisticEffect.PencilGrayscale)]
    [InlineData(ImageArtisticEffect.PencilSketch)]
    [InlineData(ImageArtisticEffect.LineDrawing)]
    [InlineData(ImageArtisticEffect.Paintbrush)]
    [InlineData(ImageArtisticEffect.PaintStrokes)]
    [InlineData(ImageArtisticEffect.Photocopy)]
    [InlineData(ImageArtisticEffect.Posterize)]
    [InlineData(ImageArtisticEffect.Pastels)]
    [InlineData(ImageArtisticEffect.Watercolor)]
    [InlineData(ImageArtisticEffect.FilmGrain)]
    [InlineData(ImageArtisticEffect.Mosaic)]
    public void Effect_RoundTrips(ImageArtisticEffect effect)
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ArtisticEffect = effect };
        var read = ReadBackImage(image);
        read.ArtisticEffect.Should().Be(effect, $"{effect} must survive DocxWriter → DocxReader");
    }

    // ── Attribute value matches enum ordinal ───────────────────────────────────

    [Theory]
    [InlineData(ImageArtisticEffect.Blur)]
    [InlineData(ImageArtisticEffect.Mosaic)]
    public void Effect_WritesExpectedAttributeValue(ImageArtisticEffect effect)
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ArtisticEffect = effect };
        var blip = ReadBlip(image);
        blip.Should().NotBeNull();
        var extension = blip!.Descendants(FreeWExt + "artisticEffect").SingleOrDefault();
        extension.Should().NotBeNull("freew:artisticEffect extension must be present for non-None effects");
        extension!.Attribute("val")!.Value.Should().Be(((int)effect).ToString());
    }

    [Fact]
    public void AuthoredEffect_DoesNotClaimNativeOfficePreviewPayload()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            ArtisticEffect = ImageArtisticEffect.GlowDiffused,
        };

        var blip = ReadBlip(image)!;

        blip.Descendants(A14 + "imgEffect").Should().BeEmpty(
            "FreeW-authored bytes are the editable source, not Word's baked preview");
        image.RequiresArtisticEffectRendering.Should().BeTrue();
    }

    [Theory]
    [InlineData(ImageArtisticEffect.Blur, "artisticBlur")]
    [InlineData(ImageArtisticEffect.GlowDiffused, "artisticGlowDiffused")]
    [InlineData(ImageArtisticEffect.GlowEdges, "artisticGlowEdges")]
    [InlineData(ImageArtisticEffect.PencilGrayscale, "artisticPencilGrayscale")]
    [InlineData(ImageArtisticEffect.PencilSketch, "artisticPencilSketch")]
    [InlineData(ImageArtisticEffect.LineDrawing, "artisticLineDrawing")]
    [InlineData(ImageArtisticEffect.Paintbrush, "artisticPaintBrush")]
    [InlineData(ImageArtisticEffect.PaintStrokes, "artisticPaintStrokes")]
    [InlineData(ImageArtisticEffect.Photocopy, "artisticPhotocopy")]
    [InlineData(ImageArtisticEffect.Posterize, "artisticCutout")]
    [InlineData(ImageArtisticEffect.Pastels, "artisticPastelsSmooth")]
    [InlineData(ImageArtisticEffect.Watercolor, "artisticWatercolorSponge")]
    [InlineData(ImageArtisticEffect.FilmGrain, "artisticFilmGrain")]
    [InlineData(ImageArtisticEffect.Mosaic, "artisticMosiaicBubbles")]
    public void BakedEffect_WritesWordVisibleOfficePayload(ImageArtisticEffect effect, string elementName)
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            ArtisticEffect = effect,
            HasBakedArtisticEffectPreview = true,
        };
        var blip = ReadBlip(image)!;

        var officeExtension = blip.Elements(A + "extLst").Elements(A + "ext")
            .Single(extension => extension.Attribute("uri")?.Value == "{BEBA8EAE-BF5A-486C-A8C5-ECC9F3942E4B}");
        var imageLayer = officeExtension.Descendants(A14 + "imgLayer").Single();
        imageLayer.Attribute(R + "embed")!.Value.Should().Be(blip.Attribute(R + "embed")!.Value);
        imageLayer.Descendants(A14 + "imgEffect").Single().Elements().Single().Name
            .Should().Be(A14 + elementName);
    }

    [Theory]
    [InlineData(ImageArtisticEffect.Blur)]
    [InlineData(ImageArtisticEffect.GlowDiffused)]
    [InlineData(ImageArtisticEffect.GlowEdges)]
    [InlineData(ImageArtisticEffect.PencilGrayscale)]
    [InlineData(ImageArtisticEffect.PencilSketch)]
    [InlineData(ImageArtisticEffect.LineDrawing)]
    [InlineData(ImageArtisticEffect.Paintbrush)]
    [InlineData(ImageArtisticEffect.PaintStrokes)]
    [InlineData(ImageArtisticEffect.Photocopy)]
    [InlineData(ImageArtisticEffect.Posterize)]
    [InlineData(ImageArtisticEffect.Pastels)]
    [InlineData(ImageArtisticEffect.Watercolor)]
    [InlineData(ImageArtisticEffect.FilmGrain)]
    [InlineData(ImageArtisticEffect.Mosaic)]
    public void BakedEffect_ReadsFromWordVisibleOfficePayloadWithoutFreeWExtension(ImageArtisticEffect effect)
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            ArtisticEffect = effect,
            HasBakedArtisticEffectPreview = true,
        };
        using var stream = new MemoryStream();
        DocxWriter.Write(DocumentWith(image), stream);

        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("word/document.xml")!;
            XDocument documentXml;
            using (var input = entry.Open())
                documentXml = XDocument.Load(input);

            documentXml.Descendants(A + "ext")
                .Where(extension => extension.Attribute("uri")?.Value == "{FREEW-BLIP-EXT-2024}")
                .Remove();
            entry.Delete();
            using var output = zip.CreateEntry("word/document.xml").Open();
            documentXml.Save(output);
        }

        stream.Position = 0;
        var read = DocxReader.Read(stream).Paragraphs.First().Runs.Single().Image!;
        read.ArtisticEffect.Should().Be(effect);
        read.HasBakedArtisticEffectPreview.Should().BeTrue();
    }

    [Fact]
    public void BakedPreviewProvenance_RoundTripsThroughFreeWExtension()
    {
        var image = new InlineImage(MinimalPng(), 100, 80)
        {
            ArtisticEffect = ImageArtisticEffect.GlowDiffused,
            HasBakedArtisticEffectPreview = true,
        };

        var read = ReadBackImage(image);

        read.ArtisticEffect.Should().Be(ImageArtisticEffect.GlowDiffused);
        read.HasBakedArtisticEffectPreview.Should().BeTrue();
        read.RequiresArtisticEffectRendering.Should().BeFalse();
    }

    // ── HasArtisticEffect property ─────────────────────────────────────────────

    [Fact]
    public void HasArtisticEffect_IsTrue_WhenEffectIsNotNone()
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { ArtisticEffect = ImageArtisticEffect.Watercolor };
        image.HasArtisticEffect.Should().BeTrue();
    }

    [Fact]
    public void HasArtisticEffect_IsFalse_WhenEffectIsNone()
    {
        var image = new InlineImage(MinimalPng(), 100, 80);
        image.HasArtisticEffect.Should().BeFalse();
    }

    // ── Combined: existing adjustment + artistic effect ────────────────────────

    [Fact]
    public void ArtisticEffect_CombinesWithBrightnessAdjustment()
    {
        var png = MinimalPng();
        var image = new InlineImage(png, 100, 80)
        {
            BrightnessPct = 20,
            ArtisticEffect = ImageArtisticEffect.Posterize,
        };
        var read = ReadBackImage(image);
        read.BrightnessPct.Should().BeApproximately(20, 0.01);
        read.ArtisticEffect.Should().Be(ImageArtisticEffect.Posterize);
        read.Bytes.Should().Equal(png);
    }
}
