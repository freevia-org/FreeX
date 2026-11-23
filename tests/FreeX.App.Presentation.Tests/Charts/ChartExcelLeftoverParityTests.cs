using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Portable-layout side of the Excel visual leftovers: scatter lines, smooth curves, axis-title gutters, stacked
/// segment labels and the radar grid. Expected numbers were read from real Excel through COM / Chart.Export.
/// </summary>
public sealed class ChartExcelLeftoverParityTests
{
    private static ChartSeriesFormat ExcelScatterLine(int index) =>
        new(index, StrokeThemeColor: null, StrokeColor: new CellColor(0x15, 0x60, 0x82), StrokeThickness: 1.5);

    private static ChartLayoutRequest ScatterRequest(Action<ChartModel> configure)
    {
        var chart = ChartLayoutTestData.Chart(ChartType.Scatter, configure);
        return ChartLayoutTestData.Request(
            chart,
            [],
            [ChartLayoutTestData.ScatterSeries(0, "North", [1, 2.5, 4, 5.5, 7], 12, 19, 14, 22, 17)]);
    }

    [Fact]
    public void Scatter_with_an_authored_line_connects_its_points_like_xlXYScatterLines()
    {
        var layout = ChartLayoutEngine.Layout(ScatterRequest(c => c.SeriesFormats.Add(ExcelScatterLine(0))));

        layout.Series.Single().ConnectPoints.Should().BeTrue();
        layout.Series.Single().CurvePoints.Should().BeEmpty("a straight-line scatter strokes the polyline through its points");
    }

    [Fact]
    public void Scatter_whose_line_is_no_fill_stays_markers_only()
    {
        var format = ExcelScatterLine(0) with { NoLine = true };

        var layout = ChartLayoutEngine.Layout(ScatterRequest(c => c.SeriesFormats.Add(format)));

        layout.Series.Single().ConnectPoints.Should().BeFalse("Excel stores plain Insert > Scatter as <a:ln><a:noFill/>");
    }

    [Fact]
    public void Scatter_without_any_line_format_is_the_default_markers_only_chart()
    {
        var layout = ChartLayoutEngine.Layout(ScatterRequest(_ => { }));

        layout.Series.Single().ConnectPoints.Should().BeFalse();
    }

    [Fact]
    public void Smooth_scatter_carries_a_curve_through_every_data_point()
    {
        var format = ExcelScatterLine(0) with { Smooth = true };

        var series = ChartLayoutEngine.Layout(ScatterRequest(c => c.SeriesFormats.Add(format))).Series.Single();

        series.ConnectPoints.Should().BeTrue();
        series.CurvePoints.Should().HaveCount((series.Points.Count - 1) * 16 + 1);
        series.CurvePoints[0].Should().Be(series.Points[0].Position);
        series.CurvePoints[^1].Should().Be(series.Points[^1].Position);
        for (var i = 0; i < series.Points.Count; i++)
            series.CurvePoints[i * 16].Should().Be(series.Points[i].Position, "a smooth line passes through each data point");
    }

    [Fact]
    public void Curve_smoother_breaks_the_curve_at_a_gap()
    {
        SeriesPoint P(int i, double y) => new(i, i, y, new Free.Shared.Drawing.LayoutPoint(i * 10, y));
        var points = new[] { P(0, 5), P(1, 9), P(2, 4), P(3, double.NaN), P(4, 7), P(5, 2) };

        var curve = ChartCurveSmoother.Smooth(points);

        curve.Count(p => double.IsNaN(p.Y)).Should().Be(1);
        curve.Should().NotContain(p => double.IsInfinity(p.Y));
    }

    [Fact]
    public void Smooth_line_series_carries_a_curve()
    {
        var chart = ChartLayoutTestData.Chart(ChartType.Line, c => c.SeriesFormats.Add(new ChartSeriesFormat(0, Smooth: true)));
        var request = ChartLayoutTestData.Request(chart, ["Q1", "Q2", "Q3", "Q4"], [ChartLayoutTestData.Series(0, "North", 12, 19, 14, 22)]);

        var series = ChartLayoutEngine.Layout(request).Series.Single();

        series.CurvePoints.Should().HaveCount(3 * 16 + 1);
    }

    [Fact]
    public void A_titled_axis_gives_the_plot_a_title_line_gutter_like_excel()
    {
        // Excel (474 x 294 pt column chart): the value-axis title moves the inside plot area 21.2 pt (28 DIP)
        // to the right and the category-axis title lifts its bottom edge by the same 21.2 pt.
        var plot = new PlotRect(28, 28, 584, 344);
        ChartLayoutRequest Build(bool titles) => ChartLayoutTestData.Request(
                ChartLayoutTestData.Chart(ChartType.Column, c =>
                {
                    if (titles)
                    {
                        c.XAxisTitle = "Quarter";
                        c.YAxisTitle = "Revenue ($)";
                    }
                }),
                ["Q1", "Q2", "Q3"],
                [ChartLayoutTestData.Series(0, "North", 12, 19, 14)],
                plot)
            .WithChartSize(640, 400);

        var plain = ChartLayoutEngine.Layout(Build(false)).PlotArea;
        var titled = ChartLayoutEngine.Layout(Build(true)).PlotArea;

        (titled.Left - plain.Left).Should().BeInRange(20, 36, "the Y title takes one text line off the left");
        (plain.Bottom - titled.Bottom).Should().BeInRange(20, 36, "the X title takes one text line off the bottom");
        titled.Right.Should().BeApproximately(plain.Right, 0.01);
        titled.Top.Should().BeApproximately(plain.Top, 0.01);
    }

    [Fact]
    public void Stacked_column_segments_carry_centred_data_labels_like_excel()
    {
        var chart = ChartLayoutTestData.Chart(ChartType.StackedColumn, c =>
        {
            c.ShowDataLabels = true;
            c.ShowDataLabelValue = true;
        });
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2"],
            [ChartLayoutTestData.Series(0, "North", 12, 19), ChartLayoutTestData.Series(1, "South", 8, 15)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.DataLabels.Should().HaveCount(4);
        foreach (var series in layout.Series)
        {
            foreach (var bar in series.Bars)
            {
                var label = layout.DataLabels.Single(l => l.SeriesIndex == series.SeriesIndex && l.PointIndex == bar.PointIndex);
                label.Anchor.X.Should().BeApproximately(bar.Rect.Center.X, 0.01);
                label.Anchor.Y.Should().BeApproximately(bar.Rect.Center.Y, 0.01, "Excel centres a label inside its stacked segment");
            }
        }
    }

    [Fact]
    public void Radar_grid_uses_excels_automatic_rings()
    {
        // Excel radar with data up to 22 reads 0..25 in steps of 5 (COM MaximumScale 25, MajorUnit 5).
        var chart = ChartLayoutTestData.Chart(ChartType.Radar);
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2", "Q3", "Q4", "Q5"],
            [ChartLayoutTestData.Series(0, "North", 12, 19, 14, 22, 17)]);

        var radar = ChartLayoutEngine.Layout(request).Radar!;

        radar.ValueMaximum.Should().Be(25);
        radar.Rings.Select(r => r.Value).Should().Equal(5, 10, 15, 20, 25);
        radar.Rings[^1].Radius.Should().BeApproximately(radar.OuterRadius, 1e-9);
        radar.CategoryLabels.Should().Equal("Q1", "Q2", "Q3", "Q4", "Q5");
        radar.Filled.Should().BeFalse("plain xlRadar is an outline per series");
    }
}
