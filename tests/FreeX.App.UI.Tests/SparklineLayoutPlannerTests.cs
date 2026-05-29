using FluentAssertions;
using FreeX.App.UI;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class SparklineLayoutPlannerTests
{
    [Fact]
    public void CalculateLineLayout_ReturnsEmptyLayoutForNoValues()
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([], new Rect(10, 20, 80, 40));

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().BeEmpty();
    }

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
    public void CalculateColumnLayout_ReturnsEmptyLayoutForNoValues()
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([], new Rect(0, 0, 100, 40), winLoss: false);

        layout.Bars.Should().BeEmpty();
    }

    [Fact]
    public void CalculateColumnLayout_WinLossIgnoresMagnitude()
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([10, -2], new Rect(0, 0, 100, 40), winLoss: true);

        layout.Bars[0].Rect.Height.Should().Be(20);
        layout.Bars[1].Rect.Height.Should().Be(20);
        layout.Bars[1].IsNegative.Should().BeTrue();
    }

    [Fact]
    public void SparklineLayoutPlanner_AvoidsLinqAndIntermediatePointArrays()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SparklineLayoutPlanner.cs"));

        source.Should().Contain("for (var i = 1; i < values.Count; i++)");
        source.Should().Contain("foreach (var value in values)");
        source.Should().NotContain("values.Min(");
        source.Should().NotContain("values.Max(");
        source.Should().NotContain(".Select(");
        source.Should().NotContain(".ToArray(");
        source.Should().NotContain(".DefaultIfEmpty(");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
