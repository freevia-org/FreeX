using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

// Excel stores trendlines and error bars per series; the WPF (OxyPlot) renderer must draw them only on the
// series that carries them, each with its own settings.
public sealed partial class ChartRendererTests
{
    private static ViewportModel ThreeSeriesViewport() => new(
        [
            Cell(1, 1, "Category"), Cell(1, 2, "S1"), Cell(1, 3, "S2"), Cell(1, 4, "S3"),
            Cell(2, 1, "A"), Cell(2, 2, "10"), Cell(2, 3, "20"), Cell(2, 4, "30"),
            Cell(3, 1, "B"), Cell(3, 2, "15"), Cell(3, 3, "25"), Cell(3, 4, "35"),
            Cell(4, 1, "C"), Cell(4, 2, "22"), Cell(4, 3, "21"), Cell(4, 4, "50"),
        ],
        [],
        []);

    private static ChartModel ThreeSeriesChart(ChartType type, Action<ChartModel> configure)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = type,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 4)),
        };
        configure(chart);
        return chart;
    }

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.Area)]
    public void Renderer_DrawsATrendlineOnlyOnTheSeriesThatCarriesIt(ChartType type)
    {
        var chart = ThreeSeriesChart(type, c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines =
            [
                new ChartSeriesTrendline { SeriesIndex = 1, Type = ChartTrendlineType.Polynomial, Order = 2, Thickness = 4 },
            ];
        });

        var model = BuildPlotModel(chart, ThreeSeriesViewport());

        var trendlines = model.Series.OfType<LineSeries>().Where(s => s.Title is not null && s.Title.EndsWith(")")).ToList();
        trendlines.Should().ContainSingle("only series index 1 carries a trendline");
        trendlines[0].Title.Should().Be("Poly. (S2)");
        trendlines[0].StrokeThickness.Should().Be(4);
    }

    [Fact]
    public void Renderer_ChartWideTrendlineFlag_StillMeansEverySeries()
    {
        var chart = ThreeSeriesChart(ChartType.Column, c => c.ShowLinearTrendline = true);

        var model = BuildPlotModel(chart, ThreeSeriesViewport());

        model.Series.OfType<LineSeries>().Count(s => s.Title is not null && s.Title.EndsWith(")")).Should().Be(3);
    }

    [Fact]
    public void Renderer_DrawsErrorBarsOnlyOnTheirSeries_WithPerSeriesFormatting()
    {
        var chart = ThreeSeriesChart(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars =
            [
                new ChartSeriesErrorBars { SeriesIndex = 0, Kind = ChartErrorBarKind.FixedValue, Value = 2, Thickness = 1 },
                new ChartSeriesErrorBars { SeriesIndex = 2, Kind = ChartErrorBarKind.FixedValue, Value = 5, Thickness = 3 },
            ];
        });

        var model = BuildPlotModel(chart, ThreeSeriesViewport());

        model.Series.OfType<RectangleBarSeries>().Should().HaveCount(3);
        var whiskers = model.Series.OfType<LineSeries>().ToList();
        whiskers.Should().HaveCount(2, "series 1 has no error bars");
        whiskers.Select(w => w.StrokeThickness).Should().BeEquivalentTo(new[] { 1.0, 3.0 });
    }

    [Fact]
    public void Renderer_CustomErrorBars_AreAppliedToTheSeriesThatOwnsThem()
    {
        var chart = ThreeSeriesChart(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars =
            [
                new ChartSeriesErrorBars
                {
                    SeriesIndex = 2,
                    Kind = ChartErrorBarKind.Custom,
                    PlusRangeCacheXml = "<numCache><pt idx=\"0\"><v>1</v></pt><pt idx=\"1\"><v>2</v></pt><pt idx=\"2\"><v>3</v></pt></numCache>",
                },
            ];
        });

        var model = BuildPlotModel(chart, ThreeSeriesViewport());

        model.Series.OfType<LineSeries>().Should().ContainSingle();
    }
}
