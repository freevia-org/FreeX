using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// A Word-style ruler drawn directly above (horizontal) or to the left of (vertical) the
/// <see cref="DocumentView"/> in Print-Layout mode. It is a passive, read-only piece of view chrome:
/// it draws an inch tick scale across the page, shades the left/right (or top/bottom) margin zones from
/// the document's <see cref="PageSettings"/>, and — for the horizontal ruler — overlays the current
/// paragraph's left / right / first-line indent markers and its tab stops.
///
/// It owns no model state. It mirrors the editor's geometry by reading <see cref="DocumentView.Model"/>'s
/// <see cref="PageSettings"/> and <see cref="DocumentView.ZoomLevel"/>, and redraws when any of those
/// change: the editor raises <see cref="DocumentView.LayoutChanged"/> on page/margin/print-layout changes,
/// <see cref="DocumentView.ZoomChanged"/> on zoom, and the host wires selection changes so the indent/tab
/// markers follow the caret. Coordinates match the editor: the page is sized to
/// <see cref="PageLayout.PageSizeDip"/> and centred, then scaled by the editor's zoom — exactly how
/// <c>DocumentView.ApplyPageChrome</c> places the page on the grey workspace — so the ruler's ticks line up
/// with the text column underneath.
///
/// The horizontal ruler also exposes the backed Word-style editing affordances FreeW can faithfully
/// support today: click the text ruler to add a left tab stop, drag an existing tab mark to move it, or
/// drag the indent markers to update the selected paragraph indents through the editor's undoable model
/// commands.
/// </summary>
public sealed class Ruler : FrameworkElement
{
    private const double DipPerPoint = PageLayout.DipPerPoint;
    private const double DipPerInch = 96.0;

    // A 16-DIP-tall horizontal strip / 16-DIP-wide vertical strip — Word's slim ruler footprint.
    private const double Thickness = 16;

    private static readonly Brush Background = Frozen(Color.FromRgb(0xF3, 0xF3, 0xF3));
    private static readonly Brush PageFill = Frozen(Colors.White);
    private static readonly Brush MarginFill = Frozen(Color.FromRgb(0xD9, 0xD9, 0xD9));
    private static readonly Pen TickPen = FrozenPen(Color.FromRgb(0x80, 0x80, 0x80), 1.0);
    private static readonly Pen PageEdgePen = FrozenPen(Color.FromRgb(0xA0, 0xA0, 0xA0), 1.0);
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly Brush IndentBrush = Frozen(Color.FromRgb(0x2B, 0x57, 0x9A));
    private static readonly Pen IndentPen = FrozenPen(Color.FromRgb(0x2B, 0x57, 0x9A), 1.0);
    private static readonly Pen TabPen = FrozenPen(Color.FromRgb(0x40, 0x40, 0x40), 1.0);
    private const double HitRadius = 7;
    private const double TabGridPt = 6;

    private readonly DocumentView _editor;
    private readonly Orientation _orientation;
    private DragOperation? _drag;

    public Ruler(DocumentView editor, Orientation orientation)
    {
        _editor = editor;
        _orientation = orientation;

        if (orientation == Orientation.Horizontal)
            Height = Thickness;
        else
            Width = Thickness;

        // Redraw on every geometry change the editor can signal: page size / margins / print-layout
        // toggle (LayoutChanged), zoom (ZoomChanged). The host additionally calls Refresh() on selection
        // change so the indent/tab markers follow the caret.
        _editor.LayoutChanged += (_, _) => Refresh();
        _editor.ZoomChanged += (_, _) => Refresh();

        if (orientation == Orientation.Horizontal)
        {
            Focusable = true;
            Cursor = Cursors.Arrow;
        }
    }

    /// <summary>Force a redraw (used by the host on caret/selection changes so the markers follow the caret).</summary>
    public void Refresh() => InvalidateVisual();

    public enum Orientation { Horizontal, Vertical }

    internal enum DragKind { None, LeftIndent, FirstLineIndent, RightIndent, TabStop, NewTabStop }

    private sealed record DragOperation(DragKind Kind, int TabIndex, ParagraphFormatting StartFormatting, Point Start);

    internal sealed record HorizontalMetrics(double ContentStart, double ContentEnd, double Zoom)
    {
        public double PointToContentPt(double x) =>
            Math.Clamp((x - ContentStart) / (DipPerPoint * Zoom), 0, Math.Max(0, (ContentEnd - ContentStart) / (DipPerPoint * Zoom)));

        public double ContentPtToX(double pt) => ContentStart + PageLayout.PointsToDip(pt) * Zoom;
    }

    internal static IReadOnlyList<TabStop> MoveOrAddLeftTabStop(
        IReadOnlyList<TabStop> stops,
        int index,
        double positionPt)
    {
        var snapped = SnapPoint(positionPt);
        var result = stops.ToList();
        var replacement = new TabStop(snapped, TabStopAlignment.Left);
        if (index >= 0 && index < result.Count)
        {
            var current = result[index];
            replacement = current with { PositionPt = snapped };
            result[index] = replacement;
        }
        else
        {
            result.Add(replacement);
        }

        return result
            .Where(s => s.PositionPt >= 0)
            .OrderBy(s => s.PositionPt)
            .ThenBy(s => s.Alignment)
            .ThenBy(s => s.Leader)
            .ToArray();
    }

    internal static double SnapPoint(double pt) =>
        Math.Max(0, Math.Round(pt / TabGridPt, MidpointRounding.AwayFromZero) * TabGridPt);

    internal static ParagraphFormatting IndentsForDrag(ParagraphFormatting start, DragKind kind, double pointPt) => kind switch
    {
        DragKind.LeftIndent => Indentation.SetIndents(start, SnapPoint(pointPt), start.IndentRightPt, start.FirstLineIndentPt),
        DragKind.FirstLineIndent => Indentation.SetIndents(start, start.IndentLeftPt, start.IndentRightPt, SnapPoint(pointPt - start.IndentLeftPt)),
        DragKind.RightIndent => Indentation.SetIndents(start, start.IndentLeftPt, SnapPoint(pointPt), start.FirstLineIndentPt),
        _ => start
    };

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        // Only meaningful in Print-Layout mode (where the page is sized + margins shown). In the plain
        // continuous view we just paint the empty strip background, matching the workspace.
        dc.DrawRectangle(Background, null, new Rect(size));
        if (!_editor.PrintLayoutEnabled)
            return;

        var page = _editor.Model.Page;
        var zoom = _editor.ZoomLevel;

        if (_orientation == Orientation.Horizontal)
            RenderHorizontal(dc, size, page, zoom);
        else
            RenderVertical(dc, size, page, zoom);
    }

    private void RenderHorizontal(DrawingContext dc, Size size, PageSettings page, double zoom)
    {
        var pageWidth = PageLayout.PointsToDip(page.WidthPt) * zoom;
        var left = PageLayout.PointsToDip(page.MarginLeftPt) * zoom;
        var right = PageLayout.PointsToDip(page.MarginRightPt) * zoom;

        // The page is centred over the workspace (DocumentView centres its surface), so the ruler's page
        // band starts at the same centred offset and the ticks line up with the text column below.
        var pageX = Math.Max(0, (size.Width - pageWidth) / 2);
        var bottom = size.Height;

        // Page band (white) with the two margin zones shaded.
        dc.DrawRectangle(PageFill, null, new Rect(pageX, 0, pageWidth, bottom));
        dc.DrawRectangle(MarginFill, null, new Rect(pageX, 0, left, bottom));
        dc.DrawRectangle(MarginFill, null, new Rect(pageX + pageWidth - right, 0, right, bottom));
        dc.DrawLine(PageEdgePen, new Point(pageX, bottom), new Point(pageX + pageWidth, bottom));

        // Inch tick scale across the printable content area, numbered every inch (0 at the left margin).
        var contentStart = pageX + left;
        var contentEnd = pageX + pageWidth - right;
        var step = DipPerInch * zoom;
        DrawTicks(dc, contentStart, contentEnd, step, bottom, horizontal: true);

        DrawHorizontalIndentMarkers(dc, contentStart, contentEnd, zoom, bottom);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_orientation != Orientation.Horizontal || !_editor.PrintLayoutEnabled || TryMetrics(RenderSize, _editor.Model.Page, _editor.ZoomLevel) is not { } metrics)
            return;

        Focus();
        var point = e.GetPosition(this);
        var formatting = _editor.CurrentParagraphFormatting;
        _drag = HitTest(point, metrics, formatting);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_orientation != Orientation.Horizontal || TryMetrics(RenderSize, _editor.Model.Page, _editor.ZoomLevel) is not { } metrics)
            return;

        var point = e.GetPosition(this);
        Cursor = HitTest(point, metrics, _editor.CurrentParagraphFormatting).Kind switch
        {
            DragKind.LeftIndent or DragKind.FirstLineIndent or DragKind.RightIndent or DragKind.TabStop => Cursors.SizeWE,
            _ => Cursors.Arrow
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag is not { } drag || TryMetrics(RenderSize, _editor.Model.Page, _editor.ZoomLevel) is not { } metrics)
        {
            ReleaseMouseCapture();
            _drag = null;
            return;
        }

        var pointPt = metrics.PointToContentPt(e.GetPosition(this).X);
        switch (drag.Kind)
        {
            case DragKind.None:
                break;
            case DragKind.LeftIndent:
            case DragKind.FirstLineIndent:
                var next = IndentsForDrag(drag.StartFormatting, drag.Kind, pointPt);
                _editor.SetParagraphIndents(next.IndentLeftPt, next.IndentRightPt, next.FirstLineIndentPt);
                break;
            case DragKind.RightIndent:
                var right = IndentsForDrag(drag.StartFormatting, drag.Kind, metrics.PointToContentPt(metrics.ContentEnd) - pointPt);
                _editor.SetParagraphIndents(right.IndentLeftPt, right.IndentRightPt, right.FirstLineIndentPt);
                break;
            case DragKind.TabStop:
            case DragKind.NewTabStop:
                var stops = MoveOrAddLeftTabStop(drag.StartFormatting.TabStops, drag.TabIndex, pointPt);
                _editor.SetParagraphTabStops(stops);
                break;
        }

        ReleaseMouseCapture();
        _drag = null;
        Refresh();
        e.Handled = true;
    }

    private DragOperation HitTest(Point point, HorizontalMetrics metrics, ParagraphFormatting f)
    {
        if (point.X < metrics.ContentStart || point.X > metrics.ContentEnd)
            return new DragOperation(DragKind.None, -1, f, point);

        var leftX = metrics.ContentPtToX(f.IndentLeftPt);
        var firstX = metrics.ContentPtToX(f.IndentLeftPt + f.FirstLineIndentPt);
        var rightX = metrics.ContentEnd - PageLayout.PointsToDip(f.IndentRightPt) * metrics.Zoom;

        if (Math.Abs(point.X - firstX) <= HitRadius && point.Y <= Thickness * 0.55)
            return new DragOperation(DragKind.FirstLineIndent, -1, f, point);
        if (Math.Abs(point.X - leftX) <= HitRadius && point.Y >= Thickness * 0.45)
            return new DragOperation(DragKind.LeftIndent, -1, f, point);
        if (Math.Abs(point.X - rightX) <= HitRadius && point.Y >= Thickness * 0.45)
            return new DragOperation(DragKind.RightIndent, -1, f, point);

        for (var i = 0; i < f.TabStops.Count; i++)
        {
            var x = metrics.ContentPtToX(f.TabStops[i].PositionPt);
            if (Math.Abs(point.X - x) <= HitRadius)
                return new DragOperation(DragKind.TabStop, i, f, point);
        }

        return new DragOperation(DragKind.NewTabStop, -1, f, point);
    }

    internal static HorizontalMetrics? TryMetrics(Size size, PageSettings page, double zoom)
    {
        if (size.Width <= 0 || zoom <= 0)
            return null;

        var pageWidth = PageLayout.PointsToDip(page.WidthPt) * zoom;
        var left = PageLayout.PointsToDip(page.MarginLeftPt) * zoom;
        var right = PageLayout.PointsToDip(page.MarginRightPt) * zoom;
        var pageX = Math.Max(0, (size.Width - pageWidth) / 2);
        var contentStart = pageX + left;
        var contentEnd = pageX + pageWidth - right;
        return contentEnd <= contentStart ? null : new HorizontalMetrics(contentStart, contentEnd, zoom);
    }

    private void RenderVertical(DrawingContext dc, Size size, PageSettings page, double zoom)
    {
        var pageHeight = PageLayout.PointsToDip(page.HeightPt) * zoom;
        var top = PageLayout.PointsToDip(page.MarginTopPt) * zoom;
        var bottomMargin = PageLayout.PointsToDip(page.MarginBottomPt) * zoom;

        // The editor pins the page top under the ribbon (it scrolls, but for a static read-only scale we
        // anchor the band at the strip top, mirroring the horizontal ruler's left-anchored content).
        var pageY = 0.0;
        var rightEdge = size.Width;

        dc.DrawRectangle(PageFill, null, new Rect(0, pageY, rightEdge, pageHeight));
        dc.DrawRectangle(MarginFill, null, new Rect(0, pageY, rightEdge, top));
        dc.DrawRectangle(MarginFill, null, new Rect(0, pageY + pageHeight - bottomMargin, rightEdge, bottomMargin));
        dc.DrawLine(PageEdgePen, new Point(rightEdge, pageY), new Point(rightEdge, pageY + pageHeight));

        var contentStart = pageY + top;
        var contentEnd = pageY + pageHeight - bottomMargin;
        var step = DipPerInch * zoom;
        DrawTicks(dc, contentStart, contentEnd, step, rightEdge, horizontal: false);
    }

    // Draw the inch ticks (major lines + numerals) and half-inch minor ticks along an axis. `cross` is the
    // far edge of the strip (height for horizontal, width for vertical); `horizontal` selects the axis.
    private static void DrawTicks(DrawingContext dc, double start, double end, double inch, double cross, bool horizontal)
    {
        if (inch <= 0)
            return;

        var inchIndex = 0;
        for (var pos = start; pos <= end + 0.5; pos += inch / 2.0, inchIndex++)
        {
            var major = inchIndex % 2 == 0;
            var tickLen = major ? cross * 0.55 : cross * 0.3;

            if (horizontal)
                dc.DrawLine(TickPen, new Point(pos, cross - tickLen), new Point(pos, cross));
            else
                dc.DrawLine(TickPen, new Point(cross - tickLen, pos), new Point(cross, pos));

            if (major && inchIndex > 0)
            {
                var number = inchIndex / 2;
                var text = new FormattedText(
                    number.ToString(CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    8.0,
                    LabelBrush,
                    1.0);

                if (horizontal)
                    dc.DrawText(text, new Point(pos - text.Width / 2, 1));
                else
                    dc.DrawText(text, new Point(1, pos - text.Height / 2));
            }
        }
    }

    // Overlay the current paragraph's left / right / first-line indent markers (small triangles) and its
    // tab stops (short tick marks) on the horizontal ruler, read-only. Positions are offsets from the page
    // content edges, scaled by zoom — matching how the editor lays the text column out.
    private void DrawHorizontalIndentMarkers(DrawingContext dc, double contentStart, double contentEnd, double zoom, double bottom)
    {
        var f = _editor.CurrentParagraphFormatting;

        var leftX = contentStart + PageLayout.PointsToDip(f.IndentLeftPt) * zoom;
        var rightX = contentEnd - PageLayout.PointsToDip(f.IndentRightPt) * zoom;
        var firstX = leftX + PageLayout.PointsToDip(f.FirstLineIndentPt) * zoom; // negative = hanging

        // Left-indent marker (up triangle at the bottom) and right-indent marker (up triangle, mirrored).
        DrawUpTriangle(dc, leftX, bottom);
        DrawUpTriangle(dc, rightX, bottom);

        // First-line-indent marker: a down triangle at the top of the strip.
        DrawDownTriangle(dc, firstX, 0);

        foreach (var tab in f.TabStops)
        {
            var x = contentStart + PageLayout.PointsToDip(tab.PositionPt) * zoom;
            if (x < contentStart - 0.5 || x > contentEnd + 0.5)
                continue;
            dc.DrawLine(TabPen, new Point(x, bottom * 0.45), new Point(x, bottom));
        }
    }

    private static void DrawUpTriangle(DrawingContext dc, double x, double bottom)
    {
        const double h = 5;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x, bottom - h), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(x - h, bottom), true, false);
            ctx.LineTo(new Point(x + h, bottom), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(IndentBrush, IndentPen, geometry);
    }

    private static void DrawDownTriangle(DrawingContext dc, double x, double top)
    {
        const double h = 5;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x, top + h), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(x - h, top), true, false);
            ctx.LineTo(new Point(x + h, top), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(IndentBrush, IndentPen, geometry);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
