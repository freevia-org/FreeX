using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls.Shapes;
using Avalonia.Headless;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless-render regression tests that the Avalonia stacked-area renderer fills a true
/// variable-baseline stack (each band riding on the cumulative top of the bands below), matching the
/// WPF/OxyPlot AreaSeries.Points/Points2 render — not the pre-fix stopgap that dropped every band to
/// the same flat zero line. Follow-up to R27-chart-types-deep-1.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartStackedAreaTests
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
    public async Task StackedArea_fills_the_upper_band_on_the_lower_bands_cumulative_top()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.StackedArea, ShowLegend = false };
            // North = 10, 20 (lower band, on zero); South = 5, 15 (upper band, rides on North).
            var request = BuildRequest(chart, ["Q1", "Q2"], [Series(0, "North", 10, 20), Series(1, "South", 5, 15)]);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            var polygons = canvas.Children.OfType<Polygon>().ToList();
            polygons.Should().HaveCount(2, "one filled band per stacked-area series");

            // Larger pixel Y = lower on screen. The lower band (North) drops to the zero line, so its
            // lowest point is the plot's zero baseline. The upper band (South) rides ON North's
            // cumulative top, so its lowest point sits strictly above North's — the geometric proof of
            // a true variable-baseline stack. Before the fix both bands dropped to the same zero line,
            // making these maxima equal.
            var northBottom = polygons[0].Points.Max(p => p.Y);
            var southBottom = polygons[1].Points.Max(p => p.Y);
            southBottom.Should().BeLessThan(northBottom,
                "the upper band's bottom must ride on the lower band's top, not the shared zero line");

            // The upper band's bottom baseline must coincide with the lower band's top (contiguous, no
            // gap/overlap) at each category, per the laid-out geometry.
            for (var i = 0; i < 2; i++)
                layout.Series[1].BaselinePoints[i].Position.Y
                    .Should().BeApproximately(layout.Series[0].Points[i].Position.Y, 1e-6);
        }, CancellationToken.None);
    }
}
