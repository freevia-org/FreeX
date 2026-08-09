using System.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Host.Tests;

/// <summary>
/// 18A — Unit tests for picture crop (a:srcRect) and colour effects (a:blip children):
/// model, round-trip I/O, SlideCloner, and compositor draw-op fields.
/// </summary>
public sealed class PictureCropEffectsTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.CropTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static byte[] Minimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static SlideShape MakePictureShape(ImagePart img, PictureFormat? fmt = null) => new()
    {
        Id          = 1,
        Name        = "Pic1",
        Kind        = SlideShapeKind.Picture,
        OffsetXEmu  = 914400,
        OffsetYEmu  = 457200,
        ExtentCxEmu = 2743200,
        ExtentCyEmu = 1828800,
        Picture     = img,
        PictureFormat = fmt,
    };

    // ── PictureFormat model ───────────────────────────────────────────────────────

    [Fact]
    public void PictureFormat_HasCrop_ReturnsTrueWhenAnyNonZero()
    {
        var fmt = new PictureFormat { CropLeft = 0.1 };
        fmt.HasCrop.Should().BeTrue();

        var noFmt = new PictureFormat();
        noFmt.HasCrop.Should().BeFalse();
    }

    [Fact]
    public void PictureFormat_HasColorEffect_ReturnsTrueForGrayscale()
    {
        var fmt = new PictureFormat { Grayscale = true };
        fmt.HasColorEffect.Should().BeTrue();
    }

    [Fact]
    public void PictureFormat_HasColorEffect_FalseByDefault()
    {
        new PictureFormat().HasColorEffect.Should().BeFalse();
    }

    // ── Round-trip: crop ──────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_CroppedPicture_CropFractionsPreserved()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        var fmt = new PictureFormat
        {
            CropLeft   = 0.125,  // 12500 per-mille
            CropTop    = 0.0,
            CropRight  = 0.125,
            CropBottom = 0.25,
        };

        var pres = new Presentation();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(MakePictureShape(img, fmt));

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes[0];
        shape.PictureFormat.Should().NotBeNull("crop must round-trip");
        shape.PictureFormat!.CropLeft  .Should().BeApproximately(0.125, 0.0001);
        shape.PictureFormat.CropTop    .Should().BeApproximately(0.0,   0.0001);
        shape.PictureFormat.CropRight  .Should().BeApproximately(0.125, 0.0001);
        shape.PictureFormat.CropBottom .Should().BeApproximately(0.25,  0.0001);
    }

    [Fact]
    public void RoundTrip_NoCrop_PictureFormatIsNull()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var pres = new Presentation();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(MakePictureShape(img));

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        // A plain picture with no srcRect or blip effects should have null PictureFormat
        reloaded.Slides[0].Shapes[0].PictureFormat.Should().BeNull(
            "no srcRect and no effects → PictureFormat should be null");
    }

    // ── Round-trip: colour effects ────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Grayscale_Preserved()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        var fmt = new PictureFormat { Grayscale = true };

        var pres = new Presentation();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(MakePictureShape(img, fmt));

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Shapes[0].PictureFormat!.Grayscale.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_BrightnessContrast_Preserved()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        var fmt = new PictureFormat { Brightness = 0.2, Contrast = -0.1 };

        var pres = new Presentation();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(MakePictureShape(img, fmt));

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var pf = reloaded.Slides[0].Shapes[0].PictureFormat;
        pf.Should().NotBeNull();
        pf!.Brightness.Should().BeApproximately(0.2,   0.0001);
        pf.Contrast   .Should().BeApproximately(-0.1,  0.0001);
    }

    [Fact]
    public void RoundTrip_BiLevelThreshold_Preserved()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        var fmt = new PictureFormat { BiLevelThreshold = 0.5 };

        var pres = new Presentation();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(MakePictureShape(img, fmt));

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var pf = reloaded.Slides[0].Shapes[0].PictureFormat;
        pf.Should().NotBeNull();
        pf!.BiLevelThreshold.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void RoundTrip_AlphaModPct_Preserved()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        var fmt = new PictureFormat { AlphaModPct = 0.75 };

        var pres = new Presentation();
        pres.Slides.Add(new Slide());
        pres.Slides[0].Shapes.Add(MakePictureShape(img, fmt));

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Shapes[0].PictureFormat!.AlphaModPct
            .Should().BeApproximately(0.75, 0.0001);
    }

    // ── SlideCloner ───────────────────────────────────────────────────────────────

    [Fact]
    public void SlideCloner_CroppedPicture_ClonesFormat()
    {
        var fmt = new PictureFormat
        {
            CropLeft   = 0.1,
            CropTop    = 0.2,
            CropRight  = 0.05,
            CropBottom = 0.15,
            Grayscale  = true,
            Brightness = 0.1,
            Contrast   = -0.05,
        };

        var slide = new Slide();
        slide.Shapes.Add(MakePictureShape(new ImagePart { Bytes = Minimal1x1Png() }, fmt));

        var clone = SlideCloner.CloneSlide(slide);

        var pf = clone.Shapes[0].PictureFormat;
        pf.Should().NotBeNull();
        pf!.CropLeft  .Should().BeApproximately(0.1,   0.0001);
        pf.CropTop    .Should().BeApproximately(0.2,   0.0001);
        pf.CropRight  .Should().BeApproximately(0.05,  0.0001);
        pf.CropBottom .Should().BeApproximately(0.15,  0.0001);
        pf.Grayscale  .Should().BeTrue();
        pf.Brightness .Should().BeApproximately(0.1,   0.0001);
        pf.Contrast   .Should().BeApproximately(-0.05, 0.0001);

        // Clone must be independent — mutating the clone doesn't affect the original
        clone.Shapes[0].PictureFormat!.CropLeft = 0.99;
        slide.Shapes[0].PictureFormat!.CropLeft.Should().BeApproximately(0.1, 0.0001);
    }

    // ── Compositor draw op ────────────────────────────────────────────────────────

    [Fact]
    public void Compositor_CroppedPicture_DrawOpCarriesCropFields()
    {
        var fmt = new PictureFormat
        {
            CropLeft   = 0.125,
            CropTop    = 0.0,
            CropRight  = 0.125,
            CropBottom = 0.25,
        };

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Picture,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture     = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" },
            PictureFormat = fmt,
        });

        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var picOp = ops.OfType<DrawOp.Picture>().FirstOrDefault();

        picOp.Should().NotBeNull("compositor must emit a Picture draw op");
        picOp!.HasCrop.Should().BeTrue();
        picOp.CropLeft  .Should().BeApproximately(0.125, 0.0001);
        picOp.CropTop   .Should().BeApproximately(0.0,   0.0001);
        picOp.CropRight .Should().BeApproximately(0.125, 0.0001);
        picOp.CropBottom.Should().BeApproximately(0.25,  0.0001);
    }

    [Fact]
    public void Compositor_GrayscalePicture_DrawOpCarriesGrayscaleFlag()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Picture,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture     = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" },
            PictureFormat = new PictureFormat { Grayscale = true, Brightness = 0.1, Contrast = -0.1 },
        });

        var ops  = SlideCompositor.Compose(pres, pres.Slides[0]);
        var picOp = ops.OfType<DrawOp.Picture>().First();

        picOp.Grayscale .Should().BeTrue();
        picOp.Brightness.Should().BeApproximately(0.1,  0.0001);
        picOp.Contrast  .Should().BeApproximately(-0.1, 0.0001);
    }

    // ── Crop math ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CropMath_VisibleFraction_IsCorrect()
    {
        // Crop 12.5% left, 12.5% right, 0% top, 25% bottom
        // Visible width fraction = 1 - 0.125 - 0.125 = 0.75
        // Visible height fraction = 1 - 0 - 0.25 = 0.75
        var picOp = new DrawOp.Picture
        {
            CropLeft   = 0.125,
            CropTop    = 0,
            CropRight  = 0.125,
            CropBottom = 0.25,
        };

        double visW = 1.0 - picOp.CropLeft - picOp.CropRight;
        double visH = 1.0 - picOp.CropTop  - picOp.CropBottom;

        visW.Should().BeApproximately(0.75, 0.0001);
        visH.Should().BeApproximately(0.75, 0.0001);
        picOp.HasCrop.Should().BeTrue();
    }
}
