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
    [Theory]
    [InlineData(ChartType.PercentStackedColumn, AxisPosition.Left)]
    [InlineData(ChartType.PercentStackedBar, AxisPosition.Bottom)]
    public void PercentStackedRenderer_PositiveOnlyDataUsesZeroToHundredAxis(
        ChartType chartType,
        AxisPosition valueAxisPosition)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "25"),
                Cell(2, 3, "75"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "40"),
                Cell(3, 3, "60")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(axis => axis.Position == valueAxisPosition).Subject;
        axis.Minimum.Should().Be(0);
        axis.Maximum.Should().Be(100);
    }

    [Theory]
    [InlineData(ChartType.PercentStackedColumn, AxisPosition.Left)]
    [InlineData(ChartType.PercentStackedBar, AxisPosition.Bottom)]
    public void PercentStackedRenderer_MixedSignsUseNegativeAndPositiveAxis(
        ChartType chartType,
        AxisPosition valueAxisPosition)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "25"),
                Cell(2, 3, "-75")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(axis => axis.Position == valueAxisPosition).Subject;
        axis.Minimum.Should().Be(-100);
        axis.Maximum.Should().Be(100);
    }

    [Theory]
    [InlineData(ChartType.PercentStackedColumn, AxisPosition.Left)]
    [InlineData(ChartType.PercentStackedBar, AxisPosition.Bottom)]
    public void PercentStackedRenderer_NegativeOnlyDataUsesMinusHundredToZeroAxis(
        ChartType chartType,
        AxisPosition valueAxisPosition)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "-25"),
                Cell(2, 3, "-75")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(axis => axis.Position == valueAxisPosition).Subject;
        axis.Minimum.Should().Be(-100);
        axis.Maximum.Should().Be(0);
    }

    [Fact]
    public void BarRenderer_AppliesYAxisStylingButIgnoresNumericBoundsOnCategoryAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowYAxisLabels = false,
            YAxisLabelTextColor = new CellColor(31, 78, 121),
            YAxisLabelFontSize = 14,
            YAxisLineColor = new CellColor(217, 83, 25),
            YAxisLineThickness = 2.5,
            YAxisMajorTickStyle = ChartAxisTickStyle.None,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            YAxisMinimum = 5,
            YAxisMaximum = 9
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

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Left)
            // R90-render-chart-axis-titles-5-2: the renderer now builds a skip-aware CategoryAxis
            // subclass, so assert the kind rather than the exact type.
            .Should().BeAssignableTo<CategoryAxis>().Subject;
        axis.TextColor.Should().Be(OxyColors.Transparent);
        axis.FontSize.Should().Be(14);
        axis.AxislineColor.Should().Be(OxyColor.FromRgb(217, 83, 25));
        axis.AxislineThickness.Should().Be(2.5);
        axis.MajorTickSize.Should().Be(0);
        axis.Minimum.Should().NotBe(5);
        axis.Maximum.Should().NotBe(9);
        axis.FormatValue(0).Should().Be("Q1");
    }

    /// <summary>
    /// Regression test for NN=21 (4. Dynamic Histogram): a Column chart whose series val formula
    /// is a direct cell-ref range (e.g. $B$31:$B$32) with no cat element.
    /// When categories is empty the x-axis maximum must still span all N data rows, not just 1.
    /// </summary>
    [Fact]
    public void ColumnRenderer_NoCategoryAxis_ShowsAllDataPointsWhenSeriesHasMultipleRows()
    {
        var sheetId = SheetId.New();
        // Mirrors chart21.xml: 1-column 2-row val-only range, no cat, no header (FirstColIsCategories=false)
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 31, 2), new CellAddress(sheetId, 32, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 31, 2, "6", new NumberValue(6)),
                ChartCell(sheetId, 32, 2, "4", new NumberValue(4))
            ]));

        // Should produce 1 series with 2 bars (not 1)
        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(2, "both data rows must be visible as separate bars");

        // The x-axis must span at least 0..1 so neither bar is clipped
        var xAxis = model.Axes.Single(a => a.Position == AxisPosition.Bottom);
        xAxis.Maximum.Should().BeGreaterThan(0.5,
            "axis maximum must cover both category positions (0 and 1)");
    }

    [Fact]
    public void ColumnRenderer_AppliesAxisTickPlacement()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            XAxisMajorTickStyle = ChartAxisTickStyle.Inside,
            XAxisMinorTickStyle = ChartAxisTickStyle.None,
            YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var xAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        xAxis.TickStyle.Should().Be(TickStyle.Inside);
        xAxis.MajorTickSize.Should().Be(4);
        xAxis.MinorTickSize.Should().Be(0);

        var yAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Left);
        yAxis.TickStyle.Should().Be(TickStyle.Crossing);
        yAxis.MajorTickSize.Should().Be(8);
        yAxis.MinorTickSize.Should().Be(4);
    }

    [Fact]
    public void ColumnRenderer_AppliesAxisLabelAngles()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            XAxisLabelAngle = -45,
            YAxisLabelAngle = 90
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Axes.Single(axis => axis.Position == AxisPosition.Bottom).Angle.Should().Be(-45);
        model.Axes.Single(axis => axis.Position == AxisPosition.Left).Angle.Should().Be(90);
    }

    [Fact]
    public void ScatterRenderer_AssignsRequestedSeriesToSecondaryAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1]
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

        model.Axes.Should().Contain(axis => axis.Key == "SecondaryY");
        var first = model.Series[0].Should().BeOfType<ScatterSeries>().Subject;
        var second = model.Series[1].Should().BeOfType<ScatterSeries>().Subject;
        first.YAxisKey.Should().BeNull();
        second.YAxisKey.Should().Be("SecondaryY");
    }

    [Fact]
    public void ColumnRenderer_DoesNotAddSecondaryAxisWhenNoSeriesUsesIt()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowSecondaryAxis = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle();
        model.Series.OfType<RectangleBarSeries>().Single().YAxisKey.Should().BeNull();
        model.Axes.Should().NotContain(axis => axis.Key == "SecondaryY");
    }

    [Fact]
    public void ColumnRenderer_DoesNotApplyLogScaleToCategoryXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisLogScale = true
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

        // R90-render-chart-axis-titles-5-2: the category axis is now a skip-aware LinearAxis subclass,
        // so assert what this test is really about -- that it was not swapped for a LogarithmicAxis.
        var categoryAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        categoryAxis.Should().BeAssignableTo<LinearAxis>();
        categoryAxis.Should().NotBeOfType<LogarithmicAxis>();
    }

    [Fact]
    public void ScatterRenderer_AppliesLogScaleToNumericXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisLogScale = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "10"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Axes.Single(axis => axis.Position == AxisPosition.Bottom)
            .Should().BeOfType<LogarithmicAxis>();
    }

    [Fact]
    public void ColumnRenderer_DoesNotApplyNumberFormatToCategoryXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisNumberFormat = ChartDataLabelNumberFormat.Currency
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

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        axis.FormatValue(0).Should().Be("Q1");
        axis.FormatValue(1).Should().Be("Q2");
    }

    [Fact]
    public void ScatterRenderer_AppliesNumberFormatToNumericXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "10"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        axis.FormatValue(10).Should().Be("$10.00");
    }
}
