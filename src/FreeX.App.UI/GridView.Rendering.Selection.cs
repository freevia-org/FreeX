using System.Windows;
using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private QuickAnalysisDataBarPreviewGeometryCache? _quickAnalysisDataBarPreviewGeometryCache;

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
            if (top.HasValue && bottom.HasValue)
                break;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + rowHeaderWidth;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + rowHeaderWidth;
            if (left.HasValue && right.HasValue)
                break;
        }
        return (top, left, bottom, right);
    }

    public static Rect? CalculateVisibleSelectionRect(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionRect(viewport, range, rowHeaderWidth, columnHeaderHeight);

    public static SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout? CalculateVisibleSelectionLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        SelectionMarqueeLayoutPlanner.CalculateVisibleSelectionLayout(viewport, range, rowHeaderWidth, columnHeaderHeight);

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
                DrawQuickAnalysisDataBarPreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case GridQuickAnalysisPreviewVisualKind.ColorScale:
                var colorScaleConsumer = new ColorScaleRectConsumer(dc);
                QuickAnalysisPreviewLayoutPlanner.VisitCellPreviewRects(
                    Viewport,
                    range,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    ref colorScaleConsumer);
                break;
            case GridQuickAnalysisPreviewVisualKind.IconSet:
                var iconSetConsumer = new IconSetRectConsumer(dc);
                QuickAnalysisPreviewLayoutPlanner.VisitCellPreviewRects(
                    Viewport,
                    range,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    ref iconSetConsumer);
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

    private void DrawQuickAnalysisDataBarPreview(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var viewport = Viewport!;
        if (_quickAnalysisDataBarPreviewGeometryCache is { } cached &&
            ReferenceEquals(cached.Viewport, viewport) &&
            cached.Range == range &&
            cached.RowHeaderWidth.Equals(rowHeaderWidth) &&
            cached.ColumnHeaderHeight.Equals(columnHeaderHeight))
        {
            dc.DrawGeometry(QuickAnalysisDataBarPreviewBrush, null, cached.Geometry);
            return;
        }

        if (!QuickAnalysisPreviewLayoutPlanner.TryCalculateDataBarPreviewMax(viewport, range, out var max))
            return;

        var lookups = GetRenderCellLookups(viewport);
        var geometry = new StreamGeometry();
        var dataBarCount = 0;
        using (var context = geometry.Open())
        {
            var dataBarConsumer = new QuickAnalysisDataBarGeometryConsumer(context);
            QuickAnalysisPreviewLayoutPlanner.VisitDataBarPreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                lookups.Rows,
                lookups.Columns,
                max,
                ref dataBarConsumer);
            dataBarCount = dataBarConsumer.Count;
        }

        if (dataBarCount == 0)
            return;

        geometry.Freeze();
        _quickAnalysisDataBarPreviewGeometryCache = new(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            geometry);
        dc.DrawGeometry(QuickAnalysisDataBarPreviewBrush, null, geometry);
    }

    private void DrawQuickAnalysisCellOverlays(DrawingContext dc, Brush brush, Pen pen)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var consumer = new FillStrokeRectConsumer(dc, brush, pen);
        QuickAnalysisPreviewLayoutPlanner.VisitCellPreviewRects(
            Viewport,
            range,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ref consumer);
    }

    private void DrawQuickAnalysisLineSparklinePreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var consumer = new LineSparklineRectConsumer(dc);
        QuickAnalysisPreviewLayoutPlanner.VisitSparklinePreviewRects(
            Viewport,
            range,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ref consumer);
    }

    private void DrawQuickAnalysisColumnSparklinePreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var consumer = new ColumnSparklineRectConsumer(dc);
        QuickAnalysisPreviewLayoutPlanner.VisitSparklinePreviewRects(
            Viewport,
            range,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ref consumer);
    }

    private void DrawQuickAnalysisWinLossSparklinePreview(DrawingContext dc)
    {
        if (Viewport == null || QuickAnalysisPreviewRange is not { } range)
            return;

        var consumer = new WinLossSparklineRectConsumer(dc);
        QuickAnalysisPreviewLayoutPlanner.VisitSparklinePreviewRects(
            Viewport,
            range,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ref consumer);
    }

    private readonly struct FillStrokeRectConsumer(DrawingContext dc, Brush brush, Pen pen) : IQuickAnalysisPreviewRectConsumer
    {
        public void Accept(Rect rect) => dc.DrawRectangle(brush, pen, rect);
    }

    private struct QuickAnalysisDataBarGeometryConsumer(StreamGeometryContext context) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext _context = context;

        public int Count { get; private set; }

        public void Accept(Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            _context.BeginFigure(rect.TopLeft, isFilled: true, isClosed: true);
            _context.LineTo(new Point(rect.Right, rect.Top), isStroked: true, isSmoothJoin: false);
            _context.LineTo(rect.BottomRight, isStroked: true, isSmoothJoin: false);
            _context.LineTo(new Point(rect.Left, rect.Bottom), isStroked: true, isSmoothJoin: false);
            Count++;
        }
    }

    private sealed record QuickAnalysisDataBarPreviewGeometryCache(
        ViewportModel Viewport,
        GridRange Range,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        StreamGeometry Geometry);

    private struct ColorScaleRectConsumer(DrawingContext dc) : IQuickAnalysisPreviewRectConsumer
    {
        private int _index;

        public void Accept(Rect rect) =>
            dc.DrawRectangle(
                QuickAnalysisColorScalePreviewBrushes[_index++ % QuickAnalysisColorScalePreviewBrushes.Length],
                null,
                rect);
    }

    private struct IconSetRectConsumer(DrawingContext dc) : IQuickAnalysisPreviewRectConsumer
    {
        private int _index;

        public void Accept(Rect rect)
        {
            var radius = Math.Min(rect.Width, rect.Height) / 4;
            if (radius <= 0)
                return;

            var center = new Point(rect.Left + radius + 2, rect.Top + rect.Height / 2);
            dc.DrawEllipse(
                QuickAnalysisIconSetPreviewBrushes[_index++ % QuickAnalysisIconSetPreviewBrushes.Length],
                null,
                center,
                radius,
                radius);
        }
    }

    private readonly struct LineSparklineRectConsumer(DrawingContext dc) : IQuickAnalysisPreviewRectConsumer
    {
        public void Accept(Rect rect)
        {
            var y1 = rect.Bottom;
            var y2 = rect.Top;
            var y3 = rect.Top + rect.Height * 0.65;
            dc.DrawLine(QuickAnalysisSparklinePreviewPen, new Point(rect.Left, y1), new Point(rect.Left + rect.Width * 0.45, y2));
            dc.DrawLine(QuickAnalysisSparklinePreviewPen, new Point(rect.Left + rect.Width * 0.45, y2), new Point(rect.Right, y3));
        }
    }

    private readonly struct ColumnSparklineRectConsumer(DrawingContext dc) : IQuickAnalysisPreviewRectConsumer
    {
        public void Accept(Rect rect)
        {
            var gap = Math.Min(2.0, rect.Width / 8);
            var barWidth = Math.Max(1, (rect.Width - (2 * gap)) / 3);
            var x = rect.Left;
            dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, new Rect(x, rect.Top + rect.Height * 0.35, barWidth, rect.Height * 0.65));
            dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, new Rect(x + barWidth + gap, rect.Top, barWidth, rect.Height));
            dc.DrawRectangle(QuickAnalysisDataBarPreviewBrush, null, new Rect(x + 2 * (barWidth + gap), rect.Top + rect.Height * 0.55, barWidth, rect.Height * 0.45));
        }
    }

    private readonly struct WinLossSparklineRectConsumer(DrawingContext dc) : IQuickAnalysisPreviewRectConsumer
    {
        public void Accept(Rect rect)
        {
            var gap = Math.Min(2.0, rect.Width / 8);
            var barWidth = Math.Max(1, (rect.Width - (2 * gap)) / 3);
            var halfHeight = Math.Max(2, rect.Height / 2);
            var mid = rect.Top + rect.Height / 2;
            dc.DrawRectangle(QuickAnalysisWinLossPositiveBrush, null, new Rect(rect.Left, rect.Top, barWidth, halfHeight));
            dc.DrawRectangle(QuickAnalysisWinLossNegativeBrush, null, new Rect(rect.Left + barWidth + gap, mid, barWidth, halfHeight));
            dc.DrawRectangle(QuickAnalysisWinLossPositiveBrush, null, new Rect(rect.Left + (2 * (barWidth + gap)), rect.Top, barWidth, halfHeight));
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
        for (var i = 0; i < QuickAnalysisColumnChartHeights.Length; i++)
        {
            var height = chartRect.Height * QuickAnalysisColumnChartHeights[i];
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
        var topBrush = QuickAnalysisHighlightPreviewBrush;
        for (var i = 0; i < QuickAnalysisStackedColumnChartHeights.Length; i++)
        {
            var totalHeight = chartRect.Height * QuickAnalysisStackedColumnChartHeights[i];
            var topHeight = totalHeight * QuickAnalysisStackedColumnChartTopSegments[i];
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

        var previous = CreateQuickAnalysisPreviewPoint(chartRect, QuickAnalysisLineChartPointFactors[0]);
        for (var i = 1; i < QuickAnalysisLineChartPointFactors.Length; i++)
        {
            var point = CreateQuickAnalysisPreviewPoint(chartRect, QuickAnalysisLineChartPointFactors[i]);
            dc.DrawLine(QuickAnalysisPreviewPen, previous, point);
            previous = point;
        }

        foreach (var factor in QuickAnalysisLineChartPointFactors)
        {
            var point = CreateQuickAnalysisPreviewPoint(chartRect, factor);
            dc.DrawEllipse(QuickAnalysisColumnChartPreviewBrush, null, point, 2.5, 2.5);
        }
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
        for (var i = 0; i < QuickAnalysisBarChartWidths.Length; i++)
        {
            var top = chartRect.Top + i * (barHeight + gap);
            var width = chartRect.Width * QuickAnalysisBarChartWidths[i];
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

        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            var firstPoint = CreateQuickAnalysisPreviewPoint(chartRect, QuickAnalysisAreaChartPointFactors[0]);
            var lastPoint = firstPoint;
            context.BeginFigure(new Point(firstPoint.X, baseline), isFilled: true, isClosed: true);
            foreach (var factor in QuickAnalysisAreaChartPointFactors)
            {
                var point = CreateQuickAnalysisPreviewPoint(chartRect, factor);
                context.LineTo(point, isStroked: true, isSmoothJoin: true);
                lastPoint = point;
            }

            context.LineTo(new Point(lastPoint.X, baseline), isStroked: true, isSmoothJoin: true);
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

        foreach (var factor in QuickAnalysisScatterChartPointFactors)
        {
            var point = CreateQuickAnalysisPreviewPoint(chartRect, factor);
            dc.DrawEllipse(QuickAnalysisScatterChartPreviewBrush, null, point, 3, 3);
        }
    }

    private static Point CreateQuickAnalysisPreviewPoint(Rect chartRect, (double X, double Y) factor) =>
        new(chartRect.Left + chartRect.Width * factor.X, chartRect.Top + chartRect.Height * factor.Y);

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
        var layout = CalculateVisibleSelectionLayout(Viewport, range, rowHeaderWidth, columnHeaderHeight);
        if (layout is null) return;

        var selectionLayout = layout.Value;
        var rect = selectionLayout.Rect;
        double drawTop    = rect.Top;
        double drawBottom = rect.Bottom;
        double drawLeft   = rect.Left;
        double drawRight  = rect.Right;

        dc.DrawRectangle(SelectionBrush, null, rect);

        if (selectionLayout.HasTopEdge)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
        if (selectionLayout.HasBottomEdge) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
        if (selectionLayout.HasLeftEdge)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
        if (selectionLayout.HasRightEdge)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));

        if (drawHandle)
            DrawSelectionHandle(dc, selectionLayout.HasRightEdge, selectionLayout.HasBottomEdge, drawRight, drawBottom);
    }

    private void RenderSelectionHandle(DrawingContext dc, GridRange range)
    {
        if (Viewport == null) return;
        var layout = CalculateVisibleSelectionLayout(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (layout is null) return;
        var selectionLayout = layout.Value;

        DrawSelectionHandle(
            dc,
            selectionLayout.HasRightEdge,
            selectionLayout.HasBottomEdge,
            selectionLayout.Rect.Right,
            selectionLayout.Rect.Bottom);
    }

    private static void DrawSelectionHandle(DrawingContext dc, bool hasRightEdge, bool hasBottomEdge, double drawRight, double drawBottom)
    {
        if (!hasRightEdge || !hasBottomEdge)
            return;

        const double handleSize = 6;
        double hx = drawRight - handleSize / 2;
        double hy = drawBottom - handleSize / 2;
        dc.DrawRectangle(Brushes.White, SelectionPen,
            new Rect(hx, hy, handleSize, handleSize));
        dc.DrawRectangle(
            SelectionHandleBrush, null,
            new Rect(hx + 1, hy + 1, handleSize - 2, handleSize - 2));
    }
}
