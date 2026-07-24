using System.Windows;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R83-app-view-modes-5-3: <c>GridView.HitTestPageBreakLine</c> (GridView.HitTesting.cs) computed
/// proximity to a manual row/column page-break line in Page Break Preview view, but nothing in
/// GridView.Input.cs ever called it -- there was no mouse-down/move/up wiring to begin a drag,
/// preview it, or commit a moved/removed break, unlike the fully-wired split-pane divider drag
/// (HitTestSplitDividerHandle / CalculateSplitDividerDragTarget / OnSplitDividerMoved). Dragging a
/// break line in Page Break Preview was a silent no-op. Fixed by wiring
/// HitTestPageBreakLine/CalculatePageBreakLineDragTarget into OnMouseLeftButtonDown/OnMouseMove/
/// OnMouseLeftButtonUp (capturing the mouse, snapping the drop position to the nearest row/column
/// line, and raising the new PageBreakLineMoved event so the host can update
/// Sheet.RowPageBreaks/ColumnPageBreaks -- removing the break entirely when dropped off the grid).
/// </summary>
public sealed class PageBreakLineDragTests
{
    // Primary (source-text) case: before the fix, GridView.Input.cs never referenced
    // HitTestPageBreakLine/PageBreakLineMoved at all, so a click-drag over a break line fell through
    // to the ordinary selection/click handling instead of starting a drag. This fails against the
    // pre-fix source and passes once the mouse-down/mouse-up wiring exists.
    [Fact]
    public void PageBreakLineDrag_IsWiredIntoMouseDownAndMouseUp()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");

        source.Should().Contain(
            "if (HitTestPageBreakLine(pos) is { } pageBreakLineHit)",
            "mouse-down must hit-test for a page-break line the same way it does for margin guides");
        source.Should().Contain(
            "_pageBreakLineDragHit = pageBreakLineHit;",
            "mouse-down must capture which break line is being dragged");

        source.Should().Contain(
            "if (_pageBreakLineDragHit.HasValue)",
            "mouse-move/mouse-up must branch on an in-progress page-break-line drag");
        source.Should().Contain(
            "PageBreakLineMoved?.Invoke(hit.Orientation, hit.Index, newIndex);",
            "mouse-up must commit the drag by raising PageBreakLineMoved with the drop target");

        // The drag must participate in the same capture bookkeeping as every other grid-line drag
        // (split divider, margin guide) so a released-outside-the-control drag cancels cleanly.
        source.Should().Contain("_pageBreakLineDragHit.HasValue ||",
            "HasActiveCapturedGridDrag must include the page-break-line drag");
    }

    // Sibling no-regression case: adding the new branches must not disturb the pre-existing,
    // already-wired split-divider drag machinery that sits right next to it in the same methods.
    [Fact]
    public void SplitDividerDrag_WiringIsUnaffectedByPageBreakLineDragAddition()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("GridView.Input.cs");

        source.Should().Contain(
            "if (Viewport is not null && HitTestSplitDividerHandle(Viewport, pos, GetLogicalViewportWidth(), GetLogicalViewportHeight()) is { } splitHandle &&");
        source.Should().Contain(
            "SplitDividerMoved?.Invoke(target.Row, target.Column);");
        source.Should().Contain(
            "_splitDividerDragHandle != SplitDividerHandle.None ||");
    }

    // Primary (functional) case: CalculatePageBreakLineDragTarget is brand new -- it did not exist
    // before this fix, so this test could not even compile pre-fix. It snaps a drop position to the
    // nearest row/column gridline, mirroring CalculateSplitDividerDragTarget's FindSplitRow/Column
    // nearest-boundary approach.
    [Fact]
    public void CalculatePageBreakLineDragTarget_SnapsToNearestRowAndColumnLine()
    {
        var viewport = ThreeRowColumnViewport();

        // Row metrics start at y = ColHeaderHeight (0), 20 (row 2), 45 (row 3); dropping near the
        // row-2 boundary (y=20) must snap to row 2.
        GridView.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakLineOrientation.Row,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 20 + 3),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight,
                logicalWidth: 400,
                logicalHeight: 300)
            .Should().Be(2u);

        // Column metrics start at x = RowHeaderWidth (0), 60 (col 2), 130 (col 3); dropping near the
        // col-3 boundary (x=130) must snap to col 3.
        GridView.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakLineOrientation.Column,
                new Point(GridView.RowHeaderWidth + 130 + 2, GridView.ColHeaderHeight + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight,
                logicalWidth: 400,
                logicalHeight: 300)
            .Should().Be(3u);
    }

    // Sibling no-regression case: dropping outside the grid/print-area bounds must return null (so
    // the caller removes the break), the same bounds check HitTestPageBreakLine already applies --
    // this must keep working once the drag-target calculation is added.
    [Fact]
    public void CalculatePageBreakLineDragTarget_ReturnsNullWhenDroppedOutsideGrid()
    {
        var viewport = ThreeRowColumnViewport();

        GridView.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakLineOrientation.Row,
                new Point(GridView.RowHeaderWidth - 5, GridView.ColHeaderHeight + 20),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight,
                logicalWidth: 400,
                logicalHeight: 300)
            .Should().BeNull("a pointer to the left of the row-header column is outside the grid");

        GridView.CalculatePageBreakLineDragTarget(
                viewport,
                PageBreakLineOrientation.Column,
                new Point(GridView.RowHeaderWidth + 50, GridView.ColHeaderHeight + 500),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight,
                logicalWidth: 400,
                logicalHeight: 300)
            .Should().BeNull("a pointer below the logical viewport height is outside the grid");
    }

    private static ViewportModel ThreeRowColumnViewport() =>
        new(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 25, 20), new RowMetric(3, 20, 45)],
            [new ColMetric(1, 60, 0), new ColMetric(2, 70, 60), new ColMetric(3, 60, 130)]);
}
