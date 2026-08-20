using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Regression tests for the P-chart-layout review group (G18, G33): the portable
/// <see cref="ChartLayoutEngine"/> must match the WPF renderer's bar/column geometry and
/// trendline-annotation placement exactly, since both hosts render the same workbook.
/// </summary>
public sealed class PChartLayoutRegressionTests
{
    // ---- G18: current Excel's implicit clustered-column gap is 219, while Bar keeps its
    // established 0.35 width unless a source gap is authored. ----

    [Fact]
    public void G18_Clustered_column_default_half_width_matches_Excel_gap_width_219()
    {
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(Chart(ChartType.Column), ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var catScale = layout.CategoryAxis!.Scale;
        var firstBar = layout.Series[0].Bars[0];
        firstBar.Rect.Left.Should().BeApproximately(catScale.Transform(-0.15673981191222572), 1e-6,
            "the portable engine's clustered column half-width must restore Excel's implicit gapWidth=219");
        firstBar.Rect.Right.Should().BeApproximately(catScale.Transform(0.15673981191222572), 1e-6);
    }

    [Fact]
    public void G18_Clustered_bar_default_half_width_matches_WPF_0_35_not_0_4()
    {
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(Chart(ChartType.Bar), ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var catScale = layout.CategoryAxis!.Scale;
        var firstBar = layout.Series[0].Bars[0];
        // The Bar chart's category scale runs vertically (and inverted: larger category offset
        // maps to a smaller y), so compare edge-agnostically: the rect's Top/Bottom must be the
        // min/max of the ±0.35 half-width transforms.
        var edgeA = catScale.Transform(-0.35);
        var edgeB = catScale.Transform(0.35);
        firstBar.Rect.Top.Should().BeApproximately(Math.Min(edgeA, edgeB), 1e-6,
            "the portable engine's clustered bar half-width must match WPF's ColumnBarHalfWidth default of 0.35");
        firstBar.Rect.Bottom.Should().BeApproximately(Math.Max(edgeA, edgeB), 1e-6);
    }

    [Fact]
    public void G18_Clustered_column_native_default_is_narrower_than_the_stacked_default()
    {
        // Excel's clustered-column XML writes gapWidth=219 while the stacked path retains the
        // established generic default when it has no authored gap width.
        var plot = new PlotRect(0, 0, 300, 200);
        var clusteredLayout = ChartLayoutEngine.Layout(
            Request(Chart(ChartType.Column), ["A", "B"], [Series(0, "S1", 1, 2)], plot));
        var stackedLayout = ChartLayoutEngine.Layout(
            Request(Chart(ChartType.StackedColumn), ["A", "B"], [Series(0, "S1", 1, 2)], plot));

        var clusteredBar = clusteredLayout.Series[0].Bars[0].Rect;
        var stackedBar = stackedLayout.Series[0].Bars[0].Rect;
        (clusteredBar.Right - clusteredBar.Left).Should().BeLessThan(stackedBar.Right - stackedBar.Left);
    }

    // ---- G33: Bar-chart trendline equation/R² annotation anchor must land at the same corner as
    // the WPF renderer's swapTrendlineAxes path: (min value, max category index), not
    // (min category index, max value). ----

    [Fact]
    public void G33_Bar_chart_trendline_annotation_anchors_at_min_value_max_index_like_WPF()
    {
        var plot = new PlotRect(0, 0, 300, 200);
        var chart = Chart(ChartType.Bar, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.ShowTrendlineEquation = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)], plot));

        var trend = layout.Series[0].Trendline!;
        trend.AnnotationLines.Should().ContainSingle();

        var categoryScale = layout.CategoryAxis!.Scale;
        var valueScale = layout.ValueAxis!.Scale;

        // WPF's AddTrendlineInfoIfRequested swaps source points to (value, index) before taking
        // (Min(X), Max(Y)) for swapTrendlineAxes=true (Bar), i.e. anchor = (min value, max index).
        trend.AnnotationAnchor.X.Should().BeApproximately(valueScale.Transform(10), 1e-6,
            "Bar-chart trendline annotation X must anchor at the min VALUE, matching WPF's swapped-axis anchor");
        trend.AnnotationAnchor.Y.Should().BeApproximately(categoryScale.Transform(2), 1e-6,
            "Bar-chart trendline annotation Y must anchor at the max category INDEX, matching WPF's swapped-axis anchor");
    }

    [Fact]
    public void G33_Column_chart_trendline_annotation_still_anchors_at_min_index_max_value()
    {
        // Non-swapped chart families (Column/Line/Area/Scatter) must keep the original
        // (min X, max Y) = (min index, max value) anchor convention -- only Bar swaps.
        var plot = new PlotRect(0, 0, 300, 200);
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.ShowTrendlineEquation = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)], plot));

        var trend = layout.Series[0].Trendline!;
        trend.AnnotationLines.Should().ContainSingle();

        var categoryScale = layout.CategoryAxis!.Scale;
        var valueScale = layout.ValueAxis!.Scale;

        trend.AnnotationAnchor.X.Should().BeApproximately(categoryScale.Transform(0), 1e-6);
        trend.AnnotationAnchor.Y.Should().BeApproximately(valueScale.Transform(30), 1e-6);
    }
}
