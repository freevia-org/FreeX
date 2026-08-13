using FluentAssertions;
using FreeX.App.Presentation.Rendering;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class CellTextShrinkPlannerTests
{
    [Fact]
    public void ResolveFontSize_ShrinksInWholeStepsUsingNativeMeasurementDelegate()
    {
        var measuredSizes = new List<double>();

        var result = CellTextShrinkPlanner.ResolveFontSize(
            requestedFontSize: 11,
            availableWidth: 50,
            measureTextWidth: size =>
            {
                measuredSizes.Add(size);
                return size * 8;
            },
            minimumFontSize: 6);

        result.Should().Be(6);
        measuredSizes.Should().Equal(11, 10, 9, 8, 7);
    }

    [Theory]
    [InlineData(5, 100, 6, 5)]
    [InlineData(11, 0, 6, 6)]
    [InlineData(11, -1, 6, 6)]
    public void ResolveFontSize_PreservesBoundaryBehavior(
        double requested,
        double available,
        double minimum,
        double expected)
    {
        CellTextShrinkPlanner.ResolveFontSize(requested, available, _ => 1, minimum)
            .Should().Be(expected);
    }
}
