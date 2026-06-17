using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

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
        // Bar spans index -0.4 .. 0.4 (default column half-width) around category 0.
        firstBar.Rect.Left.Should().BeApproximately(catScale.Transform(-0.4), 1e-6);
        firstBar.Rect.Right.Should().BeApproximately(catScale.Transform(0.4), 1e-6);
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
}
