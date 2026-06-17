using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class BubbleLayoutTests
{
    [Fact]
    public void Bubble_series_produces_one_bubble_per_point()
    {
        var chart = Chart(ChartType.Bubble);
        var series = BubbleSeries(0, "S1", [1, 2, 3], [10, 20, 30], [4, 16, 36]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        var s = layout.Series.Should().ContainSingle().Subject;
        s.Kind.Should().Be(SeriesGeometryKind.Bubbles);
        s.Bubbles.Should().HaveCount(3);
    }

    [Fact]
    public void Bubble_radius_scales_with_square_root_of_size_for_area_representation()
    {
        var chart = Chart(ChartType.Bubble, c => c.BubbleSizeRepresents = ChartBubbleSizeRepresents.Area);
        // Sizes 9, 36: max is 36. Area mode => radius ∝ sqrt(size/max).
        var series = BubbleSeries(0, "S1", [1, 2], [10, 20], [9, 36]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [series]));

        var bubbles = layout.Series[0].Bubbles;
        var rSmall = bubbles[0].Radius;
        var rLarge = bubbles[1].Radius;

        // sqrt(9/36) = 0.5, so the smaller bubble is half the radius of the largest.
        (rSmall / rLarge).Should().BeApproximately(0.5, 1e-9);
        // Largest bubble sits at the max radius (BubbleScale 100%).
        rLarge.Should().BeApproximately(20, 1e-9);
    }

    [Fact]
    public void Bubble_radius_scales_linearly_for_width_representation()
    {
        var chart = Chart(ChartType.Bubble, c => c.BubbleSizeRepresents = ChartBubbleSizeRepresents.Width);
        var series = BubbleSeries(0, "S1", [1, 2], [10, 20], [9, 36]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [series]));

        var bubbles = layout.Series[0].Bubbles;
        // Width mode => radius ∝ size/max = 9/36 = 0.25.
        (bubbles[0].Radius / bubbles[1].Radius).Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Bubble_scale_percentage_shrinks_radii_proportionally()
    {
        var chart = Chart(ChartType.Bubble, c => c.BubbleScale = 50);
        var series = BubbleSeries(0, "S1", [1], [10], [36]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A"], [series]));

        // Max-size bubble at 50% scale => half the max radius.
        layout.Series[0].Bubbles[0].Radius.Should().BeApproximately(10, 1e-9);
    }

    [Fact]
    public void Negative_bubbles_are_dropped_unless_shown()
    {
        var hidden = Chart(ChartType.Bubble, c => c.ShowNegativeBubbles = false);
        var shown = Chart(ChartType.Bubble, c => c.ShowNegativeBubbles = true);
        var series = BubbleSeries(0, "S1", [1, 2], [10, 20], [4, -16]);

        ChartLayoutEngine.Layout(Request(hidden, ["A", "B"], [series])).Series[0].Bubbles.Should().HaveCount(1);
        ChartLayoutEngine.Layout(Request(shown, ["A", "B"], [series])).Series[0].Bubbles.Should().HaveCount(2);
    }

    [Fact]
    public void Bubble_center_maps_through_value_axes()
    {
        var chart = Chart(ChartType.Bubble);
        var series = BubbleSeries(0, "S1", [0, 10], [0, 100], [1, 1]);
        var plot = new PlotRect(0, 0, 200, 100);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [series], plot));

        var bubbles = layout.Series[0].Bubbles;
        // x grows left→right; the larger x sits further right than the smaller x.
        bubbles[1].Center.X.Should().BeGreaterThan(bubbles[0].Center.X);
        // y grows upward on screen (smaller Y), so the larger value is higher on screen.
        bubbles[1].Center.Y.Should().BeLessThan(bubbles[0].Center.Y);
    }
}
