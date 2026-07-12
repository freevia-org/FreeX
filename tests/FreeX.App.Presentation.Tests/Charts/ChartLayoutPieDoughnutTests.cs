using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Regression coverage for R35-deferred-multiring-doughnut-1: LayoutPie used to read only
/// <c>request.Series[0]</c>, so a multi-series Doughnut chart silently dropped every ring but the
/// first. Excel renders one concentric ring per series (series 0 innermost, rising outward) for a
/// Doughnut; a multi-series Pie still shows only the first series as a single ring (real Excel
/// behavior — the extra series are ignored, not rendered).
/// </summary>
public sealed class ChartLayoutPieDoughnutTests
{
    [Fact]
    public void Doughnut_with_two_series_produces_two_ring_series_layouts_with_distinct_radii()
    {
        var chart = Chart(ChartType.Doughnut, c => c.DoughnutHoleSize = 0.5);
        var request = Request(chart, ["A", "B"],
            [Series(0, "Inner", 10, 20), Series(1, "Outer", 30, 40)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(2, "each data series becomes its own concentric ring");
        layout.Series[0].SeriesIndex.Should().Be(0);
        layout.Series[1].SeriesIndex.Should().Be(1);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.PieSlices);
        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.PieSlices);

        layout.Series[0].Slices.Should().HaveCount(2);
        layout.Series[1].Slices.Should().HaveCount(2);

        // Ring 0 (series 0) sits closer to the center than ring 1 (series 1): its inner/outer radii
        // are both smaller, and the two rings are contiguous (ring 0's outer == ring 1's inner).
        var ring0 = layout.Series[0].Slices[0].Arc;
        var ring1 = layout.Series[1].Slices[0].Arc;

        ring0.InnerRadius.Should().BeGreaterThan(0, "the doughnut hole is preserved for the innermost ring");
        ring0.OuterRadius.Should().BeGreaterThan(ring0.InnerRadius);
        ring1.InnerRadius.Should().BeApproximately(ring0.OuterRadius, 0.001, "rings are contiguous bands");
        ring1.OuterRadius.Should().BeGreaterThan(ring1.InnerRadius);

        // The outermost ring reaches the full plot radius.
        var expectedOuter = Math.Min(StandardPlot.Width, StandardPlot.Height) / 2;
        ring1.OuterRadius.Should().BeApproximately(expectedOuter, 0.001);
    }

    [Fact]
    public void Single_series_doughnut_keeps_original_single_ring_geometry()
    {
        var chart = Chart(ChartType.Doughnut, c => c.DoughnutHoleSize = 0.55);
        var request = Request(chart, ["A", "B"], [Series(0, "Only", 10, 20)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(1, "a single-series doughnut still renders as one ring");
        var arc = layout.Series[0].Slices[0].Arc;
        var expectedOuter = Math.Min(StandardPlot.Width, StandardPlot.Height) / 2;
        arc.OuterRadius.Should().BeApproximately(expectedOuter, 0.001);
        arc.InnerRadius.Should().BeApproximately(expectedOuter * 0.55, 0.001);
    }

    [Fact]
    public void Pie_with_multiple_series_renders_only_the_first_series_as_a_single_ring()
    {
        // Matches real Excel: a multi-series Pie chart is not fanned out into rings (that is
        // Doughnut-only behavior) — Excel plots only the first series.
        var chart = Chart(ChartType.Pie);
        var request = Request(chart, ["A", "B"],
            [Series(0, "First", 10, 20), Series(1, "Ignored", 30, 40)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(1, "pie charts only ever show one series, unlike doughnuts");
        layout.Series[0].SeriesIndex.Should().Be(0);
        layout.Series[0].Slices[0].Arc.InnerRadius.Should().Be(0, "a pie chart (non-doughnut) has no hole");
    }
}
