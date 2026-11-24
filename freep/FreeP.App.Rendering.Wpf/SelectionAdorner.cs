using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.Core.Model;
using FreeP.App.Compositor; // SnapGuideLine (Wave 12B)

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// A WPF <see cref="Adorner"/> drawn on top of <see cref="SlideCanvas"/> that:
/// <list type="bullet">
///   <item>Draws selection rectangles around selected shapes.</item>
///   <item>Draws 8 resize handles (N, NE, E, SE, S, SW, W, NW) and one rotate handle above.</item>
///   <item>Draws a live-preview rectangle/rotation angle during drag gestures.</item>
/// </list>
///
/// The adorner does NOT own interaction logic — that lives in <see cref="CanvasGestureHandler"/>.
/// The adorner is invalidated and redrawn whenever the handler raises a layout change.
/// </summary>
public sealed partial class SelectionAdorner : Adorner, ISelectionAdornerSurface<Rect, Point>
{
    // Handle appearance
    private const double HandleSize = SelectionAdornerGeometry.HandleSize;
    private const double RotateHandleRadius = SelectionAdornerGeometry.RotateHandleRadius;

    private static readonly Pen SelectionPen;
    private static readonly Pen PreviewPen;
    private static readonly Brush HandleFill;
    private static readonly Pen HandleBorder;
    private static readonly Brush RotateHandleFill;
    private static readonly Pen   RotateHandleBorder;
    private static readonly Pen RotateHandleStem;
    private static readonly Pen MarqueePen;
    private static readonly Brush MarqueeFill;
    private static readonly Pen SnapGuidePen;    // Wave 12B — snap guide lines
    private static readonly Brush EditPointFill;
    private static readonly Pen EditPointBorder;
    private static readonly Pen EditPointPreviewPen;

    static SelectionAdorner()
    {
        SelectionPen = ToPen(SelectionAdornerStyle.Selection);
        PreviewPen = ToPen(SelectionAdornerStyle.Preview);
        HandleFill = ToFrozenBrush(SelectionAdornerStyle.HandleFillArgb);
        HandleBorder = ToPen(SelectionAdornerStyle.HandleBorder);
        RotateHandleStem = ToPen(SelectionAdornerStyle.RotateHandleStem);
        RotateHandleFill = ToFrozenBrush(SelectionAdornerStyle.RotateHandleFillArgb);
        RotateHandleBorder = ToPen(SelectionAdornerStyle.RotateHandleBorder);
        MarqueePen = ToPen(SelectionAdornerStyle.Marquee);
        MarqueeFill = ToFrozenBrush(SelectionAdornerStyle.MarqueeFillArgb);
        SnapGuidePen = ToPen(SelectionAdornerStyle.SnapGuide);
        EditPointFill = ToFrozenBrush(SelectionAdornerStyle.EditPointFillArgb);
        EditPointBorder = ToPen(SelectionAdornerStyle.EditPointBorder);
        EditPointPreviewPen = ToPen(SelectionAdornerStyle.EditPointPreview);
    }

    private static Brush ToFrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(ToColor(argb));
        brush.Freeze();
        return brush;
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static Pen ToPen(SelectionAdornerStroke stroke)
    {
        var pen = new Pen(ToFrozenBrush(stroke.Argb), stroke.Width);
        if (stroke.Dashes is { } dashes)
            pen.DashStyle = new DashStyle(dashes, 0);
        pen.Freeze();
        return pen;
    }

    // ── State owned by CanvasGestureHandler ───────────────────────────────────────────────────

    private readonly SelectionAdornerController<Rect, Point> _controller;
    SelectionAdornerController<Rect, Point> ISelectionAdornerSurface<Rect, Point>.Controller => _controller;

    private SelectionAdornerState State => _controller.State;

    private SelectionAdornerState _state => State;

    // ── Construction ──────────────────────────────────────────────────────────────────────────

    public SelectionAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false; // Mouse events go to the canvas behind
        _controller = new(ToSelectionAdornerRect, ToCanvasPoint, InvalidateVisual);
    }

    // ── Public update API (called by handler) ─────────────────────────────────────────────────

    // ── Rendering ─────────────────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // The draw order (snap guides, marquee, selection boxes, geometry handles, previews) is the
        // shared SelectionAdornerDrawPlanner's; this adorner only maps each step onto WPF drawing.
        foreach (var op in SelectionAdornerDrawPlanner.Plan(State))
        {
            switch (op.Kind)
            {
                case SelectionAdornerDrawOpKind.SnapGuides:
                    DrawSnapGuides(dc, State.SnapGuides!, State.SnapTransform);
                    break;
                case SelectionAdornerDrawOpKind.Marquee:
                    dc.DrawRectangle(MarqueeFill, MarqueePen, ToWpfRect(op.Rect));
                    break;
                case SelectionAdornerDrawOpKind.SelectionRect:
                    DrawSelectionRect(dc, ToWpfRect(op.Rect), op.DrawHandles);
                    break;
                case SelectionAdornerDrawOpKind.GeometryHandles:
                    DrawGeometryHandles(dc);
                    break;
                case SelectionAdornerDrawOpKind.PreviewRect:
                    DrawPreviewRect(dc, ToWpfRect(op.Rect), op.RotationDeg);
                    break;
            }
        }
    }

    // Wave 12B: guide line rendering. The planner spans each guide over the adorner's own extent.
    private void DrawSnapGuides(
        DrawingContext dc,
        IReadOnlyList<SnapGuideLine> guides,
        SlideTransformCore xf)
    {
        foreach (var segment in SnapGuideRenderPlanner.Plan(guides, xf, ActualWidth, ActualHeight))
        {
            dc.DrawLine(
                SnapGuidePen,
                new Point(segment.Start.X, segment.Start.Y),
                new Point(segment.End.X, segment.End.Y));
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
            double cx = rect.Left + rect.Width / 2;
            double cy = rect.Top  + rect.Height / 2;
            dc.PushTransform(new RotateTransform(rotDeg, cx, cy));
        }

        dc.DrawRectangle(null, PreviewPen, rect);

        if (rotDeg != 0)
            dc.Pop();
    }

    private static Rect ToWpfRect(SlideScreenRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect ToWpfRect(SelectionAdornerRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static void DrawHandles(DrawingContext dc, Rect rect)
    {
        double h = HandleSize;
        double r = h / 2;

        // 8 resize handles: N, NE, E, SE, S, SW, W, NW
        var centers = GetHandleCenters(rect);
        foreach (var c in centers)
        {
            dc.DrawRectangle(HandleFill, HandleBorder,
                new Rect(c.X - r, c.Y - r, h, h));
        }

        // Rotate handle above N handle
        var topCenter = new Point(rect.Left + rect.Width / 2, rect.Top);
        var rotateCenter = GetRotateHandleCenter(rect);
        double rotY = rotateCenter.Y;
        dc.DrawLine(RotateHandleStem,
            new Point(topCenter.X, topCenter.Y),
            new Point(topCenter.X, rotY));
        dc.DrawEllipse(RotateHandleFill, RotateHandleBorder,
            new Point(topCenter.X, rotY), RotateHandleRadius, RotateHandleRadius);
    }

    private void DrawGeometryHandles(DrawingContext dc)
    {
        const double radius = SelectionAdornerStyle.EditPointRadius;
        foreach (var handle in State.GeometryHandles)
        {
            dc.DrawEllipse(
                EditPointFill,
                EditPointBorder,
                ToWpfPoint(handle.ScreenPosition),
                radius,
                radius);
        }

        if (State.GeometryPreview is { } preview)
        {
            dc.DrawEllipse(
                null,
                EditPointPreviewPen,
                ToWpfPoint(preview.ScreenPosition),
                radius + SelectionAdornerStyle.EditPointPreviewRadiusExtra,
                radius + SelectionAdornerStyle.EditPointPreviewRadiusExtra);
        }
    }

    /// <summary>
    /// Returns centers of the 8 resize handles for a given selection rect.
    /// Order: N, NE, E, SE, S, SW, W, NW.
    /// </summary>
    internal static Point[] GetHandleCenters(Rect rect)
    {
        return SelectionAdornerGeometry.GetHandleCenters(ToSelectionAdornerRect(rect))
            .Select(ToWpfPoint)
            .ToArray();
    }

    /// <summary>
    /// Returns the rotate handle center for a given selection rect.
    /// </summary>
    internal static Point GetRotateHandleCenter(Rect rect)
    {
        return ToWpfPoint(
            SelectionAdornerGeometry.GetRotateHandleCenter(ToSelectionAdornerRect(rect)));
    }

    // ── Hit-test helpers (used by CanvasGestureHandler) ───────────────────────────────────────

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

    private static Point ToWpfPoint(CanvasGesturePoint point)
        => new(point.X, point.Y);

    private static CanvasGesturePoint ToCanvasPoint(Point point)
        => new(point.X, point.Y);

    /// <summary>Selection rects accessible to the gesture handler for redraw.</summary>
    public IReadOnlyList<(uint id, Rect screenRect)> SelectionRects =>
        State.Selections
            .Select(selection => (selection.ShapeId, ToWpfRect(selection.ScreenRect)))
            .ToArray();

    /// <summary>Per-member live preview rectangles exposed for focused host tests.</summary>
    internal IReadOnlyList<CanvasShapeTransformPreview> TransformPreview => State.TransformPreview;

    /// <summary>Union box used for multi-selection handles and group gestures.</summary>
    public Rect? SelectionBounds =>
        State.SelectionBounds is { } rect ? ToWpfRect(rect) : null;
}
