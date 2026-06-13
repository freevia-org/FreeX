using System.Windows;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    [Fact]
    public void LineRenderer_ForecastSheetChartStartsForecastAndBoundsAtLastHistoricalPoint()
    {
        var sheetId = SheetId.New();
        var chart = ForecastChartPlanner.Plan(new ForecastChartLayout(sheetId, HeaderRow: 1, LastRow: 10));

        var model = BuildPlotModel(chart, CreateIssue102ForecastViewport(sheetId));

        var series = model.Series.OfType<LineSeries>().ToList();
        series.Should().HaveCount(4);
        series[0].Points.Should().EndWith(new OxyPlot.DataPoint(5, 3));
        series[1].Points.Should().StartWith(new OxyPlot.DataPoint(5, 3));
        series[2].Points.Should().StartWith(new OxyPlot.DataPoint(5, 3));
        series[3].Points.Should().StartWith(new OxyPlot.DataPoint(5, 3));
        model.Legends.Should().ContainSingle()
            .Which.LegendPosition.Should().Be(LegendPosition.BottomCenter);
    }

    [Fact]
    public void DirectFallbackLayout_ForecastSheetChartUsesBottomLegendWithoutRightSideReservation()
    {
        var sheetId = SheetId.New();
        var chart = ForecastChartPlanner.Plan(new ForecastChartLayout(sheetId, HeaderRow: 1, LastRow: 10));

        var layout = ChartRenderer.PlanDirectChartLayout(
            chart,
            seriesCount: 4,
            new Rect(0, 0, chart.Width, chart.Height),
            titleHeight: 34);

        layout.LegendFlow.Should().Be(ChartRenderer.DirectLegendFlow.Horizontal);
        layout.Plot.Width.Should().BeApproximately(chart.Width - 60, 0.001);
        layout.Plot.Height.Should().BeGreaterThan(150);
        layout.Legend.Left.Should().Be(layout.Plot.Left);
        layout.Legend.Right.Should().Be(layout.Plot.Right);
        layout.Legend.Top.Should().BeGreaterThan(layout.Plot.Bottom + 16);
        layout.Legend.Bottom.Should().BeLessThanOrEqualTo(chart.Height);
    }

    [Fact]
    public void RenderDirectFallback_ForecastSheetBottomLegendProducesNonBlankBitmap()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = ForecastChartPlanner.Plan(new ForecastChartLayout(sheetId, HeaderRow: 1, LastRow: 10));

            var image = ChartRenderer.RenderDirectFallback(
                chart,
                CreateIssue102ForecastViewport(sheetId),
                WorkbookTheme.Office,
                renderScale: 2.0);

            var bitmap = image.Should().BeAssignableTo<BitmapSource>().Subject;
            bitmap.PixelWidth.Should().Be(800);
            bitmap.PixelHeight.Should().Be(560);
            CountVisiblePixels(bitmap).Should().BeGreaterThan(3_500);
        });
    }

    private static ViewportModel CreateIssue102ForecastViewport(SheetId sheetId) =>
        new(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 1, 1, "Month", new TextValue("Month")),
                ChartCell(sheetId, 1, 2, "Sales", new TextValue("Sales")),
                ChartCell(sheetId, 1, 3, "Forecast", new TextValue("Forecast")),
                ChartCell(sheetId, 1, 4, "Lower Confidence Bound", new TextValue("Lower Confidence Bound")),
                ChartCell(sheetId, 1, 5, "Upper Confidence Bound", new TextValue("Upper Confidence Bound")),
                ChartCell(sheetId, 2, 1, "1", new NumberValue(1)),
                ChartCell(sheetId, 2, 2, "5", new NumberValue(5)),
                ChartCell(sheetId, 3, 1, "2", new NumberValue(2)),
                ChartCell(sheetId, 3, 2, "4", new NumberValue(4)),
                ChartCell(sheetId, 4, 1, "3", new NumberValue(3)),
                ChartCell(sheetId, 4, 2, "6", new NumberValue(6)),
                ChartCell(sheetId, 5, 1, "4", new NumberValue(4)),
                ChartCell(sheetId, 5, 2, "7", new NumberValue(7)),
                ChartCell(sheetId, 6, 1, "5", new NumberValue(5)),
                ChartCell(sheetId, 6, 2, "4", new NumberValue(4)),
                ChartCell(sheetId, 7, 1, "6", new NumberValue(6)),
                ChartCell(sheetId, 7, 2, "3", new NumberValue(3)),
                ChartCell(sheetId, 7, 3, "3", new NumberValue(3)),
                ChartCell(sheetId, 7, 4, "3", new NumberValue(3)),
                ChartCell(sheetId, 7, 5, "3", new NumberValue(3)),
                ChartCell(sheetId, 8, 1, "7", new NumberValue(7)),
                ChartCell(sheetId, 8, 3, "3.933333333", new NumberValue(3.933333333)),
                ChartCell(sheetId, 8, 4, "2.688333982", new NumberValue(2.688333982)),
                ChartCell(sheetId, 8, 5, "5.177832685", new NumberValue(5.177832685)),
                ChartCell(sheetId, 9, 1, "8", new NumberValue(8)),
                ChartCell(sheetId, 9, 3, "3.676190476", new NumberValue(3.676190476)),
                ChartCell(sheetId, 9, 4, "2.431691125", new NumberValue(2.431691125)),
                ChartCell(sheetId, 9, 5, "4.920689828", new NumberValue(4.920689828)),
                ChartCell(sheetId, 10, 1, "9", new NumberValue(9)),
                ChartCell(sheetId, 10, 3, "3.419047619", new NumberValue(3.419047619)),
                ChartCell(sheetId, 10, 4, "2.174548268", new NumberValue(2.174548268)),
                ChartCell(sheetId, 10, 5, "4.663546970", new NumberValue(4.663546970))
            ]);
}
