using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ChartOptionCyclerTests
{
    [Theory]
    [InlineData(ChartDataLabelPosition.BestFit, ChartDataLabelPosition.OutsideEnd)]
    [InlineData(ChartDataLabelPosition.OutsideEnd, ChartDataLabelPosition.InsideEnd)]
    [InlineData(ChartDataLabelPosition.InsideEnd, ChartDataLabelPosition.Center)]
    [InlineData(ChartDataLabelPosition.Center, ChartDataLabelPosition.BestFit)]
    public void NextDataLabelPosition_CyclesExcelLikePositions(ChartDataLabelPosition current, ChartDataLabelPosition expected)
    {
        ChartOptionCycler.NextDataLabelPosition(current).Should().Be(expected);
    }

    [Theory]
    [InlineData(ChartDataLabelNumberFormat.General, ChartDataLabelNumberFormat.Number)]
    [InlineData(ChartDataLabelNumberFormat.Number, ChartDataLabelNumberFormat.Currency)]
    [InlineData(ChartDataLabelNumberFormat.Currency, ChartDataLabelNumberFormat.Percent)]
    [InlineData(ChartDataLabelNumberFormat.Percent, ChartDataLabelNumberFormat.General)]
    public void NextDataLabelNumberFormat_CyclesCommonExcelFormats(ChartDataLabelNumberFormat current, ChartDataLabelNumberFormat expected)
    {
        ChartOptionCycler.NextDataLabelNumberFormat(current).Should().Be(expected);
    }

    [Theory]
    [InlineData(ChartTrendlineType.Linear, ChartTrendlineType.Exponential)]
    [InlineData(ChartTrendlineType.Exponential, ChartTrendlineType.Logarithmic)]
    [InlineData(ChartTrendlineType.Logarithmic, ChartTrendlineType.Power)]
    [InlineData(ChartTrendlineType.Power, ChartTrendlineType.MovingAverage)]
    [InlineData(ChartTrendlineType.MovingAverage, ChartTrendlineType.Polynomial)]
    [InlineData(ChartTrendlineType.Polynomial, ChartTrendlineType.Linear)]
    public void NextTrendlineType_CyclesSupportedTrendlineTypes(ChartTrendlineType current, ChartTrendlineType expected)
    {
        ChartOptionCycler.NextTrendlineType(current).Should().Be(expected);
    }

    [Fact]
    public void NextAxisTickState_CyclesMajorAndMinorTickCombinations()
    {
        ChartOptionCycler.NextAxisTickState(ChartAxisTickStyle.Outside, ChartAxisTickStyle.None)
            .Should()
            .Be((ChartAxisTickStyle.Inside, ChartAxisTickStyle.None));
        ChartOptionCycler.NextAxisTickState(ChartAxisTickStyle.Inside, ChartAxisTickStyle.None)
            .Should()
            .Be((ChartAxisTickStyle.Cross, ChartAxisTickStyle.Inside));
        ChartOptionCycler.NextAxisTickState(ChartAxisTickStyle.Cross, ChartAxisTickStyle.Inside)
            .Should()
            .Be((ChartAxisTickStyle.None, ChartAxisTickStyle.None));
        ChartOptionCycler.NextAxisTickState(ChartAxisTickStyle.None, ChartAxisTickStyle.None)
            .Should()
            .Be((ChartAxisTickStyle.Outside, ChartAxisTickStyle.None));
    }

    [Theory]
    [InlineData(0, -45)]
    [InlineData(-45, 45)]
    [InlineData(45, 90)]
    [InlineData(90, 0)]
    public void NextAxisLabelAngle_CyclesThroughExcelStyleAngles(double current, double expected)
    {
        ChartOptionCycler.NextAxisLabelAngle(current).Should().Be(expected);
    }

    [Fact]
    public void NextGridlineState_CyclesOffMajorMajorMinor()
    {
        ChartOptionCycler.NextGridlineState(false, false).Should().Be((true, false));
        ChartOptionCycler.NextGridlineState(true, false).Should().Be((true, true));
        ChartOptionCycler.NextGridlineState(true, true).Should().Be((false, false));
    }

    [Fact]
    public void NextSeriesColor_CyclesThroughAccessiblePalette()
    {
        var blue = new CellColor(0, 114, 178);
        var orange = new CellColor(213, 94, 0);
        var green = new CellColor(0, 158, 115);

        ChartOptionCycler.NextSeriesColor(null).Should().Be(blue);
        ChartOptionCycler.NextSeriesColor(blue).Should().Be(orange);
        ChartOptionCycler.NextSeriesColor(orange).Should().Be(green);
        ChartOptionCycler.NextSeriesColor(green).Should().Be(blue);
    }

    [Theory]
    [InlineData("doughnut", ChartType.Doughnut)]
    [InlineData("donut", ChartType.Doughnut)]
    [InlineData("100% stacked bar", ChartType.PercentStackedBar)]
    [InlineData("stacked column", ChartType.StackedColumn)]
    [InlineData("unknown", ChartType.Column)]
    public void ParseChartType_MapsRibbonTextToChartType(string text, ChartType expected)
    {
        ChartOptionCycler.ParseChartType(text).Should().Be(expected);
    }
}
