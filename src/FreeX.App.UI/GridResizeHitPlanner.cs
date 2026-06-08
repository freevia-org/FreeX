using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public enum GridResizeHitTarget
{
    None,
    Row,
    Column
}

public readonly record struct GridResizeHit(
    GridResizeHitTarget Target,
    uint Index,
    double CurrentSize,
    bool IsCollapsedBoundary = false);

public static class GridResizeHitPlanner
{
    public static GridResizeHit HitTest(
        ViewportModel? viewport,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double hitZone,
        IReadOnlyCollection<uint>? hiddenRows = null,
        IReadOnlyCollection<uint>? hiddenColumns = null)
    {
        if (viewport is null)
            return new GridResizeHit(GridResizeHitTarget.None, 0, 0);

        if (pointer.Y >= 0 && pointer.Y <= columnHeaderHeight)
        {
            var columns = viewport.ColMetrics;
            if (TryHitCollapsedColumnBoundary(columns, hiddenColumns, pointer.X, rowHeaderWidth, hitZone, out var collapsedColumnHit))
                return collapsedColumnHit;

            GridResizeHit? nearestColumnHit = null;
            var nearestColumnDistance = double.MaxValue;
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var rightEdge = column.LeftOffset + column.Width + rowHeaderWidth;
                if (rightEdge - pointer.X > hitZone)
                    break;

                var distance = Math.Abs(pointer.X - rightEdge);
                if (distance <= hitZone && distance < nearestColumnDistance)
                {
                    nearestColumnHit = new GridResizeHit(GridResizeHitTarget.Column, column.Col, column.Width);
                    nearestColumnDistance = distance;
                }
            }

            if (nearestColumnHit is { } hit)
                return hit;
        }

        if (pointer.X >= 0 && pointer.X <= rowHeaderWidth)
        {
            var rows = viewport.RowMetrics;
            if (TryHitCollapsedRowBoundary(rows, hiddenRows, pointer.Y, columnHeaderHeight, hitZone, out var collapsedRowHit))
                return collapsedRowHit;

            GridResizeHit? nearestRowHit = null;
            var nearestRowDistance = double.MaxValue;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var bottomEdge = row.TopOffset + row.Height + columnHeaderHeight;
                if (bottomEdge - pointer.Y > hitZone)
                    break;

                var distance = Math.Abs(pointer.Y - bottomEdge);
                if (distance <= hitZone && distance < nearestRowDistance)
                {
                    nearestRowHit = new GridResizeHit(GridResizeHitTarget.Row, row.Row, row.Height);
                    nearestRowDistance = distance;
                }
            }

            if (nearestRowHit is { } hit)
                return hit;
        }

        return new GridResizeHit(GridResizeHitTarget.None, 0, 0);
    }

    private static bool TryHitCollapsedColumnBoundary(
        IReadOnlyList<ColMetric> columns,
        IReadOnlyCollection<uint>? hiddenColumns,
        double pointerX,
        double rowHeaderWidth,
        double hitZone,
        out GridResizeHit hit)
    {
        hit = default;
        if (columns.Count == 0 || hiddenColumns is not { Count: > 0 })
            return false;

        var firstColumn = columns[0];
        if (TryFindHiddenColumnBefore(firstColumn.Col, hiddenColumns, out var hiddenBefore) &&
            IsNear(pointerX, firstColumn.LeftOffset + rowHeaderWidth, hitZone))
        {
            hit = new GridResizeHit(GridResizeHitTarget.Column, hiddenBefore, 0, IsCollapsedBoundary: true);
            return true;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var rightEdge = column.LeftOffset + column.Width + rowHeaderWidth;
            if (rightEdge - pointerX > hitZone)
                break;

            if (TryFindHiddenColumnAfter(column.Col, hiddenColumns, out var hiddenAfter) &&
                IsNear(pointerX, rightEdge, hitZone))
            {
                hit = new GridResizeHit(GridResizeHitTarget.Column, hiddenAfter, 0, IsCollapsedBoundary: true);
                return true;
            }
        }

        return false;
    }

    private static bool TryHitCollapsedRowBoundary(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyCollection<uint>? hiddenRows,
        double pointerY,
        double columnHeaderHeight,
        double hitZone,
        out GridResizeHit hit)
    {
        hit = default;
        if (rows.Count == 0 || hiddenRows is not { Count: > 0 })
            return false;

        var firstRow = rows[0];
        if (TryFindHiddenRowBefore(firstRow.Row, hiddenRows, out var hiddenBefore) &&
            IsNear(pointerY, firstRow.TopOffset + columnHeaderHeight, hitZone))
        {
            hit = new GridResizeHit(GridResizeHitTarget.Row, hiddenBefore, 0, IsCollapsedBoundary: true);
            return true;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var bottomEdge = row.TopOffset + row.Height + columnHeaderHeight;
            if (bottomEdge - pointerY > hitZone)
                break;

            if (TryFindHiddenRowAfter(row.Row, hiddenRows, out var hiddenAfter) &&
                IsNear(pointerY, bottomEdge, hitZone))
            {
                hit = new GridResizeHit(GridResizeHitTarget.Row, hiddenAfter, 0, IsCollapsedBoundary: true);
                return true;
            }
        }

        return false;
    }

    private static bool TryFindHiddenColumnBefore(
        uint visibleColumn,
        IReadOnlyCollection<uint> hiddenColumns,
        out uint hiddenColumn)
    {
        hiddenColumn = 0;
        if (visibleColumn <= 1)
            return false;

        var candidate = visibleColumn - 1;
        if (!hiddenColumns.Contains(candidate))
            return false;

        hiddenColumn = candidate;
        return true;
    }

    private static bool TryFindHiddenColumnAfter(
        uint visibleColumn,
        IReadOnlyCollection<uint> hiddenColumns,
        out uint hiddenColumn)
    {
        hiddenColumn = 0;
        if (visibleColumn >= CellAddress.MaxCol)
            return false;

        var candidate = visibleColumn + 1;
        if (!hiddenColumns.Contains(candidate))
            return false;

        hiddenColumn = candidate;
        return true;
    }

    private static bool TryFindHiddenRowBefore(
        uint visibleRow,
        IReadOnlyCollection<uint> hiddenRows,
        out uint hiddenRow)
    {
        hiddenRow = 0;
        if (visibleRow <= 1)
            return false;

        var candidate = visibleRow - 1;
        if (!hiddenRows.Contains(candidate))
            return false;

        hiddenRow = candidate;
        return true;
    }

    private static bool TryFindHiddenRowAfter(
        uint visibleRow,
        IReadOnlyCollection<uint> hiddenRows,
        out uint hiddenRow)
    {
        hiddenRow = 0;
        if (visibleRow >= CellAddress.MaxRow)
            return false;

        var candidate = visibleRow + 1;
        if (!hiddenRows.Contains(candidate))
            return false;

        hiddenRow = candidate;
        return true;
    }

    private static bool IsNear(double value, double edge, double hitZone) =>
        Math.Abs(value - edge) <= hitZone;
}
