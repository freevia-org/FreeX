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
    public void ColumnRenderer_RendersRequestedComboSeriesAsLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "2"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "5")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        var line = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        line.Title.Should().Be("Margin");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 2), (1, 5));
    }

    [Fact]
    public void ColumnRenderer_DoesNotTreatEmptyComboSeriesListAsAllSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = []
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(1, 4, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "2"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "12"),
                Cell(3, 4, "5")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3);
        model.Series.Should().OnlyContain(series => series is RectangleBarSeries);
    }

    [Fact]
    public void StackedColumnRenderer_RendersRequestedComboSeriesAsLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [2]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(1, 4, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "2"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "12"),
                Cell(3, 4, "5")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        model.Series[1].Should().BeOfType<RectangleBarSeries>();
        var line = model.Series[2].Should().BeOfType<LineSeries>().Subject;
        line.Title.Should().Be("Margin");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 2), (1, 5));
    }

    [Fact]
    public void PercentStackedColumnRenderer_RendersRequestedComboSeriesAsLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.PercentStackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [2]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(1, 4, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "20"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "12"),
                Cell(3, 4, "50")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        model.Series[1].Should().BeOfType<RectangleBarSeries>();
        var line = model.Series[2].Should().BeOfType<LineSeries>().Subject;
        line.Title.Should().Be("Margin");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 20), (1, 50));
    }
}
