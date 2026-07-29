using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public static class DrawingObjectViewportPlanner
{
    public const double EmusPerPixel = 9525.0;
    public const double MinimumNativeControlWidth = 80;
    public const double MinimumNativeControlHeight = 44;

    public static bool TryCreateAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out LayoutRect rect)
    {
        rect = default;
        if (viewport is null ||
            !TryGetAnchorPoints(viewport, anchor, rowHeaderWidth, columnHeaderHeight, out var topLeft, out var bottomRight))
        {
            return false;
        }

        return TryBuildRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y, out rect);
    }

    public static bool TryCreateAnchorRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out LayoutRect rect)
    {
        rect = default;
        if (!TryGetAnchorPoints(rows, columns, anchor, rowHeaderWidth, columnHeaderHeight, out var topLeft, out var bottomRight))
            return false;

        return TryBuildRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y, out rect);
    }

    public static bool TryCreateSpanningAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out LayoutRect rect)
    {
        rect = default;
        if (viewport is null || HasInvalidAnchorPoint(anchor))
            return false;

        if (!TryFindAnchorColumns(viewport.ColMetrics, anchor.From.Column + 1, anchor.To.Column + 1, out var fromColumn, out var toColumn) ||
            !TryFindAnchorRows(viewport.RowMetrics, anchor.From.Row + 1, anchor.To.Row + 1, out var fromRow, out var toRow))
        {
            return false;
        }

        return TryBuildSpanningRect(rowHeaderWidth, columnHeaderHeight, fromColumn, toColumn, fromRow, toRow, out rect);
    }

    public static bool TryCreateSpanningAnchorRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out LayoutRect rect)
    {
        rect = default;
        if (HasInvalidAnchorPoint(anchor))
            return false;

        if (!columns.TryGetValue(anchor.From.Column + 1, out var fromColumn) ||
            !columns.TryGetValue(anchor.To.Column + 1, out var toColumn) ||
            !rows.TryGetValue(anchor.From.Row + 1, out var fromRow) ||
            !rows.TryGetValue(anchor.To.Row + 1, out var toRow))
        {
            return false;
        }

        return TryBuildSpanningRect(rowHeaderWidth, columnHeaderHeight, fromColumn, toColumn, fromRow, toRow, out rect);
    }

    public static bool TryCreateAnchoredObjectRect(
        ViewportModel? viewport,
        CellAddress anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out LayoutRect rect,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0)
    {
        rect = default;
        if (viewport is null ||
            !TryFindAnchorRow(viewport.RowMetrics, anchor.Row, out var row) ||
            !TryFindAnchorColumn(viewport.ColMetrics, anchor.Col, out var column))
        {
            return false;
        }

        rect = new LayoutRect(
            column.LeftOffset + rowHeaderWidth + anchorOffsetX,
            row.TopOffset + columnHeaderHeight + anchorOffsetY,
            NormalizeObjectExtent(width, minimumWidth),
            NormalizeObjectExtent(height, minimumHeight));
        return true;
    }

    public static bool TryCreateAnchoredObjectRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        CellAddress anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out LayoutRect rect,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0)
    {
        rect = default;
        if (!rows.TryGetValue(anchor.Row, out var row) ||
            !columns.TryGetValue(anchor.Col, out var column))
        {
            return false;
        }

        rect = new LayoutRect(
            column.LeftOffset + rowHeaderWidth + anchorOffsetX,
            row.TopOffset + columnHeaderHeight + anchorOffsetY,
            NormalizeObjectExtent(width, minimumWidth),
            NormalizeObjectExtent(height, minimumHeight));
        return true;
    }

    public static bool TryCreateDisplayedObjectRect(
        DrawingObjectBounds drawingObject,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double zoomFactor,
        out LayoutRect rect)
    {
        rect = default;
        if (!IsPositiveFinite(zoomFactor))
            return false;

        var left = rowHeaderWidth + (drawingObject.Left * zoomFactor);
        var top = columnHeaderHeight + (drawingObject.Top * zoomFactor);
        var width = Math.Max(1, drawingObject.Width * zoomFactor);
        var height = Math.Max(1, drawingObject.Height * zoomFactor);
        if (!IsFinite(left) || !IsFinite(top) || !IsFinite(width) || !IsFinite(height))
            return false;

        rect = new LayoutRect(left, top, width, height);
        return true;
    }

    public static LayoutRect EnsureMinimumControlRect(LayoutRect rect) =>
        EnsureMinimumRect(rect, MinimumNativeControlWidth, MinimumNativeControlHeight);

    public static LayoutRect EnsureMinimumRect(LayoutRect rect, double minimumWidth, double minimumHeight) =>
        new(rect.Left, rect.Top, Math.Max(minimumWidth, rect.Width), Math.Max(minimumHeight, rect.Height));

    public static DrawingViewportAnchorBounds GetRenderableAnchorBounds(
        ViewportModel viewport,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double visibleRight,
        double visibleBottom) =>
        new(
            FindLastRenderableRow(viewport.RowMetrics, columnHeaderHeight, visibleBottom),
            FindLastRenderableColumn(viewport.ColMetrics, rowHeaderWidth, visibleRight));

    public static bool CanAnchoredObjectReachViewport(
        CellAddress anchor,
        DrawingViewportAnchorBounds bounds) =>
        bounds.LastRow > 0 &&
        bounds.LastColumn > 0 &&
        anchor.Row <= bounds.LastRow &&
        anchor.Col <= bounds.LastColumn;

    public static bool CanAnchorRangeReachViewport(
        DrawingAnchorRange anchor,
        DrawingViewportAnchorBounds bounds) =>
        bounds.LastRow > 0 &&
        bounds.LastColumn > 0 &&
        anchor.From.Row != uint.MaxValue &&
        anchor.From.Column != uint.MaxValue &&
        anchor.From.Row < bounds.LastRow &&
        anchor.From.Column < bounds.LastColumn;

    public static bool ShouldDisplayAnchoredObject(
        bool isVisible,
        CellAddress anchor,
        DrawingViewportAnchorBounds bounds) =>
        isVisible && CanAnchoredObjectReachViewport(anchor, bounds);

    public static bool ShouldDisplayAnchorRange(
        DrawingAnchorRange anchor,
        DrawingViewportAnchorBounds bounds) =>
        CanAnchorRangeReachViewport(anchor, bounds);

    public static bool NeedsViewportCull(
        LayoutRect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        Math.Abs(rotationDegrees % 360) > 0.0001 ||
        rect.Left < 0 ||
        rect.Top < 0 ||
        rect.Left >= visibleRight ||
        rect.Top >= visibleBottom;

    public static bool ShouldDisplayObjectRect(
        LayoutRect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        !NeedsViewportCull(rect, rotationDegrees, visibleRight, visibleBottom) ||
        IntersectsViewport(rect, rotationDegrees, visibleRight, visibleBottom);

    public static bool IntersectsViewport(
        LayoutRect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        if (visibleRight <= 0 || visibleBottom <= 0)
            return false;

        var cullRect = Math.Abs(rotationDegrees % 360) <= 0.0001
            ? rect
            : CalculateRotatedBounds(rect, rotationDegrees);
        return cullRect.Right > 0 &&
            cullRect.Left < visibleRight &&
            cullRect.Bottom > 0 &&
            cullRect.Top < visibleBottom;
    }

    public static LayoutRect CalculateRotatedBounds(LayoutRect rect, double rotationDegrees)
    {
        var radians = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var centerX = rect.Left + rect.Width / 2.0;
        var centerY = rect.Top + rect.Height / 2.0;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        IncludeRotatedCorner(rect.Left, rect.Top);
        IncludeRotatedCorner(rect.Right, rect.Top);
        IncludeRotatedCorner(rect.Right, rect.Bottom);
        IncludeRotatedCorner(rect.Left, rect.Bottom);

        return LayoutRect.FromCorners(minX, minY, maxX, maxY);

        void IncludeRotatedCorner(double x, double y)
        {
            var dx = x - centerX;
            var dy = y - centerY;
            var rotatedX = centerX + dx * cos - dy * sin;
            var rotatedY = centerY + dx * sin + dy * cos;
            minX = Math.Min(minX, rotatedX);
            minY = Math.Min(minY, rotatedY);
            maxX = Math.Max(maxX, rotatedX);
            maxY = Math.Max(maxY, rotatedY);
        }
    }

    public static DrawingObjectPaintMetadata ResolveDrawingShapePaint(DrawingShapeModel shape, WorkbookTheme theme) =>
        new(
            shape.GetEffectiveFillColor(theme, DrawingShapeModel.ResolveDefaultFillColor(theme)),
            shape.GetEffectiveOutlineColor(theme, DrawingShapeModel.ResolveDefaultOutlineColor(theme)),
            shape.HasFill,
            !shape.OutlineHasNoFill);

    public static DrawingObjectPaintMetadata ResolveTextBoxPaint(TextBoxModel textBox, WorkbookTheme theme) =>
        new(
            textBox.GetEffectiveFillColor(theme, CellColor.White),
            textBox.GetEffectiveOutlineColor(theme, new CellColor(89, 89, 89)),
            textBox.HasFill,
            !textBox.OutlineHasNoFill);

    public static string CreateObjectPlaceholderLabel(string objectType, string? objectName, int index)
    {
        var fallback = index <= 1 ? objectType : $"{objectType} {index}";
        return string.IsNullOrWhiteSpace(objectName) ? fallback : objectName.Trim();
    }

    public static double EmusToPixels(long emus) => emus / EmusPerPixel;

    private static bool TryGetAnchorPoints(
        ViewportModel viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out LayoutPoint topLeft,
        out LayoutPoint bottomRight)
    {
        topLeft = default;
        bottomRight = default;
        if (HasInvalidAnchorPoint(anchor))
            return false;

        if (!TryFindAnchorColumns(viewport.ColMetrics, anchor.From.Column + 1, anchor.To.Column + 1, out var fromColumn, out var toColumn) ||
            !TryFindAnchorRows(viewport.RowMetrics, anchor.From.Row + 1, anchor.To.Row + 1, out var fromRow, out var toRow))
        {
            return false;
        }

        topLeft = new LayoutPoint(
            rowHeaderWidth + fromColumn.LeftOffset + EmusToPixels(anchor.From.ColumnOffsetEmu),
            columnHeaderHeight + fromRow.TopOffset + EmusToPixels(anchor.From.RowOffsetEmu));
        bottomRight = new LayoutPoint(
            rowHeaderWidth + toColumn.LeftOffset + EmusToPixels(anchor.To.ColumnOffsetEmu),
            columnHeaderHeight + toRow.TopOffset + EmusToPixels(anchor.To.RowOffsetEmu));
        return true;
    }

    private static bool TryGetAnchorPoints(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out LayoutPoint topLeft,
        out LayoutPoint bottomRight)
    {
        topLeft = default;
        bottomRight = default;
        if (HasInvalidAnchorPoint(anchor))
            return false;

        var fromColumnIndex = anchor.From.Column + 1;
        var toColumnIndex = anchor.To.Column + 1;
        var fromRowIndex = anchor.From.Row + 1;
        var toRowIndex = anchor.To.Row + 1;
        if (!columns.TryGetValue(fromColumnIndex, out var fromColumn) ||
            !columns.TryGetValue(toColumnIndex, out var toColumn) ||
            !rows.TryGetValue(fromRowIndex, out var fromRow) ||
            !rows.TryGetValue(toRowIndex, out var toRow))
        {
            return false;
        }

        topLeft = new LayoutPoint(
            rowHeaderWidth + fromColumn.LeftOffset + EmusToPixels(anchor.From.ColumnOffsetEmu),
            columnHeaderHeight + fromRow.TopOffset + EmusToPixels(anchor.From.RowOffsetEmu));
        bottomRight = new LayoutPoint(
            rowHeaderWidth + toColumn.LeftOffset + EmusToPixels(anchor.To.ColumnOffsetEmu),
            columnHeaderHeight + toRow.TopOffset + EmusToPixels(anchor.To.RowOffsetEmu));
        return true;
    }

    private static bool TryBuildRect(double left, double top, double right, double bottom, out LayoutRect rect)
    {
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            rect = default;
            return false;
        }

        rect = new LayoutRect(left, top, width, height);
        return true;
    }

    private static bool TryBuildSpanningRect(
        double rowHeaderWidth,
        double columnHeaderHeight,
        ColMetric fromColumn,
        ColMetric toColumn,
        RowMetric fromRow,
        RowMetric toRow,
        out LayoutRect rect)
    {
        var left = rowHeaderWidth + fromColumn.LeftOffset;
        var top = columnHeaderHeight + fromRow.TopOffset;
        var right = rowHeaderWidth + toColumn.LeftOffset + toColumn.Width;
        var bottom = columnHeaderHeight + toRow.TopOffset + toRow.Height;
        return TryBuildRect(left, top, right, bottom, out rect);
    }

    private static bool TryFindAnchorColumns(
        IReadOnlyList<ColMetric> metrics,
        uint fromColumn,
        uint toColumn,
        out ColMetric fromMetric,
        out ColMetric toMetric)
    {
        ColMetric? foundFrom = null;
        ColMetric? foundTo = null;

        foreach (var metric in metrics)
        {
            if (metric.Col > toColumn)
                break;

            if (foundFrom is null && metric.Col == fromColumn)
                foundFrom = metric;

            if (foundTo is null && metric.Col == toColumn)
                foundTo = metric;

            if (foundFrom is not null && foundTo is not null)
            {
                fromMetric = foundFrom;
                toMetric = foundTo;
                return true;
            }
        }

        fromMetric = null!;
        toMetric = null!;
        return false;
    }

    private static bool TryFindAnchorRows(
        IReadOnlyList<RowMetric> metrics,
        uint fromRow,
        uint toRow,
        out RowMetric fromMetric,
        out RowMetric toMetric)
    {
        RowMetric? foundFrom = null;
        RowMetric? foundTo = null;

        foreach (var metric in metrics)
        {
            if (metric.Row > toRow)
                break;

            if (foundFrom is null && metric.Row == fromRow)
                foundFrom = metric;

            if (foundTo is null && metric.Row == toRow)
                foundTo = metric;

            if (foundFrom is not null && foundTo is not null)
            {
                fromMetric = foundFrom;
                toMetric = foundTo;
                return true;
            }
        }

        fromMetric = null!;
        toMetric = null!;
        return false;
    }

    private static bool TryFindAnchorRow(IReadOnlyList<RowMetric> metrics, uint row, out RowMetric rowMetric)
    {
        foreach (var metric in metrics)
        {
            if (metric.Row > row)
                break;

            if (metric.Row == row)
            {
                rowMetric = metric;
                return true;
            }
        }

        rowMetric = null!;
        return false;
    }

    private static bool TryFindAnchorColumn(IReadOnlyList<ColMetric> metrics, uint column, out ColMetric columnMetric)
    {
        foreach (var metric in metrics)
        {
            if (metric.Col > column)
                break;

            if (metric.Col == column)
            {
                columnMetric = metric;
                return true;
            }
        }

        columnMetric = null!;
        return false;
    }

    private static uint FindLastRenderableRow(
        IReadOnlyList<RowMetric> rows,
        double columnHeaderHeight,
        double visibleBottom)
    {
        uint lastRow = 0;
        foreach (var row in rows)
        {
            if (columnHeaderHeight + row.TopOffset >= visibleBottom)
                break;
            if (row.Row > lastRow)
                lastRow = row.Row;
        }

        return lastRow;
    }

    private static uint FindLastRenderableColumn(
        IReadOnlyList<ColMetric> columns,
        double rowHeaderWidth,
        double visibleRight)
    {
        uint lastColumn = 0;
        foreach (var column in columns)
        {
            if (rowHeaderWidth + column.LeftOffset >= visibleRight)
                break;
            if (column.Col > lastColumn)
                lastColumn = column.Col;
        }

        return lastColumn;
    }

    private static bool HasInvalidAnchorPoint(DrawingAnchorRange anchor) =>
        anchor.From.Column == uint.MaxValue ||
        anchor.To.Column == uint.MaxValue ||
        anchor.From.Row == uint.MaxValue ||
        anchor.To.Row == uint.MaxValue;

    private static double NormalizeObjectExtent(double extent, double minimum) =>
        Math.Max(minimum, double.IsFinite(extent) && extent > 0 ? extent : 1);

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static bool IsFinite(double value) => double.IsFinite(value);
}

public readonly record struct DrawingViewportAnchorBounds(uint LastRow, uint LastColumn);

public readonly record struct DrawingObjectPaintMetadata(
    CellColor Fill,
    CellColor Outline,
    bool HasFill = true,
    bool HasOutline = true);
