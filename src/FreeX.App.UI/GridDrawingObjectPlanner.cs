using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

internal static class GridDrawingObjectPlanner
{
    private const double EmusPerPixel = 9525.0;

    public static bool TryCreateDrawingAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect)
    {
        rect = default;
        if (viewport is null ||
            !TryGetAnchorPoints(viewport, anchor, rowHeaderWidth, columnHeaderHeight, out var topLeft, out var bottomRight))
            return false;

        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
            return false;

        rect = new Rect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    public static bool TryCreateDrawingAnchorRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect)
    {
        rect = default;
        if (!TryGetAnchorPoints(rows, columns, anchor, rowHeaderWidth, columnHeaderHeight, out var topLeft, out var bottomRight))
            return false;

        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
            return false;

        rect = new Rect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    /// <summary>
    /// Builds an anchor rect that spans from the from-cell's top-left to the bottom-right of the
    /// to-cell (i.e. inclusive of the to-cell's full width/height). Unlike
    /// <see cref="TryCreateDrawingAnchorRect(ViewportModel?, DrawingAnchorRange, double, double, out Rect)"/>
    /// — which points at the to-cell's top-left and therefore collapses to zero size when the from
    /// and to cells are the same row/column — this never degenerates for a one-cell anchor. Used for
    /// legacy form controls, whose modeled anchor drops the sub-cell EMU offsets.
    /// </summary>
    public static bool TryCreateSpanningAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect)
    {
        rect = default;
        if (viewport is null ||
            anchor.From.Column == uint.MaxValue ||
            anchor.To.Column == uint.MaxValue ||
            anchor.From.Row == uint.MaxValue ||
            anchor.To.Row == uint.MaxValue)
            return false;

        if (!TryFindAnchorColumns(viewport.ColMetrics, anchor.From.Column + 1, anchor.To.Column + 1, out var fromColumn, out var toColumn) ||
            !TryFindAnchorRows(viewport.RowMetrics, anchor.From.Row + 1, anchor.To.Row + 1, out var fromRow, out var toRow))
            return false;

        return TryBuildSpanningRect(rowHeaderWidth, columnHeaderHeight, fromColumn, toColumn, fromRow, toRow, out rect);
    }

    public static bool TryCreateSpanningAnchorRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect)
    {
        rect = default;
        if (anchor.From.Column == uint.MaxValue ||
            anchor.To.Column == uint.MaxValue ||
            anchor.From.Row == uint.MaxValue ||
            anchor.To.Row == uint.MaxValue)
            return false;

        if (!columns.TryGetValue(anchor.From.Column + 1, out var fromColumn) ||
            !columns.TryGetValue(anchor.To.Column + 1, out var toColumn) ||
            !rows.TryGetValue(anchor.From.Row + 1, out var fromRow) ||
            !rows.TryGetValue(anchor.To.Row + 1, out var toRow))
            return false;

        return TryBuildSpanningRect(rowHeaderWidth, columnHeaderHeight, fromColumn, toColumn, fromRow, toRow, out rect);
    }

    private static bool TryBuildSpanningRect(
        double rowHeaderWidth,
        double columnHeaderHeight,
        ColMetric fromColumn,
        ColMetric toColumn,
        RowMetric fromRow,
        RowMetric toRow,
        out Rect rect)
    {
        var left = rowHeaderWidth + fromColumn.LeftOffset;
        var top = columnHeaderHeight + fromRow.TopOffset;
        var right = rowHeaderWidth + toColumn.LeftOffset + toColumn.Width;
        var bottom = columnHeaderHeight + toRow.TopOffset + toRow.Height;
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            rect = default;
            return false;
        }

        rect = new Rect(left, top, width, height);
        return true;
    }

    public static Rect EnsureMinimumControlRect(Rect rect) =>
        new(rect.Left, rect.Top, Math.Max(80, rect.Width), Math.Max(44, rect.Height));

    public static bool TryCreateAnchoredObjectRect(
        ViewportModel? viewport,
        CellAddress anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        out Rect rect,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0)
    {
        rect = default;
        if (viewport is null)
            return false;

        if (!TryFindAnchorRow(viewport.RowMetrics, anchor.Row, out var row) ||
            !TryFindAnchorColumn(viewport.ColMetrics, anchor.Col, out var col))
            return false;

        rect = new Rect(
            col.LeftOffset + rowHeaderWidth + anchorOffsetX,
            row.TopOffset + columnHeaderHeight + anchorOffsetY,
            Math.Max(minimumWidth, width),
            Math.Max(minimumHeight, height));
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
        out Rect rect,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0)
    {
        rect = default;
        if (!rows.TryGetValue(anchor.Row, out var row) ||
            !columns.TryGetValue(anchor.Col, out var col))
            return false;

        rect = new Rect(
            col.LeftOffset + rowHeaderWidth + anchorOffsetX,
            row.TopOffset + columnHeaderHeight + anchorOffsetY,
            Math.Max(minimumWidth, width),
            Math.Max(minimumHeight, height));
        return true;
    }

    public static string GetNativeControlCaption(string? caption, string name, string? shapeName)
    {
        if (!string.IsNullOrWhiteSpace(caption))
            return caption.Trim();
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        return string.IsNullOrWhiteSpace(shapeName) ? "Filter" : shapeName.Trim();
    }

    public static string FormatTimelineRange(TimelineModel timeline)
    {
        var start = timeline.SelectedStartDate ?? timeline.StartDate;
        var end = timeline.SelectedEndDate ?? timeline.EndDate;
        return string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end)
            ? timeline.SourceFieldName ?? timeline.CacheName
            : $"{start ?? ""} - {end ?? ""}".Trim();
    }

    public static DrawingObjectColors ResolveDrawingShapeColors(DrawingShapeModel shape, WorkbookTheme theme) =>
        new(
            shape.GetEffectiveFillColor(theme, DrawingShapeModel.ResolveDefaultFillColor(theme)),
            shape.GetEffectiveOutlineColor(theme, DrawingShapeModel.ResolveDefaultOutlineColor(theme)));

    public static DrawingObjectColors ResolveTextBoxColors(TextBoxModel textBox, WorkbookTheme theme) =>
        new(
            textBox.GetEffectiveFillColor(theme, CellColor.White),
            textBox.GetEffectiveOutlineColor(theme, new CellColor(89, 89, 89)));

    public static string CreateObjectPlaceholderLabel(string objectType, string? objectName, int index)
    {
        var fallback = index <= 1 ? objectType : $"{objectType} {index}";
        return string.IsNullOrWhiteSpace(objectName) ? fallback : objectName.Trim();
    }

    private static bool TryGetAnchorPoints(
        ViewportModel viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Point topLeft,
        out Point bottomRight)
    {
        topLeft = default;
        bottomRight = default;
        if (anchor.From.Column == uint.MaxValue ||
            anchor.To.Column == uint.MaxValue ||
            anchor.From.Row == uint.MaxValue ||
            anchor.To.Row == uint.MaxValue)
            return false;

        if (!TryFindAnchorColumns(viewport.ColMetrics, anchor.From.Column + 1, anchor.To.Column + 1, out var fromColumn, out var toColumn) ||
            !TryFindAnchorRows(viewport.RowMetrics, anchor.From.Row + 1, anchor.To.Row + 1, out var fromRow, out var toRow))
            return false;

        topLeft = new Point(
            rowHeaderWidth + fromColumn.LeftOffset + EmusToPixels(anchor.From.ColumnOffsetEmu),
            columnHeaderHeight + fromRow.TopOffset + EmusToPixels(anchor.From.RowOffsetEmu));
        bottomRight = new Point(
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
        out Point topLeft,
        out Point bottomRight)
    {
        topLeft = default;
        bottomRight = default;
        if (anchor.From.Column == uint.MaxValue ||
            anchor.To.Column == uint.MaxValue ||
            anchor.From.Row == uint.MaxValue ||
            anchor.To.Row == uint.MaxValue)
            return false;

        var fromColumnIndex = anchor.From.Column + 1;
        var toColumnIndex = anchor.To.Column + 1;
        var fromRowIndex = anchor.From.Row + 1;
        var toRowIndex = anchor.To.Row + 1;
        if (!columns.TryGetValue(fromColumnIndex, out var fromColumn) ||
            !columns.TryGetValue(toColumnIndex, out var toColumn) ||
            !rows.TryGetValue(fromRowIndex, out var fromRow) ||
            !rows.TryGetValue(toRowIndex, out var toRow))
            return false;

        topLeft = new Point(
            rowHeaderWidth + fromColumn.LeftOffset + EmusToPixels(anchor.From.ColumnOffsetEmu),
            columnHeaderHeight + fromRow.TopOffset + EmusToPixels(anchor.From.RowOffsetEmu));
        bottomRight = new Point(
            rowHeaderWidth + toColumn.LeftOffset + EmusToPixels(anchor.To.ColumnOffsetEmu),
            columnHeaderHeight + toRow.TopOffset + EmusToPixels(anchor.To.RowOffsetEmu));
        return true;
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

    private static double EmusToPixels(long emus) => emus / EmusPerPixel;
}
