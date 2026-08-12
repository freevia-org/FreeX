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
    public void ScatterRenderer_TrendlineUsesPortableInterceptAndForecastProjection()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ShowLinearTrendline = true,
            TrendlineType = ChartTrendlineType.Linear,
            TrendlineIntercept = 5,
            TrendlineForward = 2,
            TrendlineBackward = 1,
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "9"),
                Cell(3, 1, "2"),
                Cell(3, 2, "13"),
                Cell(4, 1, "3"),
                Cell(4, 2, "17"),
            ],
            [],
            []));

        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Points.Select(point => (point.X, point.Y))
            .Should().Equal((0, 5), (1, 9), (3, 17), (5, 25));
    }

    [Fact]
    public void ScatterRenderer_AddsLinearTrendlineFromActualXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "3"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (3, 30));
    }

    [Fact]
    public void BarRenderer_AddsLinearTrendlineFromCategoryValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((10, 0), (30, 1));
    }

    [Fact]
    public void BarRenderer_CalculatesTrendlineFromCategoryOrderBeforeRenderingHorizontally()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(4, 1, "Q3"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Points.Select(point => (Math.Round(point.X, 3), point.Y))
            .Should().Equal((13.333, 0), (33.333, 2));
    }

    [Fact]
    public void BarRenderer_PositionsTrendlineInfoInHorizontalAxisSpace()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ShowLinearTrendline = true,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(4, 1, "Q3"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var annotation = model.Annotations.Should().ContainSingle().Which.Should().BeOfType<TextAnnotation>().Subject;
        annotation.Text.Should().Contain("y = 10x + 13.333");
        annotation.Text.Should().Contain("R² = ");
        annotation.TextPosition.Should().Be(new DataPoint(10, 2));
    }

    [Fact]
    public void AreaRenderer_AddsLinearTrendlineFromCategoryValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Area,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 10), (1, 30));
    }

    [Fact]
    public void BubbleRenderer_AddsLinearTrendlineFromActualXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "3"),
                Cell(3, 2, "30"),
                Cell(3, 3, "8")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (3, 30));
    }
}
