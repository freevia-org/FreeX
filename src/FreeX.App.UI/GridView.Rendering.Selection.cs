using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private QuickAnalysisDataBarPreviewGeometryCache? _quickAnalysisDataBarPreviewGeometryCache;
    private QuickAnalysisPreviewGeometryCache? _quickAnalysisPreviewGeometryCache;

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
            if (row.Row > range.End.Row)
                break;
            if (row.Row == range.Start.Row) top    = row.TopOffset + columnHeaderHeight;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + columnHeaderHeight;
            if (top.HasValue && bottom.HasValue)
                break;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col > range.End.Col)
                break;
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
            case QuickAnalysisPreviewVisualKind.DataBars:
                DrawQuickAnalysisDataBarPreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case QuickAnalysisPreviewVisualKind.ColorScale:
                DrawQuickAnalysisColorScalePreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case QuickAnalysisPreviewVisualKind.IconSet:
                DrawQuickAnalysisIconSetPreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case QuickAnalysisPreviewVisualKind.Highlight:
                DrawQuickAnalysisCellOverlays(
                    dc,
                    range,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    QuickAnalysisPreviewVisualKind.Highlight,
                    QuickAnalysisHighlightPreviewBrush,
                    QuickAnalysisHighlightPreviewPen);
                break;
            case QuickAnalysisPreviewVisualKind.ClearFormat:
                DrawQuickAnalysisCellOverlays(
                    dc,
                    range,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    QuickAnalysisPreviewVisualKind.ClearFormat,
                    QuickAnalysisClearFormatPreviewBrush,
                    QuickAnalysisClearFormatPreviewPen);
                break;
            case QuickAnalysisPreviewVisualKind.TotalFormula:
                DrawQuickAnalysisCellOverlays(
                    dc,
                    range,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    QuickAnalysisPreviewVisualKind.TotalFormula,
                    QuickAnalysisTotalPreviewBrush,
                    QuickAnalysisTotalPreviewPen);
                break;
            case QuickAnalysisPreviewVisualKind.Table:
                DrawQuickAnalysisCellOverlays(
                    dc,
                    range,
                    rowHeaderWidth,
                    columnHeaderHeight,
                    QuickAnalysisPreviewVisualKind.Table,
                    QuickAnalysisTablePreviewBrush,
                    QuickAnalysisTablePreviewPen);
                break;
            case QuickAnalysisPreviewVisualKind.LineSparkline:
                DrawQuickAnalysisLineSparklinePreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case QuickAnalysisPreviewVisualKind.ColumnSparkline:
                DrawQuickAnalysisColumnSparklinePreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case QuickAnalysisPreviewVisualKind.WinLossSparkline:
                DrawQuickAnalysisWinLossSparklinePreview(dc, range, rowHeaderWidth, columnHeaderHeight);
                break;
            case QuickAnalysisPreviewVisualKind.ColumnChart:
                DrawQuickAnalysisColumnChartPreview(dc, rect.Value);
                break;
            case QuickAnalysisPreviewVisualKind.LineChart:
                DrawQuickAnalysisLineChartPreview(dc, rect.Value);
                break;
            case QuickAnalysisPreviewVisualKind.BarChart:
                DrawQuickAnalysisBarChartPreview(dc, rect.Value);
                break;
            case QuickAnalysisPreviewVisualKind.StackedColumnChart:
                DrawQuickAnalysisStackedColumnChartPreview(dc, rect.Value);
                break;
            case QuickAnalysisPreviewVisualKind.PieChart:
                DrawQuickAnalysisPieChartPreview(dc, rect.Value);
                break;
            case QuickAnalysisPreviewVisualKind.AreaChart:
                DrawQuickAnalysisAreaChartPreview(dc, rect.Value);
                break;
            case QuickAnalysisPreviewVisualKind.ScatterChart:
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
            if (cached.Geometry is { } cachedGeometry)
                dc.DrawGeometry(QuickAnalysisDataBarPreviewBrush, null, cachedGeometry);
            return;
        }

        if (!QuickAnalysisPreviewLayoutPlanner.TryCalculateDataBarPreviewMax(viewport, range, out var max) ||
            max <= 0)
        {
            _quickAnalysisDataBarPreviewGeometryCache = new(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                Geometry: null);
            return;
        }

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
        {
            _quickAnalysisDataBarPreviewGeometryCache = new(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                Geometry: null);
            return;
        }

        geometry.Freeze();
        _quickAnalysisDataBarPreviewGeometryCache = new(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            geometry);
        dc.DrawGeometry(QuickAnalysisDataBarPreviewBrush, null, geometry);
    }

    private void DrawQuickAnalysisColorScalePreview(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var viewport = Viewport!;
        var cache = GetQuickAnalysisPreviewGeometryCache(
                QuickAnalysisPreviewVisualKind.ColorScale,
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight)
            ?? BuildQuickAnalysisColorScalePreviewGeometryCache(viewport, range, rowHeaderWidth, columnHeaderHeight);
        if (cache.BrushGeometries is not { } geometries)
            return;

        for (var i = 0; i < geometries.Length && i < QuickAnalysisColorScalePreviewBrushes.Length; i++)
        {
            if (geometries[i] is { } geometry)
                dc.DrawGeometry(QuickAnalysisColorScalePreviewBrushes[i], null, geometry);
        }
    }

    private QuickAnalysisPreviewGeometryCache BuildQuickAnalysisColorScalePreviewGeometryCache(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var streamGeometries = new StreamGeometry[QuickAnalysisColorScalePreviewBrushes.Length];
        var contexts = new StreamGeometryContext[streamGeometries.Length];
        var counts = new int[streamGeometries.Length];
        for (var i = 0; i < streamGeometries.Length; i++)
        {
            streamGeometries[i] = new StreamGeometry();
            contexts[i] = streamGeometries[i].Open();
        }

        try
        {
            var consumer = new ColorScaleGeometryConsumer(contexts, counts);
            QuickAnalysisPreviewLayoutPlanner.VisitCellPreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                ref consumer);
        }
        finally
        {
            foreach (var context in contexts)
                ((IDisposable)context).Dispose();
        }

        var geometries = new Geometry?[streamGeometries.Length];
        for (var i = 0; i < streamGeometries.Length; i++)
        {
            if (counts[i] == 0)
                continue;

            streamGeometries[i].Freeze();
            geometries[i] = streamGeometries[i];
        }

        return CacheQuickAnalysisPreviewGeometry(
            QuickAnalysisPreviewVisualKind.ColorScale,
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            geometry: null,
            secondaryGeometry: null,
            brushGeometries: geometries);
    }

    private void DrawQuickAnalysisIconSetPreview(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var viewport = Viewport!;
        var cache = GetQuickAnalysisPreviewGeometryCache(
                QuickAnalysisPreviewVisualKind.IconSet,
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight)
            ?? BuildQuickAnalysisIconSetPreviewGeometryCache(viewport, range, rowHeaderWidth, columnHeaderHeight);
        if (cache.BrushGeometries is not { } geometries)
            return;

        for (var i = 0; i < geometries.Length && i < QuickAnalysisIconSetPreviewBrushes.Length; i++)
        {
            if (geometries[i] is { } geometry)
                dc.DrawGeometry(QuickAnalysisIconSetPreviewBrushes[i], null, geometry);
        }
    }

    private QuickAnalysisPreviewGeometryCache BuildQuickAnalysisIconSetPreviewGeometryCache(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var streamGeometries = new StreamGeometry[QuickAnalysisIconSetPreviewBrushes.Length];
        var contexts = new StreamGeometryContext[streamGeometries.Length];
        var counts = new int[streamGeometries.Length];
        for (var i = 0; i < streamGeometries.Length; i++)
        {
            streamGeometries[i] = new StreamGeometry();
            contexts[i] = streamGeometries[i].Open();
        }

        try
        {
            var consumer = new IconSetGeometryConsumer(contexts, counts);
            QuickAnalysisPreviewLayoutPlanner.VisitCellPreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                ref consumer);
        }
        finally
        {
            foreach (var context in contexts)
                ((IDisposable)context).Dispose();
        }

        var geometries = new Geometry?[streamGeometries.Length];
        for (var i = 0; i < streamGeometries.Length; i++)
        {
            if (counts[i] == 0)
                continue;

            streamGeometries[i].Freeze();
            geometries[i] = streamGeometries[i];
        }

        return CacheQuickAnalysisPreviewGeometry(
            QuickAnalysisPreviewVisualKind.IconSet,
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            geometry: null,
            secondaryGeometry: null,
            brushGeometries: geometries);
    }

    private void DrawQuickAnalysisCellOverlays(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        QuickAnalysisPreviewVisualKind visual,
        Brush brush,
        Pen pen)
    {
        var viewport = Viewport!;
        var cache = GetQuickAnalysisPreviewGeometryCache(visual, viewport, range, rowHeaderWidth, columnHeaderHeight)
            ?? BuildQuickAnalysisCellOverlayGeometryCache(visual, viewport, range, rowHeaderWidth, columnHeaderHeight);

        if (cache.Geometry is { } geometry)
            dc.DrawGeometry(brush, pen, geometry);
    }

    private QuickAnalysisPreviewGeometryCache BuildQuickAnalysisCellOverlayGeometryCache(
        QuickAnalysisPreviewVisualKind visual,
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var geometry = new StreamGeometry();
        var consumer = default(QuickAnalysisRectGeometryConsumer);
        using (var context = geometry.Open())
        {
            consumer = new QuickAnalysisRectGeometryConsumer(context);
            QuickAnalysisPreviewLayoutPlanner.VisitCellPreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                ref consumer);
        }

        Geometry? frozenGeometry = null;
        if (consumer.Count > 0)
        {
            geometry.Freeze();
            frozenGeometry = geometry;
        }

        return CacheQuickAnalysisPreviewGeometry(
            visual,
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            frozenGeometry);
    }

    private void DrawQuickAnalysisLineSparklinePreview(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var viewport = Viewport!;
        var cache = GetQuickAnalysisPreviewGeometryCache(
                QuickAnalysisPreviewVisualKind.LineSparkline,
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight)
            ?? BuildQuickAnalysisLineSparklineGeometryCache(viewport, range, rowHeaderWidth, columnHeaderHeight);

        if (cache.Geometry is { } geometry)
            dc.DrawGeometry(null, QuickAnalysisSparklinePreviewPen, geometry);
    }

    private QuickAnalysisPreviewGeometryCache BuildQuickAnalysisLineSparklineGeometryCache(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var geometry = new StreamGeometry();
        var consumer = default(LineSparklineGeometryConsumer);
        using (var context = geometry.Open())
        {
            consumer = new LineSparklineGeometryConsumer(context);
            QuickAnalysisPreviewLayoutPlanner.VisitSparklinePreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                ref consumer);
        }

        Geometry? frozenGeometry = null;
        if (consumer.Count > 0)
        {
            geometry.Freeze();
            frozenGeometry = geometry;
        }

        return CacheQuickAnalysisPreviewGeometry(
            QuickAnalysisPreviewVisualKind.LineSparkline,
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            frozenGeometry);
    }

    private void DrawQuickAnalysisColumnSparklinePreview(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var viewport = Viewport!;
        var cache = GetQuickAnalysisPreviewGeometryCache(
                QuickAnalysisPreviewVisualKind.ColumnSparkline,
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight)
            ?? BuildQuickAnalysisColumnSparklineGeometryCache(viewport, range, rowHeaderWidth, columnHeaderHeight);

        if (cache.Geometry is { } geometry)
            dc.DrawGeometry(QuickAnalysisDataBarPreviewBrush, null, geometry);
    }

    private QuickAnalysisPreviewGeometryCache BuildQuickAnalysisColumnSparklineGeometryCache(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var geometry = new StreamGeometry();
        var consumer = default(ColumnSparklineGeometryConsumer);
        using (var context = geometry.Open())
        {
            consumer = new ColumnSparklineGeometryConsumer(context);
            QuickAnalysisPreviewLayoutPlanner.VisitSparklinePreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                ref consumer);
        }

        Geometry? frozenGeometry = null;
        if (consumer.Count > 0)
        {
            geometry.Freeze();
            frozenGeometry = geometry;
        }

        return CacheQuickAnalysisPreviewGeometry(
            QuickAnalysisPreviewVisualKind.ColumnSparkline,
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            frozenGeometry);
    }

    private void DrawQuickAnalysisWinLossSparklinePreview(
        DrawingContext dc,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var viewport = Viewport!;
        var cache = GetQuickAnalysisPreviewGeometryCache(
                QuickAnalysisPreviewVisualKind.WinLossSparkline,
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight)
            ?? BuildQuickAnalysisWinLossSparklineGeometryCache(viewport, range, rowHeaderWidth, columnHeaderHeight);

        if (cache.Geometry is { } positiveGeometry)
            dc.DrawGeometry(QuickAnalysisWinLossPositiveBrush, null, positiveGeometry);
        if (cache.SecondaryGeometry is { } negativeGeometry)
            dc.DrawGeometry(QuickAnalysisWinLossNegativeBrush, null, negativeGeometry);
    }

    private QuickAnalysisPreviewGeometryCache BuildQuickAnalysisWinLossSparklineGeometryCache(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var positiveGeometry = new StreamGeometry();
        var negativeGeometry = new StreamGeometry();
        var consumer = default(WinLossSparklineGeometryConsumer);
        using (var positiveContext = positiveGeometry.Open())
        using (var negativeContext = negativeGeometry.Open())
        {
            consumer = new WinLossSparklineGeometryConsumer(positiveContext, negativeContext);
            QuickAnalysisPreviewLayoutPlanner.VisitSparklinePreviewRects(
                viewport,
                range,
                rowHeaderWidth,
                columnHeaderHeight,
                ref consumer);
        }

        Geometry? frozenPositiveGeometry = null;
        if (consumer.PositiveCount > 0)
        {
            positiveGeometry.Freeze();
            frozenPositiveGeometry = positiveGeometry;
        }

        Geometry? frozenNegativeGeometry = null;
        if (consumer.NegativeCount > 0)
        {
            negativeGeometry.Freeze();
            frozenNegativeGeometry = negativeGeometry;
        }

        return CacheQuickAnalysisPreviewGeometry(
            QuickAnalysisPreviewVisualKind.WinLossSparkline,
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            frozenPositiveGeometry,
            frozenNegativeGeometry);
    }

    private QuickAnalysisPreviewGeometryCache? GetQuickAnalysisPreviewGeometryCache(
        QuickAnalysisPreviewVisualKind visual,
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (_quickAnalysisPreviewGeometryCache is not { } cached ||
            !ReferenceEquals(cached.Viewport, viewport) ||
            cached.Range != range ||
            !cached.RowHeaderWidth.Equals(rowHeaderWidth) ||
            !cached.ColumnHeaderHeight.Equals(columnHeaderHeight) ||
            cached.Visual != visual)
        {
            return null;
        }

        return cached;
    }

    private QuickAnalysisPreviewGeometryCache CacheQuickAnalysisPreviewGeometry(
        QuickAnalysisPreviewVisualKind visual,
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight,
        Geometry? geometry,
        Geometry? secondaryGeometry = null,
        Geometry?[]? brushGeometries = null)
    {
        var cache = new QuickAnalysisPreviewGeometryCache(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            visual,
            geometry,
            secondaryGeometry,
            brushGeometries);
        _quickAnalysisPreviewGeometryCache = cache;
        return cache;
    }

    private static bool AppendRectangleFigure(StreamGeometryContext context, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        context.BeginFigure(rect.TopLeft, isFilled: true, isClosed: true);
        context.LineTo(new Point(rect.Right, rect.Top), isStroked: true, isSmoothJoin: false);
        context.LineTo(rect.BottomRight, isStroked: true, isSmoothJoin: false);
        context.LineTo(new Point(rect.Left, rect.Bottom), isStroked: true, isSmoothJoin: false);
        return true;
    }

    private static bool AppendEllipseFigure(StreamGeometryContext context, Point center, double radius)
    {
        if (radius <= 0)
            return false;

        var size = new Size(radius, radius);
        context.BeginFigure(new Point(center.X + radius, center.Y), isFilled: true, isClosed: true);
        context.ArcTo(
            new Point(center.X, center.Y + radius),
            size,
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise,
            isStroked: true,
            isSmoothJoin: false);
        context.ArcTo(
            new Point(center.X - radius, center.Y),
            size,
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise,
            isStroked: true,
            isSmoothJoin: false);
        context.ArcTo(
            new Point(center.X, center.Y - radius),
            size,
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise,
            isStroked: true,
            isSmoothJoin: false);
        context.ArcTo(
            new Point(center.X + radius, center.Y),
            size,
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise,
            isStroked: true,
            isSmoothJoin: false);
        return true;
    }

    private struct QuickAnalysisRectGeometryConsumer(StreamGeometryContext context) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext _context = context;

        public int Count { get; private set; }

        public void Accept(Rect rect)
        {
            if (AppendRectangleFigure(_context, rect))
                Count++;
        }
    }

    private struct QuickAnalysisDataBarGeometryConsumer(StreamGeometryContext context) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext _context = context;

        public int Count { get; private set; }

        public void Accept(Rect rect)
        {
            if (AppendRectangleFigure(_context, rect))
                Count++;
        }
    }

    private sealed record QuickAnalysisDataBarPreviewGeometryCache(
        ViewportModel Viewport,
        GridRange Range,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        StreamGeometry? Geometry);

    private sealed record QuickAnalysisPreviewGeometryCache(
        ViewportModel Viewport,
        GridRange Range,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        QuickAnalysisPreviewVisualKind Visual,
        Geometry? Geometry,
        Geometry? SecondaryGeometry,
        Geometry?[]? BrushGeometries);

    private struct ColorScaleGeometryConsumer(
        StreamGeometryContext[] contexts,
        int[] counts) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext[] _contexts = contexts;
        private readonly int[] _counts = counts;
        private int _index;

        public void Accept(Rect rect)
        {
            var index = _index++ % _contexts.Length;
            if (AppendRectangleFigure(_contexts[index], rect))
                _counts[index]++;
        }
    }

    private struct IconSetGeometryConsumer(
        StreamGeometryContext[] contexts,
        int[] counts) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext[] _contexts = contexts;
        private readonly int[] _counts = counts;
        private int _index;

        public void Accept(Rect rect)
        {
            var radius = Math.Min(rect.Width, rect.Height) / 4;
            if (radius <= 0)
                return;

            var index = _index++ % _contexts.Length;
            var center = new Point(rect.Left + radius + 2, rect.Top + rect.Height / 2);
            if (AppendEllipseFigure(_contexts[index], center, radius))
                _counts[index]++;
        }
    }

    private struct LineSparklineGeometryConsumer(StreamGeometryContext context) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext _context = context;

        public int Count { get; private set; }

        public void Accept(Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var y1 = rect.Bottom;
            var y2 = rect.Top;
            var y3 = rect.Top + rect.Height * 0.65;
            _context.BeginFigure(new Point(rect.Left, y1), isFilled: false, isClosed: false);
            _context.LineTo(new Point(rect.Left + rect.Width * 0.45, y2), isStroked: true, isSmoothJoin: false);
            _context.LineTo(new Point(rect.Right, y3), isStroked: true, isSmoothJoin: false);
            Count++;
        }
    }

    private struct ColumnSparklineGeometryConsumer(StreamGeometryContext context) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext _context = context;

        public int Count { get; private set; }

        public void Accept(Rect rect)
        {
            var gap = Math.Min(2.0, rect.Width / 8);
            var barWidth = Math.Max(1, (rect.Width - (2 * gap)) / 3);
            var x = rect.Left;
            if (AppendRectangleFigure(_context, new Rect(x, rect.Top + rect.Height * 0.35, barWidth, rect.Height * 0.65)))
                Count++;
            if (AppendRectangleFigure(_context, new Rect(x + barWidth + gap, rect.Top, barWidth, rect.Height)))
                Count++;
            if (AppendRectangleFigure(_context, new Rect(x + 2 * (barWidth + gap), rect.Top + rect.Height * 0.55, barWidth, rect.Height * 0.45)))
                Count++;
        }
    }

    private struct WinLossSparklineGeometryConsumer(
        StreamGeometryContext positiveContext,
        StreamGeometryContext negativeContext) : IQuickAnalysisPreviewRectConsumer
    {
        private readonly StreamGeometryContext _positiveContext = positiveContext;
        private readonly StreamGeometryContext _negativeContext = negativeContext;

        public int PositiveCount { get; private set; }
        public int NegativeCount { get; private set; }

        public void Accept(Rect rect)
        {
            var gap = Math.Min(2.0, rect.Width / 8);
            var barWidth = Math.Max(1, (rect.Width - (2 * gap)) / 3);
            var halfHeight = Math.Max(2, rect.Height / 2);
            var mid = rect.Top + rect.Height / 2;
            if (AppendRectangleFigure(_positiveContext, new Rect(rect.Left, rect.Top, barWidth, halfHeight)))
                PositiveCount++;
            if (AppendRectangleFigure(_negativeContext, new Rect(rect.Left + barWidth + gap, mid, barWidth, halfHeight)))
                NegativeCount++;
            if (AppendRectangleFigure(_positiveContext, new Rect(rect.Left + (2 * (barWidth + gap)), rect.Top, barWidth, halfHeight)))
                PositiveCount++;
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

            if (EnableFillHandleAndCellDragAndDrop && SelectedRange is { } activeRange)
                RenderSelectionHandle(dc, activeRange);
            RenderActiveCellBox(dc);
            return;
        }

        if (SelectedRange == null) return;

        RenderSelectionRange(dc, SelectedRange.Value, drawHandle: EnableFillHandleAndCellDragAndDrop);
        RenderSelectionMovePreview(dc, SelectedRange.Value);
        RenderActiveCellBox(dc);
    }

    // Excel always draws a dedicated, crisp box tightly around the active cell, independent of
    // the selection outline: after Select All (or any selection whose outer perimeter is off-
    // screen), or when the active cell sits at an interior position within the selected range
    // (e.g. after Tab/Enter wraps to a new row), the active cell still needs its own locator box
    // even though none of the range's own top/bottom/left/right edges are drawn. Draw this box on
    // top of the selection fill whenever the active cell resolves to a visible pixel rect.
    private void RenderActiveCellBox(DrawingContext dc)
    {
        if (Viewport == null) return;
        var activeAddress = ActiveCell ?? SelectedRange?.Start;
        if (activeAddress is not { } address) return;

        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        if (GetActiveCellRect(Viewport, address, rowHeaderWidth, columnHeaderHeight) is { } rect)
            dc.DrawRectangle(null, SelectionPen, rect);
    }

    private void RenderSelectionRange(DrawingContext dc, GridRange range, bool drawHandle)
    {
        if (Viewport == null) return;
        var rows  = Viewport.RowMetrics;
        var cols  = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;

        var rowHeaderWidth = ActualRowHeaderWidth;
        var columnHeaderHeight = EffectiveColHeaderHeight;
        var layout = CalculateSelectionRangeLayout(Viewport, range, rowHeaderWidth, columnHeaderHeight);
        if (layout is null) return;

        var selectionLayout = layout.Value;
        var rect = selectionLayout.Rect;
        double drawTop    = rect.Top;
        double drawBottom = rect.Bottom;
        double drawLeft   = rect.Left;
        double drawRight  = rect.Right;

        // Excel never tints the active cell itself: the selection fill covers the whole
        // range except the active cell, which stays unfilled (only the heavy selection
        // border, drawn below, outlines it). For a single-cell selection the "hole" equals
        // the whole rect, so no tint is drawn at all - matching Excel's plain-border look.
        var activeCellHole = GetActiveCellFillHole(Viewport, range, rowHeaderWidth, columnHeaderHeight);
        if (BuildSelectionFillGeometry(rect, activeCellHole) is { } fillGeometry)
            dc.DrawGeometry(SelectionBrush, null, fillGeometry);

        if (selectionLayout.HasTopEdge)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
        if (selectionLayout.HasBottomEdge) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
        if (selectionLayout.HasLeftEdge)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
        if (selectionLayout.HasRightEdge)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));

        if (drawHandle)
            DrawSelectionHandle(dc, selectionLayout.HasRightEdge, selectionLayout.HasBottomEdge, drawRight, drawBottom);
    }

    // Resolves the pixel rect of the active cell within `range` (or null if there is no active
    // cell inside this range, e.g. a non-active range in a multi-range selection, or the active
    // cell can't currently be resolved to a visible pixel rect).
    private Rect? GetActiveCellFillHole(
        ViewportModel? viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (viewport == null)
            return null;

        var activeAddress = ActiveCell ?? SelectedRange?.Start;
        if (activeAddress is not { } address || !range.Contains(address))
            return null;

        return GetActiveCellRect(viewport, address, rowHeaderWidth, columnHeaderHeight);
    }

    // Resolves the pixel rect of the active cell (or, if the active cell is a merged cell, its
    // full merge footprint - matching Excel, which never tints/splits any part of the active
    // cell's merge) regardless of which selection range it logically belongs to. Shared by the
    // fill-hole punch (GetActiveCellFillHole) and the dedicated active-cell locator box
    // (RenderActiveCellBox). Returns null if the active cell isn't currently visible.
    private Rect? GetActiveCellRect(
        ViewportModel viewport,
        CellAddress address,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        var activeCellRange = FindMerge(address.Row, address.Col) is { } merge
            ? merge
            : new GridRange(address, address);
        var layout = CalculateSelectionRangeLayout(viewport, activeCellRange, rowHeaderWidth, columnHeaderHeight);
        return layout?.Rect;
    }

    // Builds the geometry for the selection tint fill over `rect`, leaving `hole` (the active
    // cell, if any) unfilled - matching Excel, which never tints the active cell within a
    // selection. Returns null when nothing should be filled: either `rect` is degenerate, or
    // `hole` covers the entire `rect` (a single-cell selection, which gets only its border).
    internal static Geometry? BuildSelectionFillGeometry(Rect rect, Rect? hole)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        if (hole is not { } holeRect || holeRect.Width <= 0 || holeRect.Height <= 0 || !rect.IntersectsWith(holeRect))
            return new RectangleGeometry(rect);

        var clippedHole = Rect.Intersect(rect, holeRect);
        if (clippedHole.Width <= 0 || clippedHole.Height <= 0)
            return new RectangleGeometry(rect);

        if (clippedHole == rect)
            return null;

        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            AppendRectangleFigure(context, rect);
            AppendRectangleFigure(context, clippedHole);
        }

        geometry.Freeze();
        return geometry;
    }

    private void RenderSelectionMovePreview(DrawingContext dc, GridRange selectedRange)
    {
        if (!_selectionMoveDragging ||
            _selectionMovePreviewRange is not { } previewRange ||
            previewRange == selectedRange)
        {
            return;
        }

        RenderSelectionRange(dc, previewRange, drawHandle: false);
    }

    private void RenderSelectionHandle(DrawingContext dc, GridRange range)
    {
        if (Viewport == null) return;
        var layout = CalculateSelectionRangeLayout(Viewport, range, ActualRowHeaderWidth, EffectiveColHeaderHeight);
        if (layout is null) return;
        var selectionLayout = layout.Value;

        DrawSelectionHandle(
            dc,
            selectionLayout.HasRightEdge,
            selectionLayout.HasBottomEdge,
            selectionLayout.Rect.Right,
            selectionLayout.Rect.Bottom);
    }

    private SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout? CalculateSelectionRangeLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        // A lone selected cell that is itself part of a merged region (e.g. clicking once on a
        // merged B2:D2 title cell) still arrives here as an anchor-only 1x1 range - selection never
        // merge-expands it (see WorkbookSession.SetSingleSelectedRange). Excel's selection outline
        // and fill-handle always wrap the WHOLE merge, matching GetActiveCellRect's own FindMerge
        // expansion just below, so route a merged single cell through the multi-cell layout path
        // using its full merge footprint instead of sizing the rect from its own 1x1 metrics.
        if (IsSingleCellRange(range) && FindMerge(range.Start.Row, range.Start.Col) is { } merge)
            range = merge;

        if (IsSingleCellRange(range))
            return CalculateVisibleSingleCellSelectionLayout(viewport, range, rowHeaderWidth, columnHeaderHeight);

        return CalculateVisibleSelectionLayout(viewport, range, rowHeaderWidth, columnHeaderHeight);
    }

    private SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout? CalculateVisibleSingleCellSelectionLayout(
        ViewportModel viewport,
        GridRange range,
        double rowHeaderWidth,
        double columnHeaderHeight)
    {
        if (!ViewportGeometryPlanner.TryGetCellBounds(
                viewport,
                range.Start.Row,
                range.Start.Col,
                new ViewportGeometrySettings(
                    rowHeaderWidth,
                    columnHeaderHeight,
                    MetricPlacement: ViewportMetricPlacement.MetricOffsets,
                    SplitColumnHeaderHeight: ColHeaderHeight,
                    SplitRowHeaderWidth: CalculateRowHeaderWidth(viewport)),
                out var bounds) ||
            bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        return new SelectionMarqueeLayoutPlanner.SelectionMarqueeLayout(
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            HasTopEdge: true,
            HasLeftEdge: true,
            HasBottomEdge: true,
            HasRightEdge: true);
    }

    private static bool IsSingleCellRange(GridRange range) =>
        range.Start.Row == range.End.Row &&
        range.Start.Col == range.End.Col;

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
