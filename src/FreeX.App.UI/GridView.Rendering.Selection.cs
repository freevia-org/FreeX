using System.Windows;
using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Returns pixel coords for a range, clamped to viewport boundaries.
    private (double? top, double? left, double? bottom, double? right) GetRangePixels(
        ViewportModel vp,
        GridRange range) =>
        GetRangePixels(vp, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);

    private (double? top, double? left, double? bottom, double? right) GetRangePixels(
        ViewportModel vp,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        double? top = null, left = null, bottom = null, right = null;
        foreach (var row in vp.RowMetrics)
        {
            if (row.Row == range.Start.Row) top    = row.TopOffset + columnHeaderHeight;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + columnHeaderHeight;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + rowHeaderWidth;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + rowHeaderWidth;
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
        QuickAnalysisPreviewLayoutPlanner.CalculatePreviewRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static IReadOnlyList<Rect> CalculateQuickAnalysisDataBarPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        QuickAnalysisPreviewLayoutPlanner.CalculateDataBarPreviewRects(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static IReadOnlyList<Rect> CalculateQuickAnalysisCellPreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        QuickAnalysisPreviewLayoutPlanner.CalculateCellPreviewRects(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static IReadOnlyList<Rect> CalculateQuickAnalysisSparklinePreviewRects(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        QuickAnalysisPreviewLayoutPlanner.CalculateSparklinePreviewRects(viewport, range, rowHeaderWidth, columnHeaderHeight);

    private void RenderQuickAnalysisPreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        var rect = CalculateQuickAnalysisPreviewRect(Viewport, range, rowHeaderWidth, columnHeaderHeight);
        if (rect is null)
            return;

        dc.DrawRectangle(QuickAnalysisPreviewBrush, QuickAnalysisPreviewPen, rect.Value);
        switch (QuickAnalysisPreviewVisual)
        {
            case GridQuickAnalysisPreviewVisualKind.DataBars:
                foreach (var bar in CalculateQuickAnalysisDataBarPreviewRects(Viewport, range, rowHeaderWidth, columnHeaderHeight))
                    dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, bar);
                break;
            case GridQuickAnalysisPreviewVisualKind.ColorScale:
                var index = 0;
                foreach (var cell in CalculateQuickAnalysisCellPreviewRects(Viewport, range, rowHeaderWidth, columnHeaderHeight))
                    dc.DrawRectangle(QuickAnalysisColorScalePreviewBrushes[index++ % QuickAnalysisColorScalePreviewBrushes.Length], null, cell);
                break;
            case GridQuickAnalysisPreviewVisualKind.IconSet:
                var iconIndex = 0;
                foreach (var cell in CalculateQuickAnalysisCellPreviewRects(Viewport, range, rowHeaderWidth, columnHeaderHeight))
                {
                    var radius = Math.Min(cell.Width, cell.Height) / 4;
                    if (radius <= 0)
                        continue;

                    var center = new Point(cell.Left + radius + 2, cell.Top + cell.Height / 2);
                    dc.DrawEllipse(QuickAnalysisIconSetPreviewBrushes[iconIndex++ % QuickAnalysisIconSetPreviewBrushes.Length], null, center, radius, radius);
                }
                break;
            case GridQuickAnalysisPreviewVisualKind.Highlight:
                DrawQuickAnalysisCellOverlays(dc, QuickAnalysisHighlightPreviewBrush, QuickAnalysisHighlightPreviewPen);
                break;
            case GridQuickAnalysisPreviewVisualKind.ClearFormat:
                DrawQuickAnalysisCellOverlays(dc, QuickAnalysisClearFormatPreviewBrush, QuickAnalysisClearFormatPreviewPen);
                break;
            case GridQuickAnalysisPreviewVisualKind.TotalFormula:
                DrawQuickAnalysisCellOverlays(dc, QuickAnalysisTotalPreviewBrush, QuickAnalysisTotalPreviewPen);
                break;
            case GridQuickAnalysisPreviewVisualKind.Table:
                DrawQuickAnalysisCellOverlays(dc, QuickAnalysisTablePreviewBrush, QuickAnalysisTablePreviewPen);
                break;
            case GridQuickAnalysisPreviewVisualKind.LineSparkline:
                DrawQuickAnalysisLineSparklinePreview(dc);
                break;
            case GridQuickAnalysisPreviewVisualKind.ColumnSparkline:
                DrawQuickAnalysisColumnSparklinePreview(dc);
                break;
            case GridQuickAnalysisPreviewVisualKind.WinLossSparkline:
                DrawQuickAnalysisWinLossSparklinePreview(dc);
                break;
            case GridQuickAnalysisPreviewVisualKind.ColumnChart:
                DrawQuickAnalysisColumnChartPreview(dc, rect.Value);
                break;
            case GridQuickAnalysisPreviewVisualKind.LineChart:
                DrawQuickAnalysisLineChartPreview(dc, rect.Value);
                break;
            case GridQuickAnalysisPreviewVisualKind.BarChart:
                DrawQuickAnalysisBarChartPreview(dc, rect.Value);
                break;
            case GridQuickAnalysisPreviewVisualKind.StackedColumnChart:
                DrawQuickAnalysisStackedColumnChartPreview(dc, rect.Value);
                break;
            case GridQuickAnalysisPreviewVisualKind.PieChart:
                DrawQuickAnalysisPieChartPreview(dc, rect.Value);
                break;
            case GridQuickAnalysisPreviewVisualKind.AreaChart:
                DrawQuickAnalysisAreaChartPreview(dc, rect.Value);
                break;
            case GridQuickAnalysisPreviewVisualKind.ScatterChart:
                DrawQuickAnalysisScatterChartPreview(dc, rect.Value);
                break;
        }
    }

    private void DrawQuickAnalysisCellOverlays(DrawingContext dc, Brush brush, Pen pen)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        foreach (var cell in CalculateQuickAnalysisCellPreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight))
            dc.DrawRectangle(brush, pen, cell);
    }

    private void DrawQuickAnalysisLineSparklinePreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        foreach (var sparkline in CalculateQuickAnalysisSparklinePreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight))
        {
            var y1 = sparkline.Bottom;
            var y2 = sparkline.Top;
            var y3 = sparkline.Top + sparkline.Height * 0.65;
            dc.DrawLine(QuickAnalysisSparklinePreviewPen, new Point(sparkline.Left, y1), new Point(sparkline.Left + sparkline.Width * 0.45, y2));
            dc.DrawLine(QuickAnalysisSparklinePreviewPen, new Point(sparkline.Left + sparkline.Width * 0.45, y2), new Point(sparkline.Right, y3));
        }
    }

    private void DrawQuickAnalysisColumnSparklinePreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        foreach (var sparkline in CalculateQuickAnalysisSparklinePreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight))
        {
            var gap = Math.Min(2.0, sparkline.Width / 8);
            var barWidth = Math.Max(1, (sparkline.Width - (2 * gap)) / 3);
            var x = sparkline.Left;
            dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, new Rect(x, sparkline.Top + sparkline.Height * 0.35, barWidth, sparkline.Height * 0.65));
            dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, new Rect(x + barWidth + gap, sparkline.Top, barWidth, sparkline.Height));
            dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, new Rect(x + 2 * (barWidth + gap), sparkline.Top + sparkline.Height * 0.55, barWidth, sparkline.Height * 0.45));
        }
    }

    private void DrawQuickAnalysisWinLossSparklinePreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        foreach (var sparkline in CalculateQuickAnalysisSparklinePreviewRects(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight))
        {
            var gap = Math.Min(2.0, sparkline.Width / 8);
            var barWidth = Math.Max(1, (sparkline.Width - (2 * gap)) / 3);
            var halfHeight = Math.Max(2, sparkline.Height / 2);
            var mid = sparkline.Top + sparkline.Height / 2;
            dc.DrawRectangle(QuickAnalysisWinLossPositiveBrush, null, new Rect(sparkline.Left, sparkline.Top, barWidth, halfHeight));
            dc.DrawRectangle(QuickAnalysisWinLossNegativeBrush, null, new Rect(sparkline.Left + barWidth + gap, mid, barWidth, halfHeight));
            dc.DrawRectangle(QuickAnalysisWinLossPositiveBrush, null, new Rect(sparkline.Left + (2 * (barWidth + gap)), sparkline.Top, barWidth, halfHeight));
        }
    }

    private static void DrawQuickAnalysisColumnChartPreview(DrawingContext dc, Rect previewRect)
    {
        var chartRect = new Rect(
            previewRect.Left + Math.Min(12, previewRect.Width * 0.12),
            previewRect.Top + Math.Min(10, previewRect.Height * 0.18),
            Math.Max(0, previewRect.Width * 0.72),
            Math.Max(0, previewRect.Height * 0.58));
        if (chartRect.Width <= 0 || chartRect.Height <= 0)
            return;

        var baseline = chartRect.Bottom;
        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, baseline), new Point(chartRect.Right, baseline));

        var gap = Math.Min(5.0, chartRect.Width / 14);
        var barWidth = Math.Max(2, (chartRect.Width - (3 * gap)) / 4);
        var heights = new[] { 0.42, 0.76, 0.58, 0.9 };
        for (var i = 0; i < heights.Length; i++)
        {
            var height = chartRect.Height * heights[i];
            var left = chartRect.Left + i * (barWidth + gap);
            dc.DrawRectangle(QuickAnalysisColumnChartPreviewBrush, null, new Rect(left, baseline - height, barWidth, height));
        }
    }

    private static void DrawQuickAnalysisStackedColumnChartPreview(DrawingContext dc, Rect previewRect)
    {
        var chartRect = new Rect(
            previewRect.Left + Math.Min(12, previewRect.Width * 0.12),
            previewRect.Top + Math.Min(10, previewRect.Height * 0.18),
            Math.Max(0, previewRect.Width * 0.72),
            Math.Max(0, previewRect.Height * 0.58));
        if (chartRect.Width <= 0 || chartRect.Height <= 0)
            return;

        var baseline = chartRect.Bottom;
        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, baseline), new Point(chartRect.Right, baseline));

        var gap = Math.Min(5.0, chartRect.Width / 14);
        var barWidth = Math.Max(2, (chartRect.Width - (3 * gap)) / 4);
        var heights = new[] { 0.68, 0.84, 0.58, 0.92 };
        var topSegments = new[] { 0.36, 0.48, 0.42, 0.31 };
        var topBrush = QuickAnalysisHighlightPreviewBrush;
        for (var i = 0; i < heights.Length; i++)
        {
            var totalHeight = chartRect.Height * heights[i];
            var topHeight = totalHeight * topSegments[i];
            var bottomHeight = totalHeight - topHeight;
            var left = chartRect.Left + i * (barWidth + gap);
            dc.DrawRectangle(QuickAnalysisColumnChartPreviewBrush, null, new Rect(left, baseline - bottomHeight, barWidth, bottomHeight));
            dc.DrawRectangle(topBrush, null, new Rect(left, baseline - totalHeight, barWidth, topHeight));
        }
    }

    private static void DrawQuickAnalysisLineChartPreview(DrawingContext dc, Rect previewRect)
    {
        var chartRect = new Rect(
            previewRect.Left + Math.Min(12, previewRect.Width * 0.12),
            previewRect.Top + Math.Min(10, previewRect.Height * 0.18),
            Math.Max(0, previewRect.Width * 0.72),
            Math.Max(0, previewRect.Height * 0.58));
        if (chartRect.Width <= 0 || chartRect.Height <= 0)
            return;

        var baseline = chartRect.Bottom;
        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, baseline), new Point(chartRect.Right, baseline));

        var points = new[]
        {
            new Point(chartRect.Left, chartRect.Top + chartRect.Height * 0.74),
            new Point(chartRect.Left + chartRect.Width * 0.32, chartRect.Top + chartRect.Height * 0.32),
            new Point(chartRect.Left + chartRect.Width * 0.66, chartRect.Top + chartRect.Height * 0.56),
            new Point(chartRect.Right, chartRect.Top + chartRect.Height * 0.18)
        };

        for (var i = 1; i < points.Length; i++)
            dc.DrawLine(QuickAnalysisPreviewPen, points[i - 1], points[i]);

        foreach (var point in points)
            dc.DrawEllipse(QuickAnalysisColumnChartPreviewBrush, null, point, 2.5, 2.5);
    }

    private static void DrawQuickAnalysisBarChartPreview(DrawingContext dc, Rect previewRect)
    {
        var chartRect = new Rect(
            previewRect.Left + Math.Min(12, previewRect.Width * 0.12),
            previewRect.Top + Math.Min(10, previewRect.Height * 0.18),
            Math.Max(0, previewRect.Width * 0.72),
            Math.Max(0, previewRect.Height * 0.58));
        if (chartRect.Width <= 0 || chartRect.Height <= 0)
            return;

        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, chartRect.Top), new Point(chartRect.Left, chartRect.Bottom));

        var gap = Math.Min(4.0, chartRect.Height / 14);
        var barHeight = Math.Max(2, (chartRect.Height - (3 * gap)) / 4);
        var widths = new[] { 0.48, 0.86, 0.64, 0.72 };
        for (var i = 0; i < widths.Length; i++)
        {
            var top = chartRect.Top + i * (barHeight + gap);
            var width = chartRect.Width * widths[i];
            dc.DrawRectangle(QuickAnalysisColumnChartPreviewBrush, null, new Rect(chartRect.Left, top, width, barHeight));
        }
    }

    private static void DrawQuickAnalysisPieChartPreview(DrawingContext dc, Rect previewRect)
    {
        var diameter = Math.Max(0, Math.Min(previewRect.Width, previewRect.Height) * 0.55);
        if (diameter <= 0)
            return;

        var center = new Point(
            previewRect.Left + Math.Min(previewRect.Width * 0.52, previewRect.Width - (diameter / 2)),
            previewRect.Top + previewRect.Height / 2);
        var radius = diameter / 2;
        dc.DrawEllipse(QuickAnalysisColumnChartPreviewBrush, null, center, radius, radius);

        var wedge = new StreamGeometry();
        using (var context = wedge.Open())
        {
            context.BeginFigure(center, isFilled: true, isClosed: true);
            context.LineTo(new Point(center.X, center.Y - radius), isStroked: true, isSmoothJoin: true);
            context.ArcTo(
                new Point(center.X + radius * 0.92, center.Y + radius * 0.39),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: true);
        }

        wedge.Freeze();
        dc.DrawGeometry(QuickAnalysisPieChartAccentBrush, null, wedge);
        dc.DrawEllipse(null, QuickAnalysisColumnChartAxisPen, center, radius, radius);
    }

    private static void DrawQuickAnalysisAreaChartPreview(DrawingContext dc, Rect previewRect)
    {
        var chartRect = new Rect(
            previewRect.Left + Math.Min(12, previewRect.Width * 0.12),
            previewRect.Top + Math.Min(10, previewRect.Height * 0.18),
            Math.Max(0, previewRect.Width * 0.72),
            Math.Max(0, previewRect.Height * 0.58));
        if (chartRect.Width <= 0 || chartRect.Height <= 0)
            return;

        var baseline = chartRect.Bottom;
        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, baseline), new Point(chartRect.Right, baseline));

        var points = new[]
        {
            new Point(chartRect.Left, chartRect.Top + chartRect.Height * 0.78),
            new Point(chartRect.Left + chartRect.Width * 0.28, chartRect.Top + chartRect.Height * 0.36),
            new Point(chartRect.Left + chartRect.Width * 0.62, chartRect.Top + chartRect.Height * 0.52),
            new Point(chartRect.Right, chartRect.Top + chartRect.Height * 0.2)
        };

        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(points[0].X, baseline), isFilled: true, isClosed: true);
            foreach (var point in points)
                context.LineTo(point, isStroked: true, isSmoothJoin: true);
            context.LineTo(new Point(points[^1].X, baseline), isStroked: true, isSmoothJoin: true);
        }

        area.Freeze();
        dc.DrawGeometry(QuickAnalysisAreaChartPreviewBrush, QuickAnalysisPreviewPen, area);
    }

    private static void DrawQuickAnalysisScatterChartPreview(DrawingContext dc, Rect previewRect)
    {
        var chartRect = new Rect(
            previewRect.Left + Math.Min(12, previewRect.Width * 0.12),
            previewRect.Top + Math.Min(10, previewRect.Height * 0.18),
            Math.Max(0, previewRect.Width * 0.72),
            Math.Max(0, previewRect.Height * 0.58));
        if (chartRect.Width <= 0 || chartRect.Height <= 0)
            return;

        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, chartRect.Bottom), new Point(chartRect.Right, chartRect.Bottom));
        dc.DrawLine(QuickAnalysisColumnChartAxisPen, new Point(chartRect.Left, chartRect.Top), new Point(chartRect.Left, chartRect.Bottom));

        var points = new[]
        {
            new Point(chartRect.Left + chartRect.Width * 0.18, chartRect.Top + chartRect.Height * 0.72),
            new Point(chartRect.Left + chartRect.Width * 0.35, chartRect.Top + chartRect.Height * 0.42),
            new Point(chartRect.Left + chartRect.Width * 0.55, chartRect.Top + chartRect.Height * 0.62),
            new Point(chartRect.Left + chartRect.Width * 0.78, chartRect.Top + chartRect.Height * 0.28)
        };

        foreach (var point in points)
            dc.DrawEllipse(QuickAnalysisScatterChartPreviewBrush, null, point, 3, 3);
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

        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        var (top, left, bottom, right) = GetRangePixels(Viewport, range, rowHeaderWidth, columnHeaderHeight);
        var rect = CalculateVisibleSelectionRect(Viewport, range, rowHeaderWidth, columnHeaderHeight);
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
        var (top, left, bottom, right) = GetRangePixels(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
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
