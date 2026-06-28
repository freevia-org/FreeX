using System.Windows;
using Free.Shared.Drawing;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.UI;

internal static class GridDrawingObjectPlanner
{
    public static bool TryCreateDrawingAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateAnchorRect(
                viewport,
                anchor,
                rowHeaderWidth,
                columnHeaderHeight,
                out var layoutRect),
            layoutRect,
            out rect);

    public static bool TryCreateDrawingAnchorRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateAnchorRect(
                rows,
                columns,
                anchor,
                rowHeaderWidth,
                columnHeaderHeight,
                out var layoutRect),
            layoutRect,
            out rect);

    /// <summary>
    /// Builds an anchor rect that spans from the from-cell's top-left to the bottom-right of the
    /// to-cell (i.e. inclusive of the to-cell's full width/height). Unlike
    /// <see cref="TryCreateDrawingAnchorRect(ViewportModel?, DrawingAnchorRange, double, double, out Rect)"/>
    /// - which points at the to-cell's top-left and therefore collapses to zero size when the from
    /// and to cells are the same row/column - this never degenerates for a one-cell anchor. Used for
    /// legacy form controls, whose modeled anchor drops the sub-cell EMU offsets.
    /// </summary>
    public static bool TryCreateSpanningAnchorRect(
        ViewportModel? viewport,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateSpanningAnchorRect(
                viewport,
                anchor,
                rowHeaderWidth,
                columnHeaderHeight,
                out var layoutRect),
            layoutRect,
            out rect);

    public static bool TryCreateSpanningAnchorRect(
        IReadOnlyDictionary<uint, RowMetric> rows,
        IReadOnlyDictionary<uint, ColMetric> columns,
        DrawingAnchorRange anchor,
        double rowHeaderWidth,
        double columnHeaderHeight,
        out Rect rect) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateSpanningAnchorRect(
                rows,
                columns,
                anchor,
                rowHeaderWidth,
                columnHeaderHeight,
                out var layoutRect),
            layoutRect,
            out rect);

    public static Rect EnsureMinimumControlRect(Rect rect) =>
        ToWpfRect(DrawingObjectViewportPlanner.EnsureMinimumControlRect(ToLayoutRect(rect)));

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
        double anchorOffsetY = 0) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateAnchoredObjectRect(
                viewport,
                anchor,
                rowHeaderWidth,
                columnHeaderHeight,
                width,
                height,
                minimumWidth,
                minimumHeight,
                out var layoutRect,
                anchorOffsetX,
                anchorOffsetY),
            layoutRect,
            out rect);

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
        double anchorOffsetY = 0) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateAnchoredObjectRect(
                rows,
                columns,
                anchor,
                rowHeaderWidth,
                columnHeaderHeight,
                width,
                height,
                minimumWidth,
                minimumHeight,
                out var layoutRect,
                anchorOffsetX,
                anchorOffsetY),
            layoutRect,
            out rect);

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

    public static DrawingObjectColors ResolveDrawingShapeColors(DrawingShapeModel shape, WorkbookTheme theme)
    {
        var paint = DrawingObjectViewportPlanner.ResolveDrawingShapePaint(shape, theme);
        return new DrawingObjectColors(paint.Fill, paint.Outline);
    }

    public static DrawingObjectColors ResolveTextBoxColors(TextBoxModel textBox, WorkbookTheme theme)
    {
        var paint = DrawingObjectViewportPlanner.ResolveTextBoxPaint(textBox, theme);
        return new DrawingObjectColors(paint.Fill, paint.Outline);
    }

    public static string CreateObjectPlaceholderLabel(string objectType, string? objectName, int index) =>
        DrawingObjectViewportPlanner.CreateObjectPlaceholderLabel(objectType, objectName, index);

    public static bool TryCreateDisplayedObjectRect(
        DrawingObjectBounds drawingObject,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double zoomFactor,
        out Rect rect) =>
        TryConvert(
            DrawingObjectViewportPlanner.TryCreateDisplayedObjectRect(
                drawingObject,
                rowHeaderWidth,
                columnHeaderHeight,
                zoomFactor,
                out var layoutRect),
            layoutRect,
            out rect);

    public static bool IntersectsViewport(
        Rect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        DrawingObjectViewportPlanner.IntersectsViewport(
            ToLayoutRect(rect),
            rotationDegrees,
            visibleRight,
            visibleBottom);

    public static bool NeedsViewportCull(
        Rect rect,
        double rotationDegrees,
        double visibleRight,
        double visibleBottom) =>
        DrawingObjectViewportPlanner.NeedsViewportCull(
            ToLayoutRect(rect),
            rotationDegrees,
            visibleRight,
            visibleBottom);

    public static Rect CalculateRotatedBounds(Rect rect, double rotationDegrees) =>
        ToWpfRect(DrawingObjectViewportPlanner.CalculateRotatedBounds(ToLayoutRect(rect), rotationDegrees));

    private static bool TryConvert(bool created, LayoutRect layoutRect, out Rect rect)
    {
        rect = created ? ToWpfRect(layoutRect) : default;
        return created;
    }

    private static Rect ToWpfRect(LayoutRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static LayoutRect ToLayoutRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);
}
