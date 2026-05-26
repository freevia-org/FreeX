using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public enum ObjectDragKind { None, Move, ResizeSE, ResizeE, ResizeS }

public static class GridObjectDragPlanner
{
    public static Rect CalculateDragRect(
        ObjectDragKind dragKind,
        Rect startRect,
        Point startPosition,
        Point currentPosition,
        double minimumSize = 8)
    {
        var dx = currentPosition.X - startPosition.X;
        var dy = currentPosition.Y - startPosition.Y;
        return dragKind switch
        {
            ObjectDragKind.Move => new Rect(
                startRect.X + dx,
                startRect.Y + dy,
                startRect.Width,
                startRect.Height),
            ObjectDragKind.ResizeSE => new Rect(
                startRect.X,
                startRect.Y,
                Math.Max(minimumSize, startRect.Width + dx),
                Math.Max(minimumSize, startRect.Height + dy)),
            ObjectDragKind.ResizeE => new Rect(
                startRect.X,
                startRect.Y,
                Math.Max(minimumSize, startRect.Width + dx),
                startRect.Height),
            ObjectDragKind.ResizeS => new Rect(
                startRect.X,
                startRect.Y,
                startRect.Width,
                Math.Max(minimumSize, startRect.Height + dy)),
            _ => startRect
        };
    }

    public static ObjectDragKind HitTestHandle(
        Point position,
        Rect objectRect,
        double handleSize = 8,
        double handleHitPadding = 4)
    {
        if (objectRect.IsEmpty)
            return ObjectDragKind.None;

        var pad = handleHitPadding + handleSize / 2;
        var nearRight = Math.Abs(position.X - objectRect.Right) < pad;
        var nearBottom = Math.Abs(position.Y - objectRect.Bottom) < pad;
        var inVertical = position.Y >= objectRect.Top - pad && position.Y <= objectRect.Bottom + pad;
        var inHorizontal = position.X >= objectRect.Left - pad && position.X <= objectRect.Right + pad;

        if (nearRight && nearBottom) return ObjectDragKind.ResizeSE;
        if (nearRight && inVertical) return ObjectDragKind.ResizeE;
        if (nearBottom && inHorizontal) return ObjectDragKind.ResizeS;
        if (objectRect.Contains(position)) return ObjectDragKind.Move;
        return ObjectDragKind.None;
    }

    public static CellAddress? HitTestAnchorCell(
        ViewportModel? viewport,
        Point position,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport is null)
            return null;

        foreach (var row in viewport.RowMetrics)
        {
            var top = row.TopOffset + columnHeaderHeight;
            if (position.Y < top || position.Y >= top + row.Height)
                continue;

            foreach (var column in viewport.ColMetrics)
            {
                var left = column.LeftOffset + rowHeaderWidth;
                if (position.X >= left && position.X < left + column.Width)
                    return new CellAddress(default, row.Row, column.Col);
            }
        }

        return null;
    }
}
