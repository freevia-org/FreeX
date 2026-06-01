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
    public void CalculateLineLayout_SkipsNonFiniteValuesAndBreaksSegments()
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([0, double.NaN, 10, 20], new Rect(10, 20, 90, 40));

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().Equal(
            (new Point(70, 40), new Point(100, 20)));
    }

    [Fact]
    public void CalculateLineLayout_ReturnsEmptyLayoutForOnlyNonFiniteValues()
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([double.NaN, double.PositiveInfinity], new Rect(10, 20, 80, 40));

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(80, 0)]
    public void CalculateLineLayout_ReturnsEmptyLayoutForDegenerateTargetRect(double width, double height)
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout([1, 2, 3], new Rect(10, 20, width, height));

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().BeEmpty();
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

    [Theory]
    [InlineData(0, 40)]
    [InlineData(100, 0)]
    public void CalculateColumnLayout_ReturnsEmptyLayoutForDegenerateTargetRect(double width, double height)
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([1, -2], new Rect(0, 0, width, height), winLoss: false);

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalculateColumnLayout_SkipsZeroValueBars(bool winLoss)
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([2, 0, -4], new Rect(0, 0, 90, 40), winLoss);

        layout.Bars.Should().Equal(
            new SparklineColumnBar(new Rect(5.25, winLoss ? 0 : 10, 19.5, winLoss ? 20 : 10), IsNegative: false),
            new SparklineColumnBar(new Rect(65.25, 20, 19.5, 20), IsNegative: true));
    }

    [Fact]
    public void CalculateColumnLayout_SkipsNonFiniteBars()
    {
        var layout = SparklineLayoutPlanner.CalculateColumnLayout([2, double.NaN, -4], new Rect(0, 0, 90, 40), winLoss: false);

        layout.Bars.Should().Equal(
            new SparklineColumnBar(new Rect(5.25, 10, 19.5, 10), IsNegative: false),
            new SparklineColumnBar(new Rect(65.25, 20, 19.5, 20), IsNegative: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalculateColumnLayout_KeepsBarsInsideTinyTargetRect(bool winLoss)
    {
        var target = new Rect(10, 20, 2, 1);

        var layout = SparklineLayoutPlanner.CalculateColumnLayout([1, -1], target, winLoss);

        layout.Bars.Should().HaveCount(2);
        foreach (var bar in layout.Bars)
        {
            bar.Rect.Left.Should().BeGreaterThanOrEqualTo(target.Left);
            bar.Rect.Right.Should().BeLessThanOrEqualTo(target.Right);
            bar.Rect.Top.Should().BeGreaterThanOrEqualTo(target.Top);
            bar.Rect.Bottom.Should().BeLessThanOrEqualTo(target.Bottom);
        }
    }

    [Fact]
    public void SparklineLayoutPlanner_AvoidsLinqAndIntermediatePointArrays()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SparklineLayoutPlanner.cs"));

        source.Should().Contain("for (var i = firstIndex + 1; i < values.Count; i++)");
        source.Should().Contain("foreach (var value in values)");
        source.Should().Contain("double.IsFinite(value)");
        source.Should().Contain("double.IsFinite(values[i])");
        source.Should().Contain("internal static void VisitLineLayout");
        source.Should().Contain("internal static void VisitColumnLayout");
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
