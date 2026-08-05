namespace FreeP.App.Host.Tests;

public sealed class BgraRasterStatisticsTests
{
    [Fact]
    public void CountNonBackgroundPixels_UsesStrictBgrDistanceAndIgnoresAlpha()
    {
        byte[] pixels =
        [
            10, 20, 30, 0,
            14, 24, 34, 255,
            15, 24, 34, 0,
            10, 20, 30, 255,
        ];

        BgraRasterStatistics.CountNonBackgroundPixels(pixels).Should().Be(1);
    }

    [Fact]
    public void CountNonBackgroundPixels_CountsDifferencesInEveryColorChannel()
    {
        byte[] pixels =
        [
            5, 10, 15, 255,
            18, 10, 15, 255,
            5, 23, 15, 255,
            5, 10, 28, 255,
        ];

        BgraRasterStatistics.CountNonBackgroundPixels(pixels).Should().Be(3);
    }

    [Fact]
    public void CountNonBackgroundPixels_ReturnsZeroWithoutACompletePixel()
    {
        byte[] empty = [];
        byte[] incompletePixel = [10, 20, 30];

        BgraRasterStatistics.CountNonBackgroundPixels(empty).Should().Be(0);
        BgraRasterStatistics.CountNonBackgroundPixels(incompletePixel).Should().Be(0);
    }
}
