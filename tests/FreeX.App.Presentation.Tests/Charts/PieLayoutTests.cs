using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class PieLayoutTests
{
    [Fact]
    public void Pie_slice_sweeps_sum_to_360_degrees()
    {
        var request = Request(Chart(ChartType.Pie), ["A", "B", "C", "D"], [Series(0, "S1", 10, 20, 30, 40)]);
        var layout = ChartLayoutEngine.Layout(request);

        var slices = layout.Series[0].Slices;
        slices.Should().HaveCount(4);
        slices.Sum(s => s.Arc.SweepAngleDegrees).Should().BeApproximately(360, 1e-6);
    }

    [Fact]
    public void Pie_slice_fractions_are_proportional_to_values()
    {
        var request = Request(Chart(ChartType.Pie), ["A", "B"], [Series(0, "S1", 25, 75)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Slices[0].Fraction.Should().BeApproximately(0.25, 1e-9);
        layout.Series[0].Slices[1].Fraction.Should().BeApproximately(0.75, 1e-9);
        layout.Series[0].Slices[0].Arc.SweepAngleDegrees.Should().BeApproximately(90, 1e-6);
    }

    [Fact]
    public void Pie_slices_are_contiguous_starting_at_the_first_slice_angle()
    {
        var request = Request(Chart(ChartType.Pie, c => c.FirstSliceAngle = 30),
            ["A", "B", "C"], [Series(0, "S1", 1, 1, 1)]);
        var layout = ChartLayoutEngine.Layout(request);

        var slices = layout.Series[0].Slices;
        slices[0].Arc.StartAngleDegrees.Should().BeApproximately(30, 1e-9);
        // Each slice starts where the previous ended.
        for (var i = 1; i < slices.Count; i++)
            slices[i].Arc.StartAngleDegrees.Should().BeApproximately(slices[i - 1].Arc.EndAngleDegrees, 1e-9);
    }

    [Fact]
    public void Pie_skips_nonpositive_values()
    {
        var request = Request(Chart(ChartType.Pie), ["A", "B", "C"], [Series(0, "S1", 10, 0, -5)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Slices.Should().HaveCount(1);
        layout.Series[0].Slices[0].Arc.SweepAngleDegrees.Should().BeApproximately(360, 1e-6);
    }

    [Fact]
    public void Doughnut_carries_an_inner_radius_from_the_hole_size()
    {
        var request = Request(Chart(ChartType.Doughnut, c => c.DoughnutHoleSize = 0.5),
            ["A", "B"], [Series(0, "S1", 1, 1)]);
        var layout = ChartLayoutEngine.Layout(request);

        var arc = layout.Series[0].Slices[0].Arc;
        arc.InnerRadius.Should().BeApproximately(arc.OuterRadius * 0.5, 1e-6);
    }

    [Fact]
    public void Pie_slices_have_no_inner_radius()
    {
        var request = Request(Chart(ChartType.Pie), ["A", "B"], [Series(0, "S1", 1, 1)]);
        var layout = ChartLayoutEngine.Layout(request);
        layout.Series[0].Slices[0].Arc.InnerRadius.Should().Be(0);
    }

    [Fact]
    public void Exploded_slice_center_is_offset_outward()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ExplodedSliceIndex = 1;
            c.ExplodedSliceDistance = 0.2;
        }), ["A", "B"], [Series(0, "S1", 1, 1)]);
        var layout = ChartLayoutEngine.Layout(request);

        var plotCenter = layout.PlotArea.Center;
        var notExploded = layout.Series[0].Slices[0].Arc.Center;
        var exploded = layout.Series[0].Slices[1].Arc.Center;

        notExploded.Should().Be(new LayoutPoint(plotCenter.X, plotCenter.Y));
        // The exploded slice's center is displaced from the plot center.
        var dx = exploded.X - plotCenter.X;
        var dy = exploded.Y - plotCenter.Y;
        Math.Sqrt(dx * dx + dy * dy).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Pie_outer_radius_fits_within_the_plot_rectangle()
    {
        var plot = new PlotRect(0, 0, 400, 200);
        var request = Request(Chart(ChartType.Pie), ["A"], [Series(0, "S1", 1)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        // Radius limited by the smaller plot dimension (height here).
        layout.Series[0].Slices[0].Arc.OuterRadius.Should().BeApproximately(100, 1e-6);
    }
}
