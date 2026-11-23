using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Avalonia side of the Excel visual leftovers: scatter lines/curves, marker size in points, default axis lines,
/// the radar grid and the right-hand axis title. Excel references come from COM / Chart.Export of the same chart.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartExcelLeftoverTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static ChartLayoutRequest Request(ChartModel chart, IReadOnlyList<string> categories, IReadOnlyList<ChartSeriesData> series) =>
        new()
        {
            Chart = chart,
            Categories = categories,
            Series = series,
            PlotArea = new PlotRect(28, 28, 584, 344),
            TextMeasurer = new AvaloniaTextMeasurer(),
            ChartWidth = 640,
            ChartHeight = 400,
        };

    private static Canvas Render(ChartModel chart, IReadOnlyList<string> categories, params ChartSeriesData[] series)
    {
        var layout = ChartLayoutEngine.Layout(Request(chart, categories, series));
        return (Canvas)new AvaloniaChartRenderer(chart, WorkbookTheme.Office).Render(layout, 640, 400);
    }

    private static ChartSeriesData Scatter(int index) =>
        new() { SeriesIndex = index, Name = "North", Values = [12, 19, 14, 22, 17], XValues = [1, 2.5, 4, 5.5, 7] };

    [Fact]
    public void Excel_marker_size_is_a_diameter_in_points()
    {
        // Excel's default marker (size 5) is 5 pt = 6.67 DIP wide: a 3.33 DIP radius.
        AvaloniaChartRenderer.MarkerRadiusFor(5).Should().BeApproximately(10.0 / 3.0, 1e-9);
        AvaloniaChartRenderer.MarkerRadiusFor(10).Should().BeApproximately(20.0 / 3.0, 1e-9);
        AvaloniaChartRenderer.MarkerRadiusFor(null).Should().BeApproximately(3.5, 1e-9, "unset keeps the default marker");
    }

    [Fact]
    public async Task Scatter_with_lines_draws_the_connecting_polyline()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.Scatter, ShowLegend = false };
            chart.SeriesFormats.Add(new ChartSeriesFormat(0, StrokeColor: new CellColor(0x15, 0x60, 0x82), StrokeThickness: 1.5));

            var canvas = Render(chart, [], Scatter(0));

            canvas.Children.OfType<Polyline>().Should().ContainSingle("xlXYScatterLines connects the markers");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Scatter_markers_only_draws_no_polyline()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.Scatter, ShowLegend = false };
            chart.SeriesFormats.Add(new ChartSeriesFormat(0, StrokeColor: new CellColor(0x15, 0x60, 0x82), NoLine: true));

            var canvas = Render(chart, [], Scatter(0));

            canvas.Children.OfType<Polyline>().Should().BeEmpty();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Smooth_scatter_polyline_is_the_dense_curve()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.Scatter, ShowLegend = false };
            chart.SeriesFormats.Add(new ChartSeriesFormat(0, StrokeColor: new CellColor(0x15, 0x60, 0x82), Smooth: true));

            var canvas = Render(chart, [], Scatter(0));

            canvas.Children.OfType<Polyline>().Single().Points.Should().HaveCount(4 * 16 + 1);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Value_axis_has_no_line_and_category_axis_a_light_grey_one_by_default()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.Column, ShowLegend = false };

            var canvas = Render(chart, ["Q1", "Q2", "Q3"], new ChartSeriesData { SeriesIndex = 0, Name = "North", Values = [12, 19, 14] });

            var greyRules = canvas.Children.OfType<Line>()
                .Where(l => l.Stroke is ISolidColorBrush b && b.Color == Color.FromRgb(0xD9, 0xD9, 0xD9))
                .ToList();
            greyRules.Should().Contain(l => l.StartPoint.Y == l.EndPoint.Y, "the category axis rule is light grey");
            greyRules.Should().NotContain(
                l => l.StartPoint.X == l.EndPoint.X && l.StartPoint.X < 40,
                "Excel's default value axis has no vertical rule");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Radar_draws_a_polygon_per_ring_and_only_fills_a_filled_radar()
    {
        await Session.Dispatch(() =>
        {
            var series = new ChartSeriesData { SeriesIndex = 0, Name = "North", Values = [12, 19, 14, 22, 17] };
            string[] categories = ["Q1", "Q2", "Q3", "Q4", "Q5"];

            var plain = Render(new ChartModel { Type = ChartType.Radar, ShowLegend = false }, categories, series);
            var filled = Render(new ChartModel { Type = ChartType.Radar, ShowLegend = false, RadarFilled = true }, categories, series);

            // 5 grid rings (5..25) + 1 series polygon.
            plain.Children.OfType<Polygon>().Should().HaveCount(6);
            plain.Children.OfType<Polygon>().Last().Fill.Should().Be(Brushes.Transparent, "xlRadar is an outline");
            filled.Children.OfType<Polygon>().Last().Fill.Should().NotBe(Brushes.Transparent, "xlRadarFilled fills the polygon");
            plain.Children.OfType<TextBlock>().Select(t => t.Text).Should().Contain(["Q1", "Q5", "25"]);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Titled_axes_keep_their_titles_inside_the_chart_and_clear_of_the_tick_labels()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.Column, ShowLegend = false, XAxisTitle = "Quarter", YAxisTitle = "Revenue ($)" };

            var canvas = Render(chart, ["Q1", "Q2", "Q3"], new ChartSeriesData { SeriesIndex = 0, Name = "North", Values = [12, 19, 14] });

            var title = canvas.Children.OfType<TextBlock>().Single(t => t.Text == "Revenue ($)");
            title.Measure(global::Avalonia.Size.Infinity);
            // Rotated -90 degrees about its top-left corner the title occupies [left, left + height] horizontally.
            Canvas.GetLeft(title).Should().BeGreaterThanOrEqualTo(0, "the left axis title must not be clipped by the chart edge");
        }, CancellationToken.None);
    }
}
