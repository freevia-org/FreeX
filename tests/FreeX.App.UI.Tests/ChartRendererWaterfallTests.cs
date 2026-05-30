using System.Reflection;
using System.IO;
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

        model.Series.Should().HaveCount(2);
        var connectors = model.Series.ElementAt(1)
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

        model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>();
    }

    [Fact]
    public void HistogramRenderer_AggregatesMinAndMaxWhileCollectingValues()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.WaterfallHistogram.cs"));
        var histogram = source[
            source.IndexOf("internal static PlotModel BuildHistogramModel", StringComparison.Ordinal)..];

        histogram.Should().Contain("if (v < min)");
        histogram.Should().Contain("if (v > max)");
        histogram.Should().NotContain("rawValues.Min()");
        histogram.Should().NotContain("rawValues.Max()");
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
        bars.Items.Select(item => item.Y1).Should().Equal(2, 2);
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport) =>
        BuildPlotModel(chart, viewport, WorkbookTheme.Office);

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel), typeof(WorkbookTheme)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport, theme]).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException("Unable to locate workspace file", Path.Combine(relativeParts));
    }
}
