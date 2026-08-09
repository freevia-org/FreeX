using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.FormulaAuditing;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    // Spreadsheet overlays: sparklines, resize/fill/copy adorners, formula traces, and page layout guides.

    private const int FormulaTraceArrowHeadGeometryCacheLimit = 4096;
    private const int FormulaTraceArrowDrawingCacheLimit = 4096;

    private readonly Dictionary<FormulaTraceArrowHeadGeometryKey, Geometry> _formulaTraceArrowHeadGeometryCache = new();
    private readonly Dictionary<FormulaTraceArrowDrawingKey, Drawing> _formulaTraceArrowDrawingCache = new();
    private FormulaTraceArrowLayerCache? _formulaTraceArrowLayerCache;

    private void RenderResizeLine(DrawingContext dc)
    {
        if (_resizeTarget == ResizeTarget.Column)
            dc.DrawLine(ResizeLinePen,
                new Point(_resizeLinePos, 0),
                new Point(_resizeLinePos, GetLogicalViewportHeight()));
        else if (_resizeTarget == ResizeTarget.Row)
            dc.DrawLine(ResizeLinePen,
                new Point(0, _resizeLinePos),
                new Point(GetLogicalViewportWidth(), _resizeLinePos));
    }

    private void RenderAutofillPreview(DrawingContext dc)
    {
        if (!_autofillDragging || !_autofillSourceRange.HasValue || !_autofillTarget.HasValue) return;
        var vp = Viewport;
        if (vp == null) return;

        var src = _autofillSourceRange.Value;
        var tgt = _autofillTarget.Value;

        // Extend selection rect to cover source + fill target
        var previewStart = new CellAddress(src.Start.Sheet,
            Math.Min(src.Start.Row, tgt.Row),
            Math.Min(src.Start.Col, tgt.Col));
        var previewEnd = new CellAddress(src.Start.Sheet,
            Math.Max(src.End.Row, tgt.Row),
            Math.Max(src.End.Col, tgt.Col));

        var layout = CalculateVisibleSelectionLayout(
            vp,
            new GridRange(previewStart, previewEnd),
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);
        if (layout is null) return;

        var previewLayout = layout.Value;
        var rect = previewLayout.Rect;
        if (previewLayout.HasTopEdge) dc.DrawLine(AutofillPreviewPen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
        if (previewLayout.HasBottomEdge) dc.DrawLine(AutofillPreviewPen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
        if (previewLayout.HasLeftEdge) dc.DrawLine(AutofillPreviewPen, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
        if (previewLayout.HasRightEdge) dc.DrawLine(AutofillPreviewPen, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
    }

    private void RenderMarchingAnts(DrawingContext dc)
    {
        var cbRange = ClipboardRange;
        if (cbRange == null || Viewport == null) return;

        // The marquee only belongs on the sheet the range was copied/cut from - Excel hides the
        // marching ants while any other sheet is active. ClipboardRange is not cleared on a sheet
        // switch, so without this check a same-numbered range (e.g. A1:B2) on an unrelated sheet
        // would draw a marquee for cells that were never copied there.
        if (cbRange.Value.Start.Sheet != ActiveSheetId || cbRange.Value.End.Sheet != ActiveSheetId) return;

        var phase = GetMarchingAntsPhase(_marchOffset);
        var blackPen = MarchingAntsBlackPens[phase];
        var overlayPen = ClipboardIsCut ? MarchingAntsCutOverlayPens[phase] : MarchingAntsCopyOverlayPens[phase];

        // A Ctrl+click multi-area copy/cut populates ClipboardRanges with every copied area
        // (R75-render-selection-marquee-4-3): stroke ants around EACH area instead of the single
        // ClipboardRange bounding box, which would otherwise sweep in any untouched gap between them
        // (e.g. column B between copied A:A and C:C).
        if (ClipboardRanges is { Count: > 1 } areas)
        {
            foreach (var area in areas)
            {
                if (area.Start.Sheet != ActiveSheetId || area.End.Sheet != ActiveSheetId) continue;

                var areaRect = CalculateClipboardMarquee(
                    Viewport,
                    area,
                    ActualRowHeaderWidth,
                    EffectiveColHeaderHeight);
                if (areaRect is null) continue;

                dc.DrawRectangle(null, blackPen, areaRect.Value);
                dc.DrawRectangle(null, overlayPen, areaRect.Value);
            }
            return;
        }

        var rect = CalculateClipboardMarquee(
            Viewport,
            cbRange.Value,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);
        if (rect is null) return;

        dc.DrawRectangle(null, blackPen, rect.Value);
        dc.DrawRectangle(null, overlayPen, rect.Value);
    }

    private void RenderFormulaTraceArrows(DrawingContext dc)
    {
        var viewport = Viewport;
        var arrows = FormulaTraceArrows;
        if (viewport is null || arrows is not { Count: > 0 }) return;

        dc.DrawDrawing(GetFormulaTraceArrowLayerDrawing(viewport, arrows, FormulaTraceSheetId));
    }

    public static IReadOnlyList<FormulaTraceArrowLayout> CalculateFormulaTraceArrowLayouts(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId) =>
        FormulaTraceLayoutPlanner.CalculateLayouts(viewport, arrows, sheetId);

    public static CellAddress? HitTestFormulaTraceMarker(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        Point pos) =>
        FormulaTraceLayoutPlanner.HitTestMarker(viewport, arrows, sheetId, pos);

    private void RenderValidationCircles(DrawingContext dc)
    {
        var viewport = Viewport;
        var cells = ValidationCircleCells;
        if (viewport is null || cells is not { Count: > 0 })
            return;

        var lookups = GetRenderMetricLookups(viewport);
        foreach (var cell in cells)
        {
            if (!TryCreateValidationCircleRect(lookups, cell, out var rect))
                continue;

            var radiusX = Math.Max(2.0, rect.Width * 0.38);
            var radiusY = Math.Max(2.0, rect.Height * 0.32);
            var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
            dc.DrawEllipse(null, ValidationCirclePen, center, radiusX, radiusY);
        }
    }

    private bool TryCreateValidationCircleRect(
        RenderMetricLookupCache lookups,
        CellAddress cell,
        out Rect rect)
    {
        if (!lookups.Rows.TryGetValue(cell.Row, out var row) ||
            !lookups.Columns.TryGetValue(cell.Col, out var column))
        {
            rect = Rect.Empty;
            return false;
        }

        rect = new Rect(
            ActualRowHeaderWidth + column.LeftOffset,
            EffectiveColHeaderHeight + row.TopOffset,
            column.Width,
            row.Height);
        return rect.Width > 0 && rect.Height > 0;
    }

    private readonly struct FormulaTraceArrowDrawingConsumer(GridView grid, DrawingContext dc) : IFormulaTraceArrowLayoutConsumer
    {
        public void AcceptLayout(
            LayoutPoint start,
            LayoutPoint end,
            FormulaTraceArrowLayoutKind kind,
            CellAddress? navigationTarget,
            FormulaTraceArrowKind arrowKind) =>
            grid.DrawFormulaTraceArrow(dc, ToWpfPoint(start), ToWpfPoint(end), kind);
    }

    private Drawing GetFormulaTraceArrowLayerDrawing(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId)
    {
        if (_formulaTraceArrowLayerCache is { } cached &&
            ReferenceEquals(cached.Viewport, viewport) &&
            cached.SheetId.Equals(sheetId) &&
            FormulaTraceArrowsEqual(arrows, cached.Arrows))
        {
            return cached.Drawing;
        }

        var drawing = CreateFormulaTraceArrowLayerDrawing(viewport, arrows, sheetId);
        _formulaTraceArrowLayerCache = new FormulaTraceArrowLayerCache(
            viewport,
            sheetId,
            CopyFormulaTraceArrows(arrows),
            drawing);
        return drawing;
    }

    private Drawing CreateFormulaTraceArrowLayerDrawing(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var consumer = new FormulaTraceArrowDrawingConsumer(this, dc);
            FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, sheetId, ref consumer);
        }

        if (drawing.CanFreeze)
            drawing.Freeze();
        return drawing;
    }

    private void DrawFormulaTraceArrow(
        DrawingContext dc,
        Point start,
        Point end,
        FormulaTraceArrowLayoutKind kind)
    {
        if (kind != FormulaTraceArrowLayoutKind.VisibleArrow)
        {
            DrawFormulaTraceMarker(dc, start, kind);
            return;
        }

        dc.DrawDrawing(GetFormulaTraceArrowDrawing(start, end));
    }

    private Drawing GetFormulaTraceArrowDrawing(Point start, Point end)
    {
        var key = new FormulaTraceArrowDrawingKey(start, end);
        if (_formulaTraceArrowDrawingCache.TryGetValue(key, out var cached))
            return cached;

        if (_formulaTraceArrowDrawingCache.Count >= FormulaTraceArrowDrawingCacheLimit)
            _formulaTraceArrowDrawingCache.Clear();

        var drawing = CreateFormulaTraceArrowDrawing(start, end);
        _formulaTraceArrowDrawingCache.Add(key, drawing);
        return drawing;
    }

    private Drawing CreateFormulaTraceArrowDrawing(Point start, Point end)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var style = FormulaTraceOverlayProfiles.Wpf.Style;
            dc.DrawLine(FormulaTraceArrowPen, start, end);
            if (style.SourceMarkerRadius > 0)
            {
                dc.DrawEllipse(
                    FormulaTraceArrowBrush,
                    null,
                    start,
                    style.SourceMarkerRadius,
                    style.SourceMarkerRadius);
            }

            var arrowHead = FormulaTraceOverlayGeometryPlanner.CalculateArrowHead(
                new LayoutPoint(start.X, start.Y),
                new LayoutPoint(end.X, end.Y),
                style);
            if (arrowHead.IsVisible)
                dc.DrawGeometry(FormulaTraceArrowBrush, null, GetFormulaTraceArrowHeadGeometry(start, end, arrowHead));
        }

        if (drawing.CanFreeze)
            drawing.Freeze();
        return drawing;
    }

    private Geometry GetFormulaTraceArrowHeadGeometry(
        Point start,
        Point end,
        FormulaTraceArrowHeadGeometry arrowHead)
    {
        var key = new FormulaTraceArrowHeadGeometryKey(start, end);
        if (_formulaTraceArrowHeadGeometryCache.TryGetValue(key, out var cached))
            return cached;

        if (_formulaTraceArrowHeadGeometryCache.Count >= FormulaTraceArrowHeadGeometryCacheLimit)
            _formulaTraceArrowHeadGeometryCache.Clear();

        var geometry = CreateFormulaTraceArrowHeadGeometry(arrowHead);
        _formulaTraceArrowHeadGeometryCache.Add(key, geometry);
        return geometry;
    }

    private static Geometry CreateFormulaTraceArrowHeadGeometry(FormulaTraceArrowHeadGeometry arrowHead)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(ToWpfPoint(arrowHead.Tip), isFilled: true, isClosed: true);
            ctx.LineTo(ToWpfPoint(arrowHead.Left), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(ToWpfPoint(arrowHead.Right), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private void ClearFormulaTraceArrowHeadGeometryCache()
    {
        _formulaTraceArrowLayerCache = null;
        _formulaTraceArrowHeadGeometryCache.Clear();
        _formulaTraceArrowDrawingCache.Clear();
    }

    private static FormulaTraceArrow[] CopyFormulaTraceArrows(IReadOnlyList<FormulaTraceArrow> arrows)
    {
        var copy = new FormulaTraceArrow[arrows.Count];
        for (var i = 0; i < copy.Length; i++)
            copy[i] = arrows[i];
        return copy;
    }

    private static bool FormulaTraceArrowsEqual(
        IReadOnlyList<FormulaTraceArrow> current,
        IReadOnlyList<FormulaTraceArrow> cached)
    {
        if (current.Count != cached.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (!current[i].Equals(cached[i]))
                return false;
        }

        return true;
    }

    private readonly record struct FormulaTraceArrowHeadGeometryKey(Point Start, Point End);
    private readonly record struct FormulaTraceArrowDrawingKey(Point Start, Point End);
    private sealed record FormulaTraceArrowLayerCache(
        ViewportModel Viewport,
        SheetId SheetId,
        FormulaTraceArrow[] Arrows,
        Drawing Drawing);

    private static void DrawFormulaTraceMarker(DrawingContext dc, Point point, FormulaTraceArrowLayoutKind kind)
    {
        var style = FormulaTraceOverlayProfiles.Wpf.Style;
        var radius = style.EndpointMarkerRadius;
        dc.DrawEllipse(FormulaTraceArrowBrush, null, point, radius, radius);
        if (kind == FormulaTraceArrowLayoutKind.CrossSheetMarker)
            dc.DrawEllipse(null, FormulaTraceArrowPen, point, style.CrossSheetRingRadius, style.CrossSheetRingRadius);
    }

    /// <summary>
    /// Resolves the FULL set of print-area ranges the Page Break Preview / Page Layout overlay should
    /// paginate/un-mask, preferring the multi-area <paramref name="printAreas"/> list over the
    /// single-range <paramref name="printArea"/>/<paramref name="pagePreviewRange"/> fallbacks -- so a
    /// sheet with more than one configured <c>_xlnm.Print_Area</c> region gets every area covered here,
    /// matching both the real print/PDF export (<c>WorkbookExportPrintPlanner</c>) and the Avalonia
    /// shell's overlay, instead of only the first (<see cref="GridRange"/> <paramref name="printArea"/>
    /// only ever carries <c>Sheet.PrintArea</c>, itself just the first of <c>Sheet.PrintAreas</c>).
    /// See R91-render-frozen-print-titles-5-2.
    /// </summary>
    internal static IReadOnlyList<GridRange>? ResolvePageBreakPreviewRanges(
        IReadOnlyList<GridRange>? printAreas, GridRange? printArea, GridRange? pagePreviewRange) =>
        printAreas is { Count: > 0 } areas
            ? areas
            : (printArea ?? pagePreviewRange) is { } single
                ? [single]
                : null;

    private void RenderWorksheetViewOverlay(DrawingContext dc)
    {
        if (Viewport == null) return;

        // Excel draws the manual (solid blue -- see MakePageBreakPen, R91-render-frozen-print-titles-5-1)
        // page-break lines in every view mode, including Normal, once the sheet has at least one
        // manual break - not just in Page Layout / Page Break Preview. The page/margin chrome below
        // is specific to those two views.
        if (WorksheetViewMode == WorksheetViewMode.Normal)
        {
            RenderManualPageBreaks(dc);
            return;
        }

        var logicalWidth = GetLogicalViewportWidth();
        var logicalHeight = GetLogicalViewportHeight();
        IReadOnlyList<GridRange>? previewRanges = ResolvePageBreakPreviewRanges(PrintAreas, PrintArea, PagePreviewRange);
        // The print-area boundary rectangle/margin guides below still only ever draw the FIRST area
        // (RenderPrintAreaBoundary/RenderPageMarginGuides are single-range helpers) -- a narrower,
        // purely cosmetic remnant of the same gap, left for a follow-up since widening those two
        // helpers to a per-area loop is separate scope from the masking/pagination fixed here.
        var previewRange = previewRanges is { Count: > 0 } ? previewRanges[0] : (GridRange?)null;
        var layout = previewRanges is { Count: > 0 }
            ? ToWpfLayout(PageBreakPreviewLayoutPlanner.Calculate(
                Viewport,
                previewRanges,
                RowPageBreaks,
                ColumnPageBreaks,
                PageOrder,
                ScaleToFit,
                PrintTitleRows,
                PrintTitleColumns,
                PaperSize,
                PageOrientation,
                PageMargins,
                ActualRowHeaderWidth,
                EffectiveColHeaderHeight,
                logicalWidth,
                logicalHeight,
                SheetRowHeights,
                SheetDefaultRowHeight,
                SheetColumnWidths,
                SheetDefaultColumnWidth,
                SheetHeaderMargin,
                SheetFooterMargin,
                // Prefer the sheet's real IsRowEffectivelyHidden/IsColEffectivelyHidden predicates
                // (AutoFilter-hidden rows + collapsed outline groups, wired from
                // MainWindow.Viewport.cs) when available, matching the actual print path's
                // pagination (see PrintPreviewPaginationContext). Fall back to the manually-hidden
                // rows/columns bound to this GridView so nothing regresses when the predicates
                // haven't been wired (R15-print-preview-interaction-2).
                SheetIsRowHiddenPredicate ?? (row => HiddenRows?.Contains(row) == true),
                SheetIsColHiddenPredicate ?? (col => HiddenColumns?.Contains(col) == true)))
            : WpfPageBreakPreviewLayout.Empty;

        if (WorksheetViewMode == WorksheetViewMode.PageBreakPreview)
        {
            dc.DrawRectangle(PageBreakPreviewBrush, null,
                new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight,
                    Math.Max(0, logicalWidth - ActualRowHeaderWidth),
                    Math.Max(0, logicalHeight - EffectiveColHeaderHeight)));

            RenderPageBreakPreviewLayout(dc, layout);
        }
        else if (WorksheetViewMode == WorksheetViewMode.PageLayout)
        {
            RenderPageLayoutPages(dc, layout);
        }

        if (previewRange is { } pageRange)
        {
            RenderPrintAreaBoundary(dc, pageRange,
                WorksheetViewMode == WorksheetViewMode.PageLayout ? PageLayoutPen : PageBreakPreviewPagePen,
                drawClippedEdges: WorksheetViewMode != WorksheetViewMode.PageLayout);
            if (WorksheetViewMode == WorksheetViewMode.PageLayout)
                RenderPageMarginGuides(dc, pageRange);
        }

        RenderManualPageBreaks(dc);
    }

    private void RenderPageBreakPreviewLayout(DrawingContext dc, WpfPageBreakPreviewLayout layout)
    {
        foreach (var mask in layout.OutsidePrintAreaMasks)
            dc.DrawRectangle(PageBreakOutsideMaskBrush, null, mask);

        foreach (var page in layout.Pages)
        {
            dc.DrawRectangle(null, PageBreakPreviewPagePen, page.Bounds);
            DrawPageBreakWatermark(dc, page);
        }

        foreach (var line in layout.AutomaticBreakLines)
            dc.DrawLine(PageBreakAutomaticPen, line.Start, line.End);
    }

    private void RenderPageLayoutPages(DrawingContext dc, WpfPageBreakPreviewLayout layout)
    {
        foreach (var page in layout.Pages)
        {
            dc.DrawRectangle(PageLayoutPageSurfaceBrush, null, page.Bounds);
            DrawPageLayoutBoundary(dc, page);
            DrawPageLayoutHeaderFooterCues(dc, page);
        }

        foreach (var line in layout.AutomaticBreakLines)
            dc.DrawLine(PageBreakAutomaticPen, line.Start, line.End);
    }

    private static void DrawPageLayoutBoundary(DrawingContext dc, WpfPageBreakPreviewPageLayout page)
    {
        var bounds = page.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (page.VisibleEdges.Top)
            dc.DrawLine(PageLayoutPen, bounds.TopLeft, bounds.TopRight);
        if (page.VisibleEdges.Bottom)
            dc.DrawLine(PageLayoutPen, bounds.BottomLeft, bounds.BottomRight);
        if (page.VisibleEdges.Left)
            dc.DrawLine(PageLayoutPen, bounds.TopLeft, bounds.BottomLeft);
        if (page.VisibleEdges.Right)
            dc.DrawLine(PageLayoutPen, bounds.TopRight, bounds.BottomRight);
    }

    private void DrawPageBreakWatermark(DrawingContext dc, WpfPageBreakPreviewPageLayout page)
    {
        if (page.Bounds.Width <= 8 || page.Bounds.Height <= 8)
            return;

        var text = new FormattedText(
            $"Page {page.PageNumber}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            PageBreakPreviewLayoutPlanner.CalculateWatermarkFontSize(ToLayoutRect(page.Bounds)),
            PageBreakWatermarkBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        text.SetFontWeight(FontWeights.Bold);

        dc.DrawText(
            text,
            new Point(
                page.Bounds.Left + Math.Max(0, (page.Bounds.Width - text.Width) / 2.0),
                page.Bounds.Top + Math.Max(0, (page.Bounds.Height - text.Height) / 2.0)));
    }

    private void DrawPageLayoutHeaderFooterCues(DrawingContext dc, WpfPageBreakPreviewPageLayout page)
    {
        var pageBounds = page.Bounds;
        if (!ShowRulers || pageBounds.Width <= 24 || pageBounds.Height <= 48)
            return;

        var inset = Math.Min(28.0, Math.Max(12.0, pageBounds.Width * 0.08));
        if (page.VisibleEdges.Top)
        {
            var headerY = pageBounds.Top + Math.Min(28.0, Math.Max(16.0, pageBounds.Height * 0.08));
            if (headerY <= pageBounds.Bottom)
                dc.DrawLine(PageLayoutHeaderFooterCuePen, new Point(pageBounds.Left + inset, headerY), new Point(pageBounds.Right - inset, headerY));
        }

        if (page.VisibleEdges.Bottom)
        {
            var footerY = pageBounds.Bottom - Math.Min(28.0, Math.Max(16.0, pageBounds.Height * 0.08));
            if (footerY >= pageBounds.Top)
                dc.DrawLine(PageLayoutHeaderFooterCuePen, new Point(pageBounds.Left + inset, footerY), new Point(pageBounds.Right - inset, footerY));
        }
    }

    private void RenderPageMarginGuides(DrawingContext dc, GridRange printArea)
    {
        if (!ShowRulers) return;
        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return;

        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.MarginLeft, guide.Value.Top), new Point(guide.Value.MarginLeft, guide.Value.Bottom));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.MarginRight, guide.Value.Top), new Point(guide.Value.MarginRight, guide.Value.Bottom));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.Left, guide.Value.MarginTop), new Point(guide.Value.Right, guide.Value.MarginTop));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.Left, guide.Value.MarginBottom), new Point(guide.Value.Right, guide.Value.MarginBottom));

        var pageBounds = new Rect(
            guide.Value.Left,
            guide.Value.Top,
            Math.Max(0, guide.Value.Right - guide.Value.Left),
            Math.Max(0, guide.Value.Bottom - guide.Value.Top));
        var handles = CalculatePageMarginRulerHandles(pageBounds, PaperSize, PageOrientation, PageMargins);
        DrawPageMarginRulerHandle(dc, handles.Left);
        DrawPageMarginRulerHandle(dc, handles.Right);
        DrawPageMarginRulerHandle(dc, handles.Top);
        DrawPageMarginRulerHandle(dc, handles.Bottom);
    }

    private static void DrawPageMarginRulerHandle(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(PageMarginRulerHandleBrush, PageMarginRulerHandlePen, rect);
    }

    public static PageMarginRulerHandles CalculatePageMarginRulerHandles(
        Rect pageBounds,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        var handles = FreeX.App.Presentation.PageLayout.PageMarginRulerLayoutPlanner.CalculateHandles(
            ToLayoutRect(pageBounds), paperSize, orientation, margins);
        return new PageMarginRulerHandles(
            ToWpfRect(handles.Left),
            ToWpfRect(handles.Right),
            ToWpfRect(handles.Top),
            ToWpfRect(handles.Bottom));
    }

    public static WorksheetPageMarginEdge? HitTestPageMarginRulerHandles(
        PageMarginRulerHandles handles,
        Point pos,
        bool showRulers = true)
    {
        var presHandles = new FreeX.App.Presentation.PageLayout.PageMarginRulerHandles(
            ToLayoutRect(handles.Left),
            ToLayoutRect(handles.Right),
            ToLayoutRect(handles.Top),
            ToLayoutRect(handles.Bottom));
        return FreeX.App.Presentation.PageLayout.PageMarginRulerLayoutPlanner.HitTestHandles(
            presHandles, new LayoutPoint(pos.X, pos.Y), showRulers);
    }

    private void RenderPrintAreaBoundary(DrawingContext dc, GridRange printArea, Pen pen, bool drawClippedEdges)
    {
        if (Viewport == null) return;
        var rows = Viewport.RowMetrics;
        var cols = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;
        if (printArea.End.Row < rows[0].Row || printArea.Start.Row > rows[^1].Row) return;
        if (printArea.End.Col < cols[0].Col || printArea.Start.Col > cols[^1].Col) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, printArea);
        var drawTop = top ?? EffectiveColHeaderHeight;
        var drawLeft = left ?? ActualRowHeaderWidth;
        var drawBottom = bottom ?? GetLogicalViewportHeight();
        var drawRight = right ?? GetLogicalViewportWidth();

        var bounds = new Rect(
            new Point(drawLeft, drawTop),
            new Point(drawRight, drawBottom));
        if (drawClippedEdges)
        {
            dc.DrawRectangle(null, pen, bounds);
            return;
        }

        if (top.HasValue)
            dc.DrawLine(pen, bounds.TopLeft, bounds.TopRight);
        if (bottom.HasValue)
            dc.DrawLine(pen, bounds.BottomLeft, bounds.BottomRight);
        if (left.HasValue)
            dc.DrawLine(pen, bounds.TopLeft, bounds.BottomLeft);
        if (right.HasValue)
            dc.DrawLine(pen, bounds.TopRight, bounds.BottomRight);
    }

    private void RenderManualPageBreaks(DrawingContext dc)
    {
        if (Viewport == null) return;

        // R115-manual-break-title-exclusion: a manual break at/before the first body row (or column)
        // after the print-title range has zero effect on the real printed/exported page layout --
        // PrintLayoutPlanner.BuildManualBreakSet (the same logic every real pagination consumer routes
        // through: printing, PDF/XPS export, print preview) silently drops it. Skip drawing the
        // indicator for those so the on-screen line never implies a split print/export won't produce.
        var effectiveRange = PrintAreas is { Count: > 0 } areas
            ? areas[0]
            : PrintArea ?? PagePreviewRange;
        var isRowHidden = SheetIsRowHiddenPredicate ?? (row => HiddenRows?.Contains(row) == true);
        var isColHidden = SheetIsColHiddenPredicate ?? (col => HiddenColumns?.Contains(col) == true);

        if (RowPageBreaks is { Count: > 0 } rowPageBreaks)
        {
            var rowBreakLookup = GetPageBreakLookup(rowPageBreaks, ref _rowPageBreakLookupCache);
            var rowStart = effectiveRange?.Start.Row ?? 1;
            var rowEnd = effectiveRange?.End.Row ?? CellAddress.MaxRow;
            foreach (var metric in Viewport.RowMetrics)
            {
                if (!rowBreakLookup.Contains(metric.Row))
                    continue;
                if (!PrintLayoutPlanner.IsManualBreakEffective(
                        metric.Row, rowStart, rowEnd, PrintTitleRows, CellAddress.MaxRow, isRowHidden))
                    continue;

                var y = metric.TopOffset + EffectiveColHeaderHeight;
                dc.DrawLine(PageBreakPen, new Point(ActualRowHeaderWidth, y), new Point(GetLogicalViewportWidth(), y));
            }
        }

        if (ColumnPageBreaks is { Count: > 0 } columnPageBreaks)
        {
            var columnBreakLookup = GetPageBreakLookup(columnPageBreaks, ref _columnPageBreakLookupCache);
            var colStart = effectiveRange?.Start.Col ?? 1;
            var colEnd = effectiveRange?.End.Col ?? CellAddress.MaxCol;
            foreach (var metric in Viewport.ColMetrics)
            {
                if (!columnBreakLookup.Contains(metric.Col))
                    continue;
                if (!PrintLayoutPlanner.IsManualBreakEffective(
                        metric.Col, colStart, colEnd, PrintTitleColumns, CellAddress.MaxCol, isColHidden))
                    continue;

                var x = metric.LeftOffset + ActualRowHeaderWidth;
                dc.DrawLine(PageBreakPen, new Point(x, EffectiveColHeaderHeight), new Point(x, GetLogicalViewportHeight()));
            }
        }
    }

    private static IReadOnlySet<uint> GetPageBreakLookup(
        IReadOnlyCollection<uint> pageBreaks,
        ref PageBreakLookupCache? cache)
    {
        if (pageBreaks is IReadOnlySet<uint> set)
            return set;

        var fingerprint = CalculatePageBreakFingerprint(pageBreaks);
        if (cache is not null &&
            ReferenceEquals(cache.Source, pageBreaks) &&
            cache.Count == pageBreaks.Count &&
            cache.Fingerprint == fingerprint)
        {
            return cache.Lookup;
        }

        var lookup = new HashSet<uint>(pageBreaks);
        cache = new PageBreakLookupCache(pageBreaks, pageBreaks.Count, fingerprint, lookup);
        return lookup;
    }

    private static ulong CalculatePageBreakFingerprint(IReadOnlyCollection<uint> pageBreaks)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        foreach (var pageBreak in pageBreaks)
        {
            hash ^= pageBreak;
            hash *= prime;
        }

        return hash;
    }

    private void ClearRowPageBreakLookupCache() => _rowPageBreakLookupCache = null;

    private void ClearColumnPageBreakLookupCache() => _columnPageBreakLookupCache = null;

    // The page-break-preview / page-layout geometry is computed by the portable
    // FreeX.App.Presentation.PageLayout.PageBreakPreviewLayoutPlanner over platform-neutral
    // LayoutRect/LayoutPoint. Convert that result to WPF Rect/Point once at the render boundary so the
    // drawing helpers below stay in WPF types.
    private static WpfPageBreakPreviewLayout ToWpfLayout(PageBreakPreviewLayout layout)
    {
        var masks = new Rect[layout.OutsidePrintAreaMasks.Count];
        for (var i = 0; i < masks.Length; i++)
            masks[i] = ToWpfRect(layout.OutsidePrintAreaMasks[i]);

        var pages = new WpfPageBreakPreviewPageLayout[layout.Pages.Count];
        for (var i = 0; i < pages.Length; i++)
        {
            var page = layout.Pages[i];
            pages[i] = new WpfPageBreakPreviewPageLayout(page.PageNumber, ToWpfRect(page.Bounds), page.VisibleEdges);
        }

        var lines = new WpfPageBreakPreviewBreakLine[layout.AutomaticBreakLines.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = layout.AutomaticBreakLines[i];
            lines[i] = new WpfPageBreakPreviewBreakLine(ToWpfPoint(line.Start), ToWpfPoint(line.End));
        }

        return new WpfPageBreakPreviewLayout(masks, pages, lines);
    }

    private static Point ToWpfPoint(LayoutPoint point) => new(point.X, point.Y);
}

/// <summary>A page-break-preview page rectangle in WPF pixel space, with its on-screen edges.</summary>
internal sealed record WpfPageBreakPreviewPageLayout(
    int PageNumber,
    Rect Bounds,
    PageBreakPreviewPageEdges VisibleEdges);

/// <summary>An automatic page-break line in WPF pixel space.</summary>
internal sealed record WpfPageBreakPreviewBreakLine(Point Start, Point End);

/// <summary>The page-break-preview overlay geometry converted to WPF Rect/Point for rendering.</summary>
internal sealed record WpfPageBreakPreviewLayout(
    IReadOnlyList<Rect> OutsidePrintAreaMasks,
    IReadOnlyList<WpfPageBreakPreviewPageLayout> Pages,
    IReadOnlyList<WpfPageBreakPreviewBreakLine> AutomaticBreakLines)
{
    public static WpfPageBreakPreviewLayout Empty { get; } = new([], [], []);
}

public sealed record PageMarginRulerHandles(
    Rect Left,
    Rect Right,
    Rect Top,
    Rect Bottom);
