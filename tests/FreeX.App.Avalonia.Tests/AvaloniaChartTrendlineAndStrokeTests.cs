using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless-render regression tests for three Avalonia chart-renderer parity fixes with WPF:
/// <list type="bullet">
///   <item>F7 — a Bar (horizontal bar) chart with <see cref="ChartModel.ShowLinearTrendline"/> must
///     draw a trendline polyline, mirroring WPF's swapTrendlineAxes behavior for ChartType.Bar.</item>
///   <item>F18 — when <see cref="ChartModel.ShowTrendlineEquation"/> or
///     <see cref="ChartModel.ShowTrendlineRSquared"/> is set, the renderer must draw the equation/R²
///     text near the trendline.</item>
///   <item>F19 — Line/Area/Radar series must honor the series' persisted
///     <see cref="ChartSeriesFormat.StrokeThickness"/> instead of a hardcoded 2px stroke.</item>
/// </list>
/// Uses the Avalonia headless platform (via the shared <see cref="RibbonHeadlessApp"/> session)
/// because Polyline/Polygon/TextBlock need <c>IPlatformRenderInterface</c> to render/measure.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartTrendlineAndStrokeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static ChartLayoutRequest BuildRequest(ChartModel chart, IReadOnlyList<string> categories, IReadOnlyList<ChartSeriesData> series) =>
        new()
        {
            Chart = chart,
            Categories = categories,
            Series = series,
            PlotArea = new PlotRect(0, 0, 300, 200),
            TextMeasurer = new AvaloniaTextMeasurer(),
        };

    private static ChartSeriesData Series(int index, string? name, params double?[] values) =>
        new() { SeriesIndex = index, Name = name, Values = values };

    // ── F7: Bar chart trendline ──────────────────────────────────────────────

    [Fact]
    public async Task F7_Bar_chart_with_ShowLinearTrendline_draws_a_trendline_polyline()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Bar,
                ShowLegend = false,
                ShowLinearTrendline = true,
                TrendlineType = ChartTrendlineType.Linear,
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            // A Polyline drawn for the trendline is on top of the bar Rectangles — the fix under
            // test is that LayoutBar now attaches a Trendline to the first series (F7), and the
            // renderer draws every attached Trendline via RenderTrendline (already wired for other
            // chart types), so at least one Polyline must be present in the canvas children.
            canvas.Children.OfType<Polyline>().Should().NotBeEmpty(
                "F7: a Bar chart with ShowLinearTrendline=true must render a trendline polyline, matching WPF");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F7_Bar_chart_without_ShowLinearTrendline_draws_no_polyline()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Bar,
                ShowLegend = false,
                ShowLinearTrendline = false,
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            canvas.Children.OfType<Polyline>().Should().BeEmpty(
                "a Bar chart's own series geometry is Rectangles, not Polylines — without a trendline request there must be no polyline");
        }, CancellationToken.None);
    }

    // ── F18: trendline equation / R² annotation ──────────────────────────────

    [Fact]
    public async Task F18_ShowTrendlineEquation_draws_an_equation_text_block()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                ShowLinearTrendline = true,
                TrendlineType = ChartTrendlineType.Linear,
                ShowTrendlineEquation = true,
            };
            var request = BuildRequest(chart, ["A", "B", "C", "D"], [Series(0, "S1", 1, 2, 3, 4)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            canvas.Children.OfType<TextBlock>()
                .Should().Contain(tb => tb.Text != null && tb.Text.StartsWith("y = "),
                    "F18: ShowTrendlineEquation must draw the equation text near the trendline");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F18_NeitherEquationNorRSquared_draws_no_extra_annotation_text_block()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                ShowLinearTrendline = true,
                TrendlineType = ChartTrendlineType.Linear,
            };
            var request = BuildRequest(chart, ["A", "B", "C", "D"], [Series(0, "S1", 1, 2, 3, 4)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            canvas.Children.OfType<TextBlock>()
                .Should().NotContain(tb => tb.Text != null && (tb.Text.StartsWith("y = ") || tb.Text.StartsWith("R² = ")),
                    "no equation/R² annotation should render when neither flag is set");
        }, CancellationToken.None);
    }

    // ── F19: series StrokeThickness honored ──────────────────────────────────

    [Fact]
    public async Task F19_Line_series_with_custom_StrokeThickness_uses_it_instead_of_hardcoded_two()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, StrokeThickness: 5.0)],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var polyline = canvas.Children.OfType<Polyline>().Should().ContainSingle(
                "F19: the line series must draw exactly one polyline for its series geometry").Subject;
            polyline.StrokeThickness.Should().Be(5.0,
                "F19: the series' persisted StrokeThickness must be honored instead of the hardcoded 2px default");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F19_Line_series_without_explicit_StrokeThickness_falls_back_to_two()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var polyline = canvas.Children.OfType<Polyline>().Should().ContainSingle().Subject;
            polyline.StrokeThickness.Should().Be(2.0,
                "with no explicit series StrokeThickness the renderer must fall back to the prior default of 2px");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task F19_Radar_series_with_custom_StrokeThickness_uses_it_instead_of_hardcoded_two()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Radar,
                ShowLegend = false,
                SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, StrokeThickness: 4.5)],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var polygon = canvas.Children.OfType<Polygon>().Should().ContainSingle(
                "F19: the radar series must draw exactly one closed polygon").Subject;
            polygon.StrokeThickness.Should().Be(4.5,
                "F19: the radar series' persisted StrokeThickness must be honored instead of the hardcoded 2px default");
        }, CancellationToken.None);
    }
}
