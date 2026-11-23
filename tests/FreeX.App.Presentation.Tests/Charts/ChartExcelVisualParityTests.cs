using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Pins portable chart geometry to what real Microsoft Excel draws. Every expected number was measured on
/// a chart Excel rendered (<c>Chart.Export</c> PNG at 962 px for a 640 px wide chart, so one category slot
/// is 178 px; bar widths are the exact pixel runs of the series fill colour) or read through COM.
/// </summary>
public sealed class ChartExcelVisualParityTests
{
    private const double ExcelSlotPixels = 178.0;

    [Theory]
    // Excel default clustered column (gapWidth 219, overlap -27), one series: 56 px bars.
    [InlineData(1, 219, -27, 56.0)]
    // Same defaults, three series: 31 px bars with 39.5 px pitch.
    [InlineData(3, 219, -27, 31.0)]
    // gapWidth 50, overlap -20, three series: 46 px bars.
    [InlineData(3, 50, -20, 46.0)]
    public void Clustered_bar_width_matches_excel(int seriesCount, int gapWidth, int overlap, double excelBarPixels)
    {
        var halfWidth = 0.5 * 100.0 / (100.0 + gapWidth);

        var (left, right) = ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(halfWidth, 0, seriesCount, overlap);

        ((right - left) * ExcelSlotPixels).Should().BeApproximately(excelBarPixels, 0.7);
    }

    [Fact]
    public void Clustered_bars_are_spaced_by_the_overlap_and_centred_in_the_slot_like_excel()
    {
        const int gapWidth = 219;
        const int overlap = -27;
        var halfWidth = 0.5 * 100.0 / (100.0 + gapWidth);

        var bars = Enumerable.Range(0, 3)
            .Select(i => ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(halfWidth, i, 3, overlap))
            .ToArray();

        // Excel pixel starts of the three bars in one slot: 83, 122, 162 -> pitch 39.5 px.
        ((bars[1].Left - bars[0].Left) * ExcelSlotPixels).Should().BeApproximately(39.5, 0.8);
        ((bars[2].Left - bars[1].Left) * ExcelSlotPixels).Should().BeApproximately(39.5, 0.8);
        (bars[0].Left + bars[2].Right).Should().BeApproximately(0, 1e-9, "the cluster is centred in its category slot");
    }

    [Fact]
    public void Overlapping_series_collapse_to_a_single_bar_width()
    {
        var halfWidth = 0.5 * 100.0 / (100.0 + 150);

        var first = ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(halfWidth, 0, 3, 100);
        var last = ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(halfWidth, 2, 3, 100);

        first.Should().Be(last);
        (first.Right - first.Left).Should().BeApproximately(1.0 / 2.5, 1e-9);
    }

    [Theory]
    [InlineData(ChartType.PercentStackedColumn, false)]
    [InlineData(ChartType.PercentStackedBar, true)]
    public void Percent_stacked_value_axis_reads_0_to_100_percent_like_excel(ChartType type, bool horizontal)
    {
        var chart = new ChartModel
        {
            Type = type,
            ShowLegend = false,
            YAxisNumberFormatCode = "0%",
            XAxisNumberFormatCode = "0%",
        };
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2", "Q3"],
            [ChartLayoutTestData.Series(0, "North", 12, 19, 14), ChartLayoutTestData.Series(1, "South", 8, 15, 21)]);

        var layout = ChartLayoutEngine.Layout(request);

        var valueAxis = horizontal ? layout.ValueAxis ?? layout.CategoryAxis : layout.ValueAxis;
        var labels = (horizontal ? layout.CategoryAxis!.Ticks.Concat(layout.ValueAxis!.Ticks) : layout.ValueAxis!.Ticks)
            .Select(t => t.Label)
            .ToArray();
        labels.Should().Contain("0%").And.Contain("100%");
        labels.Should().NotContain(l => l.Contains("0000%"), "Excel never shows 10000%");
        valueAxis.Should().NotBeNull();
    }

    [Fact]
    public void Percent_stacked_scaling_only_applies_to_percent_formats_on_the_value_axis()
    {
        var chart = new ChartModel { Type = ChartType.PercentStackedColumn };

        ChartRenderPolicyPlanner.ScalePercentStackedAxisLabelValue(chart, false, ChartDataLabelNumberFormat.General, "0%", 50)
            .Should().Be(0.5);
        ChartRenderPolicyPlanner.ScalePercentStackedAxisLabelValue(chart, false, ChartDataLabelNumberFormat.General, "General", 50)
            .Should().Be(50);
        ChartRenderPolicyPlanner.ScalePercentStackedAxisLabelValue(chart, true, ChartDataLabelNumberFormat.General, "0%", 50)
            .Should().Be(50, "the category axis is never rescaled");
        ChartRenderPolicyPlanner.ScalePercentStackedAxisLabelValue(new ChartModel { Type = ChartType.Column }, false, ChartDataLabelNumberFormat.General, "0%", 0.5)
            .Should().Be(0.5);
    }

    [Fact]
    public void Bottom_legend_is_centred_below_the_axis_labels_like_excel()
    {
        // Excel (COM): chart 474 x 294 pt, bottom legend 104 x 16.9 pt centred horizontally (left 184,
        // i.e. (474 - 104) / 2 within 1 pt) with its bottom edge 4 pt above the chart edge.
        var chart = ChartLayoutTestData.Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Bottom;
        });
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2", "Q3"],
            [ChartLayoutTestData.Series(0, "North", 12, 19, 14), ChartLayoutTestData.Series(1, "South", 8, 15, 21), ChartLayoutTestData.Series(2, "East", 15, 9, 13)],
            plot: new PlotRect(28, 28, 584, 344)).WithChartSize(640, 400);

        var layout = ChartLayoutEngine.Layout(request);

        var legend = layout.Legend.Bounds;
        (legend.X + (legend.Width / 2.0)).Should().BeApproximately(28 + (584 / 2.0), 1.0, "centred on the plot");
        legend.Bottom.Should().BeLessThanOrEqualTo(400 - 3, "Excel keeps the legend inside the chart edge");
        legend.Y.Should().BeGreaterThanOrEqualTo(
            layout.PlotArea.Bottom + 12,
            "the legend sits under the category labels instead of on top of them");
    }

    [Fact]
    public void Right_legend_is_vertically_centred_beside_the_plot_like_excel()
    {
        // Excel (COM): the right legend of a one series chart is centred vertically (top 152 + 9 = 161 pt
        // of a 294 pt chart whose plot spans 33..250 pt).
        var chart = ChartLayoutTestData.Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        });
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2", "Q3"],
            [ChartLayoutTestData.Series(0, "North", 12, 19, 14)],
            plot: new PlotRect(28, 28, 584, 344)).WithChartSize(640, 400);

        var layout = ChartLayoutEngine.Layout(request);

        var legend = layout.Legend.Bounds;
        (legend.Y + (legend.Height / 2.0)).Should().BeApproximately(28 + (344 / 2.0), 1.0);
    }

    [Fact]
    public void Stacked_column_side_legend_lists_the_last_series_first_like_excel()
    {
        // Excel (rendered): a stacked column chart's right legend reads East, South, North top to bottom.
        var chart = ChartLayoutTestData.Chart(ChartType.StackedColumn, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        });
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2"],
            [ChartLayoutTestData.Series(0, "North", 12, 19), ChartLayoutTestData.Series(1, "South", 8, 15), ChartLayoutTestData.Series(2, "East", 15, 9)],
            plot: new PlotRect(28, 28, 584, 344)).WithChartSize(640, 400);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("East", "South", "North");
    }

    [Fact]
    public void Clustered_column_side_legend_keeps_series_order()
    {
        var chart = ChartLayoutTestData.Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        });
        var request = ChartLayoutTestData.Request(
            chart,
            ["Q1", "Q2"],
            [ChartLayoutTestData.Series(0, "North", 12, 19), ChartLayoutTestData.Series(1, "South", 8, 15)],
            plot: new PlotRect(28, 28, 584, 344)).WithChartSize(640, 400);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("North", "South");
    }
}
