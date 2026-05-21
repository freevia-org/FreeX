using FluentAssertions;
using Freexcel.App.UI;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class SparklineLayoutPlannerTests
{
    [Fact]
    public void CalculateLineLayout_ReturnsCenteredPointForSingleValue()
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([42], new Rect(10, 20, 80, 40));

        layout.SinglePoint.Should().Be(new Point(50, 40));
        layout.Segments.Should().BeEmpty();
    }

    [Fact]
    public void CalculateLineLayout_ScalesValuesAcrossRect()
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([0, 5, 10], new Rect(10, 20, 100, 40));

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().Equal(
            (new Point(10, 60), new Point(60, 40)),
            (new Point(60, 40), new Point(110, 20)));
    }

    [Fact]
    public void CalculateLineLayout_UsesBottomEdgeForFlatSeries()
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([5, 5], new Rect(10, 20, 100, 40));

        layout.Segments.Should().Equal((new Point(10, 60), new Point(110, 60)));
    }

    [Fact]
    public void CalculateColumnLayout_ScalesPositiveAndNegativeBarsAroundAxis()
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([2, -4], new Rect(0, 0, 100, 40), winLoss: false);

        layout.Bars.Should().HaveCount(2);
        layout.Bars[0].IsNegative.Should().BeFalse();
        layout.Bars[0].Rect.Should().Be(new Rect(8.75, 10, 32.5, 10));
        layout.Bars[1].IsNegative.Should().BeTrue();
        layout.Bars[1].Rect.Should().Be(new Rect(58.75, 20, 32.5, 20));
    }

    [Fact]
    public void CalculateColumnLayout_WinLossIgnoresMagnitude()
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([10, -2], new Rect(0, 0, 100, 40), winLoss: true);

        layout.Bars[0].Rect.Height.Should().Be(20);
        layout.Bars[1].Rect.Height.Should().Be(20);
        layout.Bars[1].IsNegative.Should().BeTrue();
    }
}
