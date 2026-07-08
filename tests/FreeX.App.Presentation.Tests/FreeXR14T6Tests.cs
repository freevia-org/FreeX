using FluentAssertions;
using FreeX.App.Presentation.Sparklines;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Focused regression test for round-14 bucket T6 finding R14-sparklines-1.
/// </summary>
public sealed class FreeXR14T6Tests
{
    // R14-sparklines-1: Column sparkline bars for all-positive (or all-negative) data must fill the
    // full cell height from a zero baseline at the matching edge, matching Excel — not float
    // half-height in the top (or bottom) half of the cell around a wrongly-centered axis.
    [Fact]
    public void ColumnSparkline_AllPositiveValues_FillsFullCellHeightFromBottomBaseline()
    {
        var rect = new LayoutRect(0, 0, 100, 40);

        var layout = SparklineLayoutEngine.CalculateColumnLayout(
            new double[] { 1, 2, 3, 4, 5 }, rect, winLoss: false);

        layout.Bars.Should().HaveCount(5);

        // The max value (5) must fill the entire cell height, growing up from the bottom edge —
        // not half the height, floating in the top half of the cell.
        var maxBar = layout.Bars[^1];
        maxBar.IsNegative.Should().BeFalse();
        maxBar.Rect.Height.Should().Be(40);
        maxBar.Rect.Top.Should().Be(0);
        maxBar.Rect.Bottom.Should().Be(40);

        // Every bar's baseline sits at the cell bottom, not the vertical midline.
        layout.Bars.Should().OnlyContain(b => b.Rect.Bottom == 40);
    }

    [Fact]
    public void ColumnSparkline_AllNegativeValues_FillsFullCellHeightFromTopBaseline()
    {
        var rect = new LayoutRect(0, 0, 100, 40);

        var layout = SparklineLayoutEngine.CalculateColumnLayout(
            new double[] { -1, -2, -3, -4, -5 }, rect, winLoss: false);

        layout.Bars.Should().HaveCount(5);

        // The most-negative value (-5) must fill the entire cell height, growing down from the top
        // edge — not half the height, floating in the bottom half of the cell.
        var maxBar = layout.Bars[^1];
        maxBar.IsNegative.Should().BeTrue();
        maxBar.Rect.Height.Should().Be(40);
        maxBar.Rect.Top.Should().Be(0);
        maxBar.Rect.Bottom.Should().Be(40);

        // Every bar's baseline sits at the cell top, not the vertical midline.
        layout.Bars.Should().OnlyContain(b => b.Rect.Top == 0);
    }
}
