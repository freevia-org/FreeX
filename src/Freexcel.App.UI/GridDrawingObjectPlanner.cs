using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

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
        out Rect rect)
    {
        rect = default;
        if (viewport is null)
            return false;

        if (!TryFindAnchorRow(viewport.RowMetrics, anchor.Row, out var row) ||
            !TryFindAnchorColumn(viewport.ColMetrics, anchor.Col, out var col))
            return false;

        rect = new Rect(
            col.LeftOffset + rowHeaderWidth,
            row.TopOffset + columnHeaderHeight,
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
            shape.GetEffectiveFillColor(theme, new CellColor(31, 119, 180)),
            shape.GetEffectiveOutlineColor(theme, new CellColor(68, 68, 68)));

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
