using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class PictureInsertionPlannerTests
{
    [Fact]
    public void PickerContractIncludesRasterFormatsAndSvgForBothRenderers()
    {
        PictureInsertionPlanner.SupportedFilePatterns.Should().Equal(
            "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.tif", "*.tiff", "*.svg");
        PictureInsertionPlanner.SupportedMimeTypes.Should().Contain("image/svg+xml");
        PictureInsertionPlanner.BuildWindowsFileDialogFilter().Should().Be(
            "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.svg)|" +
            "*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.svg|All files (*.*)|*.*");
    }

    [Fact]
    public void VectorRasterSurfacePreservesAspectRatioWithinSharedExtent()
    {
        PictureInsertionPlanner.BuildVectorRasterSurface(200, 100)
            .Should().Be(new PictureRasterSurfacePlan(400, 200));
        PictureInsertionPlanner.BuildVectorRasterSurface(100, 200)
            .Should().Be(new PictureRasterSurfacePlan(200, 400));
    }

    [Fact]
    public void VectorRasterSurfaceUsesSquareFallbackForInvalidBounds()
    {
        PictureInsertionPlanner.BuildVectorRasterSurface(0, double.NaN, 128)
            .Should().Be(new PictureRasterSurfacePlan(128, 128));
    }

    [Fact]
    public void CreatePngImageOwnsNaturalSizeWidthCapAndResetMetadata()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47 };

        var image = PictureInsertionPlanner.CreatePngImage(bytes, 1600, 800);

        image.Bytes.Should().BeSameAs(bytes);
        image.Format.Should().Be(ImageFormat.Png);
        image.WidthPt.Should().Be(400);
        image.HeightPt.Should().Be(200);
        image.OriginalPixelWidth.Should().Be(1600);
        image.OriginalPixelHeight.Should().Be(800);
    }

    [Fact]
    public void FitIconCapsWidthWithoutChangingSourceModelOrAspectRatio()
    {
        var image = PictureInsertionPlanner.CreatePngImage(
            new byte[] { 0x89, 0x50, 0x4e, 0x47 },
            400,
            200);

        var fitted = PictureInsertionPlanner.FitIcon(image);

        fitted.Should().NotBeSameAs(image);
        fitted.WidthPt.Should().Be(72);
        fitted.HeightPt.Should().Be(36);
        fitted.OriginalPixelWidth.Should().Be(400);
        fitted.OriginalPixelHeight.Should().Be(200);
        image.WidthPt.Should().Be(300);
        image.HeightPt.Should().Be(150);
    }

    [Fact]
    public void CreatePngIconBuildsCanonicalAspectPreservingOneInchModel()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4e, 0x47 };

        var wide = PictureInsertionPlanner.CreatePngIcon(bytes, 400, 200);
        var tall = PictureInsertionPlanner.CreatePngIcon(bytes, 200, 400);

        wide.Bytes.Should().BeSameAs(bytes);
        wide.WidthPt.Should().Be(72);
        wide.HeightPt.Should().Be(36);
        wide.OriginalPixelWidth.Should().Be(400);
        wide.OriginalPixelHeight.Should().Be(200);
        tall.WidthPt.Should().Be(72);
        tall.HeightPt.Should().Be(144);
        tall.OriginalPixelWidth.Should().Be(200);
        tall.OriginalPixelHeight.Should().Be(400);
    }
}
