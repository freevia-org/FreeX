using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Layout regressions for the chart types whose Excel rendering was measured through COM: column + area
/// combos, the waterfall legend/colours, the histogram bins and the stock legend.
/// </summary>
public sealed class ChartTypesLeftoversLayoutTests
{
    [Fact]
    public void ColumnAreaCombo_DrawsTheAreaSeriesAsAreaBehindTheColumns()
    {
        var chart = Chart(ChartType.Column, c => c.ComboAreaSeriesIndexes = [1]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"],
            [Series(0, "North", 12, 19, 14), Series(1, "South", 8, 15, 21)]));

        layout.Series.Should().HaveCount(2);
        // z-order: area first (behind), columns after.
        layout.Series[0].SeriesIndex.Should().Be(1);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Area);
        layout.Series[1].SeriesIndex.Should().Be(0);
        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.Columns);

        // The remaining column series owns the whole category slot (the area is not a clustered slot).
        var single = ChartLayoutEngine.Layout(Request(Chart(ChartType.Column), ["A", "B", "C"], [Series(0, "North", 12, 19, 14)]));
        layout.Series[1].Bars[1].Rect.Width.Should().BeApproximately(single.Series[0].Bars[1].Rect.Width, 0.5);
    }

    [Fact]
    public void ColumnAreaCombo_ListsTheAreaSeriesFirstInTheLegend()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ComboAreaSeriesIndexes = [1];
            c.SeriesPlotOrder = [1, 0];
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Bottom;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "North", 12, 19), Series(1, "South", 8, 15)]));

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("South", "North");
    }

    [Fact]
    public void Waterfall_LegendListsIncreaseDecreaseTotalWithAccentColours()
    {
        var chart = Chart(ChartType.Waterfall, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Top;
            c.WaterfallTotalPointIndices = [];
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["Start", "Cost", "Sales", "Fees", "Total"],
            [Series(0, "Amount", 100, -30, 45, -20, 60)]));

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("Increase", "Decrease", "Total");
        layout.Legend.Entries.Select(e => e.WaterfallKind).Should().Equal(
            WaterfallBarKind.Increase, WaterfallBarKind.Decrease, WaterfallBarKind.Total);

        // Excel (default Office theme): Increase 156082, Decrease E97132, Total 196B24 (accents 1-3).
        ChartRenderPolicyPlanner.WaterfallIncreaseColor.Should().Be(new CellColor(0x15, 0x60, 0x82));
        ChartRenderPolicyPlanner.WaterfallDecreaseColor.Should().Be(new CellColor(0xE9, 0x71, 0x32));
        ChartRenderPolicyPlanner.WaterfallTotalColor.Should().Be(new CellColor(0x19, 0x6B, 0x24));
    }

    [Fact]
    public void Waterfall_WithoutTotals_TreatsTheLastColumnAsAPlainIncrease()
    {
        var chart = Chart(ChartType.Waterfall, c => c.WaterfallTotalPointIndices = []);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["Start", "Cost", "Sales", "Fees", "Total"],
            [Series(0, "Amount", 100, -30, 45, -20, 60)]));

        layout.Series[0].Bars.Select(b => b.WaterfallKind).Should().Equal(
            WaterfallBarKind.Increase, WaterfallBarKind.Decrease, WaterfallBarKind.Increase,
            WaterfallBarKind.Decrease, WaterfallBarKind.Increase);
    }

    [Fact]
    public void Histogram_UsesExcelAutomaticBinsAndNoDefaultValueAxisTitle()
    {
        double[] values = [2, 3, 3, 4, 4, 4, 5, 5, 6, 7, 7, 8, 9, 9, 10, 11, 12, 12, 15, 18];
        var chart = Chart(ChartType.Histogram);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [new ChartSeriesData
        {
            SeriesIndex = 0,
            Name = "Value",
            Values = values.Select(v => (double?)v).ToList(),
        }]));

        layout.CategoryAxis!.Ticks.Select(t => t.Label).Should().Equal("[2, 7.6]", "(7.6, 13.2]", "(13.2, 18.8]");
        layout.Series[0].Bars.Select(b => b.Value).Should().Equal(11, 7, 2);
        layout.ValueAxis!.Title.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Stock_LegendListsTheColumnsByNameWithoutSwatches()
    {
        var chart = Chart(ChartType.Stock, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Bottom;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["Q1", "Q2"], [new ChartSeriesData
        {
            SeriesIndex = 0,
            Name = "Stock",
            Values = new double?[] { 22, 21 },
            HighValues = new double?[] { 25, 27 },
            LowValues = new double?[] { 18, 19 },
            StockLegendNames = ["High", "Low", "Close"],
        }]));

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("High", "Low", "Close");
        layout.Legend.Entries.Should().OnlyContain(e => e.HideSwatch);
    }
}
