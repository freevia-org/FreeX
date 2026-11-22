using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>Trendline / error bar / data point / legend entry / secondary-axis-title selection.</summary>
public sealed class ChartElementSelectionExtendedTests
{
    private static readonly PlotRect Inset = new(50, 40, 300, 200);

    private static (ChartModel Chart, ChartLayout Layout, ChartElementMap Map) Build(
        ChartType type,
        Action<ChartModel>? configure = null)
    {
        var chart = Chart(type, c =>
        {
            c.Title = "Sales";
            c.ShowLegend = true;
            c.ShowYAxisMajorGridlines = false;
            configure?.Invoke(c);
        });
        var request = Request(
            chart,
            ["Q1", "Q2", "Q3", "Q4"],
            [Series(0, "North", 10, 20, 30, 45), Series(1, "South", 5, 15, 25, 20)],
            Inset);
        var layout = ChartLayoutEngine.Layout(request);
        return (chart, layout, ChartElementMapPlanner.Plan(chart, layout, 400, 300, request.TextMeasurer));
    }

    // ---- Trendline ------------------------------------------------------------------------

    [Fact]
    public void Trendline_is_selectable_and_wins_over_the_series_it_crosses()
    {
        var (_, layout, map) = Build(ChartType.Line, c => c.ShowLinearTrendline = true);
        var points = layout.Series[0].Trendline!.Points;
        var mid = new LayoutPoint((points[0].X + points[^1].X) / 2, (points[0].Y + points[^1].Y) / 2);

        map.HitTest(mid).Should().Be(ChartElementRef.ForTrendline(0));
        map.GetHighlightBounds(ChartElementRef.ForTrendline(0)).Should().ContainSingle();
    }

    [Fact]
    public void Delete_trendline_hides_it_and_is_a_noop_when_absent()
    {
        var withTrend = Chart(ChartType.Line, c => c.ShowLinearTrendline = true);
        var plan = ChartElementCommandPlanner.PlanDelete(withTrend, ChartElementRef.ForTrendline(0));
        plan.Action.Should().Be(ChartElementDeleteAction.ApplyLayout);
        plan.Options!.ShowLinearTrendline.Should().BeFalse();

        ChartElementCommandPlanner.PlanDelete(Chart(ChartType.Line), ChartElementRef.ForTrendline(0)).Action
            .Should().Be(ChartElementDeleteAction.None);
    }

    // ---- Error bars -----------------------------------------------------------------------

    [Fact]
    public void Error_bar_whisker_is_selectable_over_the_bar_it_belongs_to()
    {
        var (_, layout, map) = Build(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.ErrorBarKind = ChartErrorBarKind.FixedValue;
            c.ErrorBarValue = 5;
            c.ErrorBarEndCaps = true;
        });
        var whisker = layout.Series[1].ErrorBars!.Whiskers[2];
        var halfway = new LayoutPoint(
            (whisker.Center.X + whisker.PlusEnd.X) / 2,
            (whisker.Center.Y + whisker.PlusEnd.Y) / 2);

        map.HitTest(halfway).Should().Be(ChartElementRef.ForErrorBars(1));
    }

    [Fact]
    public void Delete_error_bars_turns_them_off()
    {
        var chart = Chart(ChartType.Column, c => c.ShowErrorBars = true);
        var plan = ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForErrorBars(0));

        plan.Options!.ShowErrorBars.Should().BeFalse();
        ChartElementCommandPlanner.GetFormatTarget(chart, ChartElementRef.ForErrorBars(0))
            .Should().Be(ChartElementFormatTarget.ErrorBars);
        ChartElementCommandPlanner.GetFormatTarget(chart, ChartElementRef.ForTrendline(0))
            .Should().Be(ChartElementFormatTarget.Trendline);
    }

    // ---- Data points (second click on a selected series) ----------------------------------

    [Fact]
    public void Second_click_on_a_selected_series_selects_the_data_point_under_the_pointer()
    {
        var (_, layout, map) = Build(ChartType.Column);
        var bar = layout.Series[0].Bars[2];

        map.ResolveClick(bar.Rect.Center, null).Should().Be(ChartElementRef.ForSeries(0));
        map.ResolveClick(bar.Rect.Center, ChartElementRef.ForSeries(0))
            .Should().Be(ChartElementRef.ForDataPoint(0, bar.PointIndex));
        // A different series selected: the first click just selects the clicked series.
        map.ResolveClick(bar.Rect.Center, ChartElementRef.ForSeries(1)).Should().Be(ChartElementRef.ForSeries(0));
        // Clicking another point of the same series while a point is selected moves the point selection.
        var other = layout.Series[0].Bars[0];
        map.ResolveClick(other.Rect.Center, ChartElementRef.ForDataPoint(0, bar.PointIndex))
            .Should().Be(ChartElementRef.ForDataPoint(0, other.PointIndex));
    }

    [Fact]
    public void Line_series_points_are_selectable_and_highlighted_individually()
    {
        var (_, layout, map) = Build(ChartType.Line);
        var point = layout.Series[1].Points[1];
        var selected = ChartElementRef.ForDataPoint(1, point.PointIndex);

        map.ResolveClick(point.Position, ChartElementRef.ForSeries(1)).Should().Be(selected);
        map.Contains(selected).Should().BeTrue();
        var bounds = map.GetHighlightBounds(selected).Should().ContainSingle().Subject;
        bounds.Left.Should().BeLessThanOrEqualTo(point.Position.X);
        bounds.Right.Should().BeGreaterThanOrEqualTo(point.Position.X);
        ChartElementCommandPlanner.GetFormatTarget(Chart(ChartType.Line), selected)
            .Should().Be(ChartElementFormatTarget.DataSeries);
        // Excel: Delete on a selected point removes the point's whole series.
        var delete = ChartElementCommandPlanner.PlanDelete(Chart(ChartType.Line), selected);
        delete.Action.Should().Be(ChartElementDeleteAction.RemoveSeries);
        delete.SeriesOrdinal.Should().Be(1);
    }

    [Fact]
    public void Pie_slices_are_selectable_as_points()
    {
        var chart = Chart(ChartType.Pie, c => c.Title = "Share");
        var request = Request(chart, ["A", "B", "C"], [Series(0, "S", 1, 2, 3)], Inset);
        var layout = ChartLayoutEngine.Layout(request);
        var map = ChartElementMapPlanner.Plan(chart, layout, 400, 300, request.TextMeasurer);
        var slice = layout.Series[0].Slices[1];
        var arc = slice.Arc;
        var mid = (arc.StartAngleDegrees + (arc.SweepAngleDegrees / 2)) * Math.PI / 180;
        var radius = (arc.InnerRadius + arc.OuterRadius) / 2;
        var p = new LayoutPoint(arc.Center.X + (radius * Math.Sin(mid)), arc.Center.Y - (radius * Math.Cos(mid)));

        map.ResolveClick(p, ChartElementRef.ForSeries(0))
            .Should().Be(ChartElementRef.ForDataPoint(0, slice.PointIndex));
    }

    // ---- Legend entries -------------------------------------------------------------------

    [Fact]
    public void Second_click_on_a_selected_legend_selects_the_entry_under_the_pointer()
    {
        var (_, layout, map) = Build(ChartType.Column);
        var entry = layout.Legend.Entries[1];
        var p = entry.LabelRect.Center;
        var legend = new ChartElementRef(ChartElementKind.Legend);

        map.HitTest(p).Should().Be(legend);
        map.ResolveClick(p, null).Should().Be(legend);
        map.ResolveClick(p, legend).Should().Be(ChartElementRef.ForLegendEntry(entry.SeriesIndex));
        map.GetDisplayName(ChartElementRef.ForLegendEntry(entry.SeriesIndex)).Should().Be(entry.Label);
    }

    [Fact]
    public void Delete_legend_entry_marks_only_that_entry_deleted_in_plot_order()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.SeriesPlotOrder = [1, 0];
            c.LegendEntries = [new ChartLegendEntryModel(0, null, TextBold: true)];
        });

        var plan = ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForLegendEntry(0));

        // Series 0 is declared second, so its c:legendEntry index is 1; the existing bold entry is kept.
        var entries = plan.Options!.LegendEntries!;
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.Index == 0 && e.TextBold == true && e.IsDeleted == null);
        entries.Should().Contain(e => e.Index == 1 && e.IsDeleted == true);
    }

    [Fact]
    public void Double_click_on_a_selected_point_or_entry_targets_the_parent_dialog()
    {
        var (_, layout, map) = Build(ChartType.Column);
        var bar = layout.Series[0].Bars[2];
        var point = ChartElementRef.ForDataPoint(0, bar.PointIndex);

        map.IsDoubleClickOnSelection(bar.Rect.Center, point).Should().BeTrue();
        map.IsDoubleClickOnSelection(bar.Rect.Center, ChartElementRef.ForSeries(1)).Should().BeFalse();
        map.IsDoubleClickOnSelection(layout.Legend.Entries[0].LabelRect.Center, ChartElementRef.ForLegendEntry(0)).Should().BeTrue();
    }

    // ---- Secondary axis title -------------------------------------------------------------

    [Fact]
    public void Secondary_axis_title_is_selectable_and_deletable()
    {
        var (chart, layout, map) = Build(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
            c.YAxisTitle = "Revenue";
            c.SecondaryAxisTitle = "Growth";
        });

        layout.SecondaryValueAxis!.Title.Should().Be("Growth");
        layout.ValueAxis!.Title.Should().Be("Revenue");
        var region = map.Regions.Single(r => r.Element.Kind == ChartElementKind.SecondaryAxisTitle);
        map.HitTest(region.Shapes[0].Bounds.Center).Should().Be(new ChartElementRef(ChartElementKind.SecondaryAxisTitle));

        var plan = ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(ChartElementKind.SecondaryAxisTitle));
        plan.Options!.SecondaryAxisTitle.Should().BeEmpty();
        ChartElementCommandPlanner.GetFormatTarget(chart, new ChartElementRef(ChartElementKind.SecondaryAxisTitle))
            .Should().Be(ChartElementFormatTarget.ChartTitles);
    }

    // ---- Keyboard navigation --------------------------------------------------------------

    [Fact]
    public void Arrow_keys_walk_points_and_entries_and_cycle_top_level_elements()
    {
        var (_, layout, map) = Build(ChartType.Column);

        // Left/Right step through the points of the selected series and clamp at the ends.
        var first = ChartElementRef.ForDataPoint(0, 0);
        map.Navigate(ChartElementRef.ForSeries(0), ChartNavigationKey.Right).Should().Be(first);
        map.Navigate(first, ChartNavigationKey.Right).Should().Be(ChartElementRef.ForDataPoint(0, 1));
        map.Navigate(first, ChartNavigationKey.Left).Should().Be(first);

        // Legend entries likewise.
        var legend = new ChartElementRef(ChartElementKind.Legend);
        var entryA = ChartElementRef.ForLegendEntry(layout.Legend.Entries[0].SeriesIndex);
        var entryB = ChartElementRef.ForLegendEntry(layout.Legend.Entries[1].SeriesIndex);
        map.Navigate(legend, ChartNavigationKey.Right).Should().Be(entryA);
        map.Navigate(entryA, ChartNavigationKey.Right).Should().Be(entryB);

        // Up/Down cycle through the top-level elements and wrap around via the chart area.
        var visited = new List<ChartElementRef> { ChartElementRef.ChartArea };
        var current = map.Navigate(ChartElementRef.ChartArea, ChartNavigationKey.Down);
        while (!current.IsChartArea && visited.Count < 50)
        {
            visited.Add(current);
            current = map.Navigate(current, ChartNavigationKey.Down);
        }

        visited.Should().Contain(new ChartElementRef(ChartElementKind.PlotArea));
        visited.Should().Contain(ChartElementRef.ForSeries(0));
        visited.Should().Contain(legend);
        map.Navigate(ChartElementRef.ChartArea, ChartNavigationKey.Up).Should().Be(visited[^1]);
        map.Navigate(entryA, ChartNavigationKey.Down)
            .Should().Be(map.Navigate(legend, ChartNavigationKey.Down));
    }
}
