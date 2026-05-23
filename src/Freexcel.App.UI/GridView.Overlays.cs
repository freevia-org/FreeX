using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    // Spreadsheet overlays: sparklines, resize/fill/copy adorners, formula traces, and page layout guides.

    private void RenderSparklines(DrawingContext dc)
    {
        if (Sparklines == null || SparklineValues == null || Viewport == null) return;

        var rowLookup = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookup = Viewport.ColMetrics.ToDictionary(c => c.Col);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 1.25);
        var fill = new SolidColorBrush(Color.FromRgb(33, 115, 70));
        var negativeFill = new SolidColorBrush(Color.FromRgb(192, 0, 0));

        foreach (var sparkline in Sparklines)
        {
            if (!rowLookup.TryGetValue(sparkline.Location.Row, out var row) ||
                !colLookup.TryGetValue(sparkline.Location.Col, out var col) ||
                !SparklineValues.TryGetValue(sparkline.Id, out var values) ||
                values.Count == 0)
            {
                continue;
            }

            var rect = new Rect(
                col.LeftOffset + ActualRowHeaderWidth + 3,
                row.TopOffset + EffectiveColHeaderHeight + 3,
                Math.Max(1, col.Width - 6),
                Math.Max(1, row.Height - 6));

            dc.PushClip(new RectangleGeometry(rect));
            if (sparkline.Kind == SparklineKind.Line)
                DrawLineSparkline(dc, values, rect, pen);
            else
                DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, fill, negativeFill);
            dc.Pop();
        }
    }

    private static void DrawLineSparkline(DrawingContext dc, IReadOnlyList<double> values, Rect rect, Pen pen)
    {
        var layout = SparklineLayoutPlanner.CalculateLineLayout(values, rect);
        if (layout.SinglePoint is { } point)
        {
            dc.DrawEllipse(pen.Brush, null, point, 1.5, 1.5);
            return;
        }

        foreach (var segment in layout.Segments)
            dc.DrawLine(pen, segment.Start, segment.End);
    }

    private static void DrawColumnSparkline(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill)
    {
        foreach (var bar in SparklineLayoutPlanner.CalculateColumnLayout(values, rect, winLoss).Bars)
            dc.DrawRectangle(bar.IsNegative ? negativeFill : positiveFill, null, bar.Rect);
    }

    private void RenderResizeLine(DrawingContext dc)
    {
        if (_resizeTarget == ResizeTarget.Column)
            dc.DrawLine(ResizeLinePen,
                new Point(_resizeLinePos, 0),
                new Point(_resizeLinePos, ActualHeight));
        else if (_resizeTarget == ResizeTarget.Row)
            dc.DrawLine(ResizeLinePen,
                new Point(0, _resizeLinePos),
                new Point(ActualWidth, _resizeLinePos));
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

        var (top, left, bottom, right) = GetRangePixels(vp,
            new GridRange(previewStart, previewEnd));
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return;

        var rect = new Rect(left.Value, top.Value, right.Value - left.Value, bottom.Value - top.Value);
        var dashPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 2.0)
            { DashStyle = new DashStyle([4.0, 4.0], 0) };
        dashPen.Freeze();
        dc.DrawRectangle(null, dashPen, rect);
    }

    private void RenderMarchingAnts(DrawingContext dc)
    {
        var cbRange = ClipboardRange;
        if (cbRange == null || Viewport == null) return;

        var rect = CalculateClipboardMarquee(
            Viewport,
            cbRange.Value,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight);
        if (rect is null) return;

        var dashBlack = new DashStyle([4.0, 4.0], _marchOffset);
        var penBlack = new Pen(new SolidColorBrush(Color.FromRgb(0, 0, 0)), 2.5) { DashStyle = dashBlack };
        penBlack.Freeze();
        dc.DrawRectangle(null, penBlack, rect.Value);

        var overlayBrush = ClipboardIsCut
            ? new SolidColorBrush(Color.FromRgb(245, 124, 0))
            : new SolidColorBrush(Color.FromRgb(255, 255, 255));
        overlayBrush.Freeze();
        var dashOverlay = new DashStyle([4.0, 4.0], _marchOffset);
        var penOverlay = new Pen(overlayBrush, 1.5) { DashStyle = dashOverlay };
        penOverlay.Freeze();
        dc.DrawRectangle(null, penOverlay, rect.Value);
    }

    private void RenderFormulaTraceArrows(DrawingContext dc)
    {
        if (Viewport is null || FormulaTraceArrows is not { Count: > 0 }) return;

        foreach (var arrow in CalculateFormulaTraceArrowLayouts(Viewport, FormulaTraceArrows, FormulaTraceSheetId))
            DrawFormulaTraceArrow(dc, arrow);
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

    private static void DrawFormulaTraceArrow(DrawingContext dc, FormulaTraceArrowLayout arrow)
    {
        if (arrow.Kind != FormulaTraceArrowLayoutKind.VisibleArrow)
        {
            DrawFormulaTraceMarker(dc, arrow);
            return;
        }

        dc.DrawLine(FormulaTraceArrowPen, arrow.Start, arrow.End);

        var vector = arrow.Start - arrow.End;
        if (vector.Length <= 0.1) return;
        vector.Normalize();
        var perpendicular = new Vector(-vector.Y, vector.X);
        const double arrowHeadLength = 8;
        const double arrowHeadHalfWidth = 4;
        var p1 = arrow.End + vector * arrowHeadLength + perpendicular * arrowHeadHalfWidth;
        var p2 = arrow.End + vector * arrowHeadLength - perpendicular * arrowHeadHalfWidth;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(arrow.End, isFilled: true, isClosed: true);
            ctx.LineTo(p1, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(p2, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(FormulaTraceArrowBrush, null, geometry);
    }

    private static void DrawFormulaTraceMarker(DrawingContext dc, FormulaTraceArrowLayout arrow)
    {
        const double radius = 5;
        dc.DrawEllipse(FormulaTraceArrowBrush, null, arrow.Start, radius, radius);
        if (arrow.Kind == FormulaTraceArrowLayoutKind.CrossSheetMarker)
            dc.DrawEllipse(null, FormulaTraceArrowPen, arrow.Start, radius + 3, radius + 3);
    }

    private void RenderWorksheetViewOverlay(DrawingContext dc)
    {
        if (Viewport == null || WorksheetViewMode == WorksheetViewMode.Normal) return;

        if (WorksheetViewMode == WorksheetViewMode.PageBreakPreview)
        {
            dc.DrawRectangle(PageBreakPreviewBrush, null,
                new Rect(ActualRowHeaderWidth, EffectiveColHeaderHeight,
                    Math.Max(0, ActualWidth - ActualRowHeaderWidth),
                    Math.Max(0, ActualHeight - EffectiveColHeaderHeight)));
        }

        if (PrintArea is { } printArea)
        {
            RenderPrintAreaBoundary(dc, printArea,
                WorksheetViewMode == WorksheetViewMode.PageLayout ? PageLayoutPen : PageBreakPen);
            if (WorksheetViewMode == WorksheetViewMode.PageLayout)
                RenderPageMarginGuides(dc, printArea);
        }

        RenderManualPageBreaks(dc);
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
        WorksheetPageMargins margins) =>
        PageMarginRulerLayoutPlanner.CalculateHandles(pageBounds, paperSize, orientation, margins);

    public static WorksheetPageMarginEdge? HitTestPageMarginRulerHandles(
        PageMarginRulerHandles handles,
        Point pos,
        bool showRulers = true)
    {
        return PageMarginRulerLayoutPlanner.HitTestHandles(handles, pos, showRulers);
    }

    private void RenderPrintAreaBoundary(DrawingContext dc, GridRange printArea, Pen pen)
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
        var drawBottom = bottom ?? ActualHeight;
        var drawRight = right ?? ActualWidth;

        dc.DrawRectangle(null, pen, new Rect(
            new Point(drawLeft, drawTop),
            new Point(drawRight, drawBottom)));
    }

    private void RenderManualPageBreaks(DrawingContext dc)
    {
        if (Viewport == null) return;

        if (RowPageBreaks is not null)
        {
            foreach (var rowBreak in RowPageBreaks)
            {
                var metric = Viewport.RowMetrics.FirstOrDefault(row => row.Row == rowBreak);
                if (metric is null) continue;
                var y = metric.TopOffset + EffectiveColHeaderHeight;
                dc.DrawLine(PageBreakPen, new Point(ActualRowHeaderWidth, y), new Point(ActualWidth, y));
            }
        }

        if (ColumnPageBreaks is not null)
        {
            foreach (var columnBreak in ColumnPageBreaks)
            {
                var metric = Viewport.ColMetrics.FirstOrDefault(col => col.Col == columnBreak);
                if (metric is null) continue;
                var x = metric.LeftOffset + ActualRowHeaderWidth;
                dc.DrawLine(PageBreakPen, new Point(x, EffectiveColHeaderHeight), new Point(x, ActualHeight));
            }
        }
    }
}

public enum FormulaTraceArrowLayoutKind
{
    VisibleArrow,
    OffscreenMarker,
    CrossSheetMarker
}
public sealed record FormulaTraceArrowLayout(
    Point Start,
    Point End,
    FormulaTraceArrowLayoutKind Kind = FormulaTraceArrowLayoutKind.VisibleArrow,
    CellAddress? NavigationTarget = null);
public sealed record PageMarginRulerHandles(
    Rect Left,
    Rect Right,
    Rect Top,
    Rect Bottom);
