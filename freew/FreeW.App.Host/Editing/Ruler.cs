using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// A Word-style ruler drawn directly above (horizontal) or to the left of (vertical) the
/// <see cref="DocumentView"/> in Print-Layout mode. It draws an inch tick scale across the page and
/// shades the left/right (or top/bottom) margin zones from
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
/// The horizontal ruler exposes the backed Word-style editing affordances FreeW can faithfully
/// support today: choose a tab-stop type from the selector, click the text ruler to add that tab stop,
/// drag an existing tab mark to move or remove it, or drag the indent markers to update the selected
/// paragraph indents through the editor's undoable model commands.
///
/// The vertical ruler exposes top/bottom page-margin editing: hovering within <c>HitRadius</c> DIP of
/// either margin boundary shows a <see cref="Cursors.SizeNS"/> cursor, and dragging commits the new
/// margin through <see cref="DocumentView.ApplyPageSettings"/> — the same commit + re-render path used
/// by the Page Setup dialog and the Layout ribbon margin presets.
/// </summary>
public sealed class Ruler : FrameworkElement
{
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

    private readonly DocumentView _editor;
    private readonly Orientation _orientation;
    private DragOperation? _drag;
    private TabStopAlignment _selectedTabStopAlignment = TabStopAlignment.Left;

    // Live-drag preview: while a vertical margin drag is in progress, holds the clamped margin value
    // being previewed so RenderVertical can draw the boundary at the drag position before the model
    // is committed on mouse-up. Null when no drag is in progress or the drag is horizontal.
    private double? _dragPreviewMarginPt;

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

        // Scroll sync: repaint the vertical ruler whenever the editor scrolls so the drawn margin
        // boundaries stay aligned with the on-screen page. TextBoxBase.ScrollChanged fires on every
        // scroll step (mouse wheel, drag, keyboard). It is a RoutedEvent so we subscribe via AddHandler.
        if (orientation == Orientation.Vertical)
            _editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => Refresh()));

        Focusable = true;
        Cursor = Cursors.Arrow;
    }

    /// <summary>Force a redraw (used by the host on caret/selection changes so the markers follow the caret).</summary>
    public void Refresh() => InvalidateVisual();

    public enum Orientation { Horizontal, Vertical }

    public TabStopAlignment SelectedTabStopAlignment
    {
        get => _selectedTabStopAlignment;
        set
        {
            if (_selectedTabStopAlignment == value)
                return;

            _selectedTabStopAlignment = value;
            Refresh();
        }
    }

    internal enum DragKind { None, LeftIndent, FirstLineIndent, RightIndent, TabStop, NewTabStop, TopMargin, BottomMargin }

    private sealed record DragOperation(DragKind Kind, int TabIndex, ParagraphFormatting StartFormatting, Point Start, double StartMarginPt = 0);

    internal sealed record HorizontalMetrics(double ContentStart, double ContentEnd, double Zoom)
    {
        private DocumentRulerHorizontalMetrics Shared => new(ContentStart, ContentEnd, Zoom);

        public double PointToContentPt(double x) => Shared.XToContentPoint(x);

        public double ContentPtToX(double pt) => Shared.ContentPointToX(pt);
    }

    // Vertical ruler hit geometry: the two margin boundary Y positions (in ruler/screen DIP) computed
    // from the scroll-adjusted pageY anchor used by RenderVertical, so grab points line up exactly with
    // the drawn shading edges at any scroll position. PageHeight, TopMargin, and BottomMargin are in
    // points; all Y values in DIP. ScrollOffsetDip holds the editor's VerticalOffset at the time the
    // metrics were computed, so callers can detect staleness if needed.
    internal sealed record VerticalMetrics(double TopBoundaryY, double BottomBoundaryY, double PageHeightPt, double Zoom, double ScrollOffsetDip)
    {
        /// <summary>
        /// Convert a Y-delta in DIP (positive = down) into a margin delta in points, preserving sign.
        /// Divides by (DipPerPoint * Zoom) — the exact inverse of PointsToDip(pt) * zoom.
        /// </summary>
        public double DipDeltaToPointsDelta(double dipDelta) =>
            new DocumentRulerVerticalMetrics(TopBoundaryY, BottomBoundaryY, PageHeightPt, Zoom, -ScrollOffsetDip)
                .DipDeltaToPointsDelta(dipDelta);
    }

    /// <summary>
    /// Compute the vertical ruler hit geometry for <paramref name="page"/> at <paramref name="zoom"/>,
    /// offset by the editor's current vertical scroll position (<paramref name="scrollOffsetDip"/>).
    /// Both <see cref="RenderVertical"/> and <see cref="TryVerticalMetrics"/> use the same
    /// <c>pageY = -scrollOffsetDip</c> anchor so the drawn boundary lines and the drag grab points
    /// are always co-located, regardless of scroll position.
    /// </summary>
    internal static VerticalMetrics? TryVerticalMetrics(PageSettings page, double zoom, double scrollOffsetDip = 0)
    {
        var shared = DocumentRulerInteractionPlanner.TryBuildVerticalMetrics(page, zoom, -scrollOffsetDip);
        return shared is null
            ? null
            : new VerticalMetrics(
                shared.TopBoundaryY,
                shared.BottomBoundaryY,
                shared.PageHeightPt,
                shared.Zoom,
                scrollOffsetDip);
    }

    // Clamp a new margin so it is non-negative and leaves at least a 1-pt content strip on the page
    // (top + bottom < pageHeight). Mirrors the implicit guarantee in PageLayout.ContentAreaDip.
    internal static double ClampVerticalMargin(double newMarginPt, double otherMarginPt, double pageHeightPt)
    {
        return DocumentRulerInteractionPlanner.ClampVerticalMargin(newMarginPt, otherMarginPt, pageHeightPt);
    }

    internal static IReadOnlyList<TabStop> MoveOrAddLeftTabStop(
        IReadOnlyList<TabStop> stops,
        int index,
        double positionPt) =>
        DocumentRulerInteractionPlanner.MoveOrAddTabStop(stops, index, positionPt, TabStopAlignment.Left);

    internal static IReadOnlyList<TabStop> MoveOrAddTabStop(
        IReadOnlyList<TabStop> stops,
        int index,
        double positionPt,
        TabStopAlignment alignment)
    {
        return DocumentRulerInteractionPlanner.MoveOrAddTabStop(stops, index, positionPt, alignment);
    }

    internal static IReadOnlyList<TabStop> RemoveTabStop(IReadOnlyList<TabStop> stops, int index)
    {
        return DocumentRulerInteractionPlanner.RemoveTabStop(stops, index);
    }

    internal static bool IsTabStopRemovalDrop(Point point, Size size) =>
        DocumentRulerInteractionPlanner.IsTabStopRemovalDrop(point.Y, size.Height);

    internal static double SnapPoint(double pt) =>
        DocumentRulerInteractionPlanner.SnapPoint(pt);

    internal static ParagraphFormatting IndentsForDrag(ParagraphFormatting start, DragKind kind, double pointPt) =>
        DocumentRulerInteractionPlanner.BuildIndentFormatting(start, (DocumentRulerDragKind)kind, pointPt);

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
        if (!_editor.PrintLayoutEnabled)
            return;

        if (_orientation == Orientation.Horizontal)
        {
            if (TryMetrics(RenderSize, _editor.Model.Page, _editor.ZoomLevel) is not { } metrics)
                return;
            Focus();
            var point = e.GetPosition(this);
            var formatting = _editor.CurrentParagraphFormatting;
            _drag = HitTest(point, metrics, formatting);
            CaptureMouse();
            e.Handled = true;
        }
        else
        {
            // Vertical ruler: start a top- or bottom-margin drag on the boundary hit.
            var page = _editor.Model.Page;
            if (TryVerticalMetrics(page, _editor.ZoomLevel, _editor.VerticalOffset) is not { } vm)
                return;
            var point = e.GetPosition(this);
            var kind = VerticalHitTest(point.Y, vm);
            if (kind == DragKind.None)
                return;
            var startMargin = kind == DragKind.TopMargin ? page.MarginTopPt : page.MarginBottomPt;
            Focus();
            _drag = new DragOperation(kind, -1, _editor.CurrentParagraphFormatting, point, startMargin);
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_orientation == Orientation.Horizontal)
        {
            if (TryMetrics(RenderSize, _editor.Model.Page, _editor.ZoomLevel) is not { } metrics)
                return;
            var point = e.GetPosition(this);
            Cursor = HitTest(point, metrics, _editor.CurrentParagraphFormatting).Kind switch
            {
                DragKind.LeftIndent or DragKind.FirstLineIndent or DragKind.RightIndent or DragKind.TabStop => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
        }
        else
        {
            // Vertical ruler: show SizeNS cursor when hovering near a margin boundary (or while dragging).
            // During an active drag, also update the live preview and repaint so the boundary indicator
            // follows the pointer — matching the visual feedback pattern of the horizontal ruler (which
            // redraws on every mouse-move while mouse is captured). The model is NOT mutated here;
            // the commit via ApplyPageSettings happens on mouse-up as before.
            if (!_editor.PrintLayoutEnabled)
                return;
            var page = _editor.Model.Page;
            if (TryVerticalMetrics(page, _editor.ZoomLevel, _editor.VerticalOffset) is not { } vm)
                return;
            var point = e.GetPosition(this);
            var hoveredKind = _drag is { Kind: DragKind.TopMargin or DragKind.BottomMargin }
                ? _drag.Kind
                : VerticalHitTest(point.Y, vm);
            Cursor = hoveredKind is DragKind.TopMargin or DragKind.BottomMargin
                ? Cursors.SizeNS
                : Cursors.Arrow;

            if (_drag is { Kind: DragKind.TopMargin or DragKind.BottomMargin } vDrag)
            {
                // Compute the preview margin from the current pointer position (same math as mouse-up
                // commit, but clamped and stored for RenderVertical to draw the dashed preview line).
                var sharedMetrics = new DocumentRulerVerticalMetrics(
                    vm.TopBoundaryY, vm.BottomBoundaryY, vm.PageHeightPt, vm.Zoom, -vm.ScrollOffsetDip);
                var otherMargin = vDrag.Kind == DragKind.TopMargin ? page.MarginBottomPt : page.MarginTopPt;
                _dragPreviewMarginPt = DocumentRulerInteractionPlanner.ResolveVerticalMargin(
                    (DocumentRulerDragKind)vDrag.Kind,
                    vDrag.StartMarginPt,
                    point.Y - vDrag.Start.Y,
                    otherMargin,
                    sharedMetrics);
                Refresh();
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_drag is { Kind: DragKind.TopMargin or DragKind.BottomMargin } vDrag)
        {
            // Vertical margin drag: commit the new margin via the backed ApplyPageSettings path.
            // Clear the live-preview value first so RenderVertical reverts to the committed geometry.
            _dragPreviewMarginPt = null;
            var page = _editor.Model.Page;
            if (TryVerticalMetrics(page, _editor.ZoomLevel, _editor.VerticalOffset) is { } vm)
            {
                var sharedMetrics = new DocumentRulerVerticalMetrics(
                    vm.TopBoundaryY, vm.BottomBoundaryY, vm.PageHeightPt, vm.Zoom, -vm.ScrollOffsetDip);
                var releaseDelta = e.GetPosition(this).Y - vDrag.Start.Y;
                if (vDrag.Kind == DragKind.TopMargin)
                {
                    var newTop = DocumentRulerInteractionPlanner.ResolveVerticalMargin(
                        DocumentRulerDragKind.TopMargin,
                        vDrag.StartMarginPt,
                        releaseDelta,
                        page.MarginBottomPt,
                        sharedMetrics);
                    _editor.ApplyPageSettings(p => p.MarginTopPt = newTop);
                }
                else
                {
                    // Bottom margin drag: positive Y-delta (drag down) shrinks the bottom margin.
                    var newBottom = DocumentRulerInteractionPlanner.ResolveVerticalMargin(
                        DocumentRulerDragKind.BottomMargin,
                        vDrag.StartMarginPt,
                        releaseDelta,
                        page.MarginTopPt,
                        sharedMetrics);
                    _editor.ApplyPageSettings(p => p.MarginBottomPt = newBottom);
                }
            }

            ReleaseMouseCapture();
            _drag = null;
            Refresh();
            e.Handled = true;
            return;
        }

        if (_drag is not { } drag || TryMetrics(RenderSize, _editor.Model.Page, _editor.ZoomLevel) is not { } metrics)
        {
            ReleaseMouseCapture();
            _drag = null;
            return;
        }

        var releasePoint = e.GetPosition(this);
        var pointPt = metrics.PointToContentPt(releasePoint.X);
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
            {
                var stops = IsTabStopRemovalDrop(releasePoint, RenderSize)
                    ? RemoveTabStop(drag.StartFormatting.TabStops, drag.TabIndex)
                    : MoveOrAddTabStop(drag.StartFormatting.TabStops, drag.TabIndex, pointPt, SelectedTabStopAlignment);
                _editor.SetParagraphTabStops(stops);
                break;
            }
            case DragKind.NewTabStop:
            {
                if (IsTabStopRemovalDrop(releasePoint, RenderSize))
                    break;
                var stops = MoveOrAddTabStop(drag.StartFormatting.TabStops, drag.TabIndex, pointPt, SelectedTabStopAlignment);
                _editor.SetParagraphTabStops(stops);
                break;
            }
        }

        ReleaseMouseCapture();
        _drag = null;
        Refresh();
        e.Handled = true;
    }

    private DragOperation HitTest(Point point, HorizontalMetrics metrics, ParagraphFormatting f)
    {
        var shared = new DocumentRulerHorizontalMetrics(metrics.ContentStart, metrics.ContentEnd, metrics.Zoom);
        var kind = DocumentRulerInteractionPlanner.HitTestHorizontal(
            new DocumentRulerPoint(point.X, point.Y),
            Thickness,
            shared,
            f,
            out var tabIndex);
        return new DragOperation((DragKind)kind, tabIndex, f, point);
    }

    // Vertical hit-test: return TopMargin if the Y coordinate is within HitRadius of the top-margin
    // boundary, BottomMargin if within HitRadius of the bottom-margin boundary, else None.
    internal static DragKind VerticalHitTest(double y, VerticalMetrics vm)
    {
        var shared = new DocumentRulerVerticalMetrics(
            vm.TopBoundaryY, vm.BottomBoundaryY, vm.PageHeightPt, vm.Zoom, -vm.ScrollOffsetDip);
        return (DragKind)DocumentRulerInteractionPlanner.HitTestVertical(y, shared);
    }

    internal static HorizontalMetrics? TryMetrics(Size size, PageSettings page, double zoom)
    {
        var shared = DocumentRulerInteractionPlanner.TryBuildCenteredHorizontalMetrics(size.Width, page, zoom);
        return shared is null ? null : new HorizontalMetrics(shared.ContentStart, shared.ContentEnd, shared.Zoom);
    }

    // Dashed pen for the live-drag preview line: a thin blue rule that previews the margin boundary
    // position as the user drags, before the commit on mouse-up. Matches the Word blue-indent colour.
    private static readonly Pen DragPreviewPen = MakeDragPreviewPen();

    private static Pen MakeDragPreviewPen()
    {
        // Mirror the FrozenPen helper: freeze the brush first, then assign it to the pen, then freeze
        // the whole pen (which also deep-freezes the DashStyle sub-object).
        var pen = new Pen(Frozen(Color.FromRgb(0x2B, 0x57, 0x9A)), 1.0)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
        };
        pen.Freeze();
        return pen;
    }

    private void RenderVertical(DrawingContext dc, Size size, PageSettings page, double zoom)
    {
        var pageHeight   = PageLayout.PointsToDip(page.HeightPt) * zoom;
        var topMargin    = PageLayout.PointsToDip(page.MarginTopPt) * zoom;
        var bottomMargin = PageLayout.PointsToDip(page.MarginBottomPt) * zoom;

        // Scroll sync: offset the page-top anchor by the editor's vertical scroll so the drawn margin
        // shading and tick scale stay aligned with the on-screen page as the document scrolls.
        // TryVerticalMetrics uses the same anchor so hit-test grab points track the drawn edges exactly.
        var pageY     = -_editor.VerticalOffset;
        var rightEdge = size.Width;

        // Clip the drawing to the visible strip so margin fills don't bleed outside the strip bounds.
        dc.PushClip(new RectangleGeometry(new Rect(size)));

        dc.DrawRectangle(PageFill, null, new Rect(0, pageY, rightEdge, pageHeight));
        dc.DrawRectangle(MarginFill, null, new Rect(0, pageY, rightEdge, topMargin));
        dc.DrawRectangle(MarginFill, null, new Rect(0, pageY + pageHeight - bottomMargin, rightEdge, bottomMargin));
        dc.DrawLine(PageEdgePen, new Point(rightEdge, pageY), new Point(rightEdge, pageY + pageHeight));

        // If a live-drag preview margin is set, overdraw a dashed horizontal rule at the preview
        // boundary position so the user sees continuous feedback as they drag — no model mutation yet.
        if (_dragPreviewMarginPt is { } previewPt && _drag is { Kind: DragKind.TopMargin or DragKind.BottomMargin } activeDrag)
        {
            var previewDip = PageLayout.PointsToDip(previewPt) * zoom;
            var previewY   = activeDrag.Kind == DragKind.TopMargin
                ? pageY + previewDip                               // top: offset from page top
                : pageY + pageHeight - previewDip;                 // bottom: offset from page bottom
            dc.DrawLine(DragPreviewPen, new Point(0, previewY), new Point(rightEdge, previewY));
        }

        var contentStart = pageY + topMargin;
        var contentEnd   = pageY + pageHeight - bottomMargin;
        var step = DipPerInch * zoom;
        DrawTicks(dc, contentStart, contentEnd, step, rightEdge, horizontal: false);

        dc.Pop(); // end clip
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
            DrawTabStopMarker(dc, tab.Alignment, x, bottom);
        }
    }

    private static void DrawTabStopMarker(DrawingContext dc, TabStopAlignment alignment, double x, double bottom)
    {
        var top = bottom * 0.45;
        dc.DrawLine(TabPen, new Point(x, top), new Point(x, bottom));
        switch (alignment)
        {
            case TabStopAlignment.Center:
                dc.DrawLine(TabPen, new Point(x - 4, top), new Point(x + 4, top));
                break;
            case TabStopAlignment.Right:
                dc.DrawLine(TabPen, new Point(x - 8, top), new Point(x, top));
                break;
            case TabStopAlignment.Decimal:
                dc.DrawLine(TabPen, new Point(x, top), new Point(x + 7, top));
                dc.DrawEllipse(null, TabPen, new Point(x + 5, bottom - 3), 1.2, 1.2);
                break;
            default:
                dc.DrawLine(TabPen, new Point(x, top), new Point(x + 8, top));
                break;
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
