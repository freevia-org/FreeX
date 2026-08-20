using System.Globalization;
using System.IO;
using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    [Fact]
    public void RadarRenderer_UsesPolarAxesAndClosesEachSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Radar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Metric"),
                Cell(1, 2, "Product A"),
                Cell(1, 3, "Product B"),
                Cell(2, 1, "Speed"),
                Cell(2, 2, "4"),
                Cell(2, 3, "3"),
                Cell(3, 1, "Cost"),
                Cell(3, 2, "2"),
                Cell(3, 3, "5"),
                Cell(4, 1, "Quality"),
                Cell(4, 2, "5"),
                Cell(4, 3, "4")
            ],
            [],
            []));

        model.PlotType.Should().Be(PlotType.Polar);
        model.Axes.Should().ContainSingle(axis => axis is AngleAxis);
        model.Axes.Should().ContainSingle(axis => axis is MagnitudeAxis);

        var series = model.Series.Should().HaveCount(2).And.AllBeOfType<LineSeries>().Subject.Cast<LineSeries>().ToList();
        series[0].Title.Should().Be("Product A");
        series[0].Points.Should().HaveCount(4);
        series[0].Points.First().Should().Be(series[0].Points.Last());
        series[0].Points.Select(point => point.Y).Should().Equal(4, 2, 5, 4);
    }

    [Fact]
    public void StockRenderer_UsesHighLowSeriesWithOhlcColumns()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Open"),
                Cell(1, 3, "High"),
                Cell(1, 4, "Low"),
                Cell(1, 5, "Close"),
                Cell(2, 1, "Mon"),
                Cell(2, 2, "10"),
                Cell(2, 3, "15"),
                Cell(2, 4, "9"),
                Cell(2, 5, "13"),
                Cell(3, 1, "Tue"),
                Cell(3, 2, "13"),
                Cell(3, 3, "18"),
                Cell(3, 4, "12"),
                Cell(3, 5, "16")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<HighLowSeries>().Subject;
        series.Title.Should().Be("Stock");
        series.Items.Should().HaveCount(2);
        series.Items[0].Open.Should().Be(10);
        series.Items[0].High.Should().Be(15);
        series.Items[0].Low.Should().Be(9);
        series.Items[0].Close.Should().Be(13);
    }

    [Fact]
    public void StockRenderer_UsesVolumeColumnAndOhlcColumnsForVolumeOpenHighLowClose()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 6))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Volume"),
                Cell(1, 3, "Open"),
                Cell(1, 4, "High"),
                Cell(1, 5, "Low"),
                Cell(1, 6, "Close"),
                Cell(2, 1, "Mon"),
                Cell(2, 2, "1000"),
                Cell(2, 3, "10"),
                Cell(2, 4, "15"),
                Cell(2, 5, "9"),
                Cell(2, 6, "13"),
                Cell(3, 1, "Tue"),
                Cell(3, 2, "1200"),
                Cell(3, 3, "13"),
                Cell(3, 4, "18"),
                Cell(3, 5, "12"),
                Cell(3, 6, "16")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        var stockSeries = model.Series[1].Should().BeOfType<HighLowSeries>().Subject;
        stockSeries.Items[0].Open.Should().Be(10);
        stockSeries.Items[0].High.Should().Be(15);
        stockSeries.Items[0].Low.Should().Be(9);
        stockSeries.Items[0].Close.Should().Be(13);
    }

    [Fact]
    public void ThreeDBarRenderer_UsesBarSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDBar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>();
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Left);
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Bottom);
        model.Annotations.OfType<PolygonAnnotation>().Should().HaveCount(4,
            "each 3-D bar gets a visible top and right facet while the bar series remains the front face");
    }

    [Fact]
    public void ThreeDPieRenderer_UsesPieSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDPie,
            ShowLegend = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>();
        model.Axes.Should().BeEmpty();
    }

    [Fact]
    public void StockRenderer_UsesDateTimeAxisForDateCategories()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 6))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Volume"),
                Cell(1, 3, "Open"),
                Cell(1, 4, "High"),
                Cell(1, 5, "Low"),
                Cell(1, 6, "Close"),
                Cell(2, 1, "2026-01-02"),
                Cell(2, 2, "1000"),
                Cell(2, 3, "10"),
                Cell(2, 4, "15"),
                Cell(2, 5, "9"),
                Cell(2, 6, "13"),
                Cell(3, 1, "2026-01-05"),
                Cell(3, 2, "1200"),
                Cell(3, 3, "13"),
                Cell(3, 4, "18"),
                Cell(3, 5, "12"),
                Cell(3, 6, "16")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().BeOfType<DateTimeAxis>();

        var stockSeries = model.Series[1].Should().BeOfType<HighLowSeries>().Subject;
        stockSeries.Items[0].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 2)), 0.0001);
        stockSeries.Items[1].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 5)), 0.0001);

        var volumeSeries = model.Series[0].Should().BeOfType<RectangleBarSeries>().Subject;
        var firstVolume = volumeSeries.Items[0];
        ((firstVolume.X0 + firstVolume.X1) / 2).Should().BeApproximately(stockSeries.Items[0].X, 0.0001);
    }

    [Fact]
    public void StockRenderer_UsesCandlestickSeriesWhenUpDownBarsAreEnabled()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            ShowUpDownBars = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Open"),
                Cell(1, 3, "High"),
                Cell(1, 4, "Low"),
                Cell(1, 5, "Close"),
                Cell(2, 1, "2026-01-02"),
                Cell(2, 2, "10"),
                Cell(2, 3, "15"),
                Cell(2, 4, "9"),
                Cell(2, 5, "13"),
                Cell(3, 1, "2026-01-05"),
                Cell(3, 2, "13"),
                Cell(3, 3, "18"),
                Cell(3, 4, "12"),
                Cell(3, 5, "11")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<CandleStickSeries>().Subject;
        series.IncreasingColor.Should().Be(OxyColors.White);
        series.DecreasingColor.Should().Be(OxyColors.Black);
        series.Items.Should().HaveCount(2);
        series.Items[0].Open.Should().Be(10);
        series.Items[0].Close.Should().Be(13);
        series.Items[1].Open.Should().Be(13);
        series.Items[1].Close.Should().Be(11);
    }

    [Fact]
    public void StockRenderer_AppliesUpDownBarFormattingToCandlesticks()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            ShowUpDownBars = true,
            UpDownBarGapWidth = 150,
            UpBarFillColor = new CellColor(226, 239, 218),
            UpBarBorderColor = new CellColor(84, 130, 53),
            UpBarBorderThickness = 2.25,
            DownBarFillColor = new CellColor(248, 203, 173),
            DownBarBorderColor = new CellColor(192, 0, 0),
            DownBarBorderThickness = 1.25,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Open"),
                Cell(1, 3, "High"),
                Cell(1, 4, "Low"),
                Cell(1, 5, "Close"),
                Cell(2, 1, "2026-01-02"),
                Cell(2, 2, "10"),
                Cell(2, 3, "15"),
                Cell(2, 4, "9"),
                Cell(2, 5, "13"),
                Cell(3, 1, "2026-01-05"),
                Cell(3, 2, "13"),
                Cell(3, 3, "18"),
                Cell(3, 4, "12"),
                Cell(3, 5, "11")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<CandleStickSeries>().Subject;
        series.IncreasingColor.Should().Be(OxyColor.FromRgb(226, 239, 218));
        series.DecreasingColor.Should().Be(OxyColor.FromRgb(248, 203, 173));
        series.Color.Should().Be(OxyColor.FromRgb(84, 130, 53));
        series.StrokeThickness.Should().Be(2.25);
        series.CandleWidth.Should().BeApproximately(0.4, 0.0001);
    }
}
