using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class RadarLayoutTests
{
    private static readonly PlotRect SquarePlot = new(0, 0, 200, 200);

    [Fact]
    public void Radar_produces_one_spoke_per_category_at_even_angles()
    {
        var chart = Chart(ChartType.Radar);
        var series = Series(0, "S1", 1, 2, 3, 4);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"], [series], SquarePlot));

        layout.Radar.Should().NotBeNull();
        var spokes = layout.Radar!.Spokes;
        spokes.Should().HaveCount(4);
        spokes[0].AngleDegrees.Should().Be(0);
        spokes[1].AngleDegrees.Should().Be(90);
        spokes[2].AngleDegrees.Should().Be(180);
        spokes[3].AngleDegrees.Should().Be(270);
    }

    [Fact]
    public void Radar_first_spoke_points_straight_up_from_center()
    {
        var chart = Chart(ChartType.Radar);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 1, 1, 1)], SquarePlot));

        var radar = layout.Radar!;
        var first = radar.Spokes[0];
        // 0° = straight up: same X as center, smaller Y (toward the top of the plot).
        first.Outer.X.Should().BeApproximately(radar.Center.X, 1e-9);
        first.Outer.Y.Should().BeApproximately(radar.Center.Y - radar.OuterRadius, 1e-9);
    }

    [Fact]
    public void Radar_series_polyline_closes_back_to_first_vertex()
    {
        var chart = Chart(ChartType.Radar);
        var series = Series(0, "S1", 5, 10, 15);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series], SquarePlot));

        var points = layout.Series[0].Points;
        points.Should().HaveCount(4); // 3 vertices + closing vertex
        points[^1].Position.Should().Be(points[0].Position);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.RadarPolyline);
    }

    [Fact]
    public void Radar_vertex_radius_is_proportional_to_value()
    {
        var chart = Chart(ChartType.Radar, c => c.YAxisMaximum = 10);
        // Value 10 reaches the outer radius; value 5 is halfway.
        var series = Series(0, "S1", 10, 5, 10);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series], SquarePlot));

        var radar = layout.Radar!;
        var points = layout.Series[0].Points;

        // First vertex (top, value 10) sits at the outer radius distance from center.
        var d0 = Distance(radar.Center, points[0].Position);
        d0.Should().BeApproximately(radar.OuterRadius, 1e-6);

        // Second vertex (value 5) is at half the radius.
        var d1 = Distance(radar.Center, points[1].Position);
        d1.Should().BeApproximately(radar.OuterRadius / 2, 1e-6);
    }

    [Fact]
    public void Radar_single_point_series_is_not_closed()
    {
        var chart = Chart(ChartType.Radar);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A"], [Series(0, "S1", 5)], SquarePlot));
        layout.Series[0].Points.Should().HaveCount(1);
    }

    private static double Distance(LayoutPoint a, LayoutPoint b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
