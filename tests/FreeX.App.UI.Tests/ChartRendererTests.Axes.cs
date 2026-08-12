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

    // R135-render-chart-secondary-axis-scale: ChartModel.SecondaryAxisMinimum/Maximum
    // (XlsxChartAxisReader.ApplySecondaryAxisProperties, XlsxChartAxisReader.cs:144-145) round-trips
    // but was never applied by ApplyAxisBounds -- the Left/Right branch applied the PRIMARY Y axis's
    // chart.YAxisMinimum/Maximum to every Left/Right axis unconditionally, including the secondary
    // one, so a combo chart with a primary axis fixed 0-10 and a secondary axis fixed 0-1000 drew the
    // secondary series against the primary's 0-10 scale, misrepresenting the data.
    [Fact]
    public void ColumnRenderer_SecondaryAxisMinMax_UsesOwnBoundsNotPrimarys()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            YAxisMinimum = 0,
            YAxisMaximum = 10,
            SecondaryAxisMinimum = 0,
            SecondaryAxisMaximum = 1000
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Units"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "5"),
                Cell(2, 3, "500"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "8"),
                Cell(3, 3, "900")
            ],
            [],
            []));

        var primaryAxis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Left).Subject;
        primaryAxis.Maximum.Should().Be(10);

        var secondaryAxis = model.Axes.Should().ContainSingle(a => a.Key == "SecondaryY").Subject;
        secondaryAxis.Minimum.Should().Be(0);
        secondaryAxis.Maximum.Should().Be(1000);
    }

    // Forward half of the R135 log-scale fix: ChartModel.SecondaryAxisLogScale must convert the
    // secondary axis to a LogarithmicAxis on its own, without the primary axis also requesting log
    // scale. Before the fix, ApplyAxisBounds's log-axis gate (ShouldUseLogAxis) only ever consulted
    // chart.YAxisLogScale for any Left/Right axis, so a secondary-only log request had no effect.
    [Fact]
    public void ColumnRenderer_SecondaryAxisLogScale_ConvertsSecondaryAxisIndependentlyOfPrimary()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SecondaryAxisLogScale = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Units"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "5"),
                Cell(2, 3, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "8"),
                Cell(3, 3, "100")
            ],
            [],
            []));

        model.Axes.Should().ContainSingle(a => a.Key == "SecondaryY" && a.GetType() == typeof(LogarithmicAxis));
        model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Left && a.GetType() != typeof(LogarithmicAxis));
    }

    // Reverse/leak half of the R135 log-scale fix: a PRIMARY axis log-scale request must not also
    // convert the secondary axis -- before the fix, ShouldUseLogAxis(chart, axis-with-Position.Right)
    // read chart.YAxisLogScale for the secondary axis too (its own SecondaryAxisLogScale was never
    // consulted at all), so setting only YAxisLogScale silently log-converted the secondary axis as
    // well.
    [Fact]
    public void ColumnRenderer_PrimaryAxisLogScale_DoesNotConvertSecondaryAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            YAxisLogScale = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Units"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "5"),
                Cell(2, 3, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "8"),
                Cell(3, 3, "100")
            ],
            [],
            []));

        var primaryAxis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Left).Subject;
        primaryAxis.Should().BeOfType<LogarithmicAxis>();

        var secondaryAxis = model.Axes.Should().ContainSingle(a => a.Key == "SecondaryY").Subject;
        secondaryAxis.Should().NotBeOfType<LogarithmicAxis>();
    }

    // Forward half of the R135 number-format fix: ChartModel.SecondaryAxisNumberFormat must drive the
    // secondary axis's tick LabelFormatter on its own. Before the fix, the Left/Right branch always
    // read chart.YAxisNumberFormat (General by default) for every Left/Right axis, so a
    // secondary-only Currency request never installed a LabelFormatter and ticks stayed unformatted.
    [Fact]
    public void ColumnRenderer_SecondaryAxisNumberFormat_AppliesIndependentlyOfPrimary()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SecondaryAxisNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Units"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "5"),
                Cell(2, 3, "1000"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "8"),
                Cell(3, 3, "2000")
            ],
            [],
            []));

        var secondaryAxis = model.Axes.Should().ContainSingle(a => a.Key == "SecondaryY").Subject;
        secondaryAxis.FormatValue(1000).Should().Be("$1,000.00");
    }

    // Reverse/leak half of the R135 number-format fix: a PRIMARY axis Currency format must not also
    // format the secondary axis's ticks -- before the fix, chart.YAxisNumberFormat was applied to
    // every Left/Right axis unconditionally, so setting only YAxisNumberFormat silently formatted the
    // secondary axis's ticks as currency too even though SecondaryAxisNumberFormat stayed General.
    [Fact]
    public void ColumnRenderer_PrimaryAxisNumberFormat_DoesNotLeakToSecondaryAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Units"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "5"),
                Cell(2, 3, "1000"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "8"),
                Cell(3, 3, "2000")
            ],
            [],
            []));

        var secondaryAxis = model.Axes.Should().ContainSingle(a => a.Key == "SecondaryY").Subject;
        secondaryAxis.FormatValue(1000).Should().NotStartWith("$");
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

    // R135-render-chart-axis-numfmt-parity: chart.YAxisNumberFormat/XAxisNumberFormat is a coarse
    // enum (ChartDataLabelTextPlanner.FormatAxisValue only knows General/Number/Currency/Percent).
    // The actual raw OOXML format code is preserved separately on YAxisNumberFormatCode/
    // XAxisNumberFormatCode (populated from <c:numFmt formatCode="..."/> by
    // XlsxChartAxisReader.FromXlsxNumberFormatCode, which itself only maps 3 exact literal codes to
    // a non-General bucket and silently drops every other real-world code -- e.g. a plain thousands
    // separator like "#,##0" -- to General). Before this fix, the WPF value-axis LabelFormatter only
    // ever consulted the coarse enum and never looked at the raw code at all, so it rendered plain
    // "1234" instead of Excel's "1,234" here. The portable/Avalonia layout
    // (ChartLayoutEngine.BuildValueAxisLayout) already prefers the raw code through the shared
    // NumberFormatter; this pins the WPF renderer routing through the same formatter for parity.
    [Fact]
    public void ColumnRenderer_AppliesRawNumberFormatCodeToYAxis_MatchingAvaloniaFormatting()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            YAxisNumberFormatCode = "#,##0"
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "1000"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "3000")
            ],
            [],
            []));

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Left);
        axis.FormatValue(1234).Should().Be("1,234");
    }

    // Sibling of ColumnRenderer_AppliesRawNumberFormatCodeToYAxis_MatchingAvaloniaFormatting: the
    // X-axis block in ChartRenderer.Axes.cs mirrors the Y-axis block line-for-line and carried the
    // identical gap (it never consulted XAxisNumberFormatCode either) -- covers the X-axis twin so
    // the fix isn't verified on only one of the two near-duplicated code paths.
    [Fact]
    public void ScatterRenderer_AppliesRawNumberFormatCodeToXAxis_MatchingAvaloniaFormatting()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisNumberFormatCode = "#,##0"
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1000"),
                Cell(2, 2, "10"),
                Cell(3, 1, "3000"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        axis.FormatValue(1234).Should().Be("1,234");
    }

    // R131-render-chart-date-category-axis: XAxisIsDateAxis was parsed/round-tripped
    // (XlsxChartAxisReader/XlsxChartXmlWriter.Axes.cs) but BuildPlotModel always built an evenly
    // spaced 0,1,2… indexed category axis for Column/Area/Line charts regardless, so unevenly spaced
    // dates (Jan 1, Jan 2, Jan 10) plotted with equal gaps instead of Excel's proportional Date Axis
    // spacing. This test pins the actual plotted X positions for an uneven date series.
    [Fact]
    public void ColumnRenderer_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisIsDateAxis = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "2026-01-01"),
                Cell(2, 2, "10"),
                Cell(3, 1, "2026-01-02"),
                Cell(3, 2, "20"),
                Cell(4, 1, "2026-01-10"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().BeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(3);

        var expectedDay1 = DateTimeAxis.ToDouble(new DateTime(2026, 1, 1));
        var expectedDay2 = DateTimeAxis.ToDouble(new DateTime(2026, 1, 2));
        var expectedDay10 = DateTimeAxis.ToDouble(new DateTime(2026, 1, 10));

        // The bar's center (midpoint of its X0/X1 half-width) must sit at the date's actual
        // proportional position -- NOT at the plain category index (0, 1, 2), which would collapse
        // the real 1-day/8-day gaps into two equal-looking 1-unit gaps.
        ((series.Items[0].X0 + series.Items[0].X1) / 2).Should().BeApproximately(expectedDay1, 0.01);
        ((series.Items[1].X0 + series.Items[1].X1) / 2).Should().BeApproximately(expectedDay2, 0.01);
        ((series.Items[2].X0 + series.Items[2].X1) / 2).Should().BeApproximately(expectedDay10, 0.01);

        // The gap between the 1st and 2nd bar (1 day) must be far smaller than the gap between the
        // 2nd and 3rd bar (8 days) -- an evenly spaced index axis would make both gaps equal (1 unit).
        var firstGap = expectedDay2 - expectedDay1;
        var secondGap = expectedDay10 - expectedDay2;
        secondGap.Should().BeApproximately(8 * firstGap, 0.01);
    }

    // Sibling of ColumnRenderer_DateCategoryAxis_PlotsProportionalToActualDates: a chart that never
    // opted into a date axis (XAxisIsDateAxis stays at its ChartModel default of false) must keep
    // rendering its plain text categories on the original evenly spaced 0,1,2… indexed axis --
    // proving the date-axis fix cannot widen past its own XAxisIsDateAxis guard.
    [Fact]
    public void ColumnRenderer_PlainTextCategoryAxis_StaysEvenlySpacedIndexAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "North"),
                Cell(2, 2, "10"),
                Cell(3, 1, "South"),
                Cell(3, 2, "20"),
                Cell(4, 1, "East"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().NotBeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(3);
        ((series.Items[0].X0 + series.Items[0].X1) / 2).Should().BeApproximately(0, 0.01);
        ((series.Items[1].X0 + series.Items[1].X1) / 2).Should().BeApproximately(1, 0.01);
        ((series.Items[2].X0 + series.Items[2].X1) / 2).Should().BeApproximately(2, 0.01);
    }

    // A chart marked as a date axis but whose category text isn't actually parseable as dates must
    // fall back to the plain indexed axis rather than throw or silently misplace every point.
    [Fact]
    public void ColumnRenderer_DateAxisFlagWithUnparsableCategories_FallsBackToIndexAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            XAxisIsDateAxis = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Label"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Alpha"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Beta"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().NotBeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        ((series.Items[0].X0 + series.Items[0].X1) / 2).Should().BeApproximately(0, 0.01);
        ((series.Items[1].X0 + series.Items[1].X1) / 2).Should().BeApproximately(1, 0.01);
    }

    // R131-render-chart-date-category-axis (Line-chart family member): the same date-axis fix must
    // also apply to Line charts, not just Column -- AddLinePoints threads the same date-proportional
    // X positions through, so an unevenly spaced date series draws its connecting line at the right
    // shape instead of an evenly spaced one.
    [Fact]
    public void LineRenderer_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            XAxisIsDateAxis = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "2026-01-01"),
                Cell(2, 2, "10"),
                Cell(3, 1, "2026-01-02"),
                Cell(3, 2, "20"),
                Cell(4, 1, "2026-01-10"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().BeOfType<DateTimeAxis>();

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.Points.Should().HaveCount(3);
        series.Points[0].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 1)), 0.01);
        series.Points[1].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 2)), 0.01);
        series.Points[2].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 10)), 0.01);
    }

    // R131-render-chart-axis-crosses: XAxisCrosses/YAxisCrosses were parsed/round-tripped
    // (XlsxChartAxisReader/XlsxChartXmlWriter.Axes.cs) but ApplyAxisBounds never consulted them, so a
    // Bar chart's value axis (physically drawn Bottom, using chart.XAxisCrosses -- see the axis
    // created inline in the Bar branch of BuildPlotModel) explicitly set to "crosses at maximum
    // category" still always drew at the bottom edge. This pins that it now flips to the top.
    [Fact]
    public void BarRenderer_ValueAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            XAxisCrosses = ChartAxisCrosses.Maximum,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
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

        model.Axes.Should().ContainSingle(a => a is LinearAxis && a.Position == AxisPosition.Top);
        model.Axes.Should().NotContain(a => a.Position == AxisPosition.Bottom);
    }

    // Sibling of BarRenderer_ValueAxisCrossesAtMaximum_MovesAxisToOppositeEdge: the overwhelming
    // majority of charts never set XAxisCrosses explicitly, leaving it at ChartModel's own default
    // (AutoZero). That default must keep rendering at the original bottom edge exactly as before --
    // proving the crosses fix only reacts to an explicit Maximum, not the common default.
    [Fact]
    public void BarRenderer_ValueAxisCrossesDefaultAutoZero_StaysAtBottomEdge()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };
        chart.XAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);

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

        model.Axes.Should().ContainSingle(a => a is LinearAxis && a.Position == AxisPosition.Bottom);
        model.Axes.Should().NotContain(a => a.Position == AxisPosition.Top);
    }
}
