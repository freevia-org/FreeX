using System.Windows;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;
using PlannerCore = FreeX.App.Presentation.DrawingInteraction.ObjectDragPlanner;
using CoreDragKind = FreeX.App.Presentation.DrawingInteraction.ObjectDragKind;

namespace FreeX.App.UI;

public enum ObjectDragKind
{
    None,
    Move,
    ResizeNW,
    ResizeN,
    ResizeNE,
    ResizeE,
    ResizeSE,
    ResizeS,
    ResizeSW,
    ResizeW,
    Rotate
}

public readonly record struct ObjectDragTransform(
    Rect Rect,
    bool CrossedHorizontally,
    bool CrossedVertically);

/// <summary>
/// WPF host adapter over the portable <see cref="PlannerCore"/>: bridges System.Windows
/// <see cref="Rect"/>/<see cref="Point"/> to the shared <see cref="LayoutRect"/>/<see cref="LayoutPoint"/>
/// geometry and delegates all drag/resize/rotation/hit-test math to it, then adds the WPF-only
/// viewport anchor-cell hit-testing that depends on host grid metrics. The local
/// <see cref="ObjectDragKind"/> enum mirrors the portable one member-for-member.
/// </summary>
public static class GridObjectDragPlanner
{
    public const double MinimumObjectSize = PlannerCore.MinimumObjectSize;

    /// <summary>
    /// Vertical distance (in pixels) of the rotation grip's center above the top edge of the object.
    /// </summary>
    public const double RotationGripOffset = PlannerCore.RotationGripOffset;

    public static Rect CalculateDragRect(
        ObjectDragKind dragKind,
        Rect startRect,
        Point startPosition,
        Point currentPosition,
        double minimumSize = MinimumObjectSize) =>
        CalculateDragTransform(dragKind, startRect, startPosition, currentPosition, minimumSize).Rect;

    public static ObjectDragTransform CalculateDragTransform(
        ObjectDragKind dragKind,
        Rect startRect,
        Point startPosition,
        Point currentPosition,
        double minimumSize = MinimumObjectSize)
    {
        var transform = PlannerCore.CalculateDragTransform(
            ToCore(dragKind),
            ToLayoutRect(startRect),
            ToLayoutPoint(startPosition),
            ToLayoutPoint(currentPosition),
            minimumSize);
        return new ObjectDragTransform(
            ToWpfRect(transform.Rect),
            transform.CrossedHorizontally,
            transform.CrossedVertically);
    }

    /// <summary>
    /// Computes the rotation angle (in degrees, clockwise, 0 = pointer straight up) of the
    /// pointer relative to the object center. Returns 0 when the pointer is at the center.
    /// </summary>
    public static double CalculateRotationDegrees(Point center, Point pointer) =>
        PlannerCore.CalculateRotationDegrees(ToLayoutPoint(center), ToLayoutPoint(pointer));

    public static ObjectDragKind HitTestHandle(
        Point position,
        Rect objectRect,
        double handleSize = 8,
        double handleHitPadding = 4,
        double rotationDegrees = 0) =>
        ToWpf(PlannerCore.HitTestHandle(
            ToLayoutPoint(position),
            ToLayoutRect(objectRect),
            handleSize,
            handleHitPadding,
            rotationDegrees));

    public static Point RotateHandleCenter(
        ObjectDragKind handle,
        Rect objectRect,
        double rotationDegrees) =>
        ToWpfPoint(PlannerCore.RotateHandleCenter(ToCore(handle), ToLayoutRect(objectRect), rotationDegrees));

    public static Point RotatePointAroundCenter(Point point, Rect objectRect, double rotationDegrees) =>
        ToWpfPoint(PlannerCore.RotatePointAroundCenter(
            ToLayoutPoint(point), ToLayoutRect(objectRect), rotationDegrees));

    public static ObjectDragCommitPlan PlanCommit(
        ObjectDragKind dragKind,
        Rect startRect,
        Rect currentRect,
        CellAddress startAnchor,
        CellAddress? currentAnchor,
        double width,
        double height,
        double rotationDegrees,
        bool startFlipHorizontal,
        bool startFlipVertical,
        bool currentFlipHorizontal,
        bool currentFlipVertical) =>
        PlannerCore.PlanCommit(
            ToCore(dragKind),
            ToLayoutRect(startRect),
            ToLayoutRect(currentRect),
            startAnchor,
            currentAnchor,
            width,
            height,
            rotationDegrees,
            startFlipHorizontal,
            startFlipVertical,
            currentFlipHorizontal,
            currentFlipVertical);

    private static CoreDragKind ToCore(ObjectDragKind kind) => (CoreDragKind)(int)kind;

    private static ObjectDragKind ToWpf(CoreDragKind kind) => (ObjectDragKind)(int)kind;

    private static LayoutRect ToLayoutRect(Rect rect) =>
        rect.IsEmpty ? new LayoutRect(0, 0, -1, -1) : new LayoutRect(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToWpfRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static LayoutPoint ToLayoutPoint(Point point) => new(point.X, point.Y);

    private static Point ToWpfPoint(LayoutPoint point) => new(point.X, point.Y);

    public static CellAddress? HitTestAnchorCell(
        ViewportModel? viewport,
        Point position,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport is null)
            return null;

        if (position.X < rowHeaderWidth || position.Y < columnHeaderHeight)
            return null;

        if (viewport.SplitPanes is { } splitPanes)
        {
            var divider = CalculateSplitDividerLayout(viewport, rowHeaderWidth, columnHeaderHeight);
            var topRows = splitPanes.TopRows ?? [];
            var leftColumns = splitPanes.LeftColumns ?? [];
            var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
            var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
            var isTop = divider.HorizontalY.HasValue && position.Y < divider.HorizontalY.Value;
            var isLeft = divider.VerticalX.HasValue && position.X < divider.VerticalX.Value;

            var rows = (isTop, isLeft) switch
            {
                (true, _) => topRows,
                (false, true) => bottomLeftRows,
                _ => viewport.RowMetrics
            };
            var columns = (isTop, isLeft) switch
            {
                (_, true) => leftColumns,
                (true, false) => topRightColumns,
                _ => viewport.ColMetrics
            };
            var rowOrigin = !isTop && divider.HorizontalY.HasValue
                ? divider.HorizontalY.Value
                : columnHeaderHeight;
            var columnOrigin = !isLeft && divider.VerticalX.HasValue
                ? divider.VerticalX.Value
                : rowHeaderWidth;

            return HitTestMetrics(rows, columns, position, rowOrigin, columnOrigin);
        }

        return HitTestMetrics(viewport.RowMetrics, viewport.ColMetrics, position, columnHeaderHeight, rowHeaderWidth);
    }

    private static CellAddress? HitTestMetrics(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns,
        Point position,
        double rowOrigin,
        double columnOrigin)
    {
        foreach (var row in rows)
        {
            var top = row.TopOffset + rowOrigin;
            if (position.Y < top)
                break;

            if (position.Y >= top + row.Height)
                continue;

            foreach (var column in columns)
            {
                var left = column.LeftOffset + columnOrigin;
                if (position.X < left)
                    break;

                if (position.X < left + column.Width)
                    return new CellAddress(default, row.Row, column.Col);
            }
        }

        return null;
    }

    private static (double? HorizontalY, double? VerticalX) CalculateSplitDividerLayout(
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return (null, null);

        double? horizontalY = null;
        if (splitPanes.Row.HasValue)
        {
            var pinnedRows = splitPanes.TopRows ?? [];
            horizontalY = pinnedRows.Count > 0
                ? columnHeaderHeight + SumRowHeights(pinnedRows)
                : FindRowMetric(viewport.RowMetrics, splitPanes.Row.Value)?.TopOffset + columnHeaderHeight;
        }

        double? verticalX = null;
        if (splitPanes.Column.HasValue)
        {
            var pinnedColumns = splitPanes.LeftColumns ?? [];
            verticalX = pinnedColumns.Count > 0
                ? rowHeaderWidth + SumColumnWidths(pinnedColumns)
                : FindColMetric(viewport.ColMetrics, splitPanes.Column.Value)?.LeftOffset + rowHeaderWidth;
        }

        return (horizontalY, verticalX);
    }

    private static double SumRowHeights(IReadOnlyList<RowMetric> rows)
    {
        var height = 0d;
        foreach (var row in rows)
            height += row.Height;

        return height;
    }

    private static double SumColumnWidths(IReadOnlyList<ColMetric> columns)
    {
        var width = 0d;
        foreach (var column in columns)
            width += column.Width;

        return width;
    }

    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> metrics, uint row)
    {
        foreach (var metric in metrics)
        {
            if (metric.Row > row)
                break;

            if (metric.Row == row)
                return metric;
        }

        return null;
    }

    private static ColMetric? FindColMetric(IReadOnlyList<ColMetric> metrics, uint column)
    {
        foreach (var metric in metrics)
        {
            if (metric.Col > column)
                break;

            if (metric.Col == column)
                return metric;
        }

        return null;
    }
}
