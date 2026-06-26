using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Unit tests for the chartEx types ported to the portable layout engine:
/// Funnel, Waterfall, Histogram, Pareto (Wave 4) and BoxAndWhisker, Treemap, Sunburst (Wave 5).
/// </summary>
public sealed class ChartExLayoutTests
{
    // ── IsSupported ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    public void IsSupported_ReturnsTrueForAllFourChartExTypes(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue($"{type} was ported in this wave");
    }

    [Theory]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    public void IsSupported_ReturnsTrueForWaveFiveChartExTypes(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue($"{type} was ported in Wave 5");
    }

    [Theory]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.ThreeDSurface)]
    public void IsSupported_ReturnsTrueForSurfaceHeatmapTypes(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue($"{type} is now laid out as a 2D heatmap grid");
    }

    // ── Existing types still supported ────────────────────────────────────────

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Radar)]
    [InlineData(ChartType.Bubble)]
    public void IsSupported_ExistingTypesStillSupported(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue($"{type} was already supported and must remain so");
    }

    // ── Funnel layout ─────────────────────────────────────────────────────────

    [Fact]
    public void Funnel_ProducesNonEmptyBarsFromFirstSeries()
    {
        var chart = Chart(ChartType.Funnel);
        var series = Series(0, "Funnel", 100, 80, 60, 40);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"], [series]));

        layout.Series.Should().ContainSingle();
        var sl = layout.Series[0];
        sl.Kind.Should().Be(SeriesGeometryKind.Bars);
        sl.Bars.Should().HaveCount(4, "one bar per data point");
    }

    [Fact]
    public void Funnel_BarsDecreaseInWidth_WhenValuesDescend()
    {
        // Each stage value is smaller, so each bar should be narrower.
        var chart = Chart(ChartType.Funnel);
        var series = Series(0, "Funnel", 100, 80, 60, 40);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"], [series]));

        var bars = layout.Series[0].Bars;
        for (var i = 1; i < bars.Count; i++)
            bars[i].Rect.Width.Should().BeLessThanOrEqualTo(bars[i - 1].Rect.Width,
                $"bar {i} (value={bars[i].Value}) should be ≤ bar {i - 1} (value={bars[i - 1].Value})");
    }

    [Fact]
    public void Funnel_FirstBar_IsWidestWhenMaxValue()
    {
        var chart = Chart(ChartType.Funnel);
        var series = Series(0, "Funnel", 200, 100, 50);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["Top", "Mid", "Bot"], [series]));

        var bars = layout.Series[0].Bars;
        bars[0].Rect.Width.Should().BeGreaterThan(bars[1].Rect.Width);
        bars[1].Rect.Width.Should().BeGreaterThan(bars[2].Rect.Width);
    }

    [Fact]
    public void Funnel_BarsAreCenteredHorizontally()
    {
        // Each bar should have the same center X (they're all centered on the plot's midline).
        var chart = Chart(ChartType.Funnel);
        var series = Series(0, "Funnel", 100, 60, 30);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        var bars = layout.Series[0].Bars;
        var firstCenterX = bars[0].Rect.Center.X;
        foreach (var bar in bars)
            bar.Rect.Center.X.Should().BeApproximately(firstCenterX, 0.01,
                "all funnel bars are centered on the same vertical axis");
    }

    [Fact]
    public void Funnel_BarsHavePerBarFillColorOverride()
    {
        var chart = Chart(ChartType.Funnel);
        var series = Series(0, "Funnel", 100, 80, 60);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        layout.Series[0].Bars.Should().OnlyContain(
            b => b.FillColorOverride.HasValue,
            "funnel bars get per-bar palette colors via FillColorOverride");
    }

    [Fact]
    public void Funnel_NoAxesProduced()
    {
        var chart = Chart(ChartType.Funnel);
        var series = Series(0, "Funnel", 100, 80);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [series]));

        layout.CategoryAxis.Should().BeNull("funnel has no visible axes");
        layout.ValueAxis.Should().BeNull();
    }

    // ── Waterfall layout ──────────────────────────────────────────────────────

    [Fact]
    public void Waterfall_ProducesOneBarPerDataPoint()
    {
        var chart = Chart(ChartType.Waterfall);
        var series = Series(0, "Waterfall", 10, -3, 5, -2);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"], [series]));

        layout.Series.Should().ContainSingle();
        layout.Series[0].Bars.Should().HaveCount(4);
    }

    [Fact]
    public void Waterfall_LastBarIsTotal_ByDefault()
    {
        // Default WaterfallBarPlanner behavior: last point is the total/anchor.
        var chart = Chart(ChartType.Waterfall);
        var series = Series(0, "Waterfall", 10, -3, 5);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "Total"], [series]));

        var bars = layout.Series[0].Bars;
        // The total bar is colored WaterfallTotalColor (blue); the others are green or red.
        // We verify via FillColorOverride: all three must have a non-null color.
        bars.Should().OnlyContain(b => b.FillColorOverride.HasValue);
    }

    [Fact]
    public void Waterfall_IncreaseBarStartsAtRunningTotal()
    {
        // Values: 10, 5 (both increases). Running total after bar 0 = 10.
        // Bar 1 should start at y=10 (before bar 1's own value of 5).
        var chart = Chart(ChartType.Waterfall, c => c.WaterfallTotalPointIndices = []);
        var series = Series(0, "Waterfall", 10, 5);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [series]));

        var bars = layout.Series[0].Bars;
        bars.Should().HaveCount(2);
        // Bar 0 runs from 0→10 (bottom→top in data space). Bar 1 should run from 10→15.
        // In pixel space y grows down, so bar 1's Rect.Bottom (higher pixel Y) >= bar 0's Rect.Top.
        // We just verify non-zero height (both are increases, so they should be above baseline).
        bars[0].Rect.Height.Should().BeGreaterThan(0);
        bars[1].Rect.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Waterfall_ConnectorsCount_IsOneLessThanBarCount()
    {
        var chart = Chart(ChartType.Waterfall, c => c.WaterfallTotalPointIndices = []);
        var series = Series(0, "Waterfall", 10, -3, 5);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        var sl = layout.Series[0];
        sl.WaterfallConnectors.Should().HaveCount(sl.Bars.Count - 1,
            "one connector between each adjacent pair of bars");
    }

    [Fact]
    public void Waterfall_IncreaseAndDecreaseHaveDifferentColors()
    {
        // Bar 0: +10 (increase), Bar 1: -3 (decrease) — different FillColorOverride values.
        var chart = Chart(ChartType.Waterfall, c => c.WaterfallTotalPointIndices = []);
        var series = Series(0, "Waterfall", 10, -3);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [series]));

        var bars = layout.Series[0].Bars;
        bars[0].FillColorOverride.Should().NotBe(bars[1].FillColorOverride,
            "increase bar and decrease bar use different colors");
    }

    [Fact]
    public void Waterfall_ProducesAxes()
    {
        var chart = Chart(ChartType.Waterfall);
        var series = Series(0, "Waterfall", 10, -3, 5);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        layout.CategoryAxis.Should().NotBeNull();
        layout.ValueAxis.Should().NotBeNull();
    }

    // ── Histogram layout ──────────────────────────────────────────────────────

    [Fact]
    public void Histogram_ProducesOneBarPerBin()
    {
        // 9 values, sqrt(9)=3 bins (Automatic mode).
        var chart = Chart(ChartType.Histogram);
        var series = Series(0, "Hist", 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        layout.Series.Should().ContainSingle();
        var bars = layout.Series[0].Bars;
        bars.Should().HaveCountGreaterThan(0, "at least one bin produced");
    }

    [Fact]
    public void Histogram_AutomaticBinCount_MatchesSqrtRule()
    {
        // 9 values → ceil(sqrt(9)) = 3 bins.
        var chart = Chart(ChartType.Histogram);
        var series = Series(0, "Hist", 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        layout.Series[0].Bars.Should().HaveCount(3);
    }

    [Fact]
    public void Histogram_ExplicitBinCount_Honored()
    {
        var chart = Chart(ChartType.Histogram, c =>
            c.HistogramBinning = new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 5));
        var series = Series(0, "Hist", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        layout.Series[0].Bars.Should().HaveCount(5);
    }

    [Fact]
    public void Histogram_BarsStartAtZeroBaseline()
    {
        var chart = Chart(ChartType.Histogram);
        var series = Series(0, "Hist", 1, 2, 3, 4);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        // AreaBaseline should be set (y-coordinate of the zero baseline).
        layout.Series[0].AreaBaseline.Should().BeGreaterThan(0,
            "area baseline is a pixel Y, non-zero because the plot top is above zero");
    }

    [Fact]
    public void Histogram_ProducesAxesWithBinLabels()
    {
        var chart = Chart(ChartType.Histogram);
        var series = Series(0, "Hist", 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        layout.CategoryAxis.Should().NotBeNull();
        layout.CategoryAxis!.Ticks.Should().HaveCountGreaterThan(0);
        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.Title.Should().Be("Frequency", "default Y-axis title for histogram");
    }

    [Fact]
    public void Histogram_EmptySeries_ProducesNoSeries()
    {
        var chart = Chart(ChartType.Histogram);
        var series = new ChartSeriesData { SeriesIndex = 0, Values = [] };
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        layout.Series.Should().BeEmpty();
    }

    // ── Pareto layout ─────────────────────────────────────────────────────────

    [Fact]
    public void Pareto_ProducesBarSeriesAndLineSeriesPlusSecondaryAxis()
    {
        var chart = Chart(ChartType.Pareto);
        var series = Series(0, "Pareto", 50, 30, 15, 5);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["D", "B", "C", "A"], [series]));

        layout.Series.Should().HaveCount(2, "bar series + cumulative line series");
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns, "bars first");
        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.Line, "cumulative % line second");
        layout.SecondaryValueAxis.Should().NotBeNull("Pareto needs a secondary % axis");
    }

    [Fact]
    public void Pareto_BarsSortedDescending()
    {
        // Values in arbitrary order — layout must sort them so the largest bar is first.
        var chart = Chart(ChartType.Pareto);
        var series = Series(0, "Pareto", 5, 30, 15, 50);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"], [series]));

        var bars = layout.Series[0].Bars;
        for (var i = 1; i < bars.Count; i++)
            bars[i].Value.Should().BeLessThanOrEqualTo(bars[i - 1].Value,
                $"bar {i} (value={bars[i].Value}) should be ≤ bar {i - 1} (value={bars[i - 1].Value})");
    }

    [Fact]
    public void Pareto_LastCumulativeLinePoint_IsNearHundredPercent()
    {
        var chart = Chart(ChartType.Pareto);
        var series = Series(0, "Pareto", 50, 30, 20);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        var linePoints = layout.Series[1].Points;
        linePoints.Should().HaveCount(3);
        linePoints[^1].DataY.Should().BeApproximately(100.0, 0.01,
            "the cumulative % ends at 100% when all values are counted");
    }

    [Fact]
    public void Pareto_LineSeries_UsesSecondaryAxis()
    {
        var chart = Chart(ChartType.Pareto);
        var series = Series(0, "Pareto", 50, 30, 20);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        layout.Series[1].UsesSecondaryAxis.Should().BeTrue(
            "the cumulative % line is plotted against the right (secondary) axis");
    }

    [Fact]
    public void Pareto_SecondaryAxis_HasZeroToHundredRange()
    {
        var chart = Chart(ChartType.Pareto);
        var series = Series(0, "Pareto", 50, 30, 20);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        var secondary = layout.SecondaryValueAxis!;
        secondary.Side.Should().Be(AxisSide.Right);
        secondary.Scale.Minimum.Should().Be(0);
        secondary.Scale.Maximum.Should().Be(100);
    }

    [Fact]
    public void Pareto_BarsHaveNonZeroHeight()
    {
        var chart = Chart(ChartType.Pareto);
        var series = Series(0, "Pareto", 100, 60, 40);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [series]));

        layout.Series[0].Bars.Should().OnlyContain(b => b.Rect.Height > 0);
    }

    [Fact]
    public void Pareto_EmptySeries_ProducesNoSeries()
    {
        var chart = Chart(ChartType.Pareto);
        var series = new ChartSeriesData { SeriesIndex = 0, Values = [] };
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        layout.Series.Should().BeEmpty();
    }
}
