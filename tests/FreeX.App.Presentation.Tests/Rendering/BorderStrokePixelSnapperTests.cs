using FluentAssertions;
using FreeX.App.Presentation.Rendering;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class BorderStrokePixelSnapperTests
{
    [Theory]
    [InlineData(0.25, 1.0, 1)]
    [InlineData(0.5, 1.0, 1)]
    [InlineData(1.5, 1.0, 2)]
    [InlineData(2.5, 1.0, 3)]
    [InlineData(0.5, 2.0, 1)]
    [InlineData(1.5, 2.0, 3)]
    public void SnapThicknessToDevicePixels_RoundsToStableIntegerPixels(
        double thicknessDip,
        double effectivePixelsPerDip,
        int expectedDevicePixels)
    {
        BorderStrokePixelSnapper.SnapThicknessToDevicePixels(thicknessDip, effectivePixelsPerDip)
            .Should().Be(expectedDevicePixels);
    }

    [Theory]
    [InlineData(20.0, 1.0, 1.0, 20.5)]
    [InlineData(20.25, 1.0, 1.0, 20.5)]
    [InlineData(20.75, 1.0, 1.0, 20.5)]
    [InlineData(20.0, 2.0, 1.0, 20.0)]
    public void SnapCenter_AlignsStrokeEdgesToDevicePixels(
        double centerDip,
        double snappedThicknessDip,
        double effectivePixelsPerDip,
        double expectedCenterDip)
    {
        BorderStrokePixelSnapper
            .SnapCenter(centerDip, snappedThicknessDip, effectivePixelsPerDip)
            .Should().BeApproximately(expectedCenterDip, 0.0001);
    }
}
