using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
public sealed class SelectionAdornerLayer : Control
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

    static SelectionAdornerLayer()
    {
        SelectionPen       = new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 1.5);
        PreviewPen         = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x21, 0x96, 0xF3)), 1.5)
                             { DashStyle = new DashStyle([4.0, 2.0], 0) };
        HandleFill         = new SolidColorBrush(Colors.White);
        HandleBorder       = new Pen(new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), 1.5);
        RotateHandleFill   = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        RotateHandleBorder = new Pen(new SolidColorBrush(Colors.White), 1.0);
        MarqueePen         = new Pen(new SolidColorBrush(Color.FromArgb(0xBB, 0x21, 0x96, 0xF3)), 1.0)
                             { DashStyle = DashStyle.Dash };
        MarqueeFill        = new SolidColorBrush(Color.FromArgb(0x22, 0x21, 0x96, 0xF3));
        SnapGuidePen       = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xE9, 0x1E, 0x63)), 1.0);
        EditPointFill      = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        EditPointBorder    = new Pen(new SolidColorBrush(Colors.White), 1.0);
        EditPointPreviewPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)), 1.5)
                             { DashStyle = DashStyle.Dash };
    }

    // ── State owned / updated by AvaloniaCanvasGestureHandler ──────────────────

    private readonly List<(uint id, Rect screenRect)> _selectionRects = new();
    private Rect? _previewRect;
    private double _previewRotationDeg;
    private Rect? _marqueeRect;
    private IReadOnlyList<SnapGuideLine>? _snapGuides;
    private SlideTransformCore _snapTransform = SlideTransformCore.Identity;
    private readonly List<(string Name, Point Position)> _geometryHandles = new();
    private (string Name, Point Position)? _geometryPreview;

    // ── Construction ───────────────────────────────────────────────────────────

    public SelectionAdornerLayer()
    {
        IsHitTestVisible = false; // pointer events go to canvas below
    }

    // ── Public update API ───────────────────────────────────────────────────────

    /// <summary>Replaces the selection rectangles and triggers repaint.</summary>
    public void UpdateSelection(IEnumerable<(uint id, Rect screenRect)> rects)
    {
        _selectionRects.Clear();
        _selectionRects.AddRange(rects);
        _previewRect = null;
        InvalidateVisual();
    }

    /// <summary>Replaces the visible preset-shape edit points.</summary>
    public void UpdateGeometryHandles(IEnumerable<(string Name, Point Position)> handles)
    {
        _geometryHandles.Clear();
        _geometryHandles.AddRange(handles);
        _geometryPreview = null;
        InvalidateVisual();
    }

    /// <summary>Shows the transient position of the handle being dragged.</summary>
    public void UpdateGeometryPreview(string? name, Point? position)
    {
        _geometryPreview = name is not null && position is { } point
            ? (name, point)
            : null;
        InvalidateVisual();
    }

    /// <summary>Updates the live preview rectangle during a move/resize gesture.</summary>
    public void UpdatePreview(Rect? screenRect, double rotationDeg = 0)
    {
        _previewRect        = screenRect;
        _previewRotationDeg = rotationDeg;
        InvalidateVisual();
    }

    /// <summary>Updates the marquee rectangle during a marquee-selection drag.</summary>
    public void UpdateMarquee(Rect? screenRect)
    {
        _marqueeRect = screenRect;
        InvalidateVisual();
    }

    /// <summary>Updates the transient snap guide lines shown during a move/resize gesture.</summary>
    public void UpdateSnapGuides(IReadOnlyList<SnapGuideLine>? guides, SlideTransformCore transform)
    {
        _snapGuides    = guides;
        _snapTransform = transform;
        InvalidateVisual();
    }

    internal bool HasTransientInteractionVisualsForTests =>
        _previewRect.HasValue ||
        _marqueeRect.HasValue ||
        _snapGuides is { Count: > 0 } ||
        _geometryPreview.HasValue;

    // ── Rendering ───────────────────────────────────────────────────────────────

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);

        // Snap guide lines (drawn first — behind selection rects).
        if (_snapGuides is { Count: > 0 })
            DrawSnapGuides(dc, _snapGuides, _snapTransform);

        // Marquee
        if (_marqueeRect.HasValue)
            dc.DrawRectangle(MarqueeFill, MarqueePen, _marqueeRect.Value);

        // Multi-selection uses one group box for handles; individual boxes remain visible
        // without handles so the selected members are still discoverable.
        if (_selectionRects.Count == 1)
        {
            DrawSelectionRect(dc, _selectionRects[0].screenRect, drawHandles: true);
        }
        else
        {
            foreach (var (_, rect) in _selectionRects)
                DrawSelectionRect(dc, rect, drawHandles: false);

            if (SelectionBounds is { } groupBounds)
                DrawSelectionRect(dc, groupBounds, drawHandles: true);
        }

        DrawGeometryHandles(dc);

        // Preview
        if (_previewRect.HasValue)
            DrawPreviewRect(dc, _previewRect.Value, _previewRotationDeg);
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
        dc.DrawLine(new Pen(((Pen)HandleBorder).Brush, 1.0),
            new Point(topCenterX, rect.Top),
            new Point(topCenterX, rotY));
        dc.DrawEllipse(RotateHandleFill, RotateHandleBorder,
            new Point(topCenterX, rotY), RotateHandleRadius, RotateHandleRadius);
    }

    private void DrawGeometryHandles(DrawingContext dc)
    {
        const double radius = 5.0;
        foreach (var (_, point) in _geometryHandles)
            dc.DrawEllipse(EditPointFill, EditPointBorder, point, radius, radius);

        if (_geometryPreview is { } preview)
            dc.DrawEllipse(null, EditPointPreviewPen, preview.Position, radius + 2, radius + 2);
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
        const double hitRadius = 9.0;
        foreach (var (name, position) in _geometryHandles)
        {
            var dx = screenPt.X - position.X;
            var dy = screenPt.Y - position.Y;
            if (dx * dx + dy * dy <= hitRadius * hitRadius)
                return name;
        }

        return null;
    }

    private static SelectionAdornerRect ToSelectionAdornerRect(Rect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Point ToAvaloniaPoint(CanvasGesturePoint point)
        => new(point.X, point.Y);

    private static CanvasGesturePoint ToCanvasPoint(Point point)
        => new(point.X, point.Y);

    /// <summary>Selection rects accessible to the gesture handler for external queries.</summary>
    public IReadOnlyList<(uint id, Rect screenRect)> SelectionRects => _selectionRects;

    /// <summary>Union box used for multi-selection handles and group gestures.</summary>
    public Rect? SelectionBounds
    {
        get
        {
            if (_selectionRects.Count == 0)
                return null;

            double left = _selectionRects.Min(item => item.screenRect.Left);
            double top = _selectionRects.Min(item => item.screenRect.Top);
            double right = _selectionRects.Max(item => item.screenRect.Right);
            double bottom = _selectionRects.Max(item => item.screenRect.Bottom);
            return new Rect(left, top, right - left, bottom - top);
        }
    }
}
