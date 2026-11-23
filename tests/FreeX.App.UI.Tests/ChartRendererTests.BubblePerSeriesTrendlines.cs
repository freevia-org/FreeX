using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

// Excel bubble charts carry one optional trendline per series (c:bubbleChart/c:ser/c:trendline); the WPF
// renderer must draw each series' own trendline rather than only series 0.
public sealed partial class ChartRendererTests
{
    private static ViewportModel TwoSeriesBubbleViewport() => new(
        [
            Cell(1, 1, "X"), Cell(1, 2, "Y1"), Cell(1, 3, "S1"), Cell(1, 4, "Y2"), Cell(1, 5, "S2"),
            Cell(2, 1, "1"), Cell(2, 2, "10"), Cell(2, 3, "3"), Cell(2, 4, "30"), Cell(2, 5, "4"),
            Cell(3, 1, "2"), Cell(3, 2, "14"), Cell(3, 3, "5"), Cell(3, 4, "22"), Cell(3, 5, "6"),
            Cell(4, 1, "3"), Cell(4, 2, "19"), Cell(4, 3, "2"), Cell(4, 4, "17"), Cell(4, 5, "8"),
        ],
        [],
        []);

    private static ChartModel TwoSeriesBubbleChart(params ChartSeriesTrendline[] trendlines)
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Bubble,
            FirstRowIsHeader = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 5)),
            ShowLinearTrendline = true,
            SeriesTrendlines = [.. trendlines],
        };
    }

    [Fact]
    public void Renderer_Bubble_DrawsEachSeriesOwnTrendline()
    {
        var chart = TwoSeriesBubbleChart(
            new ChartSeriesTrendline { SeriesIndex = 0, Thickness = 2 },
            new ChartSeriesTrendline { SeriesIndex = 1, Type = ChartTrendlineType.Polynomial, Order = 2, Thickness = 4 });

        var model = BuildPlotModel(chart, TwoSeriesBubbleViewport());

        var trendlines = model.Series.OfType<LineSeries>().Where(s => s.Title is not null && s.Title.EndsWith(")")).ToList();
        trendlines.Should().HaveCount(2, "each bubble series carries its own trendline");
        trendlines.Select(t => t.StrokeThickness).Should().BeEquivalentTo(new[] { 2.0, 4.0 });
    }

    [Fact]
    public void Renderer_Bubble_TrendlineOnlyOnTheSeriesThatCarriesIt()
    {
        var chart = TwoSeriesBubbleChart(new ChartSeriesTrendline { SeriesIndex = 1, Thickness = 5 });

        var model = BuildPlotModel(chart, TwoSeriesBubbleViewport());

        model.Series.OfType<LineSeries>().Where(s => s.Title is not null && s.Title.EndsWith(")"))
            .Should().ContainSingle().Which.StrokeThickness.Should().Be(5);
    }

    [Fact]
    public void Renderer_Bubble_ChartWideTrendlineFlag_MeansEverySeries()
    {
        var chart = TwoSeriesBubbleChart();

        var model = BuildPlotModel(chart, TwoSeriesBubbleViewport());

        model.Series.OfType<LineSeries>().Count(s => s.Title is not null && s.Title.EndsWith(")")).Should().Be(2);
    }
}
