using Free.Shared.Drawing;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Presentation.Rendering;

public enum ViewportMetricPlacement
{
    MetricOffsets,
    Sequential,
}

public enum ViewportHitTestEdgeBehavior
{
    ExclusiveEnd,
    InclusiveEnd,
}

public enum ViewportOverflowTraversal
{
    LogicalColumns,
    VisibleMetrics,
}

public enum ViewportFrozenQuadrant
{
    Scrollable,
    FrozenRows,
    FrozenColumns,
    FrozenRowsAndColumns,
}

public readonly record struct ViewportGeometrySettings(
    double RowHeaderWidth,
    double ColumnHeaderHeight,
    double Scale = 1,
    double MinimumColumnWidth = 0,
    double MinimumRowHeight = 0,
    ViewportMetricPlacement MetricPlacement = ViewportMetricPlacement.MetricOffsets,
    ViewportHitTestEdgeBehavior HitTestEdges = ViewportHitTestEdgeBehavior.ExclusiveEnd,
    double? SplitColumnHeaderHeight = null,
    double? SplitRowHeaderWidth = null)
{
    public double EffectiveScale => Scale > 0 ? Scale : 1;

    public double EffectiveSplitColumnHeaderHeight => SplitColumnHeaderHeight ?? ColumnHeaderHeight;

    public double EffectiveSplitRowHeaderWidth => SplitRowHeaderWidth ?? RowHeaderWidth;

    public double GetColumnWidth(ColMetric metric) =>
        Math.Max(MinimumColumnWidth, metric.Width) * EffectiveScale;

    public double GetRowHeight(RowMetric metric) =>
        Math.Max(MinimumRowHeight, metric.Height) * EffectiveScale;
}

public readonly record struct ViewportCellLayout(
    DisplayCell Cell,
    LayoutRect Bounds,
    LayoutRect TextClipBounds,
    SplitPanePointerRegion Region);

public interface IViewportCellLayoutConsumer
{
    void AcceptLayout(ViewportCellLayout layout);
}

public readonly record struct ViewportMergeSpan(int RowSpan, int ColumnSpan, double Height, double Width);

public readonly record struct ViewportOverflowAvailability(double LeftWidth, double RightWidth);

public readonly record struct ViewportCellEdgeVisibility(bool Top, bool Bottom, bool Left, bool Right);

/// <summary>
/// Portable worksheet viewport geometry. Renderers retain native rectangles, clipping, measurement,
/// and visual-tree operations; this planner owns the worksheet-to-layout projection they share.
/// </summary>
public static class ViewportGeometryPlanner
{
    public static IReadOnlyList<ViewportCellLayout> CalculateSplitPaneLayouts(
        ViewportModel viewport,
        ViewportGeometrySettings settings,
        IReadOnlyList<GridRange>? mergedRegions = null,
        CellAddress? editingCell = null)
    {
        var cells = viewport.SplitPanes?.Cells ?? [];
        if (cells.Count == 0)
            return [];

        var consumer = new ViewportCellLayoutCollector(cells.Count);
        VisitSplitPaneLayouts(viewport, settings, mergedRegions, editingCell, ref consumer);
        return consumer.Layouts;
    }

    public static void VisitSplitPaneLayouts<TConsumer>(
        ViewportModel viewport,
        ViewportGeometrySettings settings,
        IReadOnlyList<GridRange>? mergedRegions,
        CellAddress? editingCell,
        ref TConsumer consumer)
        where TConsumer : struct, IViewportCellLayoutConsumer
    {
        if (viewport.SplitPanes is not { } splitPanes)
            return;

        var cells = splitPanes.Cells ?? [];
        if (cells.Count == 0)
            return;

        var topRows = splitPanes.TopRows ?? [];
        var leftColumns = splitPanes.LeftColumns ?? [];
        var topRightColumns = splitPanes.TopRightColumns ?? viewport.ColMetrics;
        var bottomLeftRows = splitPanes.BottomLeftRows ?? viewport.RowMetrics;
        var divider = SplitPanePointerPlanner.CalculateDividerLayout(
            viewport,
            settings.EffectiveSplitRowHeaderWidth,
            settings.EffectiveSplitColumnHeaderHeight,
            settings.EffectiveScale);
        var horizontalY = divider.HorizontalY ?? settings.ColumnHeaderHeight;
        var verticalX = divider.VerticalX ?? settings.RowHeaderWidth;
        HashSet<(uint Row, uint Col)>? occupied = null;

        foreach (var cell in cells)
        {
            var merge = FindMerge(mergedRegions, cell.Row, cell.Col);
            if (merge is { } coveredMerge &&
                (cell.Row != coveredMerge.Start.Row || cell.Col != coveredMerge.Start.Col))
            {
                continue;
            }

            var isTopPane = TryFindRow(topRows, cell.Row, out var topRow);
            var isLeftPane = TryFindColumn(leftColumns, cell.Col, out var leftColumn);
            if (merge is { } cellMerge)
            {
                EmitMergeLayouts(
                    cell,
                    cellMerge,
                    isTopPane,
                    isLeftPane,
                    topRows,
                    bottomLeftRows,
                    leftColumns,
                    topRightColumns,
                    settings,
                    horizontalY,
                    verticalX,
                    ref consumer);
                continue;
            }

            var rows = isTopPane ? topRows : bottomLeftRows;
            var columns = isLeftPane ? leftColumns : topRightColumns;
            var row = isTopPane
                ? topRow
                : TryFindRow(bottomLeftRows, cell.Row, out var bottomRow) ? bottomRow : null;
            var column = isLeftPane
                ? leftColumn
                : TryFindColumn(topRightColumns, cell.Col, out var rightColumn) ? rightColumn : null;
            if (row is null || column is null)
                continue;

            var rowOrigin = isTopPane ? settings.ColumnHeaderHeight : horizontalY;
            var columnOrigin = isLeftPane ? settings.RowHeaderWidth : verticalX;
            var bounds = CreateOffsetBounds(row, column, rowOrigin, columnOrigin, settings);
            var textClip = bounds;
            if (CellTextOverflowPlanner.CanOverflowCellText(
                    cell.Style,
                    cell.RawValue,
                    cell.DisplayText,
                    merge))
            {
                var occupiedCells = occupied ??= BuildOccupiedCellSet(cells, editingCell);
                var columnIndex = GetColumnIndex(columns, cell.Col);
                var availability = CalculateOverflowAvailability(
                    cell.Row,
                    cell.Col,
                    columnIndex,
                    columns,
                    0,
                    settings,
                    ViewportOverflowTraversal.LogicalColumns,
                    (rowNumber, columnNumber) => occupiedCells.Contains((rowNumber, columnNumber)));
                textClip = CalculateOverflowClip(
                    bounds,
                    cell.Style?.HorizontalAlignment ?? CellHAlign.General,
                    availability);
            }

            consumer.AcceptLayout(new ViewportCellLayout(
                cell,
                bounds,
                textClip,
                ResolveSplitRegion(isTopPane, isLeftPane)));
        }
    }

    public static IReadOnlyList<RowMetric> ProjectRows(ViewportModel viewport)
    {
        var topRows = viewport.SplitPanes?.TopRows;
        if (topRows is not { Count: > 0 })
            return viewport.RowMetrics;

        var lastPinnedRow = topRows[^1].Row;
        var projected = new List<RowMetric>(topRows.Count + viewport.RowMetrics.Count);
        projected.AddRange(topRows);
        foreach (var metric in viewport.RowMetrics)
        {
            if (metric.Row > lastPinnedRow)
                projected.Add(metric);
        }

        return projected;
    }

    public static IReadOnlyList<ColMetric> ProjectColumns(ViewportModel viewport)
    {
        var leftColumns = viewport.SplitPanes?.LeftColumns;
        if (leftColumns is not { Count: > 0 })
            return viewport.ColMetrics;

        var lastPinnedColumn = leftColumns[^1].Col;
        var projected = new List<ColMetric>(leftColumns.Count + viewport.ColMetrics.Count);
        projected.AddRange(leftColumns);
        foreach (var metric in viewport.ColMetrics)
        {
            if (metric.Col > lastPinnedColumn)
                projected.Add(metric);
        }

        return projected;
    }

    public static double CalculateProjectedWidth(ViewportModel viewport, ViewportGeometrySettings settings)
    {
        var width = settings.RowHeaderWidth;
        foreach (var metric in ProjectColumns(viewport))
            width += settings.GetColumnWidth(metric);
        return width;
    }

    public static double CalculateProjectedHeight(ViewportModel viewport, ViewportGeometrySettings settings)
    {
        var height = settings.ColumnHeaderHeight;
        foreach (var metric in ProjectRows(viewport))
            height += settings.GetRowHeight(metric);
        return height;
    }

    public static bool TryGetCellBounds(
        ViewportModel viewport,
        uint row,
        uint column,
        ViewportGeometrySettings settings,
        out LayoutRect bounds)
    {
        if (settings.MetricPlacement == ViewportMetricPlacement.Sequential)
        {
            return TryGetSequentialCellBounds(
                ProjectRows(viewport),
                ProjectColumns(viewport),
                row,
                column,
                settings,
                out bounds);
        }

        if (viewport.SplitPanes is not { } splitPanes)
        {
            if (!TryFindRow(viewport.RowMetrics, row, out var rowMetric) ||
                !TryFindColumn(viewport.ColMetrics, column, out var columnMetric))
            {
                bounds = default;
                return false;
            }

            bounds = CreateOffsetBounds(
                rowMetric,
                columnMetric,
                settings.ColumnHeaderHeight,
                settings.RowHeaderWidth,
                settings);
            return true;
        }

        var isTop = TryFindRow(splitPanes.TopRows ?? [], row, out var topRow);
        var isLeft = TryFindColumn(splitPanes.LeftColumns ?? [], column, out var leftColumn);
        var rows = isTop ? splitPanes.TopRows ?? [] : splitPanes.BottomLeftRows ?? viewport.RowMetrics;
        var columns = isLeft ? splitPanes.LeftColumns ?? [] : splitPanes.TopRightColumns ?? viewport.ColMetrics;
        var resolvedRow = isTop ? topRow : TryFindRow(rows, row, out var bottomRow) ? bottomRow : null;
        var resolvedColumn = isLeft ? leftColumn : TryFindColumn(columns, column, out var rightColumn) ? rightColumn : null;
        if (resolvedRow is null || resolvedColumn is null)
        {
            bounds = default;
            return false;
        }

        var divider = SplitPanePointerPlanner.CalculateDividerLayout(
            viewport,
            settings.EffectiveSplitRowHeaderWidth,
            settings.EffectiveSplitColumnHeaderHeight,
            settings.EffectiveScale);
        bounds = CreateOffsetBounds(
            resolvedRow,
            resolvedColumn,
            isTop ? settings.ColumnHeaderHeight : divider.HorizontalY ?? settings.ColumnHeaderHeight,
            isLeft ? settings.RowHeaderWidth : divider.VerticalX ?? settings.RowHeaderWidth,
            settings);
        return true;
    }

    public static bool TryGetCellBounds(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns,
        uint row,
        uint column,
        ViewportGeometrySettings settings,
        out LayoutRect bounds)
    {
        if (settings.MetricPlacement == ViewportMetricPlacement.Sequential)
            return TryGetSequentialCellBounds(rows, columns, row, column, settings, out bounds);

        if (!TryFindRow(rows, row, out var rowMetric) ||
            !TryFindColumn(columns, column, out var columnMetric))
        {
            bounds = default;
            return false;
        }

        bounds = CreateOffsetBounds(
            rowMetric,
            columnMetric,
            settings.ColumnHeaderHeight,
            settings.RowHeaderWidth,
            settings);
        return true;
    }

    public static bool TryGetVisibleRangeBounds(
        ViewportModel viewport,
        GridRange range,
        ViewportGeometrySettings settings,
        out LayoutRect bounds)
    {
        var startRow = Math.Min(range.Start.Row, range.End.Row);
        var endRow = Math.Max(range.Start.Row, range.End.Row);
        var startColumn = Math.Min(range.Start.Col, range.End.Col);
        var endColumn = Math.Max(range.Start.Col, range.End.Col);
        var rows = settings.MetricPlacement == ViewportMetricPlacement.Sequential
            ? ProjectRows(viewport)
            : viewport.RowMetrics;
        var columns = settings.MetricPlacement == ViewportMetricPlacement.Sequential
            ? ProjectColumns(viewport)
            : viewport.ColMetrics;

        double top;
        double height;
        double left;
        double width;
        var hasRows = settings.MetricPlacement == ViewportMetricPlacement.Sequential
            ? TryGetVisibleAxisBounds(rows, startRow, endRow, settings.ColumnHeaderHeight, settings, out top, out height)
            : TryGetVisibleOffsetAxisBounds(rows, startRow, endRow, settings.ColumnHeaderHeight, settings, out top, out height);
        var hasColumns = settings.MetricPlacement == ViewportMetricPlacement.Sequential
            ? TryGetVisibleAxisBounds(columns, startColumn, endColumn, settings.RowHeaderWidth, settings, out left, out width)
            : TryGetVisibleOffsetAxisBounds(columns, startColumn, endColumn, settings.RowHeaderWidth, settings, out left, out width);
        if (!hasRows || !hasColumns)
        {
            bounds = default;
            return false;
        }

        bounds = new LayoutRect(left, top, width, height);
        return width > 0 && height > 0;
    }

    public static CellAddress? ResolveVisibleMergeAnchor(
        GridRange merge,
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns)
    {
        uint? visibleRow = null;
        foreach (var metric in rows)
        {
            if (merge.Start.Row <= metric.Row && metric.Row <= merge.End.Row &&
                (visibleRow is null || metric.Row < visibleRow.Value))
            {
                visibleRow = metric.Row;
            }
        }

        uint? visibleColumn = null;
        foreach (var metric in columns)
        {
            if (merge.Start.Col <= metric.Col && metric.Col <= merge.End.Col &&
                (visibleColumn is null || metric.Col < visibleColumn.Value))
            {
                visibleColumn = metric.Col;
            }
        }

        return visibleRow is { } row && visibleColumn is { } column
            ? new CellAddress(merge.Start.Sheet, row, column)
            : null;
    }

    public static ViewportMergeSpan CalculateVisibleMergeSpan(
        GridRange merge,
        int rowIndex,
        int columnIndex,
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns,
        ViewportGeometrySettings settings)
    {
        var renderedRow = rows[rowIndex].Row;
        var renderedColumn = columns[columnIndex].Col;
        var rowSpan = 1;
        var height = settings.GetRowHeight(rows[rowIndex]);
        while (rowIndex + rowSpan < rows.Count)
        {
            var next = rows[rowIndex + rowSpan];
            if (next.Row != renderedRow + (uint)rowSpan || next.Row > merge.End.Row)
                break;
            height += settings.GetRowHeight(next);
            rowSpan++;
        }

        var columnSpan = 1;
        var width = settings.GetColumnWidth(columns[columnIndex]);
        while (columnIndex + columnSpan < columns.Count)
        {
            var next = columns[columnIndex + columnSpan];
            if (next.Col != renderedColumn + (uint)columnSpan || next.Col > merge.End.Col)
                break;
            width += settings.GetColumnWidth(next);
            columnSpan++;
        }

        return new ViewportMergeSpan(rowSpan, columnSpan, height, width);
    }

    public static CellAddress? HitTestCell(
        ViewportModel viewport,
        SheetId sheetId,
        LayoutPoint position,
        ViewportGeometrySettings settings)
    {
        if (position.X < settings.RowHeaderWidth || position.Y < settings.ColumnHeaderHeight)
            return null;

        if (settings.MetricPlacement == ViewportMetricPlacement.Sequential)
        {
            var row = HitTestRows(ProjectRows(viewport), position.Y, settings.ColumnHeaderHeight, settings);
            var column = HitTestColumns(ProjectColumns(viewport), position.X, settings.RowHeaderWidth, settings);
            return row is { } rowNumber && column is { } columnNumber
                ? new CellAddress(sheetId, rowNumber, columnNumber)
                : null;
        }

        var rows = viewport.RowMetrics;
        var columns = viewport.ColMetrics;
        var rowOrigin = settings.ColumnHeaderHeight;
        var columnOrigin = settings.RowHeaderWidth;
        if (viewport.SplitPanes is { } splitPanes)
        {
            var divider = SplitPanePointerPlanner.CalculateDividerLayout(
                viewport,
                settings.EffectiveSplitRowHeaderWidth,
                settings.EffectiveSplitColumnHeaderHeight,
                settings.EffectiveScale);
            var isTop = divider.HorizontalY is { } horizontalY && position.Y < horizontalY;
            var isLeft = divider.VerticalX is { } verticalX && position.X < verticalX;
            rows = isTop ? splitPanes.TopRows ?? [] : isLeft
                ? splitPanes.BottomLeftRows ?? viewport.RowMetrics
                : viewport.RowMetrics;
            columns = isLeft ? splitPanes.LeftColumns ?? [] : isTop
                ? splitPanes.TopRightColumns ?? viewport.ColMetrics
                : viewport.ColMetrics;
            rowOrigin = isTop ? settings.ColumnHeaderHeight : divider.HorizontalY ?? settings.ColumnHeaderHeight;
            columnOrigin = isLeft ? settings.RowHeaderWidth : divider.VerticalX ?? settings.RowHeaderWidth;
        }

        var hitRow = HitTestRows(rows, position.Y, rowOrigin, settings, useMetricOffsets: true);
        var hitColumn = HitTestColumns(columns, position.X, columnOrigin, settings, useMetricOffsets: true);
        return hitRow is { } rowValue && hitColumn is { } columnValue
            ? new CellAddress(sheetId, rowValue, columnValue)
            : null;
    }

    public static uint? HitTestProjectedRow(
        ViewportModel viewport,
        double coordinate,
        ViewportGeometrySettings settings) =>
        HitTestRows(ProjectRows(viewport), coordinate, settings.ColumnHeaderHeight, settings);

    public static uint? HitTestProjectedColumn(
        ViewportModel viewport,
        double coordinate,
        ViewportGeometrySettings settings) =>
        HitTestColumns(ProjectColumns(viewport), coordinate, settings.RowHeaderWidth, settings);

    public static ViewportOverflowAvailability CalculateOverflowAvailability(
        uint row,
        uint column,
        int columnIndex,
        IReadOnlyList<ColMetric> columns,
        uint frozenColumns,
        ViewportGeometrySettings settings,
        ViewportOverflowTraversal traversal,
        Func<uint, uint, bool> isOccupied)
    {
        if (columnIndex < 0 || columnIndex >= columns.Count)
            return default;

        return traversal == ViewportOverflowTraversal.VisibleMetrics
            ? CalculateVisibleMetricOverflow(row, columnIndex, columns, frozenColumns, settings, isOccupied)
            : CalculateLogicalColumnOverflow(row, column, columns, frozenColumns, settings, isOccupied);
    }

    public static LayoutRect CalculateOverflowClip(
        LayoutRect cellBounds,
        CellHAlign alignment,
        ViewportOverflowAvailability availability)
    {
        var leftWidth = alignment is CellHAlign.Right or CellHAlign.Center ? availability.LeftWidth : 0;
        var rightWidth = alignment == CellHAlign.Right ? 0 : availability.RightWidth;
        return new LayoutRect(
            cellBounds.Left - leftWidth,
            cellBounds.Top,
            cellBounds.Width + leftWidth + rightWidth,
            cellBounds.Height);
    }

    public static ViewportFrozenQuadrant ResolveFrozenQuadrant(ViewportModel viewport, uint row, uint column)
    {
        var frozenRows = viewport.FrozenPanes?.Rows ?? 0;
        var frozenColumns = viewport.FrozenPanes?.Cols ?? 0;
        var isFrozenRow = frozenRows > 0 && row <= frozenRows;
        var isFrozenColumn = frozenColumns > 0 && column <= frozenColumns;
        return (isFrozenRow, isFrozenColumn) switch
        {
            (true, true) => ViewportFrozenQuadrant.FrozenRowsAndColumns,
            (true, false) => ViewportFrozenQuadrant.FrozenRows,
            (false, true) => ViewportFrozenQuadrant.FrozenColumns,
            _ => ViewportFrozenQuadrant.Scrollable,
        };
    }

    public static ViewportCellEdgeVisibility GetCellEdgeVisibility(GridRange? merge, uint row, uint column) =>
        merge is not { } region
            ? new ViewportCellEdgeVisibility(true, true, true, true)
            : new ViewportCellEdgeVisibility(
                row == region.Start.Row,
                row == region.End.Row,
                column == region.Start.Col,
                column == region.End.Col);

    public static bool Intersects(LayoutRect bounds, LayoutRect viewportBounds, bool includeTouchingEdges = false) =>
        includeTouchingEdges
            ? bounds.Right >= viewportBounds.Left && bounds.Left <= viewportBounds.Right &&
              bounds.Bottom >= viewportBounds.Top && bounds.Top <= viewportBounds.Bottom
            : bounds.Right > viewportBounds.Left && bounds.Left < viewportBounds.Right &&
              bounds.Bottom > viewportBounds.Top && bounds.Top < viewportBounds.Bottom;

    private static void EmitMergeLayouts<TConsumer>(
        DisplayCell cell,
        GridRange merge,
        bool anchorIsTop,
        bool anchorIsLeft,
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<RowMetric> bottomRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<ColMetric> rightColumns,
        ViewportGeometrySettings settings,
        double horizontalY,
        double verticalX,
        ref TConsumer consumer)
        where TConsumer : struct, IViewportCellLayoutConsumer
    {
        var primaryRows = anchorIsTop ? topRows : bottomRows;
        var secondaryRows = anchorIsTop ? bottomRows : topRows;
        var primaryColumns = anchorIsLeft ? leftColumns : rightColumns;
        var secondaryColumns = anchorIsLeft ? rightColumns : leftColumns;
        var primaryRowSpan = FindMergeRowSpan(primaryRows, merge, settings);
        var primaryColumnSpan = FindMergeColumnSpan(primaryColumns, merge, settings);
        if (primaryRowSpan is not { } rows)
            return;

        var secondaryRowSpan = FindMergeRowSpan(secondaryRows, merge, settings, primaryRows);
        var secondaryColumnSpan = FindMergeColumnSpan(secondaryColumns, merge, settings, primaryColumns);
        var primaryRowOrigin = anchorIsTop ? settings.ColumnHeaderHeight : horizontalY;
        var secondaryRowOrigin = anchorIsTop ? horizontalY : settings.ColumnHeaderHeight;
        var primaryColumnOrigin = anchorIsLeft ? settings.RowHeaderWidth : verticalX;
        var secondaryColumnOrigin = anchorIsLeft ? verticalX : settings.RowHeaderWidth;

        if (primaryColumnSpan is { } columns)
            EmitMergeQuadrant(cell, rows, columns, anchorIsTop, anchorIsLeft, primaryRowOrigin, primaryColumnOrigin, true, ref consumer);
        if (secondaryColumnSpan is { } secondaryColumnsSpan)
            EmitMergeQuadrant(cell, rows, secondaryColumnsSpan, anchorIsTop, !anchorIsLeft, primaryRowOrigin, secondaryColumnOrigin, false, ref consumer);
        if (secondaryRowSpan is { } secondaryRowsSpan)
        {
            if (primaryColumnSpan is { } primaryColumnsSpan)
                EmitMergeQuadrant(cell, secondaryRowsSpan, primaryColumnsSpan, !anchorIsTop, anchorIsLeft, secondaryRowOrigin, primaryColumnOrigin, false, ref consumer);
            if (secondaryColumnSpan is { } secondaryColumnsSpan2)
                EmitMergeQuadrant(cell, secondaryRowsSpan, secondaryColumnsSpan2, !anchorIsTop, !anchorIsLeft, secondaryRowOrigin, secondaryColumnOrigin, false, ref consumer);
        }
    }

    private static void EmitMergeQuadrant<TConsumer>(
        DisplayCell cell,
        AxisSpan rows,
        AxisSpan columns,
        bool isTop,
        bool isLeft,
        double rowOrigin,
        double columnOrigin,
        bool isPrimary,
        ref TConsumer consumer)
        where TConsumer : struct, IViewportCellLayoutConsumer
    {
        var bounds = new LayoutRect(
            columnOrigin + columns.Offset,
            rowOrigin + rows.Offset,
            columns.Size,
            rows.Size);
        consumer.AcceptLayout(new ViewportCellLayout(
            isPrimary ? cell : StripMergeContent(cell),
            bounds,
            bounds,
            ResolveSplitRegion(isTop, isLeft)));
    }

    private static DisplayCell StripMergeContent(DisplayCell cell) =>
        cell with
        {
            DisplayText = string.Empty,
            HasComment = false,
            CommentDisplay = null,
            ConditionalIcon = null,
            ConditionalDataBar = null,
        };

    private static bool TryGetSequentialCellBounds(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns,
        uint row,
        uint column,
        ViewportGeometrySettings settings,
        out LayoutRect bounds)
    {
        var top = settings.ColumnHeaderHeight;
        RowMetric? rowMetric = null;
        foreach (var metric in rows)
        {
            if (metric.Row == row)
            {
                rowMetric = metric;
                break;
            }
            top += settings.GetRowHeight(metric);
        }

        var left = settings.RowHeaderWidth;
        ColMetric? columnMetric = null;
        foreach (var metric in columns)
        {
            if (metric.Col == column)
            {
                columnMetric = metric;
                break;
            }
            left += settings.GetColumnWidth(metric);
        }

        if (rowMetric is null || columnMetric is null)
        {
            bounds = default;
            return false;
        }

        bounds = new LayoutRect(
            left,
            top,
            settings.GetColumnWidth(columnMetric),
            settings.GetRowHeight(rowMetric));
        return true;
    }

    private static LayoutRect CreateOffsetBounds(
        RowMetric row,
        ColMetric column,
        double rowOrigin,
        double columnOrigin,
        ViewportGeometrySettings settings) =>
        new(
            columnOrigin + column.LeftOffset * settings.EffectiveScale,
            rowOrigin + row.TopOffset * settings.EffectiveScale,
            settings.GetColumnWidth(column),
            settings.GetRowHeight(row));

    private static bool TryGetVisibleAxisBounds(
        IReadOnlyList<RowMetric> metrics,
        uint start,
        uint end,
        double origin,
        ViewportGeometrySettings settings,
        out double position,
        out double size)
    {
        position = origin;
        size = 0;
        var found = false;
        foreach (var metric in metrics)
        {
            var metricSize = settings.GetRowHeight(metric);
            if (metric.Row < start || metric.Row > end)
            {
                if (!found)
                    position += metricSize;
                continue;
            }
            found = true;
            size += metricSize;
        }
        return found;
    }

    private static bool TryGetVisibleOffsetAxisBounds(
        IReadOnlyList<RowMetric> metrics,
        uint start,
        uint end,
        double origin,
        ViewportGeometrySettings settings,
        out double position,
        out double size)
    {
        position = 0;
        size = 0;
        var found = false;
        foreach (var metric in metrics)
        {
            if (metric.Row < start || metric.Row > end)
                continue;
            var metricPosition = origin + metric.TopOffset * settings.EffectiveScale;
            position = found ? Math.Min(position, metricPosition) : metricPosition;
            size += settings.GetRowHeight(metric);
            found = true;
        }
        return found;
    }

    private static bool TryGetVisibleOffsetAxisBounds(
        IReadOnlyList<ColMetric> metrics,
        uint start,
        uint end,
        double origin,
        ViewportGeometrySettings settings,
        out double position,
        out double size)
    {
        position = 0;
        size = 0;
        var found = false;
        foreach (var metric in metrics)
        {
            if (metric.Col < start || metric.Col > end)
                continue;
            var metricPosition = origin + metric.LeftOffset * settings.EffectiveScale;
            position = found ? Math.Min(position, metricPosition) : metricPosition;
            size += settings.GetColumnWidth(metric);
            found = true;
        }
        return found;
    }

    private static bool TryGetVisibleAxisBounds(
        IReadOnlyList<ColMetric> metrics,
        uint start,
        uint end,
        double origin,
        ViewportGeometrySettings settings,
        out double position,
        out double size)
    {
        position = origin;
        size = 0;
        var found = false;
        foreach (var metric in metrics)
        {
            var metricSize = settings.GetColumnWidth(metric);
            if (metric.Col < start || metric.Col > end)
            {
                if (!found)
                    position += metricSize;
                continue;
            }
            found = true;
            size += metricSize;
        }
        return found;
    }

    private static ViewportOverflowAvailability CalculateVisibleMetricOverflow(
        uint row,
        int columnIndex,
        IReadOnlyList<ColMetric> columns,
        uint frozenColumns,
        ViewportGeometrySettings settings,
        Func<uint, uint, bool> isOccupied)
    {
        var left = 0d;
        var previousColumn = columns[columnIndex].Col;
        for (var index = columnIndex - 1; index >= 0; index--)
        {
            var column = columns[index];
            if (CrossesFrozenBoundary(previousColumn, column.Col, frozenColumns) || isOccupied(row, column.Col))
                break;
            left += settings.GetColumnWidth(column);
            previousColumn = column.Col;
        }

        var right = 0d;
        previousColumn = columns[columnIndex].Col;
        for (var index = columnIndex + 1; index < columns.Count; index++)
        {
            var column = columns[index];
            if (CrossesFrozenBoundary(previousColumn, column.Col, frozenColumns) || isOccupied(row, column.Col))
                break;
            right += settings.GetColumnWidth(column);
            previousColumn = column.Col;
        }

        return new ViewportOverflowAvailability(left, right);
    }

    private static ViewportOverflowAvailability CalculateLogicalColumnOverflow(
        uint row,
        uint column,
        IReadOnlyList<ColMetric> columns,
        uint frozenColumns,
        ViewportGeometrySettings settings,
        Func<uint, uint, bool> isOccupied)
    {
        if (columns.Count == 0)
            return default;

        var minColumn = columns[0].Col;
        var maxColumn = columns[^1].Col;
        if (frozenColumns > 0)
        {
            if (column <= frozenColumns)
                maxColumn = Math.Min(maxColumn, frozenColumns);
            else
                minColumn = Math.Max(minColumn, frozenColumns + 1);
        }

        var left = 0d;
        if (column > minColumn)
        {
            var previous = column - 1;
            while (previous >= minColumn)
            {
                if (TryFindColumn(columns, previous, out var metric))
                {
                    if (isOccupied(row, previous))
                        break;
                    left += settings.GetColumnWidth(metric);
                }
                if (previous == minColumn)
                    break;
                previous--;
            }
        }

        var right = 0d;
        if (column < uint.MaxValue)
        {
            var next = column + 1;
            while (next <= maxColumn)
            {
                if (TryFindColumn(columns, next, out var metric))
                {
                    if (isOccupied(row, next))
                        break;
                    right += settings.GetColumnWidth(metric);
                }
                next++;
            }
        }

        return new ViewportOverflowAvailability(left, right);
    }

    private static bool CrossesFrozenBoundary(uint previous, uint current, uint frozenColumns) =>
        frozenColumns > 0 &&
        ((previous <= frozenColumns && current > frozenColumns) ||
         (previous > frozenColumns && current <= frozenColumns));

    private static uint? HitTestRows(
        IReadOnlyList<RowMetric> rows,
        double coordinate,
        double origin,
        ViewportGeometrySettings settings,
        bool useMetricOffsets = false)
    {
        var position = origin;
        foreach (var metric in rows)
        {
            var start = useMetricOffsets
                ? origin + metric.TopOffset * settings.EffectiveScale
                : position;
            var end = start + settings.GetRowHeight(metric);
            if (coordinate < start)
                break;
            if (ContainsCoordinate(coordinate, start, end, settings.HitTestEdges))
                return metric.Row;
            position = end;
        }
        return null;
    }

    private static uint? HitTestColumns(
        IReadOnlyList<ColMetric> columns,
        double coordinate,
        double origin,
        ViewportGeometrySettings settings,
        bool useMetricOffsets = false)
    {
        var position = origin;
        foreach (var metric in columns)
        {
            var start = useMetricOffsets
                ? origin + metric.LeftOffset * settings.EffectiveScale
                : position;
            var end = start + settings.GetColumnWidth(metric);
            if (coordinate < start)
                break;
            if (ContainsCoordinate(coordinate, start, end, settings.HitTestEdges))
                return metric.Col;
            position = end;
        }
        return null;
    }

    private static bool ContainsCoordinate(
        double coordinate,
        double start,
        double end,
        ViewportHitTestEdgeBehavior behavior) =>
        coordinate >= start &&
        (behavior == ViewportHitTestEdgeBehavior.InclusiveEnd ? coordinate <= end : coordinate < end);

    private static AxisSpan? FindMergeRowSpan(
        IReadOnlyList<RowMetric> rows,
        GridRange merge,
        ViewportGeometrySettings settings,
        IReadOnlyList<RowMetric>? exclude = null)
    {
        double total = 0;
        double? minimumOffset = null;
        foreach (var metric in rows)
        {
            if (metric.Row < merge.Start.Row || metric.Row > merge.End.Row ||
                (exclude is not null && TryFindRow(exclude, metric.Row, out _)))
            {
                continue;
            }
            minimumOffset = minimumOffset is null
                ? metric.TopOffset * settings.EffectiveScale
                : Math.Min(minimumOffset.Value, metric.TopOffset * settings.EffectiveScale);
            total += settings.GetRowHeight(metric);
        }
        return minimumOffset is { } offset ? new AxisSpan(offset, total) : null;
    }

    private static AxisSpan? FindMergeColumnSpan(
        IReadOnlyList<ColMetric> columns,
        GridRange merge,
        ViewportGeometrySettings settings,
        IReadOnlyList<ColMetric>? exclude = null)
    {
        double total = 0;
        double? minimumOffset = null;
        foreach (var metric in columns)
        {
            if (metric.Col < merge.Start.Col || metric.Col > merge.End.Col ||
                (exclude is not null && TryFindColumn(exclude, metric.Col, out _)))
            {
                continue;
            }
            minimumOffset = minimumOffset is null
                ? metric.LeftOffset * settings.EffectiveScale
                : Math.Min(minimumOffset.Value, metric.LeftOffset * settings.EffectiveScale);
            total += settings.GetColumnWidth(metric);
        }
        return minimumOffset is { } offset ? new AxisSpan(offset, total) : null;
    }

    private static HashSet<(uint Row, uint Col)> BuildOccupiedCellSet(
        IReadOnlyList<DisplayCell> cells,
        CellAddress? editingCell)
    {
        var occupied = new HashSet<(uint Row, uint Col)>();
        foreach (var cell in cells)
        {
            if (CellTextOverflowPlanner.IsOverflowOccupied(cell, editingCell))
                occupied.Add((cell.Row, cell.Col));
        }
        return occupied;
    }

    private static GridRange? FindMerge(IReadOnlyList<GridRange>? mergedRegions, uint row, uint column)
    {
        if (mergedRegions is null)
            return null;
        foreach (var merge in mergedRegions)
        {
            if (merge.Start.Row <= row && row <= merge.End.Row &&
                merge.Start.Col <= column && column <= merge.End.Col)
            {
                return merge;
            }
        }
        return null;
    }

    private static bool TryFindRow(IReadOnlyList<RowMetric> rows, uint row, out RowMetric metric)
    {
        foreach (var candidate in rows)
        {
            if (candidate.Row == row)
            {
                metric = candidate;
                return true;
            }
        }
        metric = null!;
        return false;
    }

    private static bool TryFindColumn(IReadOnlyList<ColMetric> columns, uint column, out ColMetric metric)
    {
        foreach (var candidate in columns)
        {
            if (candidate.Col == column)
            {
                metric = candidate;
                return true;
            }
        }
        metric = null!;
        return false;
    }

    public static int GetRowIndex(IReadOnlyList<RowMetric> rows, uint row)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Row == row)
                return index;
        }
        return -1;
    }

    public static int GetColumnIndex(IReadOnlyList<ColMetric> columns, uint column)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index].Col == column)
                return index;
        }
        return -1;
    }

    private static SplitPanePointerRegion ResolveSplitRegion(bool isTop, bool isLeft) =>
        (isTop, isLeft) switch
        {
            (true, true) => SplitPanePointerRegion.TopLeft,
            (true, false) => SplitPanePointerRegion.TopRight,
            (false, true) => SplitPanePointerRegion.BottomLeft,
            _ => SplitPanePointerRegion.BottomRight,
        };

    private readonly record struct AxisSpan(double Offset, double Size);

    private struct ViewportCellLayoutCollector(int capacity) : IViewportCellLayoutConsumer
    {
        private List<ViewportCellLayout>? _layouts = new(capacity);

        public void AcceptLayout(ViewportCellLayout layout) => _layouts!.Add(layout);

        public readonly IReadOnlyList<ViewportCellLayout> Layouts => _layouts ?? [];
    }
}
