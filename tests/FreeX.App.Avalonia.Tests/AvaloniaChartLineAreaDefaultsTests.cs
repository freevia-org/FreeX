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
/// Headless-render regression tests for two Avalonia Line/Area chart parity fixes with WPF/Excel
/// (findings G34, G38):
/// <list type="bullet">
///   <item>G34 — Line and Area series with no explicit <see cref="ChartSeriesFormat.MarkerStyle"/>
///     override must draw no point markers, matching WPF's OxyPlot default (MarkerType.None) and
///     Excel's plain Line/Area charts. Markers should only appear when explicitly requested.</item>
///   <item>G38 — an Area series with no explicit <see cref="ChartSeriesFormat.StrokeThickness"/>
///     override must use the same default stroke weight as Line/Radar (2px), matching WPF's OxyPlot
///     AreaSeries default, instead of a hardcoded 1px.</item>
/// </list>
/// Uses the Avalonia headless platform (via the shared <see cref="RibbonHeadlessApp"/> session)
/// because Polyline/Polygon/Ellipse need <c>IPlatformRenderInterface</c> to render/measure.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartLineAreaDefaultsTests
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

    // ── G34: no markers by default ───────────────────────────────────────────

    [Fact]
    public async Task G34_Line_series_without_explicit_MarkerStyle_draws_no_markers()
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

            canvas.Children.OfType<Ellipse>().Should().BeEmpty(
                "G34: a Line series with no explicit MarkerStyle override must not draw circle markers, matching WPF/Excel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task G34_Line_series_with_explicit_MarkerStyle_still_draws_markers()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, MarkerStyle: ChartMarkerStyle.Circle)],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            canvas.Children.OfType<Ellipse>().Should().NotBeEmpty(
                "an explicit MarkerStyle override must still draw markers");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task G34_Area_series_without_explicit_MarkerStyle_draws_no_markers()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Area,
                ShowLegend = false,
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            canvas.Children.OfType<Ellipse>().Should().BeEmpty(
                "G34: an Area series with no explicit MarkerStyle override must not draw circle markers, matching WPF/Excel");
        }, CancellationToken.None);
    }

    // ── G38: Area default stroke thickness matches Line/Radar (2px) ─────────

    [Fact]
    public async Task G38_Area_series_without_explicit_StrokeThickness_falls_back_to_two()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Area,
                ShowLegend = false,
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var polygon = canvas.Children.OfType<Polygon>().Should().ContainSingle(
                "G38: the area series must draw exactly one filled polygon").Subject;
            polygon.StrokeThickness.Should().Be(2.0,
                "G38: with no explicit series StrokeThickness the Area renderer must fall back to 2px, matching WPF's OxyPlot AreaSeries default");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task G38_Area_series_with_custom_StrokeThickness_uses_it_instead_of_default()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Area,
                ShowLegend = false,
                SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, StrokeThickness: 5.0)],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var polygon = canvas.Children.OfType<Polygon>().Should().ContainSingle().Subject;
            polygon.StrokeThickness.Should().Be(5.0,
                "an explicit series StrokeThickness override must be honored for Area series");
        }, CancellationToken.None);
    }
}
