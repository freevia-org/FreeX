using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;
using System.Linq;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ColumnBarLayoutTests
{
    [Fact]
    public void Column_chart_produces_one_bar_per_category_per_series()
    {
        var request = Request(
            Chart(ChartType.Column),
            ["A", "B", "C"],
            [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(1);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns);
        layout.Series[0].Bars.Should().HaveCount(3);
    }

    [Fact]
    public void Column_bars_grow_up_from_the_zero_baseline()
    {
        var request = Request(Chart(ChartType.Column), ["A", "B"], [Series(0, "S1", 50, 100)]);
        var layout = ChartLayoutEngine.Layout(request);

        var baseline = layout.ValueAxis!.Scale.Transform(0);
        // All-positive data: every bar's bottom edge is the zero baseline, top edge is above it.
        foreach (var bar in layout.Series[0].Bars)
        {
            bar.Rect.Bottom.Should().BeApproximately(baseline, 1e-6);
            bar.Rect.Top.Should().BeLessThan(baseline);
        }

        // Taller value -> taller bar.
        layout.Series[0].Bars[1].Rect.Height.Should().BeGreaterThan(layout.Series[0].Bars[0].Rect.Height);
    }

    [Fact]
    public void Column_bars_are_centered_on_category_index_with_default_half_width()
    {
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(Chart(ChartType.Column), ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var catScale = layout.CategoryAxis!.Scale;
        var firstBar = layout.Series[0].Bars[0];
        // Excel writes an implicit clustered-column gapWidth=219, which the reader stores as
        // null; its native half-width is therefore 50 / (100 + 219), not the generic 0.35.
        firstBar.Rect.Left.Should().BeApproximately(catScale.Transform(-0.15673981191222572), 1e-6);
        firstBar.Rect.Right.Should().BeApproximately(catScale.Transform(0.15673981191222572), 1e-6);
    }

    [Fact]
    public void Negative_column_grows_down_from_zero()
    {
        var request = Request(Chart(ChartType.Column), ["A", "B"], [Series(0, "S1", -40, 20)]);
        var layout = ChartLayoutEngine.Layout(request);

        var baseline = layout.ValueAxis!.Scale.Transform(0);
        var negativeBar = layout.Series[0].Bars[0];
        negativeBar.Rect.Top.Should().BeApproximately(baseline, 1e-6, "the negative bar's top edge is the zero line");
        negativeBar.Rect.Bottom.Should().BeGreaterThan(baseline);
    }

    [Fact]
    public void Blank_with_gap_mode_skips_the_bar_blank_with_zero_mode_emits_a_flat_bar()
    {
        var gap = Request(Chart(ChartType.Column, c => c.BlankDisplayMode = ChartBlankDisplayMode.Gap),
            ["A", "B", "C"], [Series(0, "S1", 10, null, 30)]);
        ChartLayoutEngine.Layout(gap).Series[0].Bars.Should().HaveCount(2);

        var zero = Request(Chart(ChartType.Column, c => c.BlankDisplayMode = ChartBlankDisplayMode.Zero),
            ["A", "B", "C"], [Series(0, "S1", 10, null, 30)]);
        var zeroLayout = ChartLayoutEngine.Layout(zero);
        zeroLayout.Series[0].Bars.Should().HaveCount(3);
        zeroLayout.Series[0].Bars[1].Value.Should().Be(0);
    }

    [Fact]
    public void Bar_chart_lays_categories_on_the_vertical_axis_and_values_horizontally()
    {
        var request = Request(Chart(ChartType.Bar), ["A", "B"], [Series(0, "S1", 30, 60)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Bars);
        layout.CategoryAxis!.Side.Should().Be(AxisSide.Left);
        layout.ValueAxis!.Side.Should().Be(AxisSide.Bottom);

        // Bars grow rightward from the zero baseline; longer value -> wider bar.
        var baseline = layout.ValueAxis.Scale.Transform(0);
        foreach (var bar in layout.Series[0].Bars)
            bar.Rect.Left.Should().BeApproximately(baseline, 1e-6);
        layout.Series[0].Bars[1].Rect.Width.Should().BeGreaterThan(layout.Series[0].Bars[0].Rect.Width);
    }

    [Fact]
    public void Stacked_column_stacks_segments_without_gaps()
    {
        var request = Request(Chart(ChartType.StackedColumn), ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 30, 40)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(2);
        // For category 0: first segment 0..10, second 10..40. Second segment's bottom == first's top.
        var firstSeg = layout.Series[0].Bars[0].Rect;
        var secondSeg = layout.Series[1].Bars[0].Rect;
        secondSeg.Bottom.Should().BeApproximately(firstSeg.Top, 1e-6);
    }

    [Fact]
    public void Percent_stacked_column_normalizes_each_category_to_full_height()
    {
        var request = Request(Chart(ChartType.PercentStackedColumn), ["A"],
            [Series(0, "S1", 25), Series(1, "S2", 75)]);
        var layout = ChartLayoutEngine.Layout(request);

        // Axis runs 0..100; total stack fills the whole value axis.
        var scale = layout.ValueAxis!.Scale;
        scale.Minimum.Should().Be(0);
        scale.Maximum.Should().Be(100);

        var top = layout.Series[1].Bars[0].Rect.Top;
        top.Should().BeApproximately(scale.Transform(100), 1e-6, "the two segments fill 100%");
    }

    [Fact]
    public void Stacked_column_half_width_matches_source_renderer()
    {
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(Chart(ChartType.StackedColumn), ["A", "B"],
            [Series(0, "S1", 10, 20)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var catScale = layout.CategoryAxis!.Scale;
        var bar = layout.Series[0].Bars[0].Rect;
        bar.Left.Should().BeApproximately(catScale.Transform(-0.35), 1e-6);
        bar.Right.Should().BeApproximately(catScale.Transform(0.35), 1e-6);
    }

    [Fact]
    public void Stacked_column_honors_a_non_default_BarGapWidth_F17()
    {
        // F17: stacked column/bar hardcoded a 0.35 half-width and ignored BarGapWidth, mismatching
        // WPF's ColumnBarHalfWidth (which applies the user's Gap Width to stacked types too, since
        // ChartTypeSupport.SupportsBarGapWidth includes StackedColumn/StackedBar). gapWidth=0 should
        // widen the stacked bar to the full [-0.5, 0.5] slot (touching bars, Excel's "no gap" look).
        var plot = new PlotRect(0, 0, 300, 200);
        var defaultRequest = Request(Chart(ChartType.StackedColumn), ["A", "B"], [Series(0, "S1", 10, 20)], plot);
        var gapZeroRequest = Request(Chart(ChartType.StackedColumn, c => c.BarGapWidth = 0), ["A", "B"], [Series(0, "S1", 10, 20)], plot);

        var defaultLayout = ChartLayoutEngine.Layout(defaultRequest);
        var gapZeroLayout = ChartLayoutEngine.Layout(gapZeroRequest);

        var catScale = defaultLayout.CategoryAxis!.Scale;
        var defaultBar = defaultLayout.Series[0].Bars[0].Rect;
        var gapZeroBar = gapZeroLayout.Series[0].Bars[0].Rect;

        // Default (no explicit gap width) still matches the 0.35 half-width baseline.
        defaultBar.Left.Should().BeApproximately(catScale.Transform(-0.35), 1e-6);
        defaultBar.Right.Should().BeApproximately(catScale.Transform(0.35), 1e-6);

        // gapWidth=0 must widen the bar to the full slot [-0.5, 0.5] (touching bars).
        gapZeroBar.Left.Should().BeApproximately(catScale.Transform(-0.5), 1e-6);
        gapZeroBar.Right.Should().BeApproximately(catScale.Transform(0.5), 1e-6);

        // The gap-width-driven bar must be strictly wider than the default-width bar.
        gapZeroBar.Width.Should().BeGreaterThan(defaultBar.Width);
    }

    [Fact]
    public void Stacked_bar_honors_a_non_default_BarGapWidth_F17()
    {
        // Same fix, mirrored for the horizontal StackedBar family.
        var plot = new PlotRect(0, 0, 300, 200);
        var defaultRequest = Request(Chart(ChartType.StackedBar), ["A", "B"], [Series(0, "S1", 10, 20)], plot);
        var gapZeroRequest = Request(Chart(ChartType.StackedBar, c => c.BarGapWidth = 0), ["A", "B"], [Series(0, "S1", 10, 20)], plot);

        var defaultLayout = ChartLayoutEngine.Layout(defaultRequest);
        var gapZeroLayout = ChartLayoutEngine.Layout(gapZeroRequest);

        var defaultBar = defaultLayout.Series[0].Bars[0].Rect;
        var gapZeroBar = gapZeroLayout.Series[0].Bars[0].Rect;

        // Bars are horizontal for StackedBar, so the category slot is the Height, not the Width.
        gapZeroBar.Height.Should().BeGreaterThan(defaultBar.Height);
    }

    // ── 3-D column / 3-D bar flat-render tests ───────────────────────────────

    [Fact]
    public void ThreeDColumn_is_supported_and_lays_out_as_clustered_columns()
    {
        // 3-D column maps to the same flat-clustered-column layout as ChartType.Column.
        ChartLayoutEngine.IsSupported(ChartType.ThreeDColumn).Should().BeTrue();

        var request = Request(
            Chart(ChartType.ThreeDColumn),
            ["Q1", "Q2", "Q3", "Q4"],
            [Series(0, "Revenue", 120, 150, 180, 200), Series(1, "Cost", 80, 100, 130, 160)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(2);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns, "3-D column renders as flat columns");
        layout.Series[0].Bars.Should().HaveCount(4, "one bar per category");
        layout.Series[1].Bars.Should().HaveCount(4);
        // All bars must be non-degenerate rectangles with positive area.
        foreach (var series in layout.Series)
            foreach (var bar in series.Bars)
                bar.Rect.Width.Should().BePositive("each bar must have positive width");
    }

    [Fact]
    public void ThreeDColumn_retains_legacy_default_width_when_2D_column_uses_native_gap_width()
    {
        // 3-D column is intentionally outside the native 2-D default-gap correction.
        var categories = new[] { "A", "B", "C" };
        var plot = new PlotRect(0, 0, 300, 200);

        var col3d = ChartLayoutEngine.Layout(Request(Chart(ChartType.ThreeDColumn), categories, [Series(0, "S", 10, 20, 30)], plot));
        var col2d = ChartLayoutEngine.Layout(Request(Chart(ChartType.Column), categories, [Series(0, "S", 10, 20, 30)], plot));

        // The vertical geometry remains the same, but 3-D keeps its established wider default.
        col3d.Series[0].Bars.Count.Should().Be(col2d.Series[0].Bars.Count);
        for (var i = 0; i < col3d.Series[0].Bars.Count; i++)
        {
            col3d.Series[0].Bars[i].Rect.Width.Should().BeGreaterThan(col2d.Series[0].Bars[i].Rect.Width);
            col3d.Series[0].Bars[i].Rect.Top.Should().BeApproximately(col2d.Series[0].Bars[i].Rect.Top, 1e-6);
            col3d.Series[0].Bars[i].Rect.Bottom.Should().BeApproximately(col2d.Series[0].Bars[i].Rect.Bottom, 1e-6);
        }
    }

    [Fact]
    public void ThreeDBar_is_supported_and_lays_out_as_clustered_bars()
    {
        // 3-D bar maps to the same flat-clustered-bar layout as ChartType.Bar.
        ChartLayoutEngine.IsSupported(ChartType.ThreeDBar).Should().BeTrue();

        var request = Request(
            Chart(ChartType.ThreeDBar),
            ["North", "South", "East", "West"],
            [Series(0, "Sales", 340, 280, 410, 300), Series(1, "Target", 210, 190, 250, 180)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(2);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Bars, "3-D bar renders as flat horizontal bars");
        layout.Series[0].Bars.Should().HaveCount(4, "one bar per category");
        layout.Series[1].Bars.Should().HaveCount(4);
        // Category axis on the left, value axis on the bottom (horizontal bars).
        layout.CategoryAxis!.Side.Should().Be(AxisSide.Left);
        layout.ValueAxis!.Side.Should().Be(AxisSide.Bottom);
        // All bars must be non-degenerate.
        foreach (var series in layout.Series)
            foreach (var bar in series.Bars)
                bar.Rect.Width.Should().BePositive("each bar must have positive width");
    }

    [Fact]
    public void ThreeDBar_lays_out_identically_to_2D_Bar_for_same_data()
    {
        var categories = new[] { "A", "B", "C" };
        var plot = new PlotRect(0, 0, 300, 200);

        var bar3d = ChartLayoutEngine.Layout(Request(Chart(ChartType.ThreeDBar), categories, [Series(0, "S", 10, 20, 30)], plot));
        var bar2d = ChartLayoutEngine.Layout(Request(Chart(ChartType.Bar), categories, [Series(0, "S", 10, 20, 30)], plot));

        bar3d.Series[0].Bars.Count.Should().Be(bar2d.Series[0].Bars.Count);
        for (var i = 0; i < bar3d.Series[0].Bars.Count; i++)
        {
            bar3d.Series[0].Bars[i].Rect.Left.Should().BeApproximately(bar2d.Series[0].Bars[i].Rect.Left, 1e-6);
            bar3d.Series[0].Bars[i].Rect.Right.Should().BeApproximately(bar2d.Series[0].Bars[i].Rect.Right, 1e-6);
            bar3d.Series[0].Bars[i].Rect.Top.Should().BeApproximately(bar2d.Series[0].Bars[i].Rect.Top, 1e-6);
            bar3d.Series[0].Bars[i].Rect.Bottom.Should().BeApproximately(bar2d.Series[0].Bars[i].Rect.Bottom, 1e-6);
        }
    }

    // ── Clustered multi-series offset tests (CD1) ─────────────────────────────

    [Fact]
    public void ThreeSeries_column_chart_produces_disjoint_x_ranges_per_category()
    {
        // CD1: 3-series clustered column chart must lay out 3 side-by-side bars per category.
        // Each series' bars must occupy a disjoint x-range within the same category slot.
        var request = Request(
            Chart(ChartType.Column),
            ["Cat1", "Cat2"],
            [
                Series(0, "S0", 10, 20),
                Series(1, "S1", 30, 40),
                Series(2, "S2", 50, 60),
            ]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(3);

        // For category 0: collect the [left, right] x-ranges of the three bars.
        var bar0 = layout.Series[0].Bars[0].Rect;
        var bar1 = layout.Series[1].Bars[0].Rect;
        var bar2 = layout.Series[2].Bars[0].Rect;

        // All bars must have positive width.
        bar0.Width.Should().BePositive("series 0 bar must have positive width");
        bar1.Width.Should().BePositive("series 1 bar must have positive width");
        bar2.Width.Should().BePositive("series 2 bar must have positive width");

        // The three bars must be disjoint — each must end before the next begins
        // (no overlap). Ordering: S0 < S1 < S2 (left to right within the category).
        bar0.Right.Should().BeLessThanOrEqualTo(bar1.Left + 1e-9,
            "series 0 bar must end before series 1 bar starts");
        bar1.Right.Should().BeLessThanOrEqualTo(bar2.Left + 1e-9,
            "series 1 bar must end before series 2 bar starts");

        // Same assertions for category 1.
        var b0c1 = layout.Series[0].Bars[1].Rect;
        var b1c1 = layout.Series[1].Bars[1].Rect;
        var b2c1 = layout.Series[2].Bars[1].Rect;
        b0c1.Right.Should().BeLessThanOrEqualTo(b1c1.Left + 1e-9);
        b1c1.Right.Should().BeLessThanOrEqualTo(b2c1.Left + 1e-9);
    }

    [Fact]
    public void ThreeSeries_column_bars_fill_the_same_total_slot_width_as_single_series()
    {
        // The 3 clustered bars together must span the same total width as the single bar would,
        // i.e. each sub-slot is exactly slot/3 wide and they tile perfectly.
        var plot = new PlotRect(0, 0, 600, 300);
        var singleRequest = Request(Chart(ChartType.Column), ["A"], [Series(0, "S0", 10)], plot);
        var multiRequest = Request(Chart(ChartType.Column), ["A"],
            [Series(0, "S0", 10), Series(1, "S1", 20), Series(2, "S2", 30)], plot);

        var singleLayout = ChartLayoutEngine.Layout(singleRequest);
        var multiLayout  = ChartLayoutEngine.Layout(multiRequest);

        var singleBar = singleLayout.Series[0].Bars[0].Rect;
        var multiLeft  = multiLayout.Series[0].Bars[0].Rect.Left;
        var multiRight = multiLayout.Series[2].Bars[0].Rect.Right;

        multiLeft.Should().BeApproximately(singleBar.Left, 1e-6,
            "leftmost sub-slot must align with the full slot's left edge");
        multiRight.Should().BeApproximately(singleBar.Right, 1e-6,
            "rightmost sub-slot must align with the full slot's right edge");
    }

    [Fact]
    public void SingleSeries_column_chart_fills_full_slot_no_regression()
    {
        // CD1 regression: a single-series chart must still use the full category slot
        // (same geometry as before the fix). clusterCount=1 → ordinal=0 → (-half, +half).
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(Chart(ChartType.Column), ["A", "B", "C"],
            [Series(0, "S1", 1, 2, 3)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var catScale = layout.CategoryAxis!.Scale;
        var firstBar = layout.Series[0].Bars[0];
        // The un-authored clustered column uses Excel's gapWidth=219 slot.
        firstBar.Rect.Left.Should().BeApproximately(catScale.Transform(-0.15673981191222572), 1e-6,
            "single-series bar must span the full default slot");
        firstBar.Rect.Right.Should().BeApproximately(catScale.Transform(0.15673981191222572), 1e-6);
    }

    [Fact]
    public void StackedColumn_still_stacks_not_clusters()
    {
        // CD1 regression: stacked charts must NOT apply cluster offsets — they keep the full
        // slot and stack values on top of each other, not side by side.
        var request = Request(Chart(ChartType.StackedColumn), ["A"],
            [Series(0, "S1", 10), Series(1, "S2", 20)]);
        var layout = ChartLayoutEngine.Layout(request);

        // Both series bars must cover the same x-range (stacked, not side by side).
        var bar0 = layout.Series[0].Bars[0].Rect;
        var bar1 = layout.Series[1].Bars[0].Rect;
        bar0.Left.Should().BeApproximately(bar1.Left, 1e-6,
            "stacked bars must share the same x-slot left edge");
        bar0.Right.Should().BeApproximately(bar1.Right, 1e-6,
            "stacked bars must share the same x-slot right edge");

        // And they must not overlap in Y (stacked vertically).
        bar1.Bottom.Should().BeApproximately(bar0.Top, 1e-6,
            "stacked S2 must sit on top of S1 (no gap, no overlap)");
    }

    [Fact]
    public void ThreeSeries_bar_chart_produces_disjoint_y_ranges_per_category()
    {
        // CD1: 3-series clustered bar chart must lay out 3 side-by-side horizontal bars
        // per category. Each series' bars must occupy a disjoint y-range.
        var request = Request(
            Chart(ChartType.Bar),
            ["Cat1", "Cat2"],
            [
                Series(0, "S0", 10, 20),
                Series(1, "S1", 30, 40),
                Series(2, "S2", 50, 60),
            ]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(3);

        // For category 0: y-ranges must be disjoint (bars are horizontal, so y is the category axis).
        // CategoryAxis is on the Left (AxisSide.Left), so increasing y in pixel = lower category index.
        // The layout uses y0=Transform(i+left), y1=Transform(i+right); FromCorners normalises so
        // Top < Bottom. We just need that no two bars' y-ranges overlap.
        var bar0 = layout.Series[0].Bars[0].Rect;
        var bar1 = layout.Series[1].Bars[0].Rect;
        var bar2 = layout.Series[2].Bars[0].Rect;

        bar0.Width.Should().BePositive();
        bar1.Width.Should().BePositive();
        bar2.Width.Should().BePositive();

        // Non-overlapping y-ranges: one must end before the other begins.
        // We don't prescribe order here since axis direction depends on scale orientation.
        var ranges = new[] { (bar0.Top, bar0.Bottom), (bar1.Top, bar1.Bottom), (bar2.Top, bar2.Bottom) }
            .OrderBy(r => r.Item1).ToList();

        for (var k = 1; k < ranges.Count; k++)
            ranges[k - 1].Item2.Should().BeLessThanOrEqualTo(ranges[k].Item1 + 1e-9,
                $"bar sub-slot {k - 1} must end before sub-slot {k} starts");
    }

    [Fact]
    public void ThreeDColumn_three_series_produces_disjoint_x_ranges()
    {
        // 3-D column (multi-series, inherently clustered) must behave identically to 2D Column.
        var request = Request(
            Chart(ChartType.ThreeDColumn),
            ["Q1", "Q2"],
            [Series(0, "Revenue", 100, 120), Series(1, "Cost", 60, 80), Series(2, "Profit", 40, 40)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(3);
        var bar0 = layout.Series[0].Bars[0].Rect;
        var bar1 = layout.Series[1].Bars[0].Rect;
        var bar2 = layout.Series[2].Bars[0].Rect;

        bar0.Right.Should().BeLessThanOrEqualTo(bar1.Left + 1e-9,
            "3-D column series 0 must end before series 1 starts");
        bar1.Right.Should().BeLessThanOrEqualTo(bar2.Left + 1e-9,
            "3-D column series 1 must end before series 2 starts");
    }
}
