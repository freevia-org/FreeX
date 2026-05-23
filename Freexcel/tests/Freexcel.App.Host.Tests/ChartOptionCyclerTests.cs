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
    [InlineData("3d line", ChartType.ThreeDLine)]
    [InlineData("3d area", ChartType.ThreeDArea)]
    [InlineData("3d pie", ChartType.ThreeDPie)]
    [InlineData("surface", ChartType.Surface)]
    [InlineData("surface chart", ChartType.Surface)]
    [InlineData("3d surface", ChartType.ThreeDSurface)]
    [InlineData("three-dimensional surface", ChartType.ThreeDSurface)]
    [InlineData("doughnut", ChartType.Doughnut)]
    [InlineData("donut", ChartType.Doughnut)]
    [InlineData("100% stacked bar", ChartType.PercentStackedBar)]
    [InlineData("stacked column", ChartType.StackedColumn)]
    [InlineData("unknown", ChartType.Column)]
    public void ParseChartType_MapsRibbonTextToChartType(string text, ChartType expected)
    {
        ChartOptionCycler.ParseChartType(text).Should().Be(expected);
    }
    [Fact]
    public void TryGetAxisBounds_UsesNumericChartDataAndExpandsSingleValueRanges()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var barChart = CreateChart(sheet.Id, ChartType.Bar, 1, 1, 4, 2);
        var lineChart = CreateChart(sheet.Id, ChartType.Line, 1, 4, 4, 5);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("ignored"));

        ChartOptionCycler.TryGetAxisBounds(sheet, barChart, useXAxis: true, out var xMinimum, out var xMaximum)
            .Should()
            .BeTrue();
        xMinimum.Should().Be(10);
        xMaximum.Should().Be(20);

        ChartOptionCycler.TryGetAxisBounds(sheet, lineChart, useXAxis: false, out var yMinimum, out var yMaximum)
            .Should()
            .BeTrue();
        yMinimum.Should().Be(6);
        yMaximum.Should().Be(8);
    }

    [Fact]
    public void GetNextSecondaryAxisSeries_CyclesEachSeriesThenClearsWhileKeepingAxisVisible()
    {
        var chart = CreateChart(SheetId.New(), ChartType.Column, 1, 1, 5, 4);

        var first = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, 3);
        first.ShowSecondaryAxis.Should().BeTrue();
        first.SeriesIndexes.Should().Equal(1);

        chart.ShowSecondaryAxis = true;
        chart.SecondaryAxisSeriesIndexes = [1];
        var second = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, 3);
        second.ShowSecondaryAxis.Should().BeTrue();
        second.SeriesIndexes.Should().Equal(2);

        chart.SecondaryAxisSeriesIndexes = [2];
        var clearedIndexes = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, 3);
        clearedIndexes.ShowSecondaryAxis.Should().BeTrue();
        clearedIndexes.SeriesIndexes.Should().BeEmpty();

        chart.SecondaryAxisSeriesIndexes = [];
        var clearedAxis = ChartOptionCycler.GetNextSecondaryAxisSeries(chart, 3);
        clearedAxis.ShowSecondaryAxis.Should().BeFalse();
        clearedAxis.SeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void GetNextComboLineSeries_CyclesSeriesAndClearsAtTheEnd()
    {
        var chart = CreateChart(SheetId.New(), ChartType.Column, 1, 1, 5, 4);

        ChartOptionCycler.GetNextComboLineSeries(chart, 3).Should().Equal(1);

        chart.UseComboLineForSecondarySeries = true;
        chart.ComboLineSeriesIndexes = [1];
        ChartOptionCycler.GetNextComboLineSeries(chart, 3).Should().Equal(2);

        chart.ComboLineSeriesIndexes = [2];
        ChartOptionCycler.GetNextComboLineSeries(chart, 3).Should().BeEmpty();
    }

    private static ChartModel CreateChart(SheetId sheetId, ChartType type, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new()
        {
            Type = type,
            DataRange = new GridRange(
                new CellAddress(sheetId, startRow, startCol),
                new CellAddress(sheetId, endRow, endCol))
        };
}
