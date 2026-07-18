using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class WordBaselineRasterSurfacePlannerTests
{
    [Fact]
    public void Build_PortraitWordSurface_PreservesPixels()
    {
        var plan = WordBaselineRasterSurfacePlanner.Build(816, 1056);

        plan.PixelWidth.Should().Be(816);
        plan.PixelHeight.Should().Be(1056);
        plan.Scale.Should().Be(1d);
        plan.IsIdentity.Should().BeTrue();
    }

    [Fact]
    public void Build_LandscapeLetterSurface_FitsWordCaptureWidthWithoutUpscaling()
    {
        var plan = WordBaselineRasterSurfacePlanner.Build(1056, 816);

        plan.PixelWidth.Should().Be(816);
        plan.PixelHeight.Should().Be(630);
        plan.Scale.Should().BeApproximately(816d / 1056d, 0.000001d);
        plan.IsIdentity.Should().BeFalse();
    }

    [Fact]
    public void Build_SurfaceAlreadyWithinBounds_DoesNotUpscale()
    {
        var plan = WordBaselineRasterSurfacePlanner.Build(400, 600);

        plan.PixelWidth.Should().Be(400);
        plan.PixelHeight.Should().Be(600);
        plan.Scale.Should().Be(1d);
    }

    [Fact]
    public void Build_WideSurface_FitsWithinTheWordCaptureBounds()
    {
        var plan = WordBaselineRasterSurfacePlanner.Build(2000, 1000);

        plan.PixelWidth.Should().Be(816);
        plan.PixelHeight.Should().Be(408);
    }
}
