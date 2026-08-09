using System;
using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

// R131-REMEDIATION: the WPF/OxyPlot renderer's r131 date-axis + axis-crosses fix
// (ChartRenderer.cs/ChartRenderer.Axes.cs) was never mirrored into the portable ChartLayoutEngine --
// the shared engine both the Avalonia in-app renderer (MainWindow.Charts.cs / AvaloniaChartRenderer.cs)
// and FreeX's own PDF chart export (WorkbookPdfContentBuilder.cs) call. These tests pin the portable
// engine's own date-axis and axis-crosses behavior directly (ChartLayoutEngine.Layout), which is what
// both the Avalonia shell AND PDF export actually render from.
public sealed class R131_ChartLayoutDateAxisAndCrossesTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1);
    private static readonly DateTime Day2 = new(2026, 1, 2);
    private static readonly DateTime Day10 = new(2026, 1, 10);

    // ---- Date category axis: Column (non-stacked) -----------------------------------------

    [Fact]
    public void Column_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        var chart = Chart(ChartType.Column, c => c.XAxisIsDateAxis = true);
        var request = Request(chart, ["2026-01-01", "2026-01-02", "2026-01-10"],
            [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);
        var scale = layout.CategoryAxis!.Scale;

        var day1X = scale.Transform(Day1.ToOADate());
        var day2X = scale.Transform(Day2.ToOADate());
        var day10X = scale.Transform(Day10.ToOADate());

        var bars = layout.Series[0].Bars;
        bars.Should().HaveCount(3);
        bars[0].Rect.Center.X.Should().BeApproximately(day1X, 1e-6);
        bars[1].Rect.Center.X.Should().BeApproximately(day2X, 1e-6);
        bars[2].Rect.Center.X.Should().BeApproximately(day10X, 1e-6);

        // The 1-day gap (bar 0 -> bar 1) must be far smaller than the 8-day gap (bar 1 -> bar 2) --
        // an evenly spaced index axis would make both gaps equal (1 category-unit each).
        var firstGap = bars[1].Rect.Center.X - bars[0].Rect.Center.X;
        var secondGap = bars[2].Rect.Center.X - bars[1].Rect.Center.X;
        secondGap.Should().BeApproximately(8 * firstGap, 1e-6);
    }

    [Fact]
    public void StackedColumn_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        // WPF-family gap: the WPF stacked-column date-axis fix lives in a separate file
        // (ChartRenderer.Stacked.cs) from the non-stacked fix; the portable engine's
        // LayoutColumnLineArea unifies both stacked and non-stacked under one categoryScale, so this
        // proves the single fix covers the stacked path too.
        var chart = Chart(ChartType.StackedColumn, c => c.XAxisIsDateAxis = true);
        var request = Request(chart, ["2026-01-01", "2026-01-02", "2026-01-10"],
            [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);
        var scale = layout.CategoryAxis!.Scale;

        var bars = layout.Series[0].Bars;
        bars.Should().HaveCount(3);
        var firstGap = bars[1].Rect.Center.X - bars[0].Rect.Center.X;
        var secondGap = bars[2].Rect.Center.X - bars[1].Rect.Center.X;
        secondGap.Should().BeApproximately(8 * firstGap, 1e-6);

        // Sanity: still stacked (single category has both segments stacked, not side by side).
        scale.Should().NotBeNull();
    }

    [Fact]
    public void Line_DateCategoryAxis_PlotsProportionalToActualDates()
    {
        var chart = Chart(ChartType.Line, c => c.XAxisIsDateAxis = true);
        var request = Request(chart, ["2026-01-01", "2026-01-02", "2026-01-10"],
            [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);
        var points = layout.Series[0].Points;
        points.Should().HaveCount(3);

        var firstGap = points[1].Position.X - points[0].Position.X;
        var secondGap = points[2].Position.X - points[1].Position.X;
        secondGap.Should().BeApproximately(8 * firstGap, 1e-6);
    }

    // ---- Fallback / guard tests (must NOT widen past XAxisIsDateAxis) ----------------------

    // Sibling of Column_DateCategoryAxis_PlotsProportionalToActualDates: a chart that never opted
    // into a date axis (XAxisIsDateAxis stays false) must keep the plain evenly-spaced index axis
    // even though its category labels happen to parse as real dates -- proving the fix is gated on
    // the XAxisIsDateAxis flag, not merely on whether the labels look like dates.
    [Fact]
    public void Column_DateLikeCategoriesWithoutDateAxisFlag_StaysEvenlySpacedIndexAxis()
    {
        var chart = Chart(ChartType.Column); // XAxisIsDateAxis defaults to false
        chart.XAxisIsDateAxis.Should().BeFalse();
        var request = Request(chart, ["2026-01-01", "2026-01-02", "2026-01-10"],
            [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);
        var scale = layout.CategoryAxis!.Scale;

        var bars = layout.Series[0].Bars;
        bars[0].Rect.Center.X.Should().BeApproximately(scale.Transform(0), 1e-6);
        bars[1].Rect.Center.X.Should().BeApproximately(scale.Transform(1), 1e-6);
        bars[2].Rect.Center.X.Should().BeApproximately(scale.Transform(2), 1e-6);
    }

    // A chart marked as a date axis but whose category text isn't actually parseable as dates must
    // fall back to the plain indexed axis rather than misplace every point.
    [Fact]
    public void Column_DateAxisFlagWithUnparsableCategories_FallsBackToIndexAxis()
    {
        var chart = Chart(ChartType.Column, c => c.XAxisIsDateAxis = true);
        var request = Request(chart, ["Alpha", "Beta", "Gamma"], [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);
        var scale = layout.CategoryAxis!.Scale;

        var bars = layout.Series[0].Bars;
        bars[0].Rect.Center.X.Should().BeApproximately(scale.Transform(0), 1e-6);
        bars[1].Rect.Center.X.Should().BeApproximately(scale.Transform(1), 1e-6);
        bars[2].Rect.Center.X.Should().BeApproximately(scale.Transform(2), 1e-6);
    }

    // Plain (non-date) category axis fallback test required by the remediation rules: proves a chart
    // that never touches XAxisIsDateAxis at all renders byte-identically to before this fix (same
    // assertion style as the pre-existing Column_bars_are_centered_on_category_index test).
    [Fact]
    public void Column_PlainTextCategoryAxis_StaysEvenlySpacedIndexAxis()
    {
        var request = Request(Chart(ChartType.Column), ["North", "South", "East"],
            [Series(0, "S1", 10, 20, 30)]);

        var layout = ChartLayoutEngine.Layout(request);
        var scale = layout.CategoryAxis!.Scale;

        var bars = layout.Series[0].Bars;
        bars[0].Rect.Center.X.Should().BeApproximately(scale.Transform(0), 1e-6);
        bars[1].Rect.Center.X.Should().BeApproximately(scale.Transform(1), 1e-6);
        bars[2].Rect.Center.X.Should().BeApproximately(scale.Transform(2), 1e-6);
    }

    // ---- Axis crosses: Bar chart (mirrors the WPF BarRenderer_ValueAxisCrossesAtMaximum test) ----

    [Fact]
    public void Bar_ValueAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Bar, c => c.XAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, ["Q1", "Q2"], [Series(0, "S1", 10, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        // The value axis physically sits Bottom by default; ChartAxisCrosses.Maximum flips it to Top.
        layout.ValueAxis!.Side.Should().Be(AxisSide.Top);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Top, 1e-6);
    }

    [Fact]
    public void Bar_ValueAxisCrossesDefaultAutoZero_StaysAtBottomEdge()
    {
        var chart = Chart(ChartType.Bar);
        chart.XAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, ["Q1", "Q2"], [Series(0, "S1", 10, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Side.Should().Be(AxisSide.Bottom);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Bottom, 1e-6);
    }

    [Fact]
    public void Bar_CategoryAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Bar, c => c.YAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, ["Q1", "Q2"], [Series(0, "S1", 10, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        // The category axis physically sits Left by default; ChartAxisCrosses.Maximum flips it to Right.
        layout.CategoryAxis!.Side.Should().Be(AxisSide.Right);
        layout.CategoryAxis.LinePosition.Should().BeApproximately(plot.Right, 1e-6);
    }

    // ---- Axis crosses: Column (category Bottom / value Left) -------------------------------

    [Fact]
    public void Column_ValueAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Column, c => c.YAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, ["A", "B"], [Series(0, "S1", 10, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Side.Should().Be(AxisSide.Right);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Right, 1e-6);
    }

    [Fact]
    public void Column_CategoryAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Column, c => c.XAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, ["A", "B"], [Series(0, "S1", 10, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Side.Should().Be(AxisSide.Top);
        layout.CategoryAxis.LinePosition.Should().BeApproximately(plot.Top, 1e-6);
    }

    // Sibling guard test: the overwhelming majority of charts never set XAxisCrosses/YAxisCrosses,
    // leaving both at ChartModel's own default (AutoZero). That default must keep the category axis
    // at its original Bottom edge exactly as before -- proving the crosses fix only reacts to an
    // explicit Maximum, not the common default.
    [Fact]
    public void Column_CrossesDefaultAutoZero_StaysAtOriginalEdges()
    {
        var chart = Chart(ChartType.Column);
        chart.XAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        chart.YAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, ["A", "B"], [Series(0, "S1", 10, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Side.Should().Be(AxisSide.Bottom);
        layout.ValueAxis!.Side.Should().Be(AxisSide.Left);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Left, 1e-6);
    }

    // ---- Axis crosses: Scatter (both axes are value axes: X Bottom / Y Left) ---------------
    //
    // RESIDUAL from the r131 axis-crosses remediation: LayoutScatter/LayoutBubble hardcoded
    // AxisSide.Bottom/AxisSide.Left and never called ApplyAxisCrosses, so a Scatter/Bubble chart
    // with "Axis crosses -> Maximum" drew correctly in WPF (reached via the shared ApplyAxisBounds
    // call at ChartRenderer.cs:656 for Scatter, and the explicit call at ChartRenderer.cs:258 for
    // Bubble) but stayed pinned at the default Bottom/Left edge in the Avalonia/PDF portable engine.

    [Fact]
    public void Scatter_XAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Scatter, c => c.XAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, [], [ScatterSeries(0, "S1", [1, 2, 3], 10, 20, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        // The X (category-slot) axis physically sits Bottom by default; ChartAxisCrosses.Maximum
        // flips it to Top -- the axis the CategoryAxis property carries for Scatter/Bubble layouts.
        layout.CategoryAxis!.Side.Should().Be(AxisSide.Top);
        layout.CategoryAxis.LinePosition.Should().BeApproximately(plot.Top, 1e-6);
    }

    [Fact]
    public void Scatter_YAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Scatter, c => c.YAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, [], [ScatterSeries(0, "S1", [1, 2, 3], 10, 20, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        // The Y (value) axis physically sits Left by default; ChartAxisCrosses.Maximum flips it to
        // Right -- the axis the ValueAxis property carries for Scatter/Bubble layouts.
        layout.ValueAxis!.Side.Should().Be(AxisSide.Right);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Right, 1e-6);
    }

    // Sibling guard test: the common default (AutoZero on both axes) must leave a Scatter chart's
    // axes at their original Bottom/Left edges exactly as before this fix.
    [Fact]
    public void Scatter_CrossesDefaultAutoZero_StaysAtOriginalEdges()
    {
        var chart = Chart(ChartType.Scatter);
        chart.XAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        chart.YAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, [], [ScatterSeries(0, "S1", [1, 2, 3], 10, 20, 30)], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Side.Should().Be(AxisSide.Bottom);
        layout.CategoryAxis.LinePosition.Should().BeApproximately(plot.Bottom, 1e-6);
        layout.ValueAxis!.Side.Should().Be(AxisSide.Left);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Left, 1e-6);
    }

    // ---- Axis crosses: Bubble (same layout shape as Scatter: X Bottom / Y Left) ------------

    [Fact]
    public void Bubble_XAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Bubble, c => c.XAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, [], [BubbleSeries(0, "S1", [1, 2, 3], [10, 20, 30], [5, 5, 5])], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Side.Should().Be(AxisSide.Top);
        layout.CategoryAxis.LinePosition.Should().BeApproximately(plot.Top, 1e-6);
    }

    [Fact]
    public void Bubble_YAxisCrossesAtMaximum_MovesAxisToOppositeEdge()
    {
        var chart = Chart(ChartType.Bubble, c => c.YAxisCrosses = ChartAxisCrosses.Maximum);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, [], [BubbleSeries(0, "S1", [1, 2, 3], [10, 20, 30], [5, 5, 5])], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Side.Should().Be(AxisSide.Right);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Right, 1e-6);
    }

    // Sibling guard test: the common default (AutoZero on both axes) must leave a Bubble chart's
    // axes at their original Bottom/Left edges exactly as before this fix.
    [Fact]
    public void Bubble_CrossesDefaultAutoZero_StaysAtOriginalEdges()
    {
        var chart = Chart(ChartType.Bubble);
        chart.XAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        chart.YAxisCrosses.Should().Be(ChartAxisCrosses.AutoZero);
        var plot = new PlotRect(0, 0, 300, 200);
        var request = Request(chart, [], [BubbleSeries(0, "S1", [1, 2, 3], [10, 20, 30], [5, 5, 5])], plot);

        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Side.Should().Be(AxisSide.Bottom);
        layout.CategoryAxis.LinePosition.Should().BeApproximately(plot.Bottom, 1e-6);
        layout.ValueAxis!.Side.Should().Be(AxisSide.Left);
        layout.ValueAxis.LinePosition.Should().BeApproximately(plot.Left, 1e-6);
    }
}
