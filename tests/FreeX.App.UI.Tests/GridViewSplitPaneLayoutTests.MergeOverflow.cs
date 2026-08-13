using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewSplitPaneLayoutTests
{
    [Fact]
    public void CalculateSplitPaneCellLayouts_ExpandsMergedAnchorWithinSplitPaneMetrics()
    {
        var sheetId = SheetId.New();
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
                    Cell(1, 1, "merged"),
                    Cell(1, 2, "covered"),
                    Cell(20, 1, "left")
                ]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 2))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect.X, layout.Rect.Y, layout.Rect.Width, layout.Rect.Height))
            .Should().Equal(
                (1u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight, 144, 18),
                (20u, 1u, GridView.RowHeaderWidth, GridView.ColHeaderHeight + 58, 64, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_SuppressesCoveredMergeCellWhenAnchorIsOutsideVisiblePaneMetrics()
    {
        var sheetId = SheetId.New();
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
                    Cell(20, 1, "covered"),
                    Cell(21, 1, "visible")
                ]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 19, 1),
                new CellAddress(sheetId, 20, 1))
        };

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, mergedRegions);

        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Cell.DisplayText))
            .Should().Equal((21u, 1u, "visible"));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_DelegatesMergePlanningToPortableOwner()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("SplitPaneCellLayoutPlanner.cs");

        source.Should().Contain("ViewportGeometryPlanner.CalculateSplitPaneLayouts(");
        source.Should().Contain("ViewportGeometryPlanner.VisitSplitPaneLayouts(");
        source.Should().NotContain("private static void EmitMergeLayouts");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_ContainsOnlyNativeGeometryConversion()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("SplitPaneCellLayoutPlanner.cs");

        source.Should().Contain("private static Rect ToWpf(LayoutRect rect)");
        source.Should().Contain("WpfViewportCellLayoutConsumer");
        source.Should().NotContain("MergeRangeIndex");
        source.Should().NotContain("SplitPaneOccupiedCellMap");
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_AllowsTextOverflowAcrossEmptyCellsWithinSamePane()
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
                    Cell(1, 1, "overflow"),
                    Cell(1, 3, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 144, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_AllowsTopRightTextOverflowWithinIndependentColumns()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                [
                    Cell(1, 12, "top-right overflow"),
                    Cell(1, 14, "stop")
                ],
                [
                    new ColMetric(12, 50, 0),
                    new ColMetric(13, 70, 50),
                    new ColMetric(14, 90, 120)
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 12).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth + 144, GridView.ColHeaderHeight, 120, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_NormalizesOutOfOrderOverflowOccupiedCells()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0)],
            [new ColMetric(10, 64, 0)],
            SplitPanes: new SplitPaneState(
                2,
                2,
                [new RowMetric(1, 18, 0)],
                [
                    new ColMetric(1, 40, 0),
                    new ColMetric(2, 40, 40),
                    new ColMetric(3, 40, 80),
                    new ColMetric(4, 40, 120)
                ],
                [
                    Cell(1, 4, "later"),
                    Cell(1, 1, "overflow"),
                    Cell(1, 3, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 80, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_TreatsEditingCellAsOverflowOccupied()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0)],
                [
                    new ColMetric(1, 64, 0),
                    new ColMetric(2, 80, 64),
                    new ColMetric(3, 64, 144)
                ],
                [
                    Cell(1, 1, "overflow"),
                    new DisplayCell(1, 2, BlankValue.Instance, "", null, StyleId.Default, null),
                    Cell(1, 3, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(
            viewport,
            editingCell: new CellAddress(sheetId, 1, 2));

        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_DoesNotOverflowShrinkToFitTextAcrossEmptyCells()
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
                    Cell(1, 1, "shrink text", new CellStyle { ShrinkToFit = true }),
                    Cell(1, 3, "stop")
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 64, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_RightAlignedTextOverflowsLeftwardAcrossEmptyCellsAndStopsAtOccupiedCell()
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
                    Cell(1, 1, "stop"),
                    Cell(1, 3, "overflow", new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Right })
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 3).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth + 64, GridView.ColHeaderHeight, 144, 18));
    }

    [Fact]
    public void CalculateSplitPaneCellLayouts_CenterAlignedTextOverflowsBothDirectionsAcrossEmptyCells()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
            [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18), new RowMetric(3, 18, 40), new RowMetric(4, 18, 58)],
                [
                    new ColMetric(1, 64, 0),
                    new ColMetric(2, 80, 64),
                    new ColMetric(3, 64, 144),
                    new ColMetric(4, 80, 208)
                ],
                [
                    Cell(1, 3, "overflow", new CellStyle { HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Center })
                ]));

        var layouts = GridView.CalculateSplitPaneCellLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 3).TextClipRect
            .Should().Be(new Rect(GridView.RowHeaderWidth, GridView.ColHeaderHeight, 288, 18));
    }

    [Fact]
    public void RenderSplitPaneCells_DrawsCommentIndicatorsForCommentOnlyPaneCells()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var renderSplitPaneCells = source[
            source.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)..
            source.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("if (cell.HasComment)");
        renderSplitPaneCells.Should().Contain("DrawCommentIndicator(dc, rect,");
        renderSplitPaneCells.IndexOf("DrawCommentIndicator(dc, rect,", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("ShouldDrawCellContent(cell, EditingCell)", StringComparison.Ordinal));
    }
}
