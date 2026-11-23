using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed class ChartRendererWaterfallTests
{
    [Fact]
    public void WaterfallRenderer_AppliesSeriesLineFormattingToConnectors()
    {
        var sheetId = SheetId.New();
        var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(31, 78, 121));
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ShowSeriesLines = true,
            SeriesLineColor = new CellColor(192, 0, 0),
            SeriesLineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
            SeriesLineThickness = 2.75,
            SeriesLineDashStyle = ChartLineDashStyle.Dot
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Stage"), Cell(1, 2, "Amount"),
                Cell(2, 1, "Start"), Cell(2, 2, "10"),
                Cell(3, 1, "Delta"), Cell(3, 2, "-3"),
                Cell(4, 1, "Total"), Cell(4, 2, "7")
            ],
            [],
            []),
            theme);

        // One bar series per role (Increase / Decrease / Total) plus the connector line series.
        model.Series.Should().HaveCount(4);
        var connectors = model.Series.ElementAt(3)
            .Should().BeOfType<LineSeries>().Subject;
        connectors.Color.Should().Be(OxyColor.FromRgb(31, 78, 121));
        connectors.StrokeThickness.Should().Be(2.75);
        connectors.LineStyle.Should().Be(LineStyle.Dot);
        connectors.MarkerType.Should().Be(MarkerType.None);
        connectors.Points.Should().Equal(
            new DataPoint(0.35, 10),
            new DataPoint(0.65, 10),
            DataPoint.Undefined,
            new DataPoint(1.35, 7),
            new DataPoint(1.65, 7),
            DataPoint.Undefined);
    }

    [Fact]
    public void WaterfallRenderer_OmitsConnectorsWhenSeriesLinesAreHidden()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowSeriesLines = false
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Stage"), Cell(1, 2, "Amount"),
                Cell(2, 1, "Start"), Cell(2, 2, "10"),
                Cell(3, 1, "Total"), Cell(3, 2, "10")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3).And.AllBeOfType<RectangleBarSeries>();
    }

    [Fact]
    public void WaterfallRenderer_SplitsColumnsByRoleWithExcelLegendNamesAndAccentColours()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 6, 2)),
            WaterfallTotalPointIndices = []
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Step"), Cell(1, 2, "Amount"),
                Cell(2, 1, "Start"), Cell(2, 2, "100"),
                Cell(3, 1, "Cost"), Cell(3, 2, "-30"),
                Cell(4, 1, "Sales"), Cell(4, 2, "45"),
                Cell(5, 1, "Fees"), Cell(5, 2, "-20"),
                Cell(6, 1, "Total"), Cell(6, 2, "60")
            ],
            [],
            []));

        var roles = model.Series.OfType<RectangleBarSeries>().ToList();
        roles.Select(r => r.Title).Should().Equal("Increase", "Decrease", "Total");
        // Excel (default Office theme): accents 1-3.
        roles[0].FillColor.Should().Be(OxyColor.FromRgb(0x15, 0x60, 0x82));
        roles[1].FillColor.Should().Be(OxyColor.FromRgb(0xE9, 0x71, 0x32));
        roles[2].FillColor.Should().Be(OxyColor.FromRgb(0x19, 0x6B, 0x24));
        // Without authored totals the last column is a plain increase (Excel never invents a total).
        roles[0].Items.Should().HaveCount(3);
        roles[1].Items.Should().HaveCount(2);
        roles[2].Items.Should().BeEmpty();
    }

    [Fact]
    public void HistogramRenderer_DelegatesBinningToHistogramBinPlanner()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.WaterfallHistogram.cs");
        var histogram = source[
            source.IndexOf("internal static PlotModel BuildHistogramModel", StringComparison.Ordinal)..];

        // Binning (count/width/automatic + overflow/underflow) lives in the pure, unit-tested
        // HistogramBinPlanner; the renderer must consume it rather than re-deriving bin math inline.
        histogram.Should().Contain("HistogramBinPlanner.Compute");
        histogram.Should().Contain("chart.HistogramBinning");
        histogram.Should().NotContain("Math.Sqrt");
        histogram.Should().NotContain("Math.Floor");
    }

    [Fact]
    public void HistogramRenderer_BuildsBinsFromNumericValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Histogram,
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 1))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Value"),
                Cell(2, 1, "1"),
                Cell(3, 1, "2"),
                Cell(4, 1, "3"),
                Cell(5, 1, "4")
            ],
            [],
            []));

        var bars = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        // Excel automatic binning (Scott, 2 significant digits): width 2.9 -> [1, 3.9] holds 1,2,3 and (3.9, 6.8] holds 4.
        bars.Items.Select(item => item.Y1).Should().Equal(3, 1);
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport) =>
        BuildPlotModel(chart, viewport, WorkbookTheme.Office);

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport, theme).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);
}
