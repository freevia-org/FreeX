using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

using FluentAssertions;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R91-render-chart-series-format-5-3: a per-point <c:dPt> marker override (Excel's Format Data
/// Point &gt; Marker Options) is read into <see cref="ChartModel.PointMarkerFormats"/> by
/// <c>XlsxChartSeriesFormatReader.ApplyPointMarkerOverride</c> and round-trips through
/// <c>XlsxChartXmlWriter.Series.cs</c>, but before this fix <see cref="AvaloniaChartRenderer"/> never
/// consumed it: every point in a series always drew with the uniform series-level marker
/// style/fill/stroke, so a distinctively-formatted single data point (e.g. one red diamond among
/// circles) rendered identically to its neighbors. These tests go through the renderer's real public
/// entry point (<see cref="AvaloniaChartRenderer.Render"/>) rather than calling the private marker
/// helper directly, so they exercise the actual consumer path a user's chart hits.
/// Uses the Avalonia headless platform (via the shared <see cref="RibbonHeadlessApp"/> session)
/// because Ellipse/Path/Polyline need <c>IPlatformRenderInterface</c> to render/measure.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R91_AvaloniaChartPointMarkerOverrideTests
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

    [Fact]
    public async Task PointMarkerOverride_StyleOverride_DrawsMarkerOnlyOnThatPoint()
    {
        await Session.Dispatch(() =>
        {
            // Series itself requests no markers (matches Excel's plain Line default), but one point
            // (index 1) carries an explicit dPt marker-style override -> only that point must draw.
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                PointMarkerFormats =
                [
                    new ChartPointMarkerFormat(SeriesIndex: 0, PointIndex: 1, MarkerStyle: ChartMarkerStyle.Diamond),
                ],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            // Diamond is rendered as a filled StreamGeometry Path (not an Ellipse) by BuildMarker.
            var paths = canvas.Children.OfType<AvaloniaPath>().ToList();
            paths.Should().HaveCount(1,
                "only the dPt-overridden point should draw a marker; the series itself requests none");
            canvas.Children.OfType<AvaloniaEllipse>().Should().BeEmpty(
                "the override's marker style is Diamond (a Path), not the Ellipse default Circle");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PointMarkerOverride_FillColorOverride_AppliesOnlyToThatPointsMarker()
    {
        await Session.Dispatch(() =>
        {
            var overrideFill = new CellColor(255, 0, 0);
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                SeriesFormats = [new ChartSeriesFormat(SeriesIndex: 0, MarkerStyle: ChartMarkerStyle.Circle)],
                PointMarkerFormats =
                [
                    new ChartPointMarkerFormat(SeriesIndex: 0, PointIndex: 1, FillColor: overrideFill),
                ],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var ellipses = canvas.Children.OfType<AvaloniaEllipse>().ToList();
            ellipses.Should().HaveCount(3, "all three points draw the series-level Circle marker");

            var fills = ellipses.Select(e => ((ISolidColorBrush)e.Fill!).Color).ToList();
            fills.Should().ContainSingle(c => c.R == overrideFill.R && c.G == overrideFill.G && c.B == overrideFill.B,
                "exactly one marker (the overridden point) must use the dPt fill override");
            fills.Where(c => !(c.R == overrideFill.R && c.G == overrideFill.G && c.B == overrideFill.B))
                .Should().HaveCount(2, "the two non-overridden points must keep the series-level fill");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: a point format for a DIFFERENT series/point index must not leak ──

    [Fact]
    public async Task PointMarkerOverride_ForDifferentSeriesIndex_DoesNotApplyToThisSeries()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Line,
                ShowLegend = false,
                // Override targets series 1, but only series 0 is plotted here.
                PointMarkerFormats =
                [
                    new ChartPointMarkerFormat(SeriesIndex: 1, PointIndex: 0, MarkerStyle: ChartMarkerStyle.Diamond),
                ],
            };
            var request = BuildRequest(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            // Matches the pre-existing G34 no-markers-by-default behavior: the mismatched override
            // must not leak onto series 0's points.
            canvas.Children.OfType<AvaloniaEllipse>().Should().BeEmpty();
            canvas.Children.OfType<AvaloniaPath>().Should().BeEmpty();
        }, CancellationToken.None);
    }
}
