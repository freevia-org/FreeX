using FluentAssertions;
using FreeX.Core.Calc;

namespace FreeX.Core.Calc.Tests;

public sealed class ColumnWidthPixelMapperTests
{
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 6)]
    [InlineData(1, 12)]
    [InlineData(8.428571428571429, 64)]
    [InlineData(20, 145)]
    public void ColumnWidthToPixels_UsesViewportPixelFormula(double width, double expectedPixels)
    {
        ColumnWidthPixelMapper.ColumnWidthToPixels(width).Should().Be(expectedPixels);
    }

    [Theory]
    [InlineData(1, 1.0 / 12.0)]
    [InlineData(12, 1.0)]
    [InlineData(64, 8.428571428571429)]
    [InlineData(96, 13.0)]
    [InlineData(144, 19.857142857142858)]
    [InlineData(1790, 255.0)]
    public void PixelsToColumnWidth_RoundTripsThroughViewportPixelFormula(double pixels, double expectedWidth)
    {
        var width = ColumnWidthPixelMapper.PixelsToColumnWidth(pixels);

        width.Should().BeApproximately(expectedWidth, 0.0000001);
        ColumnWidthPixelMapper.ColumnWidthToPixels(width).Should().Be(pixels);
    }

    [Fact]
    public void PixelsToColumnWidth_ClampsAtExcelMaximum()
    {
        ColumnWidthPixelMapper.PixelsToColumnWidth(ColumnWidthPixelMapper.MaximumColumnWidthPixels + 100)
            .Should()
            .Be(ColumnWidthPixelMapper.MaximumColumnWidth);
    }
}
