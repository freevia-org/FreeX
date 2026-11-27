using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Handles all pointer/keyboard interactions on the <see cref="SlideCanvas"/> for editing:
/// selection (click / Ctrl+click / Shift+click), marquee drag, move, resize, rotate,
/// delete key, and arrow-key nudge.
///
/// Architecture:
/// <list type="bullet">
///   <item>Attaches to <see cref="SlideCanvas"/> mouse/keyboard events.</item>
///   <item>Maintains a live-preview offset/resize/rotate state during drag; commits one
///         command to <see cref="FreeP.App.Compositor.EditingSession"/> on mouse-up.</item>
///   <item>Drives a <see cref="SelectionAdorner"/> for visual feedback.</item>
///   <item>All coordinate work is delegated to the framework-free gesture router and preview
///         projector so the logic is fully unit-testable.</item>
/// </list>
/// </summary>
public sealed partial class CanvasGestureHandler : IDisposable
{
    // ── Wiring ────────────────────────────────────────────────────────────────────────────────

    private readonly SlideCanvas       _canvas;
    private readonly ICanvasGestureEditingSession _editor;
    private readonly SelectionAdorner  _adorner;
    private readonly AdornerLayer?     _adornerLayer;
    private bool                       _disposed;

    // ── Drag state ────────────────────────────────────────────────────────────────────────────

    private readonly CanvasGestureRouter _gestureRouter;
    private readonly CanvasGesturePreviewSurfaceAdapter _previewSurface;
    private readonly CanvasGestureHostGlue _glue;

    // ── Move ──────────────────────────────────────────────────────────────────────────────────

    // ── Resize ────────────────────────────────────────────────────────────────────────────────

    // ── Rotate ────────────────────────────────────────────────────────────────────────────────

    // Preset geometry edit-point gesture

    // ── Marquee ───────────────────────────────────────────────────────────────────────────────

    // ── Small nudge step ──────────────────────────────────────────────────────────────────────
    // ── Wave 12B: Snap settings ───────────────────────────────────────────────────────────────
    // Both default on; holding Alt during drag disables snapping (PowerPoint convention).

    /// <summary>When true (default), shapes snap to the background grid during move/resize.</summary>
    public bool SnapToGrid
    {
        get => _gestureRouter.SnapToGrid;
        set => _gestureRouter.SnapToGrid = value;
    }

    /// <summary>When true (default), shapes snap to other shapes' edges and centers during move/resize.</summary>
    public bool SnapToShapes
    {
        get => _gestureRouter.SnapToShapes;
        set => _gestureRouter.SnapToShapes = value;
    }

    /// <summary>When enabled, supported preset shapes expose draggable edit points.</summary>
    public bool EditPointsEnabled
    {
        get => _gestureRouter.EditPointsEnabled;
        set
        {
            if (_gestureRouter.EditPointsEnabled == value)
                return;
            _gestureRouter.EditPointsEnabled = value;
            RefreshAdorner();
        }
    }

    // ── Construction / attach ─────────────────────────────────────────────────────────────────

    public CanvasGestureHandler(
        SlideCanvas canvas,
        ICanvasGestureEditingSession editor,
        Func<SlideShape, bool>? tryOpenOleInPlace = null,
        Action<ChartPointHit>? onChartPointDoubleClick = null,
        Func<OleObjectInfo?, bool>? tryActivateOleExternally = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _gestureRouter = new CanvasGestureRouter(_editor);

        // Add adorner to the adorner layer
        _adornerLayer = AdornerLayer.GetAdornerLayer(_canvas);
        _adorner      = new SelectionAdorner(_canvas);
        _adornerLayer?.Add(_adorner);
        _previewSurface = new(
            (bounds, rotation) => _adorner.UpdatePreview(
                bounds is { } value ? ToWpfRect(value) : null,
                rotation),
            _adorner.UpdateSnapGuides,
            plan =>
            {
                _adorner.UpdateTransformPreview(plan);
                _canvas.UpdateTransformPreview(plan);
            },
            (name, point) => _adorner.UpdateGeometryPreview(
                name,
                new Point(point.X, point.Y)),
            bounds => _adorner.UpdateMarquee(ToWpfRect(bounds)));
        _glue = new CanvasGestureHostGlue(
            _editor,
            _gestureRouter,
            _previewSurface,
            onChartPointDoubleClick,
            tryOpenOleInPlace,
            tryActivateOleExternally);

        // Hook canvas events
        _canvas.MouseLeftButtonDown += OnMouseDown;
        _canvas.MouseLeftButtonUp   += OnMouseUp;
        _canvas.MouseMove           += OnMouseMove;
        _canvas.LostMouseCapture   += OnLostMouseCapture;
        _canvas.KeyDown             += OnKeyDown;
        _canvas.Focusable           = true;

        // React to selection changes from the editor (e.g. SelectAll via ribbon)
        _editor.SelectionChanged    += OnEditorSelectionChanged;
        _editor.Changed             += OnEditorChanged;
        _editor.CurrentSlideChanged += OnEditorCurrentSlideChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.MouseLeftButtonDown -= OnMouseDown;
        _canvas.MouseLeftButtonUp   -= OnMouseUp;
        _canvas.MouseMove           -= OnMouseMove;
        _canvas.LostMouseCapture    -= OnLostMouseCapture;
        _canvas.KeyDown             -= OnKeyDown;
        _editor.SelectionChanged    -= OnEditorSelectionChanged;
        _editor.Changed             -= OnEditorChanged;
        _editor.CurrentSlideChanged -= OnEditorCurrentSlideChanged;

        CancelActiveGesture(releaseCapture: true);
        _adornerLayer?.Remove(_adorner);
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e) => RefreshAdorner();
    private void OnEditorChanged() => RefreshAdorner();
    private void OnEditorCurrentSlideChanged(object? sender, EventArgs e) => RefreshAdorner();

    /// <summary>Captures the selected source and arms source-then-target Format Painter mode.</summary>
    public bool BeginFormatPainter() => _editor.BeginFormatPainter();

    /// <summary>Disarms source-then-target Format Painter mode.</summary>
    public void CancelFormatPainter() => _editor.CancelFormatPainter();

    // ── Mouse down ────────────────────────────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        var pt = e.GetPosition(_canvas);
        var xf = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;

        if (slide is null || _editor.Presentation is null)
            return;

        var plan = _gestureRouter.HandlePointerPressed(_glue.CreatePressRequest(
            ToGesturePoint(pt),
            ToCoreTransform(xf),
            slide,
            e.ClickCount,
            ToGestureModifiers(Keyboard.Modifiers)));
        _glue.ApplyPressAction(plan);
        if (plan.CapturePointer)
            _canvas.CaptureMouse();
        e.Handled = plan.Handled;
    }

    // ── Mouse move ────────────────────────────────────────────────────────────────────────────

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateCursor(e.GetPosition(_canvas));
            return;
        }

        var point = e.GetPosition(_canvas);
        var transform = _canvas.CurrentTransform;
        var coreTransform = ToCoreTransform(transform);
        _glue.ApplyPreviewPlan(
            _gestureRouter.PreviewPointer(
                ToGesturePoint(point),
                coreTransform,
                ToGestureModifiers(Keyboard.Modifiers)),
            coreTransform);
    }

    // ── Mouse up ──────────────────────────────────────────────────────────────────────────────

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        CompleteGesture(
            e.GetPosition(_canvas),
            _canvas.CurrentTransform,
            ToGestureModifiers(Keyboard.Modifiers));
        _canvas.ReleaseMouseCapture();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) =>
        CancelActiveGesture(releaseCapture: false);

    private void CompleteGesture(
        Point point,
        SlideTransform transform,
        FreeKeyModifiers modifiers)
    {
        _gestureRouter.CompletePointer(
            ToGesturePoint(point),
            ToCoreTransform(transform),
            modifiers);
        ClearTransientInteractionVisuals();
    }

    private void CancelActiveGesture(bool releaseCapture)
    {
        bool wasActive = _gestureRouter.IsActive;
        ClearGestureState();
        if (wasActive && releaseCapture)
            _canvas.ReleaseMouseCapture();
    }

    private void ClearGestureState()
    {
        _gestureRouter.Cancel();
        ClearTransientInteractionVisuals();
    }

    private void ClearTransientInteractionVisuals()
    {
        _canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        _adorner.UpdatePreview(null);
        _adorner.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        _adorner.UpdateGeometryPreview(null, null);
        _adorner.UpdateMarquee(null);
        _adorner.UpdateSnapGuides(null, SlideTransformCore.Identity);
    }

    // ── Move gesture ──────────────────────────────────────────────────────────────────────────

    // ── Resize gesture ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Given the current drag screen point, computes the new shape bounds in EMU.
    /// Handles all 8 resize directions.
    /// Wave 12B: the DRAGGED edge is snapped to the grid / shape edges using the same
    /// SnapEngine path as move.  The opposite (anchored) edge is never moved.
    /// AD3: the drag delta is un-rotated into the shape's local frame before the edge math,
    /// and the anchor corner is kept fixed in world space after rotation.
    /// </summary>
    public (long newX, long newY, long newCx, long newCy) ComputeResizeBounds(
        Point screenPt, SlideTransform xf)
    {
        var result = _gestureRouter.PlanResize(
            ToGesturePoint(screenPt),
            ToCoreTransform(xf),
            ToGestureModifiers(Keyboard.Modifiers));

        return (result.XEmu, result.YEmu, result.CxEmu, result.CyEmu);
    }

    // ── Rotate gesture ────────────────────────────────────────────────────────────────────────

    // ── Preset geometry edit-point gesture ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the new absolute rotation angle in degrees given the current drag position.
    /// Snaps to 15° increments when Shift is held.
    /// </summary>
    public double ComputeRotationAngle(Point screenPt, SlideTransform xf)
    {
        return _gestureRouter.PlanRotation(
            ToGesturePoint(screenPt),
            ToCoreTransform(xf),
            ToGestureModifiers(Keyboard.Modifiers));
    }

    // ── Marquee gesture ───────────────────────────────────────────────────────────────────────

    // ── Keyboard ──────────────────────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKeyDown(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    private bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        var plan = _gestureRouter.HandleKeyDown(
            ToGestureKey(key),
            ToGestureModifiers(modifiers));
        if (plan.Action == CanvasGestureKeyboardActionKind.CancelGesture)
            CancelActiveGesture(releaseCapture: true);
        return plan.Handled;
    }

    // ── Cursor feedback ───────────────────────────────────────────────────────────────────────

    private void UpdateCursor(Point screenPt)
    {
        _canvas.Cursor = _glue.PlanCursor(
            ToGesturePoint(screenPt),
            ToCoreTransform(_canvas.CurrentTransform)) switch
        {
            CanvasGestureCursorKind.Pointer => Cursors.Hand,
            CanvasGestureCursorKind.Move => Cursors.SizeAll,
            CanvasGestureCursorKind.Rotate => Cursors.Cross,
            CanvasGestureCursorKind.ResizeNorthSouth => Cursors.SizeNS,
            CanvasGestureCursorKind.ResizeWestEast => Cursors.SizeWE,
            CanvasGestureCursorKind.ResizeNorthEastSouthWest => Cursors.SizeNESW,
            CanvasGestureCursorKind.ResizeNorthWestSouthEast => Cursors.SizeNWSE,
            _ => Cursors.Arrow
        };
    }

    // ── Adorner refresh ───────────────────────────────────────────────────────────────────────

    private void RefreshAdorner()
    {
        _adorner.UpdateProjection(
            _glue.BuildAdornerProjection(ToCoreTransform(_canvas.CurrentTransform)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static CanvasGesturePoint ToGesturePoint(Point point)
        => new(point.X, point.Y);

    private static CanvasGestureKey ToGestureKey(Key key) =>
        CanvasGestureNativeInputMapper.MapKeyName(key.ToString());

    // Meta policy: Keep. The Windows key stays visible to the gesture planner (additive-selection modifier).
    private static FreeKeyModifiers ToGestureModifiers(ModifierKeys modifiers) =>
        WpfKeyModifiersMapper.FromWpf(modifiers, KeyModifierMetaPolicy.Keep);

    private static SlideTransformCore ToCoreTransform(SlideTransform xf)
        => xf.Core;

    private static Rect ToWpfRect(SlideScreenRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect ToWpfRect(SelectionAdornerRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);
}
