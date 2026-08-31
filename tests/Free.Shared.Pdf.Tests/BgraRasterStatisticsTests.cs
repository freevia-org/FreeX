using Free.Shared.Drawing;
using FluentAssertions;

namespace Free.Shared.Pdf.Tests;

public sealed class BgraRasterStatisticsTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void CountNonBackgroundPixels_IgnoresIncompleteTrailingPixel(int length)
    {
        var pixels = new byte[length];

        var count = () => BgraRasterStatistics.CountNonBackgroundPixels(pixels);

        count.Should().NotThrow();
        count().Should().Be(0);
    }
}
