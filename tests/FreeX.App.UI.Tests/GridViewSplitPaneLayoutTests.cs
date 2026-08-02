using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitDividerLayout_UsesPinnedPaneMetricsWhenMainViewportIsScrolledPastSplit()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)]));

        var layout = GridView.CalculateSplitDividerLayout(viewport);

        layout.HorizontalY.Should().Be(GridView.ColHeaderHeight + 58);
        layout.VerticalX.Should().Be(GridView.RowHeaderWidth + 208);
    }

    [Fact]
    public void CalculateSplitDividerLayout_ReusesRowHeaderWidthForVerticalSplit()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.SplitPanes.cs");
        var calculateSplitDividerLayout = source[
            source.IndexOf("public static SplitDividerLayout CalculateSplitDividerLayout", StringComparison.Ordinal)..
            source.IndexOf("private static RowMetric? FindRowMetric", StringComparison.Ordinal)];

        calculateSplitDividerLayout.Should().Contain("var rowHeaderWidth = CalculateRowHeaderWidth(viewport);");
        calculateSplitDividerLayout.Should().Contain("SplitPanePointerPlanner.CalculateDividerLayout");
        calculateSplitDividerLayout.Should().Contain("rowHeaderWidth,");
        calculateSplitDividerLayout.Should().NotContain("SumColumnWidths");
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_MapsPinnedCellsToPinnedQuadrants()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 1, "top-left"),
                    Cell(1, 10, "top"),
                    Cell(20, 1, "left")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height, layout.Region))
            .Should().Equal(
                (1u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18, SplitPaneRegion.TopLeft),
                (1u, 10u, GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 64, 18, SplitPaneRegion.TopRight),
                (20u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18, SplitPaneRegion.BottomLeft));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_MapsPinnedCellsOutsideGridView()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 1, "top-left"),
                    Cell(1, 10, "top"),
                    Cell(20, 1, "left")
                ]));

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height, layout.Region))
            .Should().Equal(
                (1u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18, SplitPaneRegion.TopLeft),
                (1u, 10u, GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 64, 18, SplitPaneRegion.TopRight),
                (20u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18, SplitPaneRegion.BottomLeft));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_VisitLayouts_MatchesCalculateLayouts()
    {
        var viewport = MeasuredSplitPaneViewport();
        var expected = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport);
        var consumer = new CollectingSplitPaneCellLayoutConsumer();

        SplitPaneCellLayoutPlanner.VisitLayouts(viewport, null, null, ref consumer);

        consumer.Layouts.Should().Equal(expected);
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_UsesIndependentTopRightAndBottomLeftMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144)],
                [
                    Cell(1, 12, "top-offset"),
                    Cell(30, 1, "left-offset")
                ],
                [new ColMetric(12, 64, 0), new ColMetric(13, 64, 64)],
                [new RowMetric(30, 18, 0), new RowMetric(31, 18, 18)]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height))
            .Should().Equal(
                (1u, 12u, GridView.RowHeaderWidth + 208, GridView.ColHeaderHeight, 64, 18),
                (30u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18));
    }
}
