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
    // R22-chart-model-render-2: the bubble renderer no longer passes the size-column value straight
    // through as the OxyPlot marker size -- it scales it into a pixel radius via
    // ChartRenderer.Bubble.cs's ported BubbleRadius (mirroring the Avalonia ChartLayoutEngine
    // reference), using the largest size value across every series in the chart. This helper
    // reproduces that formula for the default Area/100% settings these tests exercise.
    private static double ExpectedBubbleRadius(double size, double maxSize) =>
        Math.Max(1.0, 20.0 * Math.Sqrt(Math.Clamp(Math.Abs(size) / maxSize, 0, 1)));


    [Fact]
    public void ScatterRenderer_UsesFirstNumericColumnAsXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Title.Should().Be("Revenue");
        series.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (2, 20));
    }

    [Fact]
    public void ScatterRenderer_IndexesSeriesFormatsAfterSharedXColumn()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(255, 192, 0),
                    StrokeColor: new CellColor(68, 114, 196),
                    StrokeThickness: 2,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 7),
                new ChartSeriesFormat(
                    1,
                    FillColor: new CellColor(112, 173, 71),
                    StrokeColor: new CellColor(55, 86, 35),
                    StrokeThickness: 3,
                    MarkerStyle: ChartMarkerStyle.Triangle,
                    MarkerSize: 9)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "6"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "11")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var first = model.Series[0].Should().BeOfType<ScatterSeries>().Subject;
        var second = model.Series[1].Should().BeOfType<ScatterSeries>().Subject;

        first.Title.Should().Be("Revenue");
        first.MarkerType.Should().Be(MarkerType.Diamond);
        first.MarkerFill.Should().Be(OxyColor.FromRgb(255, 192, 0));
        first.MarkerStroke.Should().Be(OxyColor.FromRgb(68, 114, 196));
        first.MarkerStrokeThickness.Should().Be(2);
        first.MarkerSize.Should().Be(7);
        first.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (2, 20));

        second.Title.Should().Be("Cost");
        second.MarkerType.Should().Be(MarkerType.Triangle);
        second.MarkerFill.Should().Be(OxyColor.FromRgb(112, 173, 71));
        second.MarkerStroke.Should().Be(OxyColor.FromRgb(55, 86, 35));
        second.MarkerStrokeThickness.Should().Be(3);
        second.MarkerSize.Should().Be(9);
        second.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 6), (2, 11));
    }

    [Fact]
    public void BubbleRenderer_UsesXyAndSizeColumns()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "8")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Title.Should().Be("Revenue");
        series.Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal(
            (1, 10, ExpectedBubbleRadius(4, 8)),
            (2, 20, ExpectedBubbleRadius(8, 8)));
    }

    [Fact]
    public void BubbleRenderer_RendersMultipleYAndSizePairsAgainstSharedXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196)),
                new ChartSeriesFormat(1, FillColor: new CellColor(112, 173, 71))
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Margin A"),
                Cell(1, 3, "Size A"),
                Cell(1, 4, "Margin B"),
                Cell(1, 5, "Size B"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "7"),
                Cell(2, 5, "3"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "8"),
                Cell(3, 4, "11"),
                Cell(3, 5, "6")
            ],
            [],
            []));

        // The shared bubble-size maximum is 8 (the largest |size| across both series), so Series B's
        // radii (sizes 3 and 6) scale relative to that same maximum rather than its own local max.
        var series = model.Series.Should().HaveCount(2).And.AllBeOfType<ScatterSeries>().Subject.Cast<ScatterSeries>().ToList();
        series[0].Title.Should().Be("Margin A");
        series[0].Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal(
            (1, 10, ExpectedBubbleRadius(4, 8)),
            (2, 20, ExpectedBubbleRadius(8, 8)));
        series[0].MarkerFill.Should().Be(OxyColor.FromRgb(68, 114, 196));
        series[1].Title.Should().Be("Margin B");
        series[1].Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal(
            (1, 7, ExpectedBubbleRadius(3, 8)),
            (2, 11, ExpectedBubbleRadius(6, 8)));
        series[1].MarkerFill.Should().Be(OxyColor.FromRgb(112, 173, 71));
    }

    [Fact]
    public void BubbleRenderer_IgnoresCategoryFlagAndUsesFirstRangeColumnAsXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "8")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Title.Should().Be("Revenue");
        series.Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal(
            (1, 10, ExpectedBubbleRadius(4, 8)),
            (2, 20, ExpectedBubbleRadius(8, 8)));
    }
}
