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
public sealed partial class SelectionAdorner : Adorner
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
    private static readonly Pen MarqueePen;
    private static readonly Pen SnapGuidePen;    // Wave 12B — snap guide lines
    private static readonly Brush EditPointFill;
    private static readonly Pen EditPointBorder;
    private static readonly Pen EditPointPreviewPen;

    static SelectionAdorner()
    {
        SelectionPen = new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 1.5);
        SelectionPen.Freeze();

        PreviewPen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x21, 0x96, 0xF3)), 1.5)
        {
            DashStyle = new DashStyle(new[] { 4.0, 2.0 }, 0)
        };
        PreviewPen.Freeze();

        HandleFill = new SolidColorBrush(Colors.White);
        HandleFill.Freeze();

        HandleBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 1.5);
        HandleBorder.Freeze();

        RotateHandleFill = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        RotateHandleFill.Freeze();

        RotateHandleBorder = new Pen(Brushes.White, 1.0);
        RotateHandleBorder.Freeze();

        MarqueePen = new Pen(new SolidColorBrush(Color.FromArgb(0xBB, 0x21, 0x96, 0xF3)), 1.0)
        {
            DashStyle = DashStyles.Dash
        };
        MarqueePen.Freeze();

        // Wave 12B: snap guide lines — thin magenta lines, PowerPoint-style.
        SnapGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xE9, 0x1E, 0x63)), 1.0);
        SnapGuidePen.Freeze();

        EditPointFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        EditPointFill.Freeze();

        EditPointBorder = new Pen(Brushes.White, 1.0);
        EditPointBorder.Freeze();

        EditPointPreviewPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 1.5)
        {
            DashStyle = DashStyles.Dash
        };
        EditPointPreviewPen.Freeze();
    }

    // ── State owned by CanvasGestureHandler ───────────────────────────────────────────────────

    private readonly SelectionAdornerState _state = new();

    // ── Construction ──────────────────────────────────────────────────────────────────────────

    public SelectionAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false; // Mouse events go to the canvas behind
    }

    // ── Public update API (called by handler) ─────────────────────────────────────────────────

    /// <summary>Replaces the selection rectangles and repaints.</summary>
    public void UpdateSelection(IEnumerable<(uint id, Rect screenRect)> rects)
    {
        _state.UpdateSelection(rects.Select(item => new SelectionAdornerSelectionPlan(
            item.id,
            ToSelectionAdornerRect(item.screenRect))));
        InvalidateVisual();
    }

    /// <summary>Replaces the visible preset-shape edit points.</summary>
    public void UpdateGeometryHandles(IEnumerable<(string Name, Point Position)> handles)
    {
        _state.UpdateGeometryHandles(handles.Select(handle =>
            new SelectionAdornerGeometryHandlePlan(handle.Name, ToCanvasPoint(handle.Position))));
        InvalidateVisual();
    }

    /// <summary>Shows the transient position of the handle being dragged.</summary>
    public void UpdateGeometryPreview(string? name, Point? position)
    {
        _state.UpdateGeometryPreview(name, position is { } point ? ToCanvasPoint(point) : null);
        InvalidateVisual();
    }

    /// <summary>Updates the live preview rectangle during a move/resize gesture.</summary>
    public void UpdatePreview(Rect? screenRect, double rotationDeg = 0)
    {
        _state.UpdatePreview(
            screenRect is { } rect ? ToSelectionAdornerRect(rect) : null,
            rotationDeg);
        InvalidateVisual();
    }

    /// <summary>Shows each member's live geometry from one shared transform plan.</summary>
    public void UpdateTransformPreview(CanvasMultiTransformPlan plan)
    {
        _state.UpdateTransformPreview(plan);
        InvalidateVisual();
    }

    /// <summary>Updates the marquee rectangle during a marquee-selection drag.</summary>
    public void UpdateMarquee(Rect? screenRect)
    {
        _state.UpdateMarquee(screenRect is { } rect ? ToSelectionAdornerRect(rect) : null);
        InvalidateVisual();
    }

    /// <summary>
    /// Wave 12B: Updates the transient snap guide lines shown during a move/resize gesture.
    /// Pass null or an empty list to clear guides.  <paramref name="transform"/> is used
    /// to convert slide-DIP guide positions to screen space.
    /// </summary>
    public void UpdateSnapGuides(IReadOnlyList<SnapGuideLine>? guides, SlideTransform transform)
    {
        _state.UpdateSnapGuides(guides, transform.Core);
        InvalidateVisual();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Wave 12B: Draw snap guide lines (drawn first so they're behind selection rects).
        if (_state.SnapGuides is { Count: > 0 } snapGuides)
            DrawSnapGuides(dc, snapGuides, _state.SnapTransform);

        // Draw marquee
        if (_state.MarqueeRect is { } marqueeRect)
        {
            var marqueeFill = new SolidColorBrush(Color.FromArgb(0x22, 0x21, 0x96, 0xF3));
            marqueeFill.Freeze();
            dc.DrawRectangle(marqueeFill, MarqueePen, ToWpfRect(marqueeRect));
        }

        // Multi-selection uses one group box for handles; individual boxes remain visible
        // without handles so the selected members are still discoverable.
        if (_state.Selections.Count == 1)
        {
            DrawSelectionRect(dc, ToWpfRect(_state.Selections[0].ScreenRect), drawHandles: true);
        }
        else
        {
            foreach (var selection in _state.Selections)
                DrawSelectionRect(dc, ToWpfRect(selection.ScreenRect), drawHandles: false);

            if (SelectionBounds is { } groupBounds)
                DrawSelectionRect(dc, groupBounds, drawHandles: true);
        }

        DrawGeometryHandles(dc);

        foreach (var preview in _state.TransformPreview)
            DrawPreviewRect(dc, ToWpfRect(preview.ScreenBounds), preview.RotationDeg);

        // Draw preview
        if (_state.PreviewRect is { } previewRect)
        {
            DrawPreviewRect(dc, ToWpfRect(previewRect), _state.PreviewRotationDeg);
        }
    }

    // Wave 12B: guide line rendering.
    private void DrawSnapGuides(
        DrawingContext dc,
        IReadOnlyList<SnapGuideLine> guides,
        SlideTransformCore xf)
    {
        double w = ActualWidth;
        double h = ActualHeight;

        foreach (var g in guides)
        {
            if (g.IsHorizontal)
            {
                // Horizontal guide: a full-width line at screen-Y derived from guide DIP position.
                double screenY = SlideCanvasGeometryPlanner.SnapGuideToScreenPosition(g, xf);
                dc.DrawLine(SnapGuidePen, new Point(0, screenY), new Point(w, screenY));
            }
            else
            {
                // Vertical guide: a full-height line at screen-X.
                double screenX = SlideCanvasGeometryPlanner.SnapGuideToScreenPosition(g, xf);
                dc.DrawLine(SnapGuidePen, new Point(screenX, 0), new Point(screenX, h));
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
        dc.DrawLine(new Pen(HandleBorder.Brush, 1.0),
            new Point(topCenter.X, topCenter.Y),
            new Point(topCenter.X, rotY));
        dc.DrawEllipse(RotateHandleFill, RotateHandleBorder,
            new Point(topCenter.X, rotY), RotateHandleRadius, RotateHandleRadius);
    }

    private void DrawGeometryHandles(DrawingContext dc)
    {
        const double radius = 5.0;
        foreach (var handle in _state.GeometryHandles)
        {
            dc.DrawEllipse(
                EditPointFill,
                EditPointBorder,
                ToWpfPoint(handle.ScreenPosition),
                radius,
                radius);
        }

        if (_state.GeometryPreview is { } preview)
        {
            dc.DrawEllipse(
                null,
                EditPointPreviewPen,
                ToWpfPoint(preview.ScreenPosition),
                radius + 2,
                radius + 2);
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
            _state.GeometryHandles,
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
        _state.Selections
            .Select(selection => (selection.ShapeId, ToWpfRect(selection.ScreenRect)))
            .ToArray();

    /// <summary>Per-member live preview rectangles exposed for focused host tests.</summary>
    internal IReadOnlyList<CanvasShapeTransformPreview> TransformPreview => _state.TransformPreview;

    /// <summary>Union box used for multi-selection handles and group gestures.</summary>
    public Rect? SelectionBounds =>
        _state.SelectionBounds is { } rect ? ToWpfRect(rect) : null;
}
