using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// An Avalonia <see cref="Control"/> rendered as a transparent overlay above <see cref="SlideCanvas"/>
/// that draws:
/// <list type="bullet">
///   <item>Selection rectangles around selected shapes.</item>
///   <item>8 resize handles (N, NE, E, SE, S, SW, W, NW) and one rotate handle above.</item>
///   <item>Live-preview rectangle during drag gestures.</item>
///   <item>Marquee selection rectangle.</item>
///   <item>Snap guide lines (thin magenta, PowerPoint-style).</item>
/// </list>
///
/// The adorner does NOT own interaction logic — that lives in <see cref="AvaloniaCanvasGestureHandler"/>.
/// Call the <c>Update*</c> methods from the gesture handler to trigger redraws.
/// </summary>
public sealed partial class SelectionAdornerLayer : Control, ISelectionAdornerSurface<Rect, Point>
{
    // ── Handle appearance ───────────────────────────────────────────────────────

    private const double HandleSize         = SelectionAdornerGeometry.HandleSize;
    private const double RotateHandleRadius = SelectionAdornerGeometry.RotateHandleRadius;

    private static readonly IPen SelectionPen;
    private static readonly IPen PreviewPen;
    private static readonly IBrush HandleFill;
    private static readonly IPen HandleBorder;
    private static readonly IBrush RotateHandleFill;
    private static readonly IPen RotateHandleBorder;
    private static readonly IPen MarqueePen;
    private static readonly IBrush MarqueeFill;
    private static readonly IPen SnapGuidePen;
    private static readonly IBrush EditPointFill;
    private static readonly IPen EditPointBorder;
    private static readonly IPen EditPointPreviewPen;

    /// <summary>The PowerPoint-style selection accent, shared by the pens and the rotate-handle line.</summary>
    private static readonly ImmutableSolidColorBrush AccentBrush =
        new(Color.FromRgb(0x21, 0x96, 0xF3));

    // Every one of these is IMMUTABLE on purpose. Pen, SolidColorBrush and DashStyle are all
    // AvaloniaObjects, so they carry thread affinity to whichever thread first ran this static
    // constructor -- and the compositor reads them again on the render thread. Holding mutable ones
    // in static fields throws "The calling thread cannot access this object because a different
    // thread owns it" out of the renderer, intermittently and depending on which test or window
    // touched the class first. The Immutable* variants have no affinity.
    static SelectionAdornerLayer()
    {
        // Copied from Avalonia's own DashStyle.Dash rather than hardcoded, so the marquee keeps its
        // exact pattern; the copy is what reaches the compositor.
        var dash = new ImmutableDashStyle(DashStyle.Dash.Dashes, DashStyle.Dash.Offset);

        SelectionPen       = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 1.5);
        PreviewPen         = new ImmutablePen(
                                 new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0x21, 0x96, 0xF3)),
                                 1.5,
                                 new ImmutableDashStyle([4.0, 2.0], 0));
        HandleFill         = new ImmutableSolidColorBrush(Colors.White);
        HandleBorder       = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 1.5);
        RotateHandleFill   = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        RotateHandleBorder = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 1.0);
        MarqueePen         = new ImmutablePen(
                                 new ImmutableSolidColorBrush(Color.FromArgb(0xBB, 0x21, 0x96, 0xF3)),
                                 1.0,
                                 dash);
        MarqueeFill        = new ImmutableSolidColorBrush(Color.FromArgb(0x22, 0x21, 0x96, 0xF3));
        SnapGuidePen       = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0xE9, 0x1E, 0x63)), 1.0);
        EditPointFill      = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        EditPointBorder    = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 1.0);
        EditPointPreviewPen = new ImmutablePen(
                                 new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                                 1.5,
                                 dash);
    }

    // ── State owned / updated by AvaloniaCanvasGestureHandler ──────────────────

    private readonly SelectionAdornerController<Rect, Point> _controller;
    SelectionAdornerController<Rect, Point> ISelectionAdornerSurface<Rect, Point>.Controller => _controller;

    private SelectionAdornerState State => _controller.State;

    private SelectionAdornerState _state => State;

    // ── Construction ───────────────────────────────────────────────────────────

    public SelectionAdornerLayer()
    {
        IsHitTestVisible = false; // pointer events go to canvas below
        _controller = new(ToSelectionAdornerRect, ToCanvasPoint, InvalidateVisual);
    }

    // ── Public update API ───────────────────────────────────────────────────────

    // ── Rendering ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);

        // Snap guide lines (drawn first — behind selection rects).
        if (State.SnapGuides is { Count: > 0 } snapGuides)
            DrawSnapGuides(dc, snapGuides, State.SnapTransform);

        // Marquee
        if (State.MarqueeRect is { } marqueeRect)
            dc.DrawRectangle(MarqueeFill, MarqueePen, ToAvaloniaRect(marqueeRect));

        // Multi-selection uses one group box for handles; individual boxes remain visible
        // without handles so the selected members are still discoverable.
        if (State.Selections.Count == 1)
        {
            DrawSelectionRect(
                dc,
                ToAvaloniaRect(State.Selections[0].ScreenRect),
                drawHandles: true);
        }
        else
        {
            foreach (var selection in State.Selections)
                DrawSelectionRect(dc, ToAvaloniaRect(selection.ScreenRect), drawHandles: false);

            if (SelectionBounds is { } groupBounds)
                DrawSelectionRect(dc, groupBounds, drawHandles: true);
        }

        DrawGeometryHandles(dc);

        foreach (var preview in State.TransformPreview)
            DrawPreviewRect(dc, ToAvaloniaRect(preview.ScreenBounds), preview.RotationDeg);

        // Preview
        if (State.PreviewRect is { } previewRect)
            DrawPreviewRect(dc, ToAvaloniaRect(previewRect), State.PreviewRotationDeg);
    }

    private static void DrawSnapGuides(DrawingContext dc, IReadOnlyList<SnapGuideLine> guides, SlideTransformCore xf)
    {
        // We don't know the actual bounds here — use very large values to span the screen.
        const double Span = 5000;
        foreach (var g in guides)
        {
            if (g.IsHorizontal)
            {
                double sy = SlideCanvasGeometryPlanner.SnapGuideToScreenPosition(g, xf);
                dc.DrawLine(SnapGuidePen, new Point(-Span, sy), new Point(Span, sy));
            }
            else
            {
                double sx = SlideCanvasGeometryPlanner.SnapGuideToScreenPosition(g, xf);
                dc.DrawLine(SnapGuidePen, new Point(sx, -Span), new Point(sx, Span));
            }
        }
    }

    private static void DrawSelectionRect(DrawingContext dc, Rect rect, bool drawHandles)
    {
        dc.DrawRectangle(null, SelectionPen, rect);
        if (drawHandles)
            DrawHandles(dc, rect);
    }

    private static void DrawPreviewRect(DrawingContext dc, Rect rect, double rotDeg)
    {
        if (rotDeg != 0)
        {
            double cx = rect.Left + rect.Width  / 2;
            double cy = rect.Top  + rect.Height / 2;
            var xform = Matrix.CreateRotation(rotDeg * Math.PI / 180.0);
            // Rotate around center
            using var _ = dc.PushTransform(
                Matrix.CreateTranslation(-cx, -cy)
                * xform
                * Matrix.CreateTranslation(cx, cy));
            dc.DrawRectangle(null, PreviewPen, rect);
        }
        else
        {
            dc.DrawRectangle(null, PreviewPen, rect);
        }
    }

    private static Rect ToAvaloniaRect(SlideScreenRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect ToAvaloniaRect(SelectionAdornerRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static void DrawHandles(DrawingContext dc, Rect rect)
    {
        double h = HandleSize;
        double r = h / 2;

        // 8 resize handles
        foreach (var c in GetHandleCenters(rect))
            dc.DrawRectangle(HandleFill, HandleBorder, new Rect(c.X - r, c.Y - r, h, h));

        // Rotate handle (circle above N)
        var rotateCenter = GetRotateHandleCenter(rect);
        double topCenterX = rotateCenter.X;
        double rotY       = rotateCenter.Y;
        // Built from the shared accent brush rather than casting HandleBorder to the concrete Pen:
        // the shared pens are ImmutablePens so they can live in static fields without thread affinity.
        dc.DrawLine(new ImmutablePen(AccentBrush, 1.0),
            new Point(topCenterX, rect.Top),
            new Point(topCenterX, rotY));
        dc.DrawEllipse(RotateHandleFill, RotateHandleBorder,
            new Point(topCenterX, rotY), RotateHandleRadius, RotateHandleRadius);
    }

    private void DrawGeometryHandles(DrawingContext dc)
    {
        const double radius = 5.0;
        foreach (var handle in State.GeometryHandles)
            dc.DrawEllipse(
                EditPointFill,
                EditPointBorder,
                ToAvaloniaPoint(handle.ScreenPosition),
                radius,
                radius);

        if (State.GeometryPreview is { } preview)
            dc.DrawEllipse(
                null,
                EditPointPreviewPen,
                ToAvaloniaPoint(preview.ScreenPosition),
                radius + 2,
                radius + 2);
    }

    // ── Geometry helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns centers of the 8 resize handles for a given selection rect.
    /// Order: N, NE, E, SE, S, SW, W, NW.
    /// </summary>
    internal static Point[] GetHandleCenters(Rect rect)
    {
        return SelectionAdornerGeometry.GetHandleCenters(ToSelectionAdornerRect(rect))
            .Select(ToAvaloniaPoint)
            .ToArray();
    }

    /// <summary>Returns the rotate handle center for a given selection rect.</summary>
    internal static Point GetRotateHandleCenter(Rect rect)
        => ToAvaloniaPoint(
            SelectionAdornerGeometry.GetRotateHandleCenter(ToSelectionAdornerRect(rect)));

    // ── Hit-test helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns which part of the selection a screen-space point hits (for a single selected shape).
    /// </summary>
    public CanvasGestureHandleKind HitTestHandle(Rect selectionRect, Point screenPt)
    {
        return SelectionAdornerGeometry.HitTestHandle(
            ToSelectionAdornerRect(selectionRect),
            ToCanvasPoint(screenPt));
    }

    /// <summary>Returns the preset edit-point name under a screen-space pointer, if any.</summary>
    public string? HitTestGeometryHandle(Point screenPt)
    {
        return SelectionAdornerGeometry.HitTestGeometryHandle(
            State.GeometryHandles,
            ToCanvasPoint(screenPt));
    }

    private static SelectionAdornerRect ToSelectionAdornerRect(Rect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Point ToAvaloniaPoint(CanvasGesturePoint point)
        => new(point.X, point.Y);

    private static CanvasGesturePoint ToCanvasPoint(Point point)
        => new(point.X, point.Y);

    /// <summary>Selection rects accessible to the gesture handler for external queries.</summary>
    public IReadOnlyList<(uint id, Rect screenRect)> SelectionRects =>
        State.Selections
            .Select(selection => (selection.ShapeId, ToAvaloniaRect(selection.ScreenRect)))
            .ToArray();

    /// <summary>Per-member live preview rectangles exposed for focused host tests.</summary>
    internal IReadOnlyList<CanvasShapeTransformPreview> TransformPreview => State.TransformPreview;

    /// <summary>Union box used for multi-selection handles and group gestures.</summary>
    public Rect? SelectionBounds =>
        State.SelectionBounds is { } rect ? ToAvaloniaRect(rect) : null;
}
