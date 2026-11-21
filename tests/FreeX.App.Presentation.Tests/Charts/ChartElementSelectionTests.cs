using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartElementSelectionTests
{
    private static readonly PlotRect Inset = new(50, 40, 300, 200);

    private static (ChartModel Chart, ChartLayout Layout, ChartElementMap Map) Build(
        ChartType type,
        Action<ChartModel>? configure = null,
        PlotRect? plot = null)
    {
        var chart = Chart(type, c =>
        {
            c.Title = "Sales";
            c.XAxisTitle = "Quarter";
            c.YAxisTitle = "Revenue";
            c.ShowYAxisMajorGridlines = true;
            configure?.Invoke(c);
        });
        var request = Request(
            chart,
            ["Q1", "Q2", "Q3"],
            [Series(0, "North", 10, 20, 30), Series(1, "South", 5, 15, 25)],
            plot ?? Inset);
        var layout = ChartLayoutEngine.Layout(request);
        var map = ChartElementMapPlanner.Plan(chart, layout, 400, 300, request.TextMeasurer);
        return (chart, layout, map);
    }

    [Fact]
    public void Click_on_empty_chart_corner_selects_chart_area()
    {
        var (_, _, map) = Build(ChartType.Column);

        map.HitTest(new LayoutPoint(2, 295)).Should().Be(ChartElementRef.ChartArea);
    }

    [Fact]
    public void Click_outside_the_chart_selects_nothing()
    {
        var (_, _, map) = Build(ChartType.Column);

        map.HitTest(new LayoutPoint(-1, 10)).Should().BeNull();
        map.HitTest(new LayoutPoint(10, 301)).Should().BeNull();
    }

    [Fact]
    public void Click_on_title_selects_title()
    {
        var (_, _, map) = Build(ChartType.Column);

        map.HitTest(new LayoutPoint(200, 10)).Should().Be(new ChartElementRef(ChartElementKind.ChartTitle));
    }

    [Fact]
    public void Click_on_a_column_selects_that_series_and_the_other_series_is_distinct()
    {
        var (_, layout, map) = Build(ChartType.Column);

        foreach (var series in layout.Series)
        {
            var bar = series.Bars[1].Rect;
            map.HitTest(bar.Center).Should().Be(ChartElementRef.ForSeries(series.SeriesIndex));
        }
    }

    [Fact]
    public void Click_in_plot_area_between_bars_selects_plot_area()
    {
        var (_, layout, map) = Build(ChartType.Column, c => c.ShowYAxisMajorGridlines = false);
        var plot = layout.PlotArea;

        // The top-left corner of the plot is above every bar and off any gridline.
        map.HitTest(new LayoutPoint(plot.Left + 8, plot.Top + 2))
            .Should().Be(new ChartElementRef(ChartElementKind.PlotArea));
    }

    [Fact]
    public void Click_on_a_major_gridline_selects_value_gridlines()
    {
        var (_, layout, map) = Build(ChartType.Column);
        var tick = layout.ValueAxis!.Ticks.First(t => t.Position > layout.PlotArea.Top + 6 && t.Position < layout.PlotArea.Bottom - 6);

        // Far left of the plot, left of any bar.
        map.HitTest(new LayoutPoint(layout.PlotArea.Left + 8, tick.Position))
            .Should().Be(new ChartElementRef(ChartElementKind.ValueGridlines));
    }

    [Fact]
    public void Gridlines_are_not_selectable_when_the_chart_does_not_show_them()
    {
        var (_, layout, map) = Build(ChartType.Column, c => c.ShowYAxisMajorGridlines = false);

        map.Regions.Should().NotContain(r => r.Element.Kind == ChartElementKind.ValueGridlines);
        layout.ValueAxis.Should().NotBeNull();
    }

    [Fact]
    public void Click_on_axis_label_gutters_selects_the_axis()
    {
        var (_, layout, map) = Build(ChartType.Column);
        var plot = layout.PlotArea;

        map.HitTest(new LayoutPoint(layout.ValueAxis!.LinePosition - 8, plot.Top + (plot.Height / 2)))
            .Should().Be(new ChartElementRef(ChartElementKind.ValueAxis));
        map.HitTest(new LayoutPoint(plot.Left + (plot.Width / 2), layout.CategoryAxis!.LinePosition + 8))
            .Should().Be(new ChartElementRef(ChartElementKind.CategoryAxis));
    }

    [Fact]
    public void Click_on_axis_titles_selects_the_title_not_the_axis()
    {
        var (_, layout, map) = Build(ChartType.Column);
        var plot = layout.PlotArea;
        var bottom = layout.CategoryAxis!;

        // Bottom title is centered under the labels.
        map.HitTest(new LayoutPoint(plot.Left + (plot.Width / 2), bottom.LinePosition + 4 + 14 + 2 + 6))
            .Should().Be(new ChartElementRef(ChartElementKind.CategoryAxisTitle));
        map.HitTest(new LayoutPoint(layout.ValueAxis!.LinePosition - 4 - 18 - 6, plot.Top + (plot.Height / 2)))
            .Should().Be(new ChartElementRef(ChartElementKind.ValueAxisTitle));
    }

    [Fact]
    public void Click_on_legend_selects_legend()
    {
        var (_, layout, map) = Build(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        }, new PlotRect(50, 40, 250, 200));

        layout.Legend.Entries.Should().NotBeEmpty();
        map.HitTest(layout.Legend.Bounds.Center).Should().Be(new ChartElementRef(ChartElementKind.Legend));
    }

    [Fact]
    public void Bar_chart_hit_testing_uses_horizontal_bars()
    {
        var (_, layout, map) = Build(ChartType.Bar);

        var bar = layout.Series[0].Bars[2].Rect;
        map.HitTest(bar.Center).Should().Be(ChartElementRef.ForSeries(0));
    }

    [Fact]
    public void Pie_slices_hit_test_by_angle_and_radius()
    {
        var (_, layout, map) = Build(ChartType.Pie);

        var slice = layout.Series[0].Slices[0];
        var arc = slice.Arc;
        var mid = arc.MidAngleDegrees * Math.PI / 180.0;
        var radius = (arc.OuterRadius + arc.InnerRadius) / 2;
        var point = new LayoutPoint(arc.Center.X + (radius * Math.Sin(mid)), arc.Center.Y - (radius * Math.Cos(mid)));

        map.HitTest(point).Should().Be(ChartElementRef.ForSeries(layout.Series[0].SeriesIndex));
    }

    [Fact]
    public void Line_series_is_hit_near_its_segment_but_not_far_from_it()
    {
        var (_, layout, map) = Build(ChartType.Line, c => c.ShowYAxisMajorGridlines = false);
        var points = layout.Series[0].Points;
        var mid = new LayoutPoint(
            (points[0].Position.X + points[1].Position.X) / 2,
            (points[0].Position.Y + points[1].Position.Y) / 2);

        map.HitTest(mid).Should().Be(ChartElementRef.ForSeries(layout.Series[0].SeriesIndex));
        map.HitTest(new LayoutPoint(mid.X, mid.Y - 30)).Should().NotBe(ChartElementRef.ForSeries(layout.Series[0].SeriesIndex));
    }

    [Fact]
    public void Highlight_bounds_outline_each_bar_of_a_series_and_the_whole_chart_for_chart_area()
    {
        var (_, layout, map) = Build(ChartType.Column);

        map.GetHighlightBounds(ChartElementRef.ForSeries(0)).Should().HaveCount(layout.Series[0].Bars.Count);
        map.GetHighlightBounds(ChartElementRef.ChartArea).Should().ContainSingle().Which.Should().Be(new LayoutRect(0, 0, 400, 300));
        map.GetHighlightBounds(new ChartElementRef(ChartElementKind.ChartTitle)).Should().ContainSingle();
    }

    [Fact]
    public void Frame_overrides_win_over_estimated_title_and_legend_rects()
    {
        var chart = Chart(ChartType.Column, c => c.Title = "Sales");
        var request = Request(chart, ["Q1"], [Series(0, "A", 1)], Inset);
        var layout = ChartLayoutEngine.Layout(request);
        var frame = new ChartElementFrame
        {
            TitleBounds = new LayoutRect(10, 10, 50, 20),
            LegendBounds = new LayoutRect(300, 100, 60, 40),
        };

        var map = ChartElementMapPlanner.Plan(chart, layout, 400, 300, request.TextMeasurer, frame);

        map.HitTest(new LayoutPoint(15, 15)).Should().Be(new ChartElementRef(ChartElementKind.ChartTitle));
        map.HitTest(new LayoutPoint(320, 120)).Should().Be(new ChartElementRef(ChartElementKind.Legend));
    }

    [Fact]
    public void Without_a_layout_only_title_legend_plot_and_chart_area_remain_selectable()
    {
        var chart = Chart(ChartType.Map, c => c.Title = "Geo");
        var map = ChartElementMapPlanner.Plan(chart, null, 400, 300, new FakeTextMeasurer());

        map.HitTest(new LayoutPoint(200, 10)).Should().Be(new ChartElementRef(ChartElementKind.ChartTitle));
        map.HitTest(new LayoutPoint(200, 150)).Should().Be(new ChartElementRef(ChartElementKind.PlotArea));
    }

    // ---- delete + format target -----------------------------------------------------------

    [Fact]
    public void Delete_title_clears_the_title_text()
    {
        var chart = Chart(ChartType.Column, c => c.Title = "Sales");

        var plan = ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(ChartElementKind.ChartTitle));

        plan.Action.Should().Be(ChartElementDeleteAction.ApplyLayout);
        plan.Options!.Title.Should().BeEmpty();
    }

    [Fact]
    public void Delete_legend_hides_the_legend_and_is_a_noop_when_already_hidden()
    {
        var shown = Chart(ChartType.Column, c => c.ShowLegend = true);
        var hidden = Chart(ChartType.Column, c => c.ShowLegend = false);
        var legend = new ChartElementRef(ChartElementKind.Legend);

        ChartElementCommandPlanner.PlanDelete(shown, legend).Options!.ShowLegend.Should().BeFalse();
        ChartElementCommandPlanner.PlanDelete(hidden, legend).Action.Should().Be(ChartElementDeleteAction.None);
    }

    [Theory]
    [InlineData(ChartType.Column, ChartElementKind.CategoryAxis, true)]
    [InlineData(ChartType.Column, ChartElementKind.ValueAxis, false)]
    [InlineData(ChartType.Bar, ChartElementKind.CategoryAxis, false)]
    [InlineData(ChartType.Bar, ChartElementKind.ValueAxis, true)]
    public void Delete_axis_hides_the_labels_of_the_matching_physical_axis(ChartType type, ChartElementKind kind, bool horizontal)
    {
        var chart = Chart(type);

        var options = ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(kind)).Options!;

        if (horizontal)
        {
            options.ShowXAxisLabels.Should().BeFalse();
            options.ShowYAxisLabels.Should().BeNull();
        }
        else
        {
            options.ShowYAxisLabels.Should().BeFalse();
            options.ShowXAxisLabels.Should().BeNull();
        }
    }

    [Fact]
    public void Delete_gridlines_and_axis_titles_target_the_right_axis()
    {
        var chart = Chart(ChartType.Column);

        ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(ChartElementKind.ValueGridlines))
            .Options!.ShowYAxisMajorGridlines.Should().BeFalse();
        ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(ChartElementKind.CategoryAxisTitle))
            .Options!.XAxisTitle.Should().BeEmpty();
        ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(ChartElementKind.ValueAxisTitle))
            .Options!.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Delete_series_chart_area_and_plot_area_follow_excel()
    {
        var chart = Chart(ChartType.Column);

        var series = ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForSeries(1));
        series.Action.Should().Be(ChartElementDeleteAction.RemoveSeries);
        series.SeriesOrdinal.Should().Be(1);

        ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ChartArea).Action
            .Should().Be(ChartElementDeleteAction.DeleteChart);
        ChartElementCommandPlanner.PlanDelete(chart, new ChartElementRef(ChartElementKind.PlotArea)).Action
            .Should().Be(ChartElementDeleteAction.None);
    }

    [Theory]
    [InlineData(ChartType.Column, ChartElementKind.CategoryAxis, ChartElementFormatTarget.XAxis)]
    [InlineData(ChartType.Column, ChartElementKind.ValueGridlines, ChartElementFormatTarget.YAxis)]
    [InlineData(ChartType.Bar, ChartElementKind.CategoryAxis, ChartElementFormatTarget.YAxis)]
    [InlineData(ChartType.Column, ChartElementKind.ChartTitle, ChartElementFormatTarget.ChartTitles)]
    [InlineData(ChartType.Column, ChartElementKind.ValueAxisTitle, ChartElementFormatTarget.ChartTitles)]
    [InlineData(ChartType.Column, ChartElementKind.Legend, ChartElementFormatTarget.ChartArea)]
    [InlineData(ChartType.Column, ChartElementKind.PlotArea, ChartElementFormatTarget.ChartArea)]
    [InlineData(ChartType.Column, ChartElementKind.Series, ChartElementFormatTarget.DataSeries)]
    [InlineData(ChartType.Column, ChartElementKind.DataLabels, ChartElementFormatTarget.DataLabels)]
    public void Format_selection_targets_the_matching_existing_dialog(
        ChartType type,
        ChartElementKind kind,
        ChartElementFormatTarget expected)
    {
        ChartElementCommandPlanner.GetFormatTarget(Chart(type), new ChartElementRef(kind, kind == ChartElementKind.Series ? 0 : -1))
            .Should().Be(expected);
    }

    [Fact]
    public void Every_element_kind_has_a_description_resource()
    {
        foreach (var kind in Enum.GetValues<ChartElementKind>())
        {
            var descriptor = ChartElementCommandPlanner.Describe(new ChartElementRef(kind, 0), "North");
            descriptor.ResourceKey.Should().StartWith("ChartElement_");
        }
    }
}
