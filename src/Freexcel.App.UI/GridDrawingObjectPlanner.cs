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
            !TryGetAnchorPoint(viewport, anchor.From, rowHeaderWidth, columnHeaderHeight, out var topLeft) ||
            !TryGetAnchorPoint(viewport, anchor.To, rowHeaderWidth, columnHeaderHeight, out var bottomRight))
        {
            return false;
        }

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

        var row = viewport.RowMetrics.FirstOrDefault(r => r.Row == anchor.Row);
        var col = viewport.ColMetrics.FirstOrDefault(c => c.Col == anchor.Col);
        if (row is null || col is null)
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

    private static bool TryGetAnchorPoint(
        ViewportModel viewport,
        DrawingAnchorPoint anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Point point)
    {
        point = default;
        if (anchor.Column == uint.MaxValue || anchor.Row == uint.MaxValue)
            return false;

        var column = viewport.ColMetrics.FirstOrDefault(metric => metric.Col == anchor.Column + 1);
        var row = viewport.RowMetrics.FirstOrDefault(metric => metric.Row == anchor.Row + 1);
        if (column is null || row is null)
            return false;

        point = new Point(
            rowHeaderWidth + column.LeftOffset + EmusToPixels(anchor.ColumnOffsetEmu),
            columnHeaderHeight + row.TopOffset + EmusToPixels(anchor.RowOffsetEmu));
        return true;
    }

    private static double EmusToPixels(long emus) => emus / EmusPerPixel;
}
