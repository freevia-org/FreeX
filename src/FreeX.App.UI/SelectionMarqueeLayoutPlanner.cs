using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public static class SelectionMarqueeLayoutPlanner
{
    public readonly record struct SelectionMarqueeLayout(
        Rect Rect,
        bool HasTopEdge,
        bool HasLeftEdge,
        bool HasBottomEdge,
        bool HasRightEdge);

    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight)?.Rect;

    public static SelectionMarqueeLayout? CalculateVisibleSelectionLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateClipboardMarquee(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        CalculateVisibleRangeLayout(viewport, range, rowHeaderWidth, columnHeaderHeight)?.Rect;

    private static SelectionMarqueeLayout? CalculateVisibleRangeLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var hasVisibleRow = false;
        var hasTopEdge = false;
        var hasBottomEdge = false;
        var top = 0d;
        var bottom = 0d;
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row < range.Start.Row)
                continue;
            if (row.Row > range.End.Row)
                break;

            var rowTop = row.TopOffset;
            var rowBottom = row.TopOffset + row.Height;
            if (row.Row == range.Start.Row)
                hasTopEdge = true;
            if (row.Row == range.End.Row)
                hasBottomEdge = true;
            if (!hasVisibleRow)
            {
                top = rowTop;
                bottom = rowBottom;
                hasVisibleRow = true;
                continue;
            }

            if (rowTop < top)
                top = rowTop;
            if (rowBottom > bottom)
                bottom = rowBottom;
        }

        if (hasVisibleRow)
        {
            top += columnHeaderHeight;
            bottom += columnHeaderHeight;
        }

        // Window > Split pins fixed panes (SplitPanes.TopRows/LeftColumns/BottomLeftRows/TopRightColumns)
        // which live OUTSIDE viewport.RowMetrics/ColMetrics once the scrollable main pane has scrolled
        // past them. Scan those fixed lists too, else a selection anchored in a fixed pane loses its
        // outline the moment the main pane scrolls (the fixed cell is still drawn by RenderSplitPaneCells).
        if (viewport.SplitPanes is { } splitPanesForRows)
        {
            var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
            AccumulateSplitPaneRowRange(splitPanesForRows.TopRows ?? [], columnHeaderHeight, range,
                ref hasVisibleRow, ref hasTopEdge, ref hasBottomEdge, ref top, ref bottom);
            AccumulateSplitPaneRowRange(splitPanesForRows.BottomLeftRows ?? viewport.RowMetrics, dividerLayout.HorizontalY ?? columnHeaderHeight, range,
                ref hasVisibleRow, ref hasTopEdge, ref hasBottomEdge, ref top, ref bottom);
        }

        if (!hasVisibleRow)
            return null;

        var hasVisibleColumn = false;
        var hasLeftEdge = false;
        var hasRightEdge = false;
        var left = 0d;
        var right = 0d;
        foreach (var column in viewport.ColMetrics)
        {
            if (column.Col < range.Start.Col)
                continue;
            if (column.Col > range.End.Col)
                break;

            var columnLeft = column.LeftOffset;
            var columnRight = column.LeftOffset + column.Width;
            if (column.Col == range.Start.Col)
                hasLeftEdge = true;
            if (column.Col == range.End.Col)
                hasRightEdge = true;
            if (!hasVisibleColumn)
            {
                left = columnLeft;
                right = columnRight;
                hasVisibleColumn = true;
                continue;
            }

            if (columnLeft < left)
                left = columnLeft;
            if (columnRight > right)
                right = columnRight;
        }

        if (hasVisibleColumn)
        {
            left += rowHeaderWidth;
            right += rowHeaderWidth;
        }

        if (viewport.SplitPanes is { } splitPanesForColumns)
        {
            var dividerLayout = GridView.CalculateSplitDividerLayout(viewport);
            AccumulateSplitPaneColumnRange(splitPanesForColumns.LeftColumns ?? [], rowHeaderWidth, range,
                ref hasVisibleColumn, ref hasLeftEdge, ref hasRightEdge, ref left, ref right);
            AccumulateSplitPaneColumnRange(splitPanesForColumns.TopRightColumns ?? viewport.ColMetrics, dividerLayout.VerticalX ?? rowHeaderWidth, range,
                ref hasVisibleColumn, ref hasLeftEdge, ref hasRightEdge, ref left, ref right);
        }

        if (!hasVisibleColumn)
            return null;

        if (right <= left || bottom <= top)
            return null;

        return new SelectionMarqueeLayout(
            new Rect(new Point(left, top), new Point(right, bottom)),
            hasTopEdge,
            hasLeftEdge,
            hasBottomEdge,
            hasRightEdge);
    }

    // Scans a Window > Split fixed pane's own row list (SplitPanes.TopRows/BottomLeftRows), which is
    // NOT part of viewport.RowMetrics once the scrollable main pane has scrolled past it. Origins
    // differ per source (top pane vs. bottom-left pane), so each row's offset is baked in as it's
    // visited rather than uniformly afterward. Harmless to re-scan a row already covered by the main
    // viewport pass above (same cell, same computed offset) -- min/max accumulation is idempotent.
    private static void AccumulateSplitPaneRowRange(
        IReadOnlyList<RowMetric> rows,
        double origin,
        GridRange range,
        ref bool hasVisibleRow,
        ref bool hasTopEdge,
        ref bool hasBottomEdge,
        ref double top,
        ref double bottom)
    {
        foreach (var row in rows)
        {
            if (row.Row < range.Start.Row)
                continue;
            if (row.Row > range.End.Row)
                break;

            var rowTop = row.TopOffset + origin;
            var rowBottom = rowTop + row.Height;
            if (row.Row == range.Start.Row)
                hasTopEdge = true;
            if (row.Row == range.End.Row)
                hasBottomEdge = true;
            if (!hasVisibleRow)
            {
                top = rowTop;
                bottom = rowBottom;
                hasVisibleRow = true;
                continue;
            }

            if (rowTop < top)
                top = rowTop;
            if (rowBottom > bottom)
                bottom = rowBottom;
        }
    }

    // Column counterpart of AccumulateSplitPaneRowRange -- scans SplitPanes.LeftColumns/TopRightColumns.
    private static void AccumulateSplitPaneColumnRange(
        IReadOnlyList<ColMetric> columns,
        double origin,
        GridRange range,
        ref bool hasVisibleColumn,
        ref bool hasLeftEdge,
        ref bool hasRightEdge,
        ref double left,
        ref double right)
    {
        foreach (var column in columns)
        {
            if (column.Col < range.Start.Col)
                continue;
            if (column.Col > range.End.Col)
                break;

            var columnLeft = column.LeftOffset + origin;
            var columnRight = columnLeft + column.Width;
            if (column.Col == range.Start.Col)
                hasLeftEdge = true;
            if (column.Col == range.End.Col)
                hasRightEdge = true;
            if (!hasVisibleColumn)
            {
                left = columnLeft;
                right = columnRight;
                hasVisibleColumn = true;
                continue;
            }

            if (columnLeft < left)
                left = columnLeft;
            if (columnRight > right)
                right = columnRight;
        }
    }
}
