using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public static class GridAutofillPlanner
{
    public static CellAddress ConstrainTarget(GridRange source, CellAddress target)
    {
        var upwardDistance = target.Row < source.Start.Row ? source.Start.Row - target.Row : 0;
        var downwardDistance = target.Row > source.End.Row ? target.Row - source.End.Row : 0;
        var leftwardDistance = target.Col < source.Start.Col ? source.Start.Col - target.Col : 0;
        var rightwardDistance = target.Col > source.End.Col ? target.Col - source.End.Col : 0;
        var verticalDistance = Math.Max(upwardDistance, downwardDistance);
        var horizontalDistance = Math.Max(leftwardDistance, rightwardDistance);

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

        if (target.Row < source.Start.Row)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, target.Row, source.Start.Col),
                new CellAddress(source.Start.Sheet, source.Start.Row - 1, source.End.Col));
        }

        if (target.Col > source.End.Col)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, source.Start.Row, source.End.Col + 1),
                new CellAddress(source.Start.Sheet, source.End.Row, target.Col));
        }

        if (target.Col < source.Start.Col)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, source.Start.Row, target.Col),
                new CellAddress(source.Start.Sheet, source.End.Row, source.Start.Col - 1));
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

        var horizontal = CalculateAxisEdgeDirection(pointerX, rowHeaderWidth, width, edgeThreshold);
        var vertical = CalculateAxisEdgeDirection(pointerY, columnHeaderHeight, height, edgeThreshold);

        return new GridAutoScrollRequest(horizontal, vertical);
    }

    private static int CalculateAxisEdgeDirection(
        double pointer,
        double contentStart,
        double contentEnd,
        double edgeThreshold)
    {
        var contentSpan = contentEnd - contentStart;
        if (contentSpan <= 0)
            return 0;

        var threshold = Math.Min(Math.Max(0, edgeThreshold), contentSpan / 2);
        var distanceFromStart = pointer - contentStart;
        var distanceFromEnd = contentEnd - pointer;

        if (distanceFromStart <= threshold && distanceFromStart <= distanceFromEnd)
            return -1;
        if (distanceFromEnd <= threshold)
            return 1;
        return 0;
    }

    public static CellAddress? CalculateDragTarget(
        ViewportModel viewport,
        GridRange source,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (!TryFindRowEndpoints(viewport.RowMetrics, source.Start.Row, source.End.Row, out var srcTopRow, out var srcBottomRow) ||
            !TryFindColumnEndpoints(viewport.ColMetrics, source.Start.Col, source.End.Col, out var srcLeftCol, out var srcRightCol))
            return null;

        var srcTop = srcTopRow.TopOffset + columnHeaderHeight;
        var srcBottom = srcBottomRow.TopOffset + columnHeaderHeight + srcBottomRow.Height;
        var srcLeft = srcLeftCol.LeftOffset + rowHeaderWidth;
        var srcRight = srcRightCol.LeftOffset + rowHeaderWidth + srcRightCol.Width;

        var boundTop = Math.Min(srcTop, pointer.Y);
        var boundBottom = Math.Max(srcBottom, pointer.Y);
        var boundLeft = Math.Min(srcLeft, pointer.X);
        var boundRight = Math.Max(srcRight, pointer.X);

        uint? targetRow = null;
        uint? targetColumn = null;
        var preferTopRow = pointer.Y < srcTop;
        var preferLeftColumn = pointer.X < srcLeft;

        foreach (var row in viewport.RowMetrics)
        {
            var midY = row.TopOffset + columnHeaderHeight + row.Height / 2;
            if (midY < boundTop)
                continue;
            if (midY > boundBottom)
                break;

            targetRow ??= row.Row;
            if (!preferTopRow)
                targetRow = row.Row;
        }

        foreach (var column in viewport.ColMetrics)
        {
            var midX = column.LeftOffset + rowHeaderWidth + column.Width / 2;
            if (midX < boundLeft)
                continue;
            if (midX > boundRight)
                break;

            targetColumn ??= column.Col;
            if (!preferLeftColumn)
                targetColumn = column.Col;
        }

        return targetRow.HasValue && targetColumn.HasValue
            ? new CellAddress(default, targetRow.Value, targetColumn.Value)
            : null;
    }

    private static bool TryFindRowEndpoints(
        IReadOnlyList<RowMetric> metrics,
        uint topRow,
        uint bottomRow,
        out RowMetric topMetric,
        out RowMetric bottomMetric)
    {
        RowMetric? foundTop = null;
        RowMetric? foundBottom = null;

        foreach (var metric in metrics)
        {
            if (foundTop is null && metric.Row == topRow)
                foundTop = metric;

            if (foundBottom is null && metric.Row == bottomRow)
                foundBottom = metric;

            if (foundTop is not null && foundBottom is not null)
            {
                topMetric = foundTop;
                bottomMetric = foundBottom;
                return true;
            }
        }

        topMetric = null!;
        bottomMetric = null!;
        return false;
    }

    private static bool TryFindColumnEndpoints(
        IReadOnlyList<ColMetric> metrics,
        uint leftColumn,
        uint rightColumn,
        out ColMetric leftMetric,
        out ColMetric rightMetric)
    {
        ColMetric? foundLeft = null;
        ColMetric? foundRight = null;

        foreach (var metric in metrics)
        {
            if (foundLeft is null && metric.Col == leftColumn)
                foundLeft = metric;

            if (foundRight is null && metric.Col == rightColumn)
                foundRight = metric;

            if (foundLeft is not null && foundRight is not null)
            {
                leftMetric = foundLeft;
                rightMetric = foundRight;
                return true;
            }
        }

        leftMetric = null!;
        rightMetric = null!;
        return false;
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
        RowMetric? endRow = null;
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row == range.End.Row)
                endRow = row;
        }

        ColMetric? endColumn = null;
        foreach (var column in viewport.ColMetrics)
        {
            if (column.Col == range.End.Col)
                endColumn = column;
        }

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
