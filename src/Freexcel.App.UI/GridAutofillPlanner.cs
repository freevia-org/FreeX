using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class GridAutofillPlanner
{
    public static CellAddress ConstrainTarget(GridRange source, CellAddress target)
    {
        var verticalDistance = target.Row > source.End.Row ? target.Row - source.End.Row : 0;
        var horizontalDistance = target.Col > source.End.Col ? target.Col - source.End.Col : 0;

        return verticalDistance >= horizontalDistance
            ? new CellAddress(target.Sheet, target.Row, source.End.Col)
            : new CellAddress(target.Sheet, source.End.Row, target.Col);
    }

    public static GridRange? CalculateFillRange(GridRange source, CellAddress target)
    {
        if (target.Row > source.End.Row)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, source.End.Row + 1, source.Start.Col),
                new CellAddress(source.Start.Sheet, target.Row, source.End.Col));
        }

        if (target.Col > source.End.Col)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, source.Start.Row, source.End.Col + 1),
                new CellAddress(source.Start.Sheet, source.End.Row, target.Col));
        }

        return null;
    }

    public static GridAutoScrollRequest CalculateEdgeScrollIntent(
        double pointerX,
        double pointerY,
        double width,
        double height,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double edgeThreshold = 24)
    {
        if (width <= 0 || height <= 0)
            return new GridAutoScrollRequest(0, 0);

        var horizontal = pointerX >= width - edgeThreshold
            ? 1
            : pointerX <= rowHeaderWidth + edgeThreshold
                ? -1
                : 0;
        var vertical = pointerY >= height - edgeThreshold
            ? 1
            : pointerY <= columnHeaderHeight + edgeThreshold
                ? -1
                : 0;

        return new GridAutoScrollRequest(horizontal, vertical);
    }

    public static CellAddress? CalculateDragTarget(
        ViewportModel viewport,
        GridRange source,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var srcTopRow = viewport.RowMetrics.FirstOrDefault(r => r.Row == source.Start.Row);
        var srcBottomRow = viewport.RowMetrics.FirstOrDefault(r => r.Row == source.End.Row);
        var srcLeftCol = viewport.ColMetrics.FirstOrDefault(c => c.Col == source.Start.Col);
        var srcRightCol = viewport.ColMetrics.FirstOrDefault(c => c.Col == source.End.Col);

        if (srcTopRow is null || srcBottomRow is null || srcLeftCol is null || srcRightCol is null)
            return null;

        var srcTop = srcTopRow.TopOffset + columnHeaderHeight;
        var srcBottom = srcBottomRow.TopOffset + columnHeaderHeight + srcBottomRow.Height;
        var srcLeft = srcLeftCol.LeftOffset + rowHeaderWidth;
        var srcRight = srcRightCol.LeftOffset + rowHeaderWidth + srcRightCol.Width;

        var boundTop = Math.Min(srcTop, pointer.Y);
        var boundBottom = Math.Max(srcBottom, pointer.Y);
        var boundLeft = Math.Min(srcLeft, pointer.X);
        var boundRight = Math.Max(srcRight, pointer.X);

        CellAddress? target = null;
        foreach (var row in viewport.RowMetrics)
        {
            var midY = row.TopOffset + columnHeaderHeight + row.Height / 2;
            if (midY < boundTop || midY > boundBottom)
                continue;

            foreach (var column in viewport.ColMetrics)
            {
                var midX = column.LeftOffset + rowHeaderWidth + column.Width / 2;
                if (midX < boundLeft || midX > boundRight)
                    continue;

                target = new CellAddress(default, row.Row, column.Col);
            }
        }

        return target;
    }

    public static bool IsOnHandle(
        ViewportModel? viewport,
        GridRange? selectedRange,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double handleSize = 6,
        double hitPadding = 3)
    {
        if (viewport is null || !selectedRange.HasValue)
            return false;

        var range = selectedRange.Value;
        var endRow = viewport.RowMetrics.FirstOrDefault(r => r.Row == range.End.Row);
        var endColumn = viewport.ColMetrics.FirstOrDefault(c => c.Col == range.End.Col);
        if (endRow is null || endColumn is null)
            return false;

        var left = endColumn.LeftOffset + endColumn.Width + rowHeaderWidth - handleSize / 2;
        var top = endRow.TopOffset + endRow.Height + columnHeaderHeight - handleSize / 2;
        return pointer.X >= left - hitPadding &&
            pointer.X <= left + handleSize + hitPadding &&
            pointer.Y >= top - hitPadding &&
            pointer.Y <= top + handleSize + hitPadding;
    }
}
