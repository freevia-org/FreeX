using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    [Theory]
    [InlineData(ChartTrendlineType.Linear, "Linear (Revenue)")]
    [InlineData(ChartTrendlineType.Exponential, "Expon. (Revenue)")]
    [InlineData(ChartTrendlineType.Polynomial, "Poly. (Revenue)")]
    public void TrendlineLegendText_UsesExcelNameWithSeriesName(ChartTrendlineType type, string expected)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLinearTrendline = true,
            TrendlineType = type,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 1, 1, "Quarter"),
                ChartCell(sheetId, 1, 2, "Revenue"),
                ChartCell(sheetId, 2, 1, "Q1"),
                ChartCell(sheetId, 2, 2, "10", new NumberValue(10)),
                ChartCell(sheetId, 3, 1, "Q2"),
                ChartCell(sheetId, 3, 2, "20", new NumberValue(20)),
                ChartCell(sheetId, 4, 1, "Q3"),
                ChartCell(sheetId, 4, 2, "35", new NumberValue(35)),
            ]));

        model.Series.OfType<LineSeries>().Should().ContainSingle()
            .Which.Title.Should().Be(expected);
    }
}
