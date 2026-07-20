using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Pure geometry and intent math for the fill-handle (autofill) drag interaction: where the handle
/// sits, which cell a drag targets, the fill range it produces, and the edge-scroll intent when the
/// pointer nears the grid border. Shared by the desktop hosts so both behave identically.
/// </summary>
public static class GridAutofillPlanner
{
    public static CellAddress ConstrainTarget(GridRange source, CellAddress target)
    {
        if (source.Contains(target))
        {
            if (source.RowCount >= 2 && source.ColCount == 1)
                return new CellAddress(target.Sheet, target.Row, source.End.Col);
            if (source.ColCount >= 2 && source.RowCount == 1)
                return new CellAddress(target.Sheet, source.End.Row, target.Col);
        }

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

    /// <summary>
    /// Computes the sub-range of <paramref name="source"/> that Excel clears when the fill
    /// handle is dragged inward (toward the source) instead of extending outward: the portion of
    /// the original selection strictly beyond the shrunk boundary implied by <paramref name="target"/>.
    /// Returns null when the drag does not shrink the range on exactly one axis (i.e. when
    /// <see cref="CalculateFillRange"/> would instead produce an outward extension, or the target
    /// sits back on the source's own edge with no movement at all).
    /// </summary>
    public static GridRange? CalculateClearRange(GridRange source, CellAddress target)
    {
        if (source.RowCount >= 2 && source.ColCount == 1 &&
            target.Row >= source.Start.Row && target.Row < source.End.Row)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, target.Row + 1, source.Start.Col),
                new CellAddress(source.Start.Sheet, source.End.Row, source.End.Col));
        }

        if (source.ColCount >= 2 && source.RowCount == 1 &&
            target.Col >= source.Start.Col && target.Col < source.End.Col)
        {
            return new GridRange(
                new CellAddress(source.Start.Sheet, source.Start.Row, target.Col + 1),
                new CellAddress(source.Start.Sheet, source.End.Row, source.End.Col));
        }

        return null;
    }

    /// <summary>
    /// Computes the range Excel fills when the fill handle is double-clicked: the selection
    /// extends straight down to match the populated data extent of the nearest non-blank adjacent
    /// column (checked to the left first, then the right, matching Excel), stopping at the first
    /// blank row. Works for any rectangular source selection -- a single seed cell, a single
    /// header/seed row, or a multi-row (and/or multi-column) block establishing a repeating
    /// pattern -- continuing the fill immediately below the source's last row, across the
    /// source's full column span. Returns null when there is no adjacent data to match (nothing
    /// to fill).
    /// </summary>
    public static GridRange? CalculateDoubleClickFillRange(GridRange source, uint? adjacentColumnLastPopulatedRow)
    {
        if (adjacentColumnLastPopulatedRow is not { } lastRow || lastRow <= source.End.Row)
            return null;

        return new GridRange(
            new CellAddress(source.Start.Sheet, source.End.Row + 1, source.Start.Col),
            new CellAddress(source.Start.Sheet, lastRow, source.End.Col));
    }

    public static GridRange CalculateCompletedSelectionRange(GridRange source, GridRange fillRange)
    {
        if (source.Contains(fillRange) && fillRange != source)
        {
            if (fillRange.Start.Row > source.Start.Row &&
                fillRange.Start.Col == source.Start.Col &&
                fillRange.End.Col == source.End.Col)
            {
                return new GridRange(
                    source.Start,
                    new CellAddress(source.End.Sheet, fillRange.Start.Row - 1, source.End.Col));
            }

            if (fillRange.Start.Col > source.Start.Col &&
                fillRange.Start.Row == source.Start.Row &&
                fillRange.End.Row == source.End.Row)
            {
                return new GridRange(
                    source.Start,
                    new CellAddress(source.End.Sheet, source.End.Row, fillRange.Start.Col - 1));
            }
        }

        return new GridRange(
            new CellAddress(
                source.Start.Sheet,
                Math.Min(source.Start.Row, fillRange.Start.Row),
                Math.Min(source.Start.Col, fillRange.Start.Col)),
            new CellAddress(
                source.Start.Sheet,
                Math.Max(source.End.Row, fillRange.End.Row),
                Math.Max(source.End.Col, fillRange.End.Col)));
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
        GridPoint pointer,
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
            if (metric.Row > bottomRow)
                break;

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
            if (metric.Col > rightColumn)
                break;

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
        GridPoint pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double handleSize = 6,
        double hitPadding = 3,
        double metricScale = 1)
    {
        if (viewport is null || !selectedRange.HasValue)
            return false;

        var range = selectedRange.Value;
        var layout = GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            metricScale);

        // A single-cell selection anchored in a fixed split-pane region falls out of
        // viewport.RowMetrics/ColMetrics once the scrollable main pane scrolls past it (those lists
        // only ever reflect the main pane's own scroll offset). The renderer already falls back to
        // the split-pane-fixed row/column lists for exactly this case (see
        // GridView.Rendering.Selection.cs's CalculateVisibleSingleCellSelectionLayout) so the handle
        // is still drawn there; mirror that fallback here so the drawn handle stays hit-testable.
        if (layout is null && range.Start.Row == range.End.Row && range.Start.Col == range.End.Col)
        {
            layout = CalculateSplitPaneSingleCellLayout(viewport, range.Start, rowHeaderWidth, columnHeaderHeight);
        }

        return IsOnHandle(layout, pointer, handleSize, hitPadding);
    }

    private static GridSelectionLayout? CalculateSplitPaneSingleCellLayout(
        ViewportModel viewport,
        CellAddress cell,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (!TryResolveSplitPaneRowMetric(viewport, cell.Row, columnHeaderHeight, out var row, out var rowOriginY) ||
            !TryResolveSplitPaneColumnMetric(viewport, cell.Col, rowHeaderWidth, out var column, out var colOriginX))
        {
            return null;
        }

        var left = column.LeftOffset + colOriginX;
        var top = row.TopOffset + rowOriginY;
        var right = left + column.Width;
        var bottom = top + row.Height;
        if (right <= left || bottom <= top)
            return null;

        return new GridSelectionLayout(
            GridRect.FromEdges(left, top, right, bottom),
            HasTopEdge: true,
            HasLeftEdge: true,
            HasBottomEdge: true,
            HasRightEdge: true);
    }

    // Mirrors GridView.Rendering.Selection.cs's TryResolveSplitPaneRowMetric: Window > Split keeps
    // its fixed-pane rows (SplitPanes.TopRows/BottomLeftRows) outside viewport.RowMetrics once the
    // scrollable main pane has scrolled past them.
    private static bool TryResolveSplitPaneRowMetric(
        ViewportModel viewport,
        uint row,
        double columnHeaderHeight,
        out RowMetric metric,
        out double originY)
    {
        if (viewport.SplitPanes is { } splitPanes)
        {
            if (FindRowMetric(splitPanes.TopRows ?? [], row) is { } topRow)
            {
                metric = topRow;
                originY = columnHeaderHeight;
                return true;
            }

            if (FindRowMetric(splitPanes.BottomLeftRows ?? viewport.RowMetrics, row) is { } bottomRow)
            {
                metric = bottomRow;
                originY = CalculateSplitDividerHorizontalY(viewport, columnHeaderHeight) ?? columnHeaderHeight;
                return true;
            }
        }

        metric = null!;
        originY = 0;
        return false;
    }

    // Mirrors GridView.Rendering.Selection.cs's TryResolveSplitPaneColumnMetric (see above).
    private static bool TryResolveSplitPaneColumnMetric(
        ViewportModel viewport,
        uint col,
        double rowHeaderWidth,
        out ColMetric metric,
        out double originX)
    {
        if (viewport.SplitPanes is { } splitPanes)
        {
            if (FindColMetric(splitPanes.LeftColumns ?? [], col) is { } leftColumn)
            {
                metric = leftColumn;
                originX = rowHeaderWidth;
                return true;
            }

            if (FindColMetric(splitPanes.TopRightColumns ?? viewport.ColMetrics, col) is { } rightColumn)
            {
                metric = rightColumn;
                originX = CalculateSplitDividerVerticalX(viewport, rowHeaderWidth) ?? rowHeaderWidth;
                return true;
            }
        }

        metric = null!;
        originX = 0;
        return false;
    }

    // Mirrors GridView.SplitPanes.cs's CalculateSplitDividerLayout horizontal-divider math.
    private static double? CalculateSplitDividerHorizontalY(ViewportModel viewport, double columnHeaderHeight)
    {
        if (viewport.SplitPanes is not { } splitPanes || splitPanes.Row is not { } splitRow)
            return null;

        var pinnedRows = splitPanes.TopRows ?? [];
        if (pinnedRows.Count > 0)
            return columnHeaderHeight + SumRowHeights(pinnedRows);

        return FindRowMetric(viewport.RowMetrics, splitRow)?.TopOffset + columnHeaderHeight;
    }

    // Mirrors GridView.SplitPanes.cs's CalculateSplitDividerLayout vertical-divider math.
    private static double? CalculateSplitDividerVerticalX(ViewportModel viewport, double rowHeaderWidth)
    {
        if (viewport.SplitPanes is not { } splitPanes || splitPanes.Column is not { } splitColumn)
            return null;

        var pinnedColumns = splitPanes.LeftColumns ?? [];
        if (pinnedColumns.Count > 0)
            return rowHeaderWidth + SumColumnWidths(pinnedColumns);

        return FindColMetric(viewport.ColMetrics, splitColumn)?.LeftOffset + rowHeaderWidth;
    }

    private static double SumRowHeights(IReadOnlyList<RowMetric> rows)
    {
        double height = 0;
        foreach (var row in rows)
            height += row.Height;

        return height;
    }

    private static double SumColumnWidths(IReadOnlyList<ColMetric> columns)
    {
        double width = 0;
        foreach (var column in columns)
            width += column.Width;

        return width;
    }

    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> metrics, uint row)
    {
        foreach (var metric in metrics)
        {
            if (metric.Row == row)
                return metric;
        }

        return null;
    }

    private static ColMetric? FindColMetric(IReadOnlyList<ColMetric> metrics, uint column)
    {
        foreach (var metric in metrics)
        {
            if (metric.Col == column)
                return metric;
        }

        return null;
    }

    public static bool IsOnHandle(
        GridSelectionLayout? selectionLayout,
        GridPoint pointer,
        double handleSize = 6,
        double hitPadding = 3)
    {
        if (selectionLayout is not { HasRightEdge: true, HasBottomEdge: true } layout)
            return false;

        var left = layout.Rect.Right - handleSize / 2;
        var top = layout.Rect.Bottom - handleSize / 2;
        return pointer.X >= left - hitPadding &&
            pointer.X <= left + handleSize + hitPadding &&
            pointer.Y >= top - hitPadding &&
            pointer.Y <= top + handleSize + hitPadding;
    }
}
