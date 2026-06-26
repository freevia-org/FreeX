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
public sealed class SelectionAdorner : Adorner
{
    // Handle appearance
    private const double HandleSize = 8.0;           // screen px
    private const double RotateHandleRadius = 4.0;   // screen px
    private const double RotateHandleOffset = 18.0;  // above top-middle handle, screen px

    private static readonly Pen SelectionPen;
    private static readonly Pen PreviewPen;
    private static readonly Brush HandleFill;
    private static readonly Pen HandleBorder;
    private static readonly Brush RotateHandleFill;
    private static readonly Pen   RotateHandleBorder;
    private static readonly Pen MarqueePen;
    private static readonly Pen SnapGuidePen;    // Wave 12B — snap guide lines

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
    }

    // ── State owned by CanvasGestureHandler ───────────────────────────────────────────────────

    /// <summary>Current selection screen rects (updated by handler on selection change).</summary>
    private readonly List<(uint id, Rect screenRect)> _selectionRects = new();

    /// <summary>Live-preview rect (null = no preview active).</summary>
    private Rect? _previewRect;

    /// <summary>Live-preview rotation angle in degrees (only meaningful during rotate gesture).</summary>
    private double _previewRotationDeg;

    /// <summary>Active marquee rect in screen coords (null = no marquee).</summary>
    private Rect? _marqueeRect;

    // Wave 12B: active snap guide lines (cleared when gesture ends).
    private IReadOnlyList<SnapGuideLine>? _snapGuides;
    private SlideTransform _snapTransform = SlideTransform.Identity;

    // ── Construction ──────────────────────────────────────────────────────────────────────────

    public SelectionAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false; // Mouse events go to the canvas behind
    }

    // ── Public update API (called by handler) ─────────────────────────────────────────────────

    /// <summary>Replaces the selection rectangles and repaints.</summary>
    public void UpdateSelection(IEnumerable<(uint id, Rect screenRect)> rects)
    {
        _selectionRects.Clear();
        _selectionRects.AddRange(rects);
        _previewRect = null;
        InvalidateVisual();
    }

    /// <summary>Updates the live preview rectangle during a move/resize gesture.</summary>
    public void UpdatePreview(Rect? screenRect, double rotationDeg = 0)
    {
        _previewRect = screenRect;
        _previewRotationDeg = rotationDeg;
        InvalidateVisual();
    }

    /// <summary>Updates the marquee rectangle during a marquee-selection drag.</summary>
    public void UpdateMarquee(Rect? screenRect)
    {
        _marqueeRect = screenRect;
        InvalidateVisual();
    }

    /// <summary>
    /// Wave 12B: Updates the transient snap guide lines shown during a move/resize gesture.
    /// Pass null or an empty list to clear guides.  <paramref name="transform"/> is used
    /// to convert slide-DIP guide positions to screen space.
    /// </summary>
    public void UpdateSnapGuides(IReadOnlyList<SnapGuideLine>? guides, SlideTransform transform)
    {
        _snapGuides   = guides;
        _snapTransform = transform;
        InvalidateVisual();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Wave 12B: Draw snap guide lines (drawn first so they're behind selection rects).
        if (_snapGuides is { Count: > 0 })
            DrawSnapGuides(dc, _snapGuides, _snapTransform);

        // Draw marquee
        if (_marqueeRect.HasValue)
        {
            var mq = _marqueeRect.Value;
            var marqueeFill = new SolidColorBrush(Color.FromArgb(0x22, 0x21, 0x96, 0xF3));
            marqueeFill.Freeze();
            dc.DrawRectangle(marqueeFill, MarqueePen, mq);
        }

        // Draw selection rects
        foreach (var (id, rect) in _selectionRects)
        {
            DrawSelectionRect(dc, rect);
        }

        // Draw preview
        if (_previewRect.HasValue)
        {
            DrawPreviewRect(dc, _previewRect.Value, _previewRotationDeg);
        }
    }

    // Wave 12B: guide line rendering.
    private void DrawSnapGuides(
        DrawingContext dc,
        IReadOnlyList<SnapGuideLine> guides,
        SlideTransform xf)
    {
        double w = ActualWidth;
        double h = ActualHeight;

        foreach (var g in guides)
        {
            if (g.IsHorizontal)
            {
                // Horizontal guide: a full-width line at screen-Y derived from guide DIP position.
                double screenY = g.Position * xf.Scale + xf.OffsetY;
                dc.DrawLine(SnapGuidePen, new Point(0, screenY), new Point(w, screenY));
            }
            else
            {
                // Vertical guide: a full-height line at screen-X.
                double screenX = g.Position * xf.Scale + xf.OffsetX;
                dc.DrawLine(SnapGuidePen, new Point(screenX, 0), new Point(screenX, h));
            }
        }
    }

    private static void DrawSelectionRect(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(null, SelectionPen, rect);
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
        double rotY = topCenter.Y - RotateHandleOffset;
        dc.DrawLine(new Pen(HandleBorder.Brush, 1.0),
            new Point(topCenter.X, topCenter.Y),
            new Point(topCenter.X, rotY));
        dc.DrawEllipse(RotateHandleFill, RotateHandleBorder,
            new Point(topCenter.X, rotY), RotateHandleRadius, RotateHandleRadius);
    }

    /// <summary>
    /// Returns centers of the 8 resize handles for a given selection rect.
    /// Order: N, NE, E, SE, S, SW, W, NW.
    /// </summary>
    internal static Point[] GetHandleCenters(Rect rect)
    {
        double mx = rect.Left + rect.Width / 2;
        double my = rect.Top  + rect.Height / 2;
        return new[]
        {
            new Point(mx,         rect.Top),     // N
            new Point(rect.Right, rect.Top),     // NE
            new Point(rect.Right, my),           // E
            new Point(rect.Right, rect.Bottom),  // SE
            new Point(mx,         rect.Bottom),  // S
            new Point(rect.Left,  rect.Bottom),  // SW
            new Point(rect.Left,  my),           // W
            new Point(rect.Left,  rect.Top),     // NW
        };
    }

    /// <summary>
    /// Returns the rotate handle center for a given selection rect.
    /// </summary>
    internal static Point GetRotateHandleCenter(Rect rect)
    {
        double topCenterX = rect.Left + rect.Width / 2;
        return new Point(topCenterX, rect.Top - RotateHandleOffset);
    }

    // ── Hit-test helpers (used by CanvasGestureHandler) ───────────────────────────────────────

    private const double HandleHitRadius = 8.0; // screen px

    public enum HandleKind
    {
        None,
        Body,
        ResizeN, ResizeNE, ResizeE, ResizeSE, ResizeS, ResizeSW, ResizeW, ResizeNW,
        Rotate
    }

    /// <summary>
    /// Returns which part of the selection a screen-space point hits (for a single selected shape).
    /// </summary>
    public HandleKind HitTestHandle(Rect selectionRect, Point screenPt)
    {
        // Rotate handle
        var rotCenter = GetRotateHandleCenter(selectionRect);
        if (Distance(screenPt, rotCenter) <= HandleHitRadius)
            return HandleKind.Rotate;

        // Resize handles
        var centers = GetHandleCenters(selectionRect);
        var kinds = new[]
        {
            HandleKind.ResizeN, HandleKind.ResizeNE, HandleKind.ResizeE, HandleKind.ResizeSE,
            HandleKind.ResizeS, HandleKind.ResizeSW, HandleKind.ResizeW, HandleKind.ResizeNW
        };

        for (int i = 0; i < centers.Length; i++)
        {
            if (Distance(screenPt, centers[i]) <= HandleHitRadius)
                return kinds[i];
        }

        // Body
        if (selectionRect.Contains(screenPt))
            return HandleKind.Body;

        return HandleKind.None;
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Selection rects accessible to the gesture handler for redraw.</summary>
    public IReadOnlyList<(uint id, Rect screenRect)> SelectionRects => _selectionRects;
}
