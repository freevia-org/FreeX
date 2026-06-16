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
        var request = Request(Chart(ChartType.Line, c => c.BlankDisplayMode = ChartBlankDisplayMode.Gap),
            ["A", "B", "C"], [Series(0, "S1", 10, null, 30)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Points.Should().HaveCount(2);
        layout.Series[0].Points.Select(p => p.PointIndex).Should().Equal(0, 2);
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
}
