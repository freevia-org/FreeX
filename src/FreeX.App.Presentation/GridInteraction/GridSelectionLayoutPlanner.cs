using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// The pixel-space rectangle a selection range occupies given the currently visible row/column
/// metrics, plus which of its four edges fall inside the viewport. Edges that lie off-screen
/// (because the range extends past the visible metrics) are reported as absent so callers can avoid
/// drawing or hit-testing a border the renderer never painted.
/// </summary>
public readonly record struct GridSelectionLayout(
    GridRect Rect,
    bool HasTopEdge,
    bool HasLeftEdge,
    bool HasBottomEdge,
    bool HasRightEdge);

/// <summary>
/// Pure layout math mapping a selection range to its visible pixel rectangle. Shared by the desktop
/// hosts for selection marquee drawing and move-border hit-testing.
/// </summary>
public static class GridSelectionLayoutPlanner
{
    public static GridSelectionLayout? CalculateVisibleSelectionLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double metricScale = 1)
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

            var rowTop = row.TopOffset * metricScale;
            var rowBottom = (row.TopOffset + row.Height) * metricScale;
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

            var columnLeft = column.LeftOffset * metricScale;
            var columnRight = (column.LeftOffset + column.Width) * metricScale;
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

        if (!hasVisibleColumn)
            return null;

        top += columnHeaderHeight;
        bottom += columnHeaderHeight;
        left += rowHeaderWidth;
        right += rowHeaderWidth;

        if (right <= left || bottom <= top)
            return null;

        return new GridSelectionLayout(
            GridRect.FromEdges(left, top, right, bottom),
            hasTopEdge,
            hasLeftEdge,
            hasBottomEdge,
            hasRightEdge);
    }
}
