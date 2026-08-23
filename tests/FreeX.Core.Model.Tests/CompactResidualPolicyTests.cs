using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class CompactResidualPolicyTests
{
    public static TheoryData<ConsolidateFunction, double> ConsolidationCases => new()
    {
        { ConsolidateFunction.Sum, 40 },
        { ConsolidateFunction.Count, 10 },
        { ConsolidateFunction.Average, 5 },
        { ConsolidateFunction.Max, 9 },
        { ConsolidateFunction.Min, 2 },
        { ConsolidateFunction.Product, 201_600 },
        { ConsolidateFunction.CountNumbers, 8 },
        { ConsolidateFunction.StdDev, Math.Sqrt(32.0 / 7.0) },
        { ConsolidateFunction.StdDevp, 2 },
        { ConsolidateFunction.Var, 32.0 / 7.0 },
        { ConsolidateFunction.Varp, 4 }
    };

    [Theory]
    [MemberData(nameof(ConsolidationCases))]
    public void ConsolidationRules_Aggregate_CoversEveryFunction(
        ConsolidateFunction function,
        double expected)
    {
        double[] values = [2, 4, 4, 4, 5, 5, 7, 9];

        ConsolidationRules.Aggregate(values, nonEmptyCount: 10, function)
            .Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData(ConsolidateFunction.Sum, 0)]
    [InlineData(ConsolidateFunction.Count, 3)]
    [InlineData(ConsolidateFunction.Average, 0)]
    [InlineData(ConsolidateFunction.Max, 0)]
    [InlineData(ConsolidateFunction.Min, 0)]
    [InlineData(ConsolidateFunction.Product, 0)]
    [InlineData(ConsolidateFunction.CountNumbers, 0)]
    [InlineData(ConsolidateFunction.StdDev, 0)]
    [InlineData(ConsolidateFunction.StdDevp, 0)]
    [InlineData(ConsolidateFunction.Var, 0)]
    [InlineData(ConsolidateFunction.Varp, 0)]
    public void ConsolidationRules_Aggregate_PreservesEmptyAndCountSemantics(
        ConsolidateFunction function,
        double expected)
    {
        ConsolidationRules.Aggregate([], nonEmptyCount: 3, function).Should().Be(expected);
    }

    [Fact]
    public void ConsolidationRules_Aggregate_UsesSumFallbackAndRejectsNull()
    {
        ConsolidationRules.Aggregate([2, 3], 2, (ConsolidateFunction)int.MaxValue).Should().Be(5);
        var action = () => ConsolidationRules.Aggregate(null!, 0, ConsolidateFunction.Sum);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ChartTrendlinePolicy_CommonReset_PreservesLabelAndExtendedState()
    {
        var chart = SeedTrendlineChart(ChartType.Pie);

        ChartTrendlineSupportPolicy.NormalizeUnsupported(chart);

        AssertCommonTrendlineDefaults(chart);
        AssertLabelStatePreserved(chart);
        AssertExtendedStatePreserved(chart);
    }

    [Fact]
    public void ChartTrendlinePolicy_LabelReset_PreservesExtendedState()
    {
        var chart = SeedTrendlineChart(ChartType.Pie);

        ChartTrendlineSupportPolicy.NormalizeUnsupported(
            chart,
            UnsupportedChartTrendlineState.LabelFormatting);

        AssertCommonTrendlineDefaults(chart);
        AssertLabelStateCleared(chart);
        AssertExtendedStatePreserved(chart);
    }

    [Fact]
    public void ChartTrendlinePolicy_ExtendedReset_ClearsOnlyRequestedExtendedState()
    {
        var chart = SeedTrendlineChart(ChartType.Pie);

        ChartTrendlineSupportPolicy.NormalizeUnsupported(
            chart,
            UnsupportedChartTrendlineState.ExtendedDefinition);

        AssertCommonTrendlineDefaults(chart);
        AssertLabelStatePreserved(chart);
        AssertExtendedStateCleared(chart);
    }

    [Fact]
    public void ChartTrendlinePolicy_FullReset_ClearsLabelAndExtendedState()
    {
        var chart = SeedTrendlineChart(ChartType.Pie);

        ChartTrendlineSupportPolicy.NormalizeUnsupported(
            chart,
            UnsupportedChartTrendlineState.LabelFormatting |
            UnsupportedChartTrendlineState.ExtendedDefinition);

        AssertCommonTrendlineDefaults(chart);
        AssertLabelStateCleared(chart);
        AssertExtendedStateCleared(chart);
        chart.TrendlineSeriesIndex.Should().Be(4);
    }

    [Fact]
    public void ChartTrendlinePolicy_SupportedChart_IsUnchanged()
    {
        var chart = SeedTrendlineChart(ChartType.Line);

        ChartTrendlineSupportPolicy.NormalizeUnsupported(
            chart,
            UnsupportedChartTrendlineState.LabelFormatting |
            UnsupportedChartTrendlineState.ExtendedDefinition);

        chart.ShowLinearTrendline.Should().BeTrue();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Polynomial);
        chart.TrendlineName.Should().Be("Forecast");
        chart.TrendlineLabelNumberFormatCode.Should().Be("0.00");
        chart.TrendlineColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void ChartTrendlinePolicy_RejectsNull()
    {
        var action = () => ChartTrendlineSupportPolicy.NormalizeUnsupported(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PivotTargetRangeResolver_ReturnsSparseOccupiedBounds_AndIgnoresOutsideCells()
    {
        var sheet = new Sheet(SheetId.New(), "Pivot");
        var pivot = Pivot(sheet, startRow: 3, startCol: 4, endRow: 9, endCol: 10);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), BlankValue.Instance);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 5), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 11), new NumberValue(3));

        var result = PivotTableTargetRangeResolver.GetOccupiedRange(sheet, pivot);

        result.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 4, 5),
            new CellAddress(sheet.Id, 8, 9)));
    }

    [Fact]
    public void PivotTargetRangeResolver_EmptyRange_FallsBackToTargetStartOnTargetSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Pivot");
        var pivot = Pivot(sheet, startRow: 3, startCol: 4, endRow: 9, endCol: 10);

        PivotTableTargetRangeResolver.GetOccupiedRange(sheet, pivot).Should().Be(
            new GridRange(pivot.TargetRange.Start, pivot.TargetRange.Start));
    }

    [Fact]
    public void PivotTargetRangeResolver_RejectsNullArguments()
    {
        var sheet = new Sheet(SheetId.New(), "Pivot");
        var pivot = Pivot(sheet, 1, 1, 2, 2);

        Action nullSheet = () => PivotTableTargetRangeResolver.GetOccupiedRange(null!, pivot);
        Action nullPivot = () => PivotTableTargetRangeResolver.GetOccupiedRange(sheet, null!);

        nullSheet.Should().Throw<ArgumentNullException>();
        nullPivot.Should().Throw<ArgumentNullException>();
    }

    private static ChartModel SeedTrendlineChart(ChartType type) => new()
    {
        Type = type,
        ShowLinearTrendline = true,
        TrendlineSeriesIndex = 4,
        TrendlineName = "Forecast",
        TrendlineType = ChartTrendlineType.Polynomial,
        TrendlinePeriod = 7,
        TrendlineOrder = 5,
        TrendlineForward = 2,
        TrendlineBackward = 3,
        TrendlineIntercept = 4,
        ShowTrendlineEquation = true,
        ShowTrendlineRSquared = true,
        TrendlineLabelNumberFormatCode = "0.00",
        TrendlineLabelNumberFormatSourceLinked = true,
        TrendlineLabelLayout = new ChartManualLayoutModel { X = 0.2 },
        TrendlineLabelFillColor = new CellColor(4, 5, 6),
        TrendlineLabelFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
        TrendlineLabelBorderColor = new CellColor(7, 8, 9),
        TrendlineLabelBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
        TrendlineLabelBorderThickness = 2,
        TrendlineLabelTextColor = new CellColor(10, 11, 12),
        TrendlineLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3),
        TrendlineLabelFontSize = 14,
        TrendlineLabelAngle = 15,
        TrendlineColor = new CellColor(1, 2, 3),
        TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
        TrendlineThickness = 4,
        TrendlineDashStyle = ChartLineDashStyle.Dot
    };

    private static void AssertCommonTrendlineDefaults(ChartModel chart)
    {
        chart.ShowLinearTrendline.Should().BeFalse();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlinePeriod.Should().Be(2);
        chart.TrendlineOrder.Should().Be(2);
        chart.ShowTrendlineEquation.Should().BeFalse();
        chart.ShowTrendlineRSquared.Should().BeFalse();
        chart.TrendlineColor.Should().BeNull();
        chart.TrendlineThemeColor.Should().BeNull();
        chart.TrendlineThickness.Should().Be(1.5);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    private static void AssertLabelStatePreserved(ChartModel chart)
    {
        chart.TrendlineLabelNumberFormatCode.Should().Be("0.00");
        chart.TrendlineLabelNumberFormatSourceLinked.Should().BeTrue();
        chart.TrendlineLabelLayout.Should().NotBeNull();
        chart.TrendlineLabelFillColor.Should().NotBeNull();
        chart.TrendlineLabelFillThemeColor.Should().NotBeNull();
        chart.TrendlineLabelBorderColor.Should().NotBeNull();
        chart.TrendlineLabelBorderThemeColor.Should().NotBeNull();
        chart.TrendlineLabelBorderThickness.Should().Be(2);
        chart.TrendlineLabelTextColor.Should().NotBeNull();
        chart.TrendlineLabelTextThemeColor.Should().NotBeNull();
        chart.TrendlineLabelFontSize.Should().Be(14);
        chart.TrendlineLabelAngle.Should().Be(15);
    }

    private static void AssertLabelStateCleared(ChartModel chart)
    {
        chart.TrendlineLabelNumberFormatCode.Should().BeNull();
        chart.TrendlineLabelNumberFormatSourceLinked.Should().BeNull();
        chart.TrendlineLabelLayout.Should().BeNull();
        chart.TrendlineLabelFillColor.Should().BeNull();
        chart.TrendlineLabelFillThemeColor.Should().BeNull();
        chart.TrendlineLabelBorderColor.Should().BeNull();
        chart.TrendlineLabelBorderThemeColor.Should().BeNull();
        chart.TrendlineLabelBorderThickness.Should().BeNull();
        chart.TrendlineLabelTextColor.Should().BeNull();
        chart.TrendlineLabelTextThemeColor.Should().BeNull();
        chart.TrendlineLabelFontSize.Should().BeNull();
        chart.TrendlineLabelAngle.Should().BeNull();
    }

    private static void AssertExtendedStatePreserved(ChartModel chart)
    {
        chart.TrendlineName.Should().Be("Forecast");
        chart.TrendlineForward.Should().Be(2);
        chart.TrendlineBackward.Should().Be(3);
        chart.TrendlineIntercept.Should().Be(4);
    }

    private static void AssertExtendedStateCleared(ChartModel chart)
    {
        chart.TrendlineName.Should().BeNull();
        chart.TrendlineForward.Should().BeNull();
        chart.TrendlineBackward.Should().BeNull();
        chart.TrendlineIntercept.Should().BeNull();
    }

    private static PivotTableModel Pivot(
        Sheet sheet,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) => new()
    {
        SourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2)),
        TargetRange = new GridRange(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol))
    };
}
