using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class StockLayoutTests
{
    [Fact]
    public void High_low_close_produces_one_element_per_category_without_open()
    {
        var chart = Chart(ChartType.Stock, c => c.StockSubtype = StockChartSubtype.HighLowClose);
        var series = StockSeries(0, high: [12, 14], low: [8, 9], close: [10, 13]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D1", "D2"], [series]));

        var s = layout.Series.Should().ContainSingle().Subject;
        s.Kind.Should().Be(SeriesGeometryKind.StockBars);
        s.StockElements.Should().HaveCount(2);
        s.StockElements.Should().OnlyContain(e => !e.HasOpen);
    }

    [Fact]
    public void Open_high_low_close_marks_elements_as_having_open()
    {
        var chart = Chart(ChartType.Stock, c => c.StockSubtype = StockChartSubtype.OpenHighLowClose);
        var series = StockSeries(0, high: [12], low: [8], close: [11], open: [9]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D1"], [series]));

        var e = layout.Series[0].StockElements.Should().ContainSingle().Subject;
        e.HasOpen.Should().BeTrue();
        e.OpenValue.Should().Be(9);
        e.CloseValue.Should().Be(11);
        e.IsUp.Should().BeTrue(); // close above open
    }

    [Fact]
    public void High_is_above_low_on_screen()
    {
        var chart = Chart(ChartType.Stock);
        var series = StockSeries(0, high: [20], low: [10], close: [15]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D1"], [series]));

        var e = layout.Series[0].StockElements[0];
        // y grows downward on screen, so the high value has the smaller Y.
        e.HighY.Should().BeLessThan(e.LowY);
        e.HighValue.Should().Be(20);
        e.LowValue.Should().Be(10);
    }

    [Fact]
    public void Close_between_high_and_low_lands_inside_the_vertical_line()
    {
        var chart = Chart(ChartType.Stock);
        var series = StockSeries(0, high: [20], low: [10], close: [12]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D1"], [series]));

        var e = layout.Series[0].StockElements[0];
        e.CloseY.Should().BeInRange(Math.Min(e.HighY, e.LowY), Math.Max(e.HighY, e.LowY));
    }

    [Fact]
    public void Down_bar_is_flagged_when_close_below_open()
    {
        var chart = Chart(ChartType.Stock);
        var series = StockSeries(0, high: [20], low: [10], close: [11], open: [18]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D1"], [series]));

        layout.Series[0].StockElements[0].IsUp.Should().BeFalse();
    }

    [Fact]
    public void Elements_share_distinct_x_positions_per_category()
    {
        var chart = Chart(ChartType.Stock);
        var series = StockSeries(0, high: [20, 22, 24], low: [10, 11, 12], close: [15, 16, 17]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        var xs = layout.Series[0].StockElements.Select(e => e.X).ToArray();
        xs.Should().OnlyHaveUniqueItems();
        xs[0].Should().BeLessThan(xs[1]);
        xs[1].Should().BeLessThan(xs[2]);
    }

    [Fact]
    public void Missing_high_or_low_yields_no_elements()
    {
        var chart = Chart(ChartType.Stock);
        var series = new ChartSeriesData
        {
            SeriesIndex = 0,
            Name = "Stock",
            Values = [10d],
            // No HighValues/LowValues supplied.
        };
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D1"], [series]));
        layout.Series[0].StockElements.Should().BeEmpty();
    }
}
