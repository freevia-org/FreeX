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

    /// <summary>The rotate-handle stem, built once from the shared style table (immutable: no thread affinity).</summary>
    private static readonly IPen RotateHandleStem = ToPen(SelectionAdornerStyle.RotateHandleStem);

    // Every one of these is IMMUTABLE on purpose. Pen, SolidColorBrush and DashStyle are all
    // AvaloniaObjects, so they carry thread affinity to whichever thread first ran this static
    // constructor -- and the compositor reads them again on the render thread. Holding mutable ones
    // in static fields throws "The calling thread cannot access this object because a different
    // thread owns it" out of the renderer, intermittently and depending on which test or window
    // touched the class first. The Immutable* variants have no affinity.
    static SelectionAdornerLayer()
    {
        SelectionPen       = ToPen(SelectionAdornerStyle.Selection);
        PreviewPen         = ToPen(SelectionAdornerStyle.Preview);
        HandleFill         = ToBrush(SelectionAdornerStyle.HandleFillArgb);
        HandleBorder       = ToPen(SelectionAdornerStyle.HandleBorder);
        RotateHandleFill   = ToBrush(SelectionAdornerStyle.RotateHandleFillArgb);
        RotateHandleBorder = ToPen(SelectionAdornerStyle.RotateHandleBorder);
        MarqueePen         = ToPen(SelectionAdornerStyle.Marquee);
        MarqueeFill        = ToBrush(SelectionAdornerStyle.MarqueeFillArgb);
        SnapGuidePen       = ToPen(SelectionAdornerStyle.SnapGuide);
        EditPointFill      = ToBrush(SelectionAdornerStyle.EditPointFillArgb);
        EditPointBorder    = ToPen(SelectionAdornerStyle.EditPointBorder);
        EditPointPreviewPen = ToPen(SelectionAdornerStyle.EditPointPreview);
    }

    private static ImmutableSolidColorBrush ToBrush(uint argb) =>
        new(Color.FromUInt32(argb));

    private static ImmutablePen ToPen(SelectionAdornerStroke stroke) =>
        new(
            ToBrush(stroke.Argb),
            stroke.Width,
            stroke.Dashes is { } dashes ? new ImmutableDashStyle([.. dashes], 0) : null);

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

        // The draw order (snap guides, marquee, selection boxes, geometry handles, previews) is the
        // shared SelectionAdornerDrawPlanner's; this layer only maps each step onto Avalonia drawing.
        foreach (var op in SelectionAdornerDrawPlanner.Plan(State))
        {
            switch (op.Kind)
            {
                case SelectionAdornerDrawOpKind.SnapGuides:
                    DrawSnapGuides(dc, State.SnapGuides!, State.SnapTransform);
                    break;
                case SelectionAdornerDrawOpKind.Marquee:
                    dc.DrawRectangle(MarqueeFill, MarqueePen, ToAvaloniaRect(op.Rect));
                    break;
                case SelectionAdornerDrawOpKind.SelectionRect:
                    DrawSelectionRect(dc, ToAvaloniaRect(op.Rect), op.DrawHandles);
                    break;
                case SelectionAdornerDrawOpKind.GeometryHandles:
                    DrawGeometryHandles(dc);
                    break;
                case SelectionAdornerDrawOpKind.PreviewRect:
                    DrawPreviewRect(dc, ToAvaloniaRect(op.Rect), op.RotationDeg);
                    break;
            }
        }
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
        dc.DrawLine(RotateHandleStem,
            new Point(topCenterX, rect.Top),
            new Point(topCenterX, rotY));
        dc.DrawEllipse(RotateHandleFill, RotateHandleBorder,
            new Point(topCenterX, rotY), RotateHandleRadius, RotateHandleRadius);
    }

    private void DrawGeometryHandles(DrawingContext dc)
    {
        const double radius = SelectionAdornerStyle.EditPointRadius;
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
                radius + SelectionAdornerStyle.EditPointPreviewRadiusExtra,
                radius + SelectionAdornerStyle.EditPointPreviewRadiusExtra);
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
