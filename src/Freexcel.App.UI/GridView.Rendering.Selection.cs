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

    public static IReadOnlyList<Rect> CalculateQuickAnalysisDataBarPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var numericCells = viewport.Cells
            .Where(cell => cell.Row >= range.Start.Row
                && cell.Row <= range.End.Row
                && cell.Col >= range.Start.Col
                && cell.Col <= range.End.Col
                && TryGetPreviewNumber(cell, out _))
            .ToList();
        if (numericCells.Count == 0)
            return [];

        var max = numericCells
            .Select(cell => TryGetPreviewNumber(cell, out var value) ? Math.Max(0, value) : 0)
            .DefaultIfEmpty(0)
            .Max();

        var rows = viewport.RowMetrics.ToDictionary(row => row.Row);
        var cols = viewport.ColMetrics.ToDictionary(col => col.Col);
        var rects = new List<Rect>();
        foreach (var cell in numericCells)
        {
            if (!rows.TryGetValue(cell.Row, out var row) || !cols.TryGetValue(cell.Col, out var col))
                continue;

            TryGetPreviewNumber(cell, out var value);
            var fraction = max <= 0 ? 0 : Math.Clamp(Math.Max(0, value) / max, 0, 1);
            var availableWidth = Math.Max(0, col.Width - 6);
            rects.Add(new Rect(
                col.LeftOffset + rowHeaderWidth + 3,
                row.TopOffset + columnHeaderHeight + 4,
                Math.Round(availableWidth * fraction, 3),
                Math.Max(0, row.Height - 8)));
        }

        return rects;
    }

    public static IReadOnlyList<Rect> CalculateQuickAnalysisCellPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var rows = viewport.RowMetrics
            .Where(row => row.Row >= range.Start.Row && row.Row <= range.End.Row)
            .ToList();
        var cols = viewport.ColMetrics
            .Where(col => col.Col >= range.Start.Col && col.Col <= range.End.Col)
            .ToList();
        if (rows.Count == 0 || cols.Count == 0)
            return [];

        var rects = new List<Rect>(rows.Count * cols.Count);
        foreach (var row in rows)
        {
            foreach (var col in cols)
            {
                rects.Add(new Rect(
                    col.LeftOffset + rowHeaderWidth + 3,
                    row.TopOffset + columnHeaderHeight + 3,
                    Math.Max(0, col.Width - 6),
                    Math.Max(0, row.Height - 6)));
            }
        }

        return rects;
    }

    private static bool TryGetPreviewNumber(DisplayCell cell, out double value)
    {
        switch (cell.RawValue)
        {
            case NumberValue number:
                value = number.Value;
                return true;
            case DateTimeValue dateTime:
                value = dateTime.Value;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private void RenderQuickAnalysisPreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var rect = CalculateQuickAnalysisPreviewRect(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (rect is null)
            return;

        dc.DrawRectangle(QuickAnalysisPreviewBrush, QuickAnalysisPreviewPen, rect.Value);
        switch (QuickAnalysisPreviewVisual)
        {
            case GridQuickAnalysisPreviewVisualKind.DataBars:
                foreach (var bar in CalculateQuickAnalysisDataBarPreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight))
                    dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, bar);
                break;
            case GridQuickAnalysisPreviewVisualKind.Highlight:
                foreach (var cell in CalculateQuickAnalysisCellPreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight))
                    dc.DrawRectangle(QuickAnalysisHighlightPreviewBrush, null, cell);
                break;
            case GridQuickAnalysisPreviewVisualKind.ColorScale:
                RenderQuickAnalysisColorScalePreview(dc, range);
                break;
            case GridQuickAnalysisPreviewVisualKind.IconSet:
                RenderQuickAnalysisIconSetPreview(dc, range);
                break;
        }
    }

    private void RenderQuickAnalysisColorScalePreview(DrawingContext dc, GridRange range)
    {
        if (Viewport is null)
            return;

        var cells = CalculateQuickAnalysisCellPreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        for (var i = 0; i < cells.Count; i++)
        {
            var brush = (i % 3) switch
            {
                0 => QuickAnalysisColorScaleLowBrush,
                1 => QuickAnalysisColorScaleMidBrush,
                _ => QuickAnalysisColorScaleHighBrush
            };
            dc.DrawRectangle(brush, null, cells[i]);
        }
    }

    private void RenderQuickAnalysisIconSetPreview(DrawingContext dc, GridRange range)
    {
        if (Viewport is null)
            return;

        var cells = CalculateQuickAnalysisCellPreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        for (var i = 0; i < cells.Count; i++)
        {
            var brush = (i % 3) switch
            {
                0 => QuickAnalysisIconSetHighBrush,
                1 => QuickAnalysisIconSetMidBrush,
                _ => QuickAnalysisIconSetLowBrush
            };
            var rect = cells[i];
            var radius = Math.Max(2, Math.Min(rect.Width, rect.Height) / 5);
            dc.DrawEllipse(brush, null, new Point(rect.Left + radius + 2, rect.Top + rect.Height / 2), radius, radius);
        }
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
