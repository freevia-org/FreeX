using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FontSizePlannerTests
{
    [Theory]
    [InlineData(8, 9)]
    [InlineData(10, 12)]
    [InlineData(22, 24)]
    [InlineData(24, 28)]
    public void Increase_UsesExcelLikeStepSizes(double current, double expected)
    {
        FontSizePlanner.Increase(current).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 7)]
    [InlineData(10, 9)]
    [InlineData(26, 24)]
    [InlineData(28, 24)]
    public void Decrease_UsesExcelLikeStepSizesAndClampsAtOne(double current, double expected)
    {
        FontSizePlanner.Decrease(current).Should().Be(expected);
    }

    [Theory]
    [InlineData(8, 18)]
    [InlineData(11, 20)]
    [InlineData(24, 37)]
    public void EstimateFittingRowHeight_CoversFontAtMinimumExcelRowHeight(double fontSize, double expected)
    {
        FontSizePlanner.EstimateFittingRowHeight(fontSize).Should().Be(expected);
    }
}
