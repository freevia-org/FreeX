using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    // Returns pixel coords for a range, clamped to viewport boundaries.
    private (double? top, double? left, double? bottom, double? right) GetRangePixels(
        ViewportModel vp, GridRange range)
    {
        double? top = null, left = null, bottom = null, right = null;
        foreach (var row in vp.RowMetrics)
        {
            if (row.Row == range.Start.Row) top    = row.TopOffset + EffectiveColHeaderHeight;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + EffectiveColHeaderHeight;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + ActualRowHeaderWidth;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + ActualRowHeaderWidth;
        }
        return (top, left, bottom, right);
    }

    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateClipboardMarquee(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateClipboardMarquee(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static Rect? CalculateQuickAnalysisPreviewRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    private void RenderQuickAnalysisPreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var rect = CalculateQuickAnalysisPreviewRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null)
            return;

        dc.DrawRectangle(QuickAnalysisPreviewBrush, QuickAnalysisPreviewPen, rect.Value);
    }

    private void RenderSelection(DrawingContext dc)
    {
        if (Viewport == null) return;
        if (SelectedRanges is { Count: > 0 } selectedRanges)
        {
            foreach (var range in selectedRanges)
                RenderSelectionRange(dc, range, drawHandle: false);

            if (SelectedRange is { } activeRange)
                RenderSelectionHandle(dc, activeRange);
            return;
        }

        if (SelectedRange == null) return;

        RenderSelectionRange(dc, SelectedRange.Value, drawHandle: true);
    }

    private void RenderSelectionRange(DrawingContext dc, GridRange range, bool drawHandle)
    {
        if (Viewport == null) return;
        var rows  = Viewport.RowMetrics;
        var cols  = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        var rect = CalculateVisibleSelectionRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null) return;

        double drawTop    = rect.Value.Top;
        double drawBottom = rect.Value.Bottom;
        double drawLeft   = rect.Value.Left;
        double drawRight  = rect.Value.Right;

        dc.DrawRectangle(SelectionBrush, null, rect.Value);

        if (top.HasValue)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
        if (bottom.HasValue) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
        if (left.HasValue)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
        if (right.HasValue)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));

        if (drawHandle)
            DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private void RenderSelectionHandle(DrawingContext dc, GridRange range)
    {
        if (Viewport == null) return;
        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        double drawBottom = bottom ?? ActualHeight;
        double drawRight  = right  ?? ActualWidth;
        DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private static void DrawSelectionHandle(DrawingContext dc, double? right, double? bottom, double drawRight, double drawBottom)
    {
        if (!right.HasValue || !bottom.HasValue)
            return;

        const double handleSize = 6;
        double hx = drawRight - handleSize / 2;
        double hy = drawBottom - handleSize / 2;
        dc.DrawRectangle(Brushes.White, SelectionPen,
            new Rect(hx, hy, handleSize, handleSize));
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(33, 115, 70)), null,
            new Rect(hx + 1, hy + 1, handleSize - 2, handleSize - 2));
    }
}
