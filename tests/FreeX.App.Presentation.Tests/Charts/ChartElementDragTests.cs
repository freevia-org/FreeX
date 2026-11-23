using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartElementDragTests
{
    private const double W = 400;
    private const double H = 300;
    private static readonly PlotRect Inset = new(50, 40, 300, 200);

    private static (ChartModel Chart, ChartLayout Layout, ChartElementMap Map) Build(
        ChartType type = ChartType.Column,
        Action<ChartModel>? configure = null)
    {
        var chart = Chart(type, c =>
        {
            c.Title = "Sales";
            c.XAxisTitle = "Quarter";
            c.YAxisTitle = "Revenue";
            configure?.Invoke(c);
        });
        var request = Request(chart, ["Q1", "Q2", "Q3"], [Series(0, "North", 10, 20, 30), Series(1, "South", 5, 15, 25)], Inset)
            .WithChartSize(W, H);
        var layout = ChartLayoutEngine.Layout(request);
        var map = ChartElementMapPlanner.Plan(chart, layout, W, H, request.TextMeasurer);
        return (chart, layout, map);
    }

    private static LayoutPoint Center(LayoutRect r) => r.Center;

    [Fact]
    public void Draggable_elements_match_excel()
    {
        foreach (var kind in new[]
                 {
                     ChartElementKind.ChartTitle, ChartElementKind.Legend, ChartElementKind.PlotArea,
                     ChartElementKind.CategoryAxisTitle, ChartElementKind.ValueAxisTitle, ChartElementKind.DataLabels,
                 })
        {
            ChartElementDragPlanner.IsDraggable(new ChartElementRef(kind)).Should().BeTrue(kind.ToString());
        }

        ChartElementDragPlanner.IsDraggable(ChartElementRef.ForSeries(0)).Should().BeFalse();
        ChartElementDragPlanner.IsDraggable(new ChartElementRef(ChartElementKind.CategoryAxis)).Should().BeFalse();
        ChartElementDragPlanner.IsDraggable(ChartElementRef.ChartArea).Should().BeFalse();
    }

    [Fact]
    public void Dragging_the_title_stores_an_edge_layout_of_the_new_top_left()
    {
        var (chart, _, map) = Build();
        var element = new ChartElementRef(ChartElementKind.ChartTitle);
        var start = map.GetHighlightBounds(element)[0];

        var session = ChartElementDragPlanner.Begin(chart, map, element, Center(start))!;
        session.Update(new LayoutPoint(Center(start).X + 40, Center(start).Y + 30));
        var options = session.Complete()!;

        options.TitleLayout.Should().NotBeNull();
        options.TitleLayout!.XMode.Should().Be("edge");
        options.TitleLayout.X.Should().BeApproximately((start.Left + 40) / W, 1e-5);
        options.TitleLayout.Y.Should().BeApproximately((start.Top + 30) / H, 1e-5);
        options.TitleLayout.Width.Should().BeNull();
    }

    [Fact]
    public void A_click_without_movement_plans_nothing()
    {
        var (chart, _, map) = Build();
        var element = new ChartElementRef(ChartElementKind.ChartTitle);
        var c = Center(map.GetHighlightBounds(element)[0]);

        var session = ChartElementDragPlanner.Begin(chart, map, element, c)!;
        session.Update(new LayoutPoint(c.X + 1, c.Y));

        session.HasMoved.Should().BeFalse();
        session.Complete().Should().BeNull();
    }

    [Fact]
    public void Drag_is_clamped_inside_the_chart()
    {
        var (chart, _, map) = Build();
        var element = new ChartElementRef(ChartElementKind.ChartTitle);
        var c = Center(map.GetHighlightBounds(element)[0]);

        var session = ChartElementDragPlanner.Begin(chart, map, element, c)!;
        session.Update(new LayoutPoint(c.X + 5000, c.Y + 5000));

        session.CurrentBounds.Right.Should().BeLessThanOrEqualTo(W + 1e-6);
        session.CurrentBounds.Bottom.Should().BeLessThanOrEqualTo(H + 1e-6);
    }

    [Fact]
    public void Axis_titles_map_to_x_and_y_layouts_by_orientation()
    {
        var (chart, _, map) = Build();
        var category = new ChartElementRef(ChartElementKind.CategoryAxisTitle);
        var value = new ChartElementRef(ChartElementKind.ValueAxisTitle);

        var s1 = ChartElementDragPlanner.Begin(chart, map, category, Center(map.GetHighlightBounds(category)[0]))!;
        s1.Update(new LayoutPoint(Center(s1.StartBounds).X + 20, Center(s1.StartBounds).Y - 10));
        s1.Complete()!.XAxisTitleLayout.Should().NotBeNull();

        var s2 = ChartElementDragPlanner.Begin(chart, map, value, Center(map.GetHighlightBounds(value)[0]))!;
        s2.Update(new LayoutPoint(Center(s2.StartBounds).X + 20, Center(s2.StartBounds).Y + 10));
        s2.Complete()!.YAxisTitleLayout.Should().NotBeNull();
    }

    [Fact]
    public void Bar_chart_category_title_is_the_vertical_y_title()
    {
        var (chart, _, map) = Build(ChartType.Bar);
        var category = new ChartElementRef(ChartElementKind.CategoryAxisTitle);

        var s = ChartElementDragPlanner.Begin(chart, map, category, Center(map.GetHighlightBounds(category)[0]))!;
        s.Update(new LayoutPoint(Center(s.StartBounds).X + 15, Center(s.StartBounds).Y + 15));

        s.Complete()!.YAxisTitleLayout.Should().NotBeNull();
    }

    [Fact]
    public void Dragging_the_legend_stores_position_and_size()
    {
        var (chart, _, map) = Build(configure: c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        });
        var element = new ChartElementRef(ChartElementKind.Legend);
        var start = map.GetHighlightBounds(element)[0];

        var s = ChartElementDragPlanner.Begin(chart, map, element, Center(start))!;
        s.Update(new LayoutPoint(Center(start).X - 60, Center(start).Y + 20));
        var layout = s.Complete()!.LegendLayout!;

        layout.X.Should().BeApproximately((start.Left - 60) / W, 1e-5);
        layout.Width.Should().BeApproximately(start.Width / W, 1e-5);
    }

    [Fact]
    public void Plot_area_move_and_resize_store_inner_layout()
    {
        var (chart, _, map) = Build();
        var element = new ChartElementRef(ChartElementKind.PlotArea);
        var start = map.GetHighlightBounds(element)[0];

        var move = ChartElementDragPlanner.Begin(chart, map, element, Center(start))!;
        move.Update(new LayoutPoint(Center(start).X + 10, Center(start).Y + 10));
        var moved = move.Complete()!.PlotAreaLayout!;
        moved.LayoutTarget.Should().Be("inner");
        moved.Width.Should().BeApproximately(start.Width / W, 1e-5);

        var handle = ChartElementDragPlanner.HitTestHandle(map, element, new LayoutPoint(start.Right, start.Bottom));
        handle.Should().Be(ChartResizeHandle.BottomRight);
        var resize = ChartElementDragPlanner.Begin(chart, map, element, new LayoutPoint(start.Right, start.Bottom), handle)!;
        resize.Update(new LayoutPoint(start.Right - 50, start.Bottom - 40));
        resize.IsResize.Should().BeTrue();
        var resized = resize.Complete()!.PlotAreaLayout!;
        resized.X.Should().BeApproximately(start.Left / W, 1e-5);
        resized.Width.Should().BeApproximately((start.Width - 50) / W, 1e-5);
        resized.Height.Should().BeApproximately((start.Height - 40) / H, 1e-5);
    }

    [Fact]
    public void Plot_resize_never_collapses_below_the_minimum()
    {
        var (chart, _, map) = Build();
        var element = new ChartElementRef(ChartElementKind.PlotArea);
        var start = map.GetHighlightBounds(element)[0];

        var s = ChartElementDragPlanner.Begin(chart, map, element, new LayoutPoint(start.Right, start.Bottom), ChartResizeHandle.BottomRight)!;
        s.Update(new LayoutPoint(start.Left - 500, start.Top - 500));

        s.CurrentBounds.Width.Should().BeGreaterThanOrEqualTo(ChartElementDragPlanner.MinPlotSize);
        s.CurrentBounds.Height.Should().BeGreaterThanOrEqualTo(ChartElementDragPlanner.MinPlotSize);
    }

    [Fact]
    public void Dragging_a_data_label_stores_a_per_point_factor_offset()
    {
        var (chart, layout, map) = Build(configure: c => c.ShowDataLabels = true);
        var box = layout.DataLabels.First(l => l.SeriesIndex == 0 && l.PointIndex == 1);
        var element = ChartElementRef.ForDataLabels(0);

        var s = ChartElementDragPlanner.Begin(chart, map, element, box.Bounds.Center)!;
        s.Update(new LayoutPoint(box.Bounds.Center.X + 8, box.Bounds.Center.Y + 12));
        var options = s.Complete()!;

        var format = options.PointDataLabelFormats!.Single();
        format.SeriesIndex.Should().Be(0);
        format.PointIndex.Should().Be(1);
        format.Layout!.XMode.Should().Be("factor");
        format.Layout.X.Should().BeApproximately(8 / W, 1e-5);
        format.Layout.Y.Should().BeApproximately(12 / H, 1e-5);
    }

    [Fact]
    public void Data_label_drag_off_a_label_does_not_start()
    {
        var (chart, _, map) = Build(configure: c => c.ShowDataLabels = true);

        ChartElementDragPlanner.Begin(chart, map, ChartElementRef.ForDataLabels(0), new LayoutPoint(1, 1)).Should().BeNull();
    }

    [Fact]
    public void Manual_plot_and_legend_layouts_move_the_laid_out_geometry()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            c.LegendLayout = ChartManualLayoutGeometry.FromOrigin(new LayoutPoint(20, 250), W, H);
            c.PlotAreaLayout = ChartManualLayoutGeometry.FromPlotArea(new LayoutRect(60, 50, 280, 150), W, H);
        });
        var request = Request(chart, ["Q1", "Q2"], [Series(0, "North", 10, 20)], Inset).WithChartSize(W, H);

        var layout = ChartLayoutEngine.Layout(request);

        layout.PlotArea.X.Should().BeApproximately(60, 1e-4);
        layout.PlotArea.Width.Should().BeApproximately(280, 1e-4);
        layout.PlotArea.Height.Should().BeApproximately(150, 1e-4);
        layout.Legend.Bounds.Left.Should().BeApproximately(20, 1e-4);
        layout.Legend.Bounds.Bottom.Should().BeLessThanOrEqualTo(H + 1e-6);
        layout.Legend.Entries[0].SwatchRect.Left.Should().BeGreaterThan(layout.Legend.Bounds.Left - 1e-6);
    }

    [Fact]
    public void Manual_layouts_are_ignored_when_chart_size_is_unknown()
    {
        var chart = Chart(ChartType.Column, c =>
            c.PlotAreaLayout = ChartManualLayoutGeometry.FromPlotArea(new LayoutRect(60, 50, 280, 150), W, H));
        var request = Request(chart, ["Q1"], [Series(0, "North", 10)], Inset);

        ChartLayoutEngine.Layout(request).PlotArea.Should().Be(Inset.ToRect());
    }

    [Fact]
    public void Manual_data_label_offset_moves_the_label_box()
    {
        var chart = Chart(ChartType.Column, c => c.ShowDataLabels = true);
        var plain = ChartLayoutEngine.Layout(
            Request(chart, ["Q1", "Q2"], [Series(0, "North", 10, 20)], Inset).WithChartSize(W, H)).DataLabels[1];
        chart.PointDataLabelFormats =
        [
            new ChartPointDataLabelFormat(0, 1) { Layout = ChartManualLayoutGeometry.FromOffset(10, -5, W, H) },
        ];

        var moved = ChartLayoutEngine.Layout(
            Request(chart, ["Q1", "Q2"], [Series(0, "North", 10, 20)], Inset).WithChartSize(W, H)).DataLabels[1];

        moved.Bounds.Left.Should().BeApproximately(plain.Bounds.Left + 10, 1e-4);
        moved.Bounds.Top.Should().BeApproximately(plain.Bounds.Top - 5, 1e-3);
    }

    [Fact]
    public void Map_reports_manual_title_position()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.Title = "Sales";
            c.TitleLayout = ChartManualLayoutGeometry.FromOrigin(new LayoutPoint(300, 200), W, H);
        });
        var request = Request(chart, ["Q1"], [Series(0, "North", 10)], Inset).WithChartSize(W, H);
        var layout = ChartLayoutEngine.Layout(request);

        var map = ChartElementMapPlanner.Plan(chart, layout, W, H, request.TextMeasurer);

        map.HitTest(new LayoutPoint(305, 203)).Should().Be(new ChartElementRef(ChartElementKind.ChartTitle));
    }

    [Fact]
    public void Geometry_round_trips_edge_and_factor_layouts()
    {
        var edge = ChartManualLayoutGeometry.FromRect(new LayoutRect(40, 30, 100, 60), W, H);
        ChartManualLayoutGeometry.TryResolveRect(edge, W, H, new LayoutRect(0, 0, 1, 1), out var rect).Should().BeTrue();
        rect.Should().Be(new LayoutRect(40, 30, 100, 60));

        var factor = ChartManualLayoutGeometry.FromOffset(20, -10, W, H);
        ChartManualLayoutGeometry.TryResolveOrigin(factor, W, H, new LayoutPoint(100, 100), out var origin).Should().BeTrue();
        origin.X.Should().BeApproximately(120, 1e-2);
        origin.Y.Should().BeApproximately(90, 1e-2);

        ChartManualLayoutGeometry.TryResolveOrigin(null, W, H, new LayoutPoint(1, 1), out _).Should().BeFalse();
    }
}
