using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class LineAreaScatterLayoutTests
{
    [Fact]
    public void Line_chart_emits_one_point_per_category_in_order()
    {
        var request = Request(Chart(ChartType.Line), ["A", "B", "C"], [Series(0, "S1", 10, 30, 20)]);
        var layout = ChartLayoutEngine.Layout(request);

        var points = layout.Series[0].Points;
        points.Should().HaveCount(3);
        points.Select(p => p.PointIndex).Should().Equal(0, 1, 2);
        // X positions strictly increase with category index.
        points[0].Position.X.Should().BeLessThan(points[1].Position.X);
        points[1].Position.X.Should().BeLessThan(points[2].Position.X);
    }

    [Fact]
    public void Line_uses_zero_based_index_axis_so_first_point_is_at_plot_left()
    {
        var plot = new PlotRect(10, 5, 300, 200);
        var request = Request(Chart(ChartType.Line), ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Points[0].Position.X.Should().BeApproximately(plot.Left, 1e-6);
        layout.Series[0].Points[2].Position.X.Should().BeApproximately(plot.Right, 1e-6);
    }

    [Fact]
    public void Line_with_gap_breaks_the_sequence()
    {
        // Round-29 finding R29-chart-render-pixel-deep-2: the blank index must still be present in
        // Points (not omitted) with a NaN value/position, so a NaN-aware polyline renderer breaks
        // the line here instead of jumping straight from the previous point to the next one. This
        // test used to assert the blank index was skipped entirely (Points.Count == 2) — that pinned
        // the bug (an omitted point is indistinguishable from "no data at this category" and let the
        // renderer connect straight across the gap); it has been corrected below.
        var request = Request(Chart(ChartType.Line, c => c.BlankDisplayMode = ChartBlankDisplayMode.Gap),
            ["A", "B", "C"], [Series(0, "S1", 10, null, 30)]);
        var layout = ChartLayoutEngine.Layout(request);

        var points = layout.Series[0].Points;
        points.Should().HaveCount(3);
        points.Select(p => p.PointIndex).Should().Equal(0, 1, 2);
        points[1].DataY.Should().Be(double.NaN);
        points[1].Position.Y.Should().Be(double.NaN);
        // The gap point's X is still a valid, correctly-spaced category position.
        points[1].Position.X.Should().BeGreaterThan(points[0].Position.X);
        points[1].Position.X.Should().BeLessThan(points[2].Position.X);
        // The surrounding real points are unaffected.
        points[0].DataY.Should().Be(10);
        points[2].DataY.Should().Be(30);
    }

    [Fact]
    public void Line_with_zero_blank_mode_still_substitutes_zero_not_a_gap()
    {
        // Sibling already-working case: BlankDisplayMode.Zero must keep substituting a real
        // zero-valued point (unaffected by the Gap-mode NaN-point fix above).
        var request = Request(Chart(ChartType.Line, c => c.BlankDisplayMode = ChartBlankDisplayMode.Zero),
            ["A", "B", "C"], [Series(0, "S1", 10, null, 30)]);
        var layout = ChartLayoutEngine.Layout(request);

        var points = layout.Series[0].Points;
        points.Should().HaveCount(3);
        points.Select(p => p.PointIndex).Should().Equal(0, 1, 2);
        points[1].DataY.Should().Be(0);
        points[1].Position.Y.Should().NotBe(double.NaN);
    }

    [Fact]
    public void Area_with_gap_emits_a_nan_point_for_the_blank_index()
    {
        // Area reuses LayoutLineSeries, so it must get the same break-marking fix as Line.
        var request = Request(Chart(ChartType.Area, c => c.BlankDisplayMode = ChartBlankDisplayMode.Gap),
            ["A", "B", "C"], [Series(0, "S1", 10, null, 30)]);
        var layout = ChartLayoutEngine.Layout(request);

        var points = layout.Series[0].Points;
        points.Should().HaveCount(3);
        points[1].DataY.Should().Be(double.NaN);
    }

    [Fact]
    public void Area_series_reports_baseline_and_carries_points()
    {
        var request = Request(Chart(ChartType.Area), ["A", "B"], [Series(0, "S1", 20, 40)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Area);
        layout.Series[0].Points.Should().HaveCount(2);
        layout.Series[0].AreaBaseline.Should().BeApproximately(layout.ValueAxis!.Scale.Transform(0), 1e-6);
    }

    [Fact]
    public void Scatter_uses_explicit_x_values_and_linear_axes()
    {
        var request = Request(
            Chart(ChartType.Scatter),
            [],
            [new ChartSeriesData
            {
                SeriesIndex = 0,
                Name = "S1",
                Values = [10, 20, 30],
                XValues = [1, 5, 9],
            }]);
        var layout = ChartLayoutEngine.Layout(request);

        var points = layout.Series[0].Points;
        points.Should().HaveCount(3);
        points.Select(p => p.DataX).Should().Equal(1, 5, 9);
        // x increases monotonically in pixel space.
        points[0].Position.X.Should().BeLessThan(points[2].Position.X);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.ScatterPoints);
    }

    [Fact]
    public void Scatter_falls_back_to_index_when_no_x_values()
    {
        var request = Request(Chart(ChartType.Scatter), [], [Series(0, "S1", 5, 15, 25)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Points.Select(p => p.DataX).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Multiple_line_series_share_the_same_axes()
    {
        var request = Request(Chart(ChartType.Line), ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 5, 40)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(2);
        // Both series map category 0 to the same X.
        layout.Series[0].Points[0].Position.X.Should().BeApproximately(layout.Series[1].Points[0].Position.X, 1e-9);
    }

    // ---- Logarithmic Y axis (F5) ------------------------------------------------------------

    [Fact]
    public void YAxisLogScale_produces_a_logarithmic_value_axis_with_decade_ticks()
    {
        // F5: the portable engine used to ignore ChartModel.YAxisLogScale entirely and always
        // build a linear axis. A Line chart supports Y log scale (ChartTypeSupport.SupportsYAxisLogScale).
        var request = Request(Chart(ChartType.Line, c => c.YAxisLogScale = true),
            ["A", "B", "C"], [Series(0, "S1", 1, 10, 100)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Scale.IsLogarithmic.Should().BeTrue();
        // Ticks land on decades (powers of 10), not a linear step.
        layout.ValueAxis.Ticks.Select(t => t.Value).Should().Contain([1, 10, 100]);
    }

    [Fact]
    public void YAxisLogScale_spaces_equal_ratio_points_equally_not_linearly()
    {
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(Chart(ChartType.Line, c => c.YAxisLogScale = true),
            ["A", "B", "C"], [Series(0, "S1", 1, 10, 100)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var points = layout.Series[0].Points;
        var y1 = points[0].Position.Y;
        var y10 = points[1].Position.Y;
        var y100 = points[2].Position.Y;

        // Equal ratios (1->10, 10->100) must produce equal pixel spacing on a log axis.
        (y1 - y10).Should().BeApproximately(y10 - y100, 1e-6, "each decade is spaced equally on a log Y axis");
    }

    [Fact]
    public void YAxisLogScale_ignored_when_the_flag_is_off()
    {
        var request = Request(Chart(ChartType.Line), ["A", "B", "C"], [Series(0, "S1", 1, 10, 100)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Scale.IsLogarithmic.Should().BeFalse();
    }

    [Fact]
    public void XAxisLogScale_produces_a_logarithmic_axis_for_scatter()
    {
        // Scatter supports both X and Y log scale (ChartTypeSupport.SupportsXAxisLogScale).
        var request = Request(Chart(ChartType.Scatter, c => c.XAxisLogScale = true),
            [],
            [new ChartSeriesData
            {
                SeriesIndex = 0,
                Name = "S1",
                Values = [1, 2, 3],
                XValues = [1, 10, 100],
            }]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Scale.IsLogarithmic.Should().BeTrue();
    }
}
