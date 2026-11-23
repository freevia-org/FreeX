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
/// Excel stores trendlines and error bars per series. The Avalonia renderer must draw each only on the
/// series that carries it, using that series' own line formatting.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartPerSeriesAdornmentTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static ChartLayoutRequest BuildRequest(ChartModel chart) =>
        new()
        {
            Chart = chart,
            Categories = ["A", "B", "C", "D"],
            Series =
            [
                new ChartSeriesData { SeriesIndex = 0, Name = "S0", Values = [1, 2, 3, 4] },
                new ChartSeriesData { SeriesIndex = 1, Name = "S1", Values = [4, 3, 5, 1] },
                new ChartSeriesData { SeriesIndex = 2, Name = "S2", Values = [2, 6, 3, 7] },
            ],
            PlotArea = new PlotRect(0, 0, 300, 200),
            TextMeasurer = new AvaloniaTextMeasurer(),
        };

    [Fact]
    public async Task Trendline_isDrawnOnlyForTheSeriesThatCarriesIt_withItsOwnThickness()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = false,
                ShowLinearTrendline = true,
                SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 1, Thickness = 5 }],
            };

            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office)
                .Render(ChartLayoutEngine.Layout(BuildRequest(chart)), 300, 200);

            var polylines = canvas.Children.OfType<Polyline>().ToList();
            polylines.Should().ContainSingle("only series 1 carries a trendline; columns are rectangles");
            polylines[0].StrokeThickness.Should().Be(5);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Trendline_chartWideFlag_stillDrawsOnEverySeries()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = ChartType.Column, ShowLegend = false, ShowLinearTrendline = true };

            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office)
                .Render(ChartLayoutEngine.Layout(BuildRequest(chart)), 300, 200);

            canvas.Children.OfType<Polyline>().Should().HaveCount(3);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ErrorBars_areDrawnOnlyForTheirSeries_withTheirOwnThickness()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = false,
                ShowErrorBars = true,
                SeriesErrorBars =
                [
                    new ChartSeriesErrorBars { SeriesIndex = 2, Kind = ChartErrorBarKind.FixedValue, Value = 1, Direction = ChartErrorBarDirection.Plus, Thickness = 3, EndCaps = false },
                ],
            };

            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office)
                .Render(ChartLayoutEngine.Layout(BuildRequest(chart)), 300, 200);

            // One whisker line per point of series 2 only (end caps off), all with series 2's thickness.
            var lines = canvas.Children.OfType<Line>().Where(l => l.StrokeThickness == 3).ToList();
            lines.Should().HaveCount(4);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legend_listsTrendlineRowsWithExcelNamesAndALineSwatch()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = true,
                LegendPosition = ChartLegendPosition.Right,
                ShowLinearTrendline = true,
                SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 1 }],
            };

            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office)
                .Render(ChartLayoutEngine.Layout(BuildRequest(chart)), 300, 200);

            canvas.Children.OfType<TextBlock>().Select(t => t.Text).Should().Contain("Linear (S1)");
        }, CancellationToken.None);
    }
}
