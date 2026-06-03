using System;
using System.IO;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void ObjectSelectionHandles_DrawWithoutMaterializingRectArray()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));
        var drawHandles = source[
            source.IndexOf("internal void DrawObjectSelectionHandles", StringComparison.Ordinal)..
            source.IndexOf("private ObjectDragKind HitTestObjectHandle", StringComparison.Ordinal)];

        drawHandles.Should().Contain("DrawObjectSelectionHandle(dc,");
        drawHandles.Should().Contain("private static void DrawObjectSelectionHandle");
        drawHandles.Should().NotContain("GetHandleRects");
        drawHandles.Should().NotContain("Rect[]");
        drawHandles.Should().NotContain("new[]");
        drawHandles.Should().NotContain("foreach");
    }

    [Fact]
    public void GridObjectDragPlanner_CalculatesMoveResizeAndHandleTargets()
    {
        var start = new Rect(10, 20, 80, 40);

        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.Move,
                start,
                new Point(15, 25),
                new Point(35, 45))
            .Should()
            .Be(new Rect(30, 40, 80, 40));
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeSE,
                start,
                new Point(90, 60),
                new Point(100, 75))
            .Should()
            .Be(new Rect(10, 20, 90, 55));
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeE,
                start,
                new Point(90, 60),
                new Point(0, 60))
            .Width.Should().Be(8);
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeS,
                start,
                new Point(90, 60),
                new Point(90, 10))
            .Height.Should().Be(8);

        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeSE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Top + 10), start)
            .Should().Be(ObjectDragKind.ResizeE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 30, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeS);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 30, start.Top + 10), start)
            .Should().Be(ObjectDragKind.Move);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left - 20, start.Top - 20), start)
            .Should().Be(ObjectDragKind.None);
    }

    [Fact]
    public void GridObjectDragPlanner_ExposesSharedMinimumResizeSizeForMouseCommit()
    {
        var start = new Rect(10, 20, 80, 40);

        GridObjectDragPlanner.MinimumObjectSize.Should().Be(8);
        GridObjectDragPlanner.CalculateDragRect(
                ObjectDragKind.ResizeSE,
                start,
                new Point(start.Right, start.Bottom),
                new Point(start.Left - 100, start.Top - 100))
            .Should()
            .Be(new Rect(
                start.Left,
                start.Top,
                GridObjectDragPlanner.MinimumObjectSize,
                GridObjectDragPlanner.MinimumObjectSize));

        var inputSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Input.cs"));
        var mouseUpStart = inputSource.IndexOf("protected override void OnMouseLeftButtonUp", StringComparison.Ordinal);
        mouseUpStart.Should().BeGreaterThanOrEqualTo(0);
        var mouseUpObjectCommit = inputSource[
            inputSource.IndexOf("if (_objectDragKind != ObjectDragKind.None)", mouseUpStart, StringComparison.Ordinal)..
            inputSource.IndexOf("if (_marginDragEdge.HasValue)", mouseUpStart, StringComparison.Ordinal)];

        mouseUpObjectCommit.Should().Contain("GridObjectDragPlanner.MinimumObjectSize");
        mouseUpObjectCommit.Should().NotContain("Math.Max(8");
    }

    [Fact]
    public void GridObjectDragPlanner_HitTestsAllEightResizeHandlesAndRotation()
    {
        var start = new Rect(10, 20, 80, 40);

        GridObjectDragPlanner.HitTestHandle(new Point(start.Left, start.Top), start)
            .Should().Be(ObjectDragKind.ResizeNW);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + start.Width / 2, start.Top), start)
            .Should().Be(ObjectDragKind.ResizeN);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Top), start)
            .Should().Be(ObjectDragKind.ResizeNE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left, start.Top + start.Height / 2), start)
            .Should().Be(ObjectDragKind.ResizeW);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Top + start.Height / 2), start)
            .Should().Be(ObjectDragKind.ResizeE);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeSW);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + start.Width / 2, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeS);
        GridObjectDragPlanner.HitTestHandle(new Point(start.Right, start.Bottom), start)
            .Should().Be(ObjectDragKind.ResizeSE);

        // Rotation grip sits above the top-center handle.
        GridObjectDragPlanner.HitTestHandle(
                new Point(start.Left + start.Width / 2, start.Top - GridObjectDragPlanner.RotationGripOffset), start)
            .Should().Be(ObjectDragKind.Rotate);

        GridObjectDragPlanner.HitTestHandle(new Point(start.Left + 16, start.Top + 16), start)
            .Should().Be(ObjectDragKind.Move);
    }

    [Fact]
    public void GridObjectDragPlanner_IncludesResizeHandleHitZoneBoundary()
    {
        var start = new Rect(10, 20, 80, 40);
        const double handleSize = 8;
        const double hitPadding = 4;
        const double pad = handleSize / 2 + hitPadding;

        GridObjectDragPlanner.HitTestHandle(
                new Point(start.Right + pad, start.Bottom),
                start,
                handleSize,
                hitPadding)
            .Should().Be(ObjectDragKind.ResizeSE);
        GridObjectDragPlanner.HitTestHandle(
                new Point(start.Right, start.Bottom + pad),
                start,
                handleSize,
                hitPadding)
            .Should().Be(ObjectDragKind.ResizeSE);
    }

    [Fact]
    public void GridObjectDragPlanner_HitTestsAnchorCellFromViewportMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(2, 20, 0), new RowMetric(3, 20, 20)],
            [new ColMetric(4, 80, 0), new ColMetric(5, 80, 80)]);

        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(30 + 80 + 10, 18 + 20 + 10),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .Be(new CellAddress(default, 3, 5));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(4, 4),
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GridObjectDragPlanner_HitTestsAnchorCellFromSplitPaneQuadrants()
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
                [],
                [new ColMetric(12, 64, 0), new ColMetric(13, 64, 64)],
                [new RowMetric(30, 18, 0), new RowMetric(31, 18, 18)]));

        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 1, 1));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 1, 12));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 5, GridView.ColHeaderHeight + 58 + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 30, 1));
        GridObjectDragPlanner.HitTestAnchorCell(
                viewport,
                new Point(GridView.RowHeaderWidth + 208 + 5, GridView.ColHeaderHeight + 58 + 5),
                GridView.RowHeaderWidth,
                GridView.ColHeaderHeight)
            .Should()
            .Be(new CellAddress(default, 20, 10));
    }

    [Fact]
    public void GridObjectDragPlanner_StopsAnchorHitScansOnceSortedMetricsPassPointer()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridObjectDragPlanner.cs"));
        var anchorHitTest = source[
            source.IndexOf("public static CellAddress? HitTestAnchorCell", StringComparison.Ordinal)..];

        anchorHitTest.Should().Contain("foreach (var row in rows)");
        anchorHitTest.Should().Contain("foreach (var column in columns)");
        anchorHitTest.Should().Contain("if (position.Y < top)");
        anchorHitTest.Should().Contain("break;");
        anchorHitTest.Should().Contain("if (position.X < left)");
        anchorHitTest.Should().Contain("SumRowHeights(pinnedRows)");
        anchorHitTest.Should().Contain("SumColumnWidths(pinnedColumns)");
        anchorHitTest.Should().Contain("if (metric.Row > row)");
        anchorHitTest.Should().Contain("if (metric.Col > column)");
        anchorHitTest.Should().Contain("return new CellAddress(default, row.Row, column.Col);");
        anchorHitTest.Should().NotContain(".Sum(");
    }

    [Fact]
    public void GridViewObjectDrag_DelegatesGeometryToPlanner()
    {
        var inputSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Input.cs"));
        var dragSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));

        inputSource.Should().Contain("GridObjectDragPlanner.CalculateDragRect(");
        inputSource.Should().Contain("_objectDragStartAnchor = GetSelectedObjectAnchor() ?? HitTestAnchorCell(pos) ?? default;");
        dragSource.Should().Contain("GridObjectDragPlanner.HitTestHandle(pos, objRect, HandleSize, HandleHitPad)");
        dragSource.Should().Contain("GridObjectDragPlanner.HitTestAnchorCell(");
    }
}
