using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Portable-engine mirror of the WPF ChartRendererTests.StackedArea suite: verifies that
/// StackedArea/PercentStackedArea produce true cumulative bands whose top polyline
/// (<see cref="SeriesLayout.Points"/>) rides on the running-stack bottom
/// (<see cref="SeriesLayout.BaselinePoints"/>), so both desktop hosts fill the same ribbons.
/// </summary>
public sealed class StackedAreaLayoutTests
{
    // Two series (North, South) over two categories, matching the WPF suite's fixture:
    // North = 10, 20; South = 5, 15.
    private static ChartLayoutRequest TwoSeriesRequest(ChartType type, PlotRect? plot = null) =>
        Request(Chart(type), ["Q1", "Q2"], [Series(0, "North", 10, 20), Series(1, "South", 5, 15)], plot);

    [Fact]
    public void StackedArea_stacks_each_band_on_the_cumulative_baseline_below()
    {
        var layout = ChartLayoutEngine.Layout(TwoSeriesRequest(ChartType.StackedArea));

        layout.Series.Should().HaveCount(2);
        var north = layout.Series[0];
        var south = layout.Series[1];
        north.Kind.Should().Be(SeriesGeometryKind.Area);
        south.Kind.Should().Be(SeriesGeometryKind.Area);

        // Bottom band sits on the zero baseline; its top is the raw values.
        north.Points.Select(p => p.DataY).Should().Equal(10, 20);
        north.BaselinePoints.Select(p => p.DataY).Should().Equal(0, 0);

        // Upper band rides on the lower band's cumulative top (10, 20) and stacks to 10+5, 20+15.
        south.BaselinePoints.Select(p => p.DataY).Should().Equal(10, 20);
        south.Points.Select(p => p.DataY).Should().Equal(15, 35);
    }

    [Fact]
    public void StackedArea_bands_are_contiguous_in_pixel_space()
    {
        var layout = ChartLayoutEngine.Layout(TwoSeriesRequest(ChartType.StackedArea));
        var north = layout.Series[0];
        var south = layout.Series[1];

        // The upper band's pixel bottom coincides with the lower band's pixel top at every category
        // (no gap, no overlap) — the geometric statement of a true cumulative stack.
        for (var i = 0; i < 2; i++)
        {
            south.BaselinePoints[i].Position.Y.Should().BeApproximately(north.Points[i].Position.Y, 1e-6);
            // Larger value ⇒ smaller pixel Y: each band's top is above (or level with) its own bottom.
            north.Points[i].Position.Y.Should().BeLessThanOrEqualTo(north.BaselinePoints[i].Position.Y);
            south.Points[i].Position.Y.Should().BeLessThan(south.BaselinePoints[i].Position.Y);
        }
    }

    [Fact]
    public void PercentStackedArea_normalizes_each_category_stack_to_100_percent()
    {
        var layout = ChartLayoutEngine.Layout(TwoSeriesRequest(ChartType.PercentStackedArea));

        var north = layout.Series[0];
        var south = layout.Series[1];

        // Category 0 total = 15 → North 66.67%; category 1 total = 35 → North 57.14%.
        north.Points[0].DataY.Should().BeApproximately(100.0 * 10 / 15, 0.01);
        north.Points[1].DataY.Should().BeApproximately(100.0 * 20 / 35, 0.01);

        // The top of the full stack normalizes to 100% in every category.
        south.Points.Select(p => p.DataY).Should().AllSatisfy(y => y.Should().BeApproximately(100, 0.01));

        // The value axis is pinned to the 0..100 percent range.
        layout.ValueAxis!.Scale.Minimum.Should().Be(0);
        layout.ValueAxis.Scale.Maximum.Should().Be(100);
    }

    [Fact]
    public void StackedArea_uses_zero_based_category_axis_like_plain_area()
    {
        var plot = new PlotRect(10, 5, 300, 200);
        var layout = ChartLayoutEngine.Layout(TwoSeriesRequest(ChartType.StackedArea, plot));

        // Area plots the first point at the plot's left edge and the last at the right edge (zero-based
        // index axis), unlike the centered column axis — so a stacked-area band spans the full plot.
        layout.Series[0].Points[0].Position.X.Should().BeApproximately(plot.Left, 1e-6);
        layout.Series[0].Points[^1].Position.X.Should().BeApproximately(plot.Right, 1e-6);
    }

    [Fact]
    public void StackedArea_treats_a_blank_cell_as_zero_to_keep_the_band_continuous()
    {
        // A gap in the lower band must not punch a hole in the stack: Excel (and WPF's
        // BuildStackedAreaModel) stacks a blank area point as zero, so the band stays continuous and
        // the layer above keeps a well-defined baseline at that category.
        var request = Request(Chart(ChartType.StackedArea), ["Q1", "Q2"],
            [Series(0, "North", 10, null), Series(1, "South", 5, 15)]);
        var layout = ChartLayoutEngine.Layout(request);

        var north = layout.Series[0];
        var south = layout.Series[1];

        // North's blank at category 1 contributes 0 (no NaN break in the band).
        north.Points.Select(p => p.DataY).Should().Equal(10, 0);
        north.Points.Select(p => p.Position.Y).Should().NotContain(double.NaN);

        // South still rides on North's cumulative top: 10 at Q1, 0 at Q2.
        south.BaselinePoints.Select(p => p.DataY).Should().Equal(10, 0);
        south.Points.Select(p => p.DataY).Should().Equal(15, 15);
    }

    [Fact]
    public void StackedArea_draws_a_combo_line_series_over_the_stack_without_stacking_it()
    {
        // A series promoted to a combo line overlay (ComboLineSeriesIndexes) is drawn as a line over
        // the stack, does not participate in the running stack totals, and keeps its own data labels —
        // mirroring WPF BuildStackedAreaModel's combo-line branch (AddLineDataLabelAnnotations).
        var chart = Chart(ChartType.StackedArea, c =>
        {
            c.UseComboLineForSecondarySeries = true;
            c.ComboLineSeriesIndexes = [1];
            c.ShowDataLabels = true;
        });
        var request = Request(chart, ["Q1", "Q2"],
            [Series(0, "North", 10, 20), Series(1, "Line", 5, 15)]);
        var layout = ChartLayoutEngine.Layout(request);

        // Series 0 is a stacked band on the zero baseline; series 1 is a plain line (not stacked).
        var band = layout.Series[0];
        var line = layout.Series[1];
        band.Kind.Should().Be(SeriesGeometryKind.Area);
        band.BaselinePoints.Select(p => p.DataY).Should().Equal(0, 0);
        line.Kind.Should().Be(SeriesGeometryKind.Line);
        // The line rides at its raw values (5, 15), never stacked on top of the band's 10, 20.
        line.Points.Select(p => p.DataY).Should().Equal(5, 15);
        line.BaselinePoints.Should().BeEmpty();
        // The combo line's own data labels are emitted (threaded dataLabels), one per point.
        layout.DataLabels.Where(d => d.SeriesIndex == 1).Should().HaveCount(2);
    }

    [Fact]
    public void Plain_area_leaves_BaselinePoints_empty_so_the_fill_drops_to_the_zero_line()
    {
        // Guards the consumer contract: only stacked area carries BaselinePoints. A plain area must
        // leave it empty so the renderer keeps closing the polygon to the flat scalar AreaBaseline.
        var layout = ChartLayoutEngine.Layout(
            Request(Chart(ChartType.Area), ["A", "B"], [Series(0, "S1", 20, 40)]));

        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Area);
        layout.Series[0].BaselinePoints.Should().BeEmpty();
        layout.Series[0].AreaBaseline.Should().BeApproximately(layout.ValueAxis!.Scale.Transform(0), 1e-6);
    }
}
