using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartLayoutEngineSupportTests
{
    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.ThreeDColumn)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    [InlineData(ChartType.ThreeDBar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.StackedArea)]
    [InlineData(ChartType.PercentStackedArea)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    [InlineData(ChartType.Radar)]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    // chartEx types now ported into the portable engine (Wave 4):
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    // chartEx types ported in Wave 5 (box-and-whisker, treemap, sunburst):
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    // surface types ported as 2D heatmap grid:
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.ThreeDSurface)]
    public void Supported_types_lay_out_without_throwing(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue();
        var request = Request(Chart(type), ["A", "B"], [Series(0, "S1", 10, 20)]);
        var act = () => ChartLayoutEngine.Layout(request);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(ChartType.Map)]
    public void Deferred_types_are_not_supported_and_throw(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeFalse();
        var request = Request(Chart(type), ["A"], [Series(0, "S1", 10)]);
        var act = () => ChartLayoutEngine.Layout(request);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Empty_series_does_not_throw()
    {
        var request = Request(Chart(ChartType.Column), [], []);
        var act = () => ChartLayoutEngine.Layout(request);
        act.Should().NotThrow();
    }

    // ── Box-and-Whisker layout tests ─────────────────────────────────────────

    [Fact]
    public void BoxAndWhisker_layout_produces_two_series()
    {
        // Two data series → two box-plots side by side.
        var request = Request(Chart(ChartType.BoxAndWhisker), ["S1", "S2"],
            [Series(0, "S1", 1, 2, 3, 4, 5), Series(1, "S2", 10, 20, 30, 40, 50)]);
        var layout = ChartLayoutEngine.Layout(request);
        // Should have at least box series + whisker series.
        layout.Series.Should().HaveCountGreaterThanOrEqualTo(2);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns);
        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.BoxWhiskers);
        layout.Series[0].Bars.Should().HaveCount(2, "one box rect per series column");
    }

    [Fact]
    public void BoxAndWhisker_quartile_math_is_correct()
    {
        // Five values 1–5: Q1=1.5+1=2? Let's verify by mirroring the engine's formula.
        // sorted=[1,2,3,4,5], n=5
        // Q1: pos=0.25*4=1.0 → sorted[1]=2.0
        // median: pos=0.5*4=2.0 → sorted[2]=3.0
        // Q3: pos=0.75*4=3.0 → sorted[3]=4.0
        var request = Request(Chart(ChartType.BoxAndWhisker), ["S1"],
            [Series(0, "S1", 1, 2, 3, 4, 5)]);
        var layout = ChartLayoutEngine.Layout(request);
        // Box rect spans Q1→Q3; both axes through value scale.
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns);
        layout.Series[0].Bars.Should().HaveCount(1);
        // Value on the bar record is Q3-Q1=2.
        layout.Series[0].Bars[0].Value.Should().BeApproximately(2.0, 0.001,
            "Q3(4) - Q1(2) = 2 for values [1,2,3,4,5]");
    }

    [Fact]
    public void BoxAndWhisker_with_outlier_produces_outlier_series()
    {
        // Values with a clear outlier beyond 1.5×IQR fence.
        // sorted=[1,2,3,4,100], Q1=2, Q3=4, IQR=2, upperFence=4+3=7, 100 > 7 → outlier.
        var request = Request(Chart(ChartType.BoxAndWhisker), ["S1"],
            [Series(0, "S1", 1, 2, 3, 4, 100)]);
        var layout = ChartLayoutEngine.Layout(request);
        // Should have outlier series (ScatterPoints) when outliers exist.
        layout.Series.Should().Contain(s => s.Kind == SeriesGeometryKind.ScatterPoints,
            "outlier beyond 1.5×IQR fence should produce an outlier ScatterPoints series");
        var outlierSeries = layout.Series.First(s => s.Kind == SeriesGeometryKind.ScatterPoints);
        outlierSeries.Points.Should().HaveCount(1, "exactly one outlier (100)");
    }

    // ── Treemap layout tests ─────────────────────────────────────────────────

    [Fact]
    public void Treemap_layout_produces_one_tile_per_positive_value()
    {
        var request = Request(Chart(ChartType.Treemap), ["A", "B", "C"],
            [Series(0, "S1", 10, 30, 60)]);
        var layout = ChartLayoutEngine.Layout(request);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.TreemapTiles);
        layout.Series[0].Bars.Should().HaveCount(3, "one tile per positive value");
    }

    [Fact]
    public void Treemap_tile_widths_proportional_to_values()
    {
        // Values 10, 30, 60 → widths should be 10%, 30%, 60% of plot width.
        var request = Request(Chart(ChartType.Treemap), ["A", "B", "C"],
            [Series(0, "S1", 10, 30, 60)], plot: new PlotRect(0, 0, 400, 300));
        var layout = ChartLayoutEngine.Layout(request);
        var bars   = layout.Series[0].Bars;
        bars.Should().HaveCount(3);
        // Tile widths proportional to 10:30:60 over 400px total.
        bars[0].Rect.Width.Should().BeApproximately(400 * 10.0 / 100, 1,
            "first tile = 10% of 400px");
        bars[1].Rect.Width.Should().BeApproximately(400 * 30.0 / 100, 1,
            "second tile = 30% of 400px");
        // Last tile gets remainder.
        (bars[0].Rect.Width + bars[1].Rect.Width + bars[2].Rect.Width)
            .Should().BeApproximately(400, 2, "all tiles sum to plot width");
    }

    [Fact]
    public void Treemap_tiles_fill_full_plot_height()
    {
        var request = Request(Chart(ChartType.Treemap), ["A", "B"],
            [Series(0, "S1", 50, 50)], plot: new PlotRect(0, 0, 400, 300));
        var layout = ChartLayoutEngine.Layout(request);
        foreach (var bar in layout.Series[0].Bars)
            bar.Rect.Height.Should().BeApproximately(300, 1, "each tile spans full plot height");
    }

    [Fact]
    public void Treemap_skips_non_positive_values()
    {
        var request = Request(Chart(ChartType.Treemap), ["A", "B", "C"],
            [Series(0, "S1", 10, -5, 20)]);
        var layout = ChartLayoutEngine.Layout(request);
        layout.Series[0].Bars.Should().HaveCount(2, "negative/zero values are excluded");
    }

    // ── Sunburst layout tests ────────────────────────────────────────────────

    [Fact]
    public void Sunburst_layout_produces_PieSlices_with_inner_radius()
    {
        var request = Request(Chart(ChartType.Sunburst), ["A", "B", "C"],
            [Series(0, "S1", 10, 30, 60)]);
        var layout = ChartLayoutEngine.Layout(request);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.PieSlices);
        layout.Series[0].Slices.Should().HaveCount(3);
    }

    [Fact]
    public void Sunburst_inner_radius_is_35_percent_of_outer()
    {
        // WPF uses InnerDiameter=0.35 → innerRadius = outerRadius * 0.35.
        var request = Request(Chart(ChartType.Sunburst), ["A", "B"],
            [Series(0, "S1", 50, 50)], plot: new PlotRect(0, 0, 400, 300));
        var layout = ChartLayoutEngine.Layout(request);
        var slices = layout.Series[0].Slices;
        slices.Should().HaveCount(2);
        var outerR = slices[0].Arc.OuterRadius;
        var innerR = slices[0].Arc.InnerRadius;
        innerR.Should().BeApproximately(outerR * 0.35, 0.1,
            "inner radius should be 35% of outer (WPF InnerDiameter=0.35)");
    }

    [Fact]
    public void Sunburst_slices_sum_to_360_degrees()
    {
        var request = Request(Chart(ChartType.Sunburst), ["A", "B", "C"],
            [Series(0, "S1", 10, 30, 60)]);
        var layout = ChartLayoutEngine.Layout(request);
        var totalSweep = layout.Series[0].Slices.Sum(s => s.Arc.SweepAngleDegrees);
        totalSweep.Should().BeApproximately(360, 0.01, "all slices span the full circle");
    }

    [Fact]
    public void Sunburst_slice_fractions_proportional_to_values()
    {
        var request = Request(Chart(ChartType.Sunburst), ["A", "B"],
            [Series(0, "S1", 25, 75)]);
        var layout = ChartLayoutEngine.Layout(request);
        var slices = layout.Series[0].Slices;
        slices[0].Fraction.Should().BeApproximately(0.25, 0.001);
        slices[1].Fraction.Should().BeApproximately(0.75, 0.001);
    }
}
