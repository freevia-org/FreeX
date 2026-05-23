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
            .WithColor(WorkbookThemeColorSlot.Dark1, new CellColor(40, 50, 60))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(70, 80, 90))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(100, 110, 120))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(130, 140, 150))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(160, 170, 180));
        var chart = new ChartModel
        {
            ChartAreaFillColor = new CellColor(200, 200, 200),
            ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            LegendTextColor = new CellColor(210, 210, 210),
            LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            ChartTitleTextColor = new CellColor(220, 220, 220),
            ChartTitleTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            AxisTitleTextColor = new CellColor(230, 230, 230),
            AxisTitleTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3),
            XAxisLabelTextColor = new CellColor(240, 240, 240),
            XAxisLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
            YAxisLabelTextColor = new CellColor(250, 250, 250),
            YAxisLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5)
        };

        chart.ResolveChartAreaFillColor(theme).Should().Be(new CellColor(10, 20, 30));
        chart.ResolveLegendTextColor(theme).Should().Be(new CellColor(40, 50, 60));
        chart.ResolveChartTitleTextColor(theme).Should().Be(new CellColor(70, 80, 90));
        chart.ResolveAxisTitleTextColor(theme).Should().Be(new CellColor(100, 110, 120));
        chart.ResolveXAxisLabelTextColor(theme).Should().Be(new CellColor(130, 140, 150));
        chart.ResolveYAxisLabelTextColor(theme).Should().Be(new CellColor(160, 170, 180));
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
