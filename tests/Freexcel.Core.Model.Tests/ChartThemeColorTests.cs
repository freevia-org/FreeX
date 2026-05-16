using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ChartThemeColorTests
{
    [Fact]
    public void ChartModel_ResolvesThemeAreaAndLegendColorsOverRgbFallback()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 20, 30))
            .WithColor(WorkbookThemeColorSlot.Dark1, new CellColor(40, 50, 60));
        var chart = new ChartModel
        {
            ChartAreaFillColor = new CellColor(200, 200, 200),
            ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            LegendTextColor = new CellColor(210, 210, 210),
            LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1)
        };

        chart.ResolveChartAreaFillColor(theme).Should().Be(new CellColor(10, 20, 30));
        chart.ResolveLegendTextColor(theme).Should().Be(new CellColor(40, 50, 60));
    }

    [Fact]
    public void ChartSeriesFormat_ResolvesThemeColorsOverRgbFallback()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(70, 80, 90))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(100, 110, 120));
        var format = new ChartSeriesFormat(
            0,
            FillColor: new CellColor(200, 200, 200),
            StrokeColor: new CellColor(210, 210, 210),
            FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3));

        format.ResolveFillColor(theme).Should().Be(new CellColor(70, 80, 90));
        format.ResolveStrokeColor(theme).Should().Be(new CellColor(100, 110, 120));
    }
}
