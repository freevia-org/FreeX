using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Handles all pointer/keyboard interactions on the Avalonia <see cref="SlideCanvas"/> for editing:
/// selection (click / Ctrl+click / Shift+click), marquee drag, move, resize, rotate,
/// delete key, and arrow-key nudge.
///
/// Architecture:
/// <list type="bullet">
///   <item>Attaches to <see cref="SlideCanvas"/> pointer/keyboard events.</item>
///   <item>Maintains live-preview state during drag; commits one command to
///         <see cref="EditingSession"/> on pointer-up.</item>
///   <item>Drives a <see cref="SelectionAdornerLayer"/> for visual feedback.</item>
///   <item>All coordinate work uses the framework-free gesture router and preview projector.</item>
/// </list>
/// </summary>
public sealed partial class AvaloniaCanvasGestureHandler : IDisposable
{
    // ── Wiring ─────────────────────────────────────────────────────────────────

    private readonly SlideCanvas            _canvas;
    private readonly EditingSession         _editor;
    private readonly SelectionAdornerLayer  _adorner;
    private readonly Action<ChartPointHit>? _onChartPointDoubleClick;
    private readonly Func<SlideShape, bool>? _tryOpenOleInPlace;
    private readonly Func<OleObjectInfo?, bool>? _tryActivateOleExternally;
    private IPointer? _capturedPointer;
    private bool _disposed;

    // ── Drag state ─────────────────────────────────────────────────────────────

    private readonly CanvasGestureRouter _gestureRouter;
    private readonly CanvasGesturePreviewSurfaceAdapter _previewSurface;

    // ── Move ───────────────────────────────────────────────────────────────────

    // ── Resize ─────────────────────────────────────────────────────────────────

    // ── Rotate ─────────────────────────────────────────────────────────────────

    // Preset geometry edit-point gesture

    // ── Marquee ────────────────────────────────────────────────────────────────

    // ── Nudge steps ────────────────────────────────────────────────────────────
    // ── Snap settings ──────────────────────────────────────────────────────────
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

    /// <summary>
    /// The <see cref="EditingSession"/> this handler drives selection/move/resize/rotate
    /// commands through. Exposed so <see cref="SlideCanvas.AttachGestureHandler"/> can point its
    /// UIA automation peer's selection-change subscription (see
    /// <c>SlideCanvasAutomationPeer</c>) at the same session, mirroring the WPF twin
    /// (FreeP.App.Rendering.Wpf.SlideCanvas tracks its EditingSession directly since its
    /// gesture handler is constructed inline).
    /// </summary>
    public EditingSession Editor => _editor;

    // ── Construction / attach ──────────────────────────────────────────────────

    public AvaloniaCanvasGestureHandler(SlideCanvas canvas, EditingSession editor,
                                         SelectionAdornerLayer adorner,
                                         Action<ChartPointHit>? onChartPointDoubleClick = null,
                                         Func<SlideShape, bool>? tryOpenOleInPlace = null,
                                         Func<OleObjectInfo?, bool>? tryActivateOleExternally = null)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _adorner = adorner ?? throw new ArgumentNullException(nameof(adorner));
        _gestureRouter = new CanvasGestureRouter(_editor);
        _previewSurface = new(
            (bounds, rotation) => _adorner.UpdatePreview(
                bounds is { } value ? ToAvaloniaRect(value) : null,
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
            bounds => _adorner.UpdateMarquee(ToAvaloniaRect(bounds)));
        _onChartPointDoubleClick = onChartPointDoubleClick;
        _tryOpenOleInPlace = tryOpenOleInPlace;
        _tryActivateOleExternally = tryActivateOleExternally;

        _canvas.PointerPressed      += OnPointerPressed;
        _canvas.PointerReleased     += OnPointerReleased;
        _canvas.PointerMoved        += OnPointerMoved;
        _canvas.PointerCaptureLost  += OnPointerCaptureLost;

        // Keyboard events are raised on the top-level window; caller must subscribe
        // the canvas's parent (the window) and delegate to HandleKeyDown.

        _editor.SelectionChanged    += OnEditorSelectionChanged;
        _editor.Changed             += OnEditorChanged;
        _editor.CurrentSlideChanged += OnEditorCurrentSlideChanged;
        RefreshAdorner();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.PointerPressed     -= OnPointerPressed;
        _canvas.PointerReleased    -= OnPointerReleased;
        _canvas.PointerMoved       -= OnPointerMoved;
        _canvas.PointerCaptureLost -= OnPointerCaptureLost;
        _editor.SelectionChanged   -= OnEditorSelectionChanged;
        _editor.Changed            -= OnEditorChanged;
        _editor.CurrentSlideChanged -= OnEditorCurrentSlideChanged;
        ClearGestureState();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e) => RefreshAdorner();
    private void OnEditorChanged() => RefreshAdorner();
    private void OnEditorCurrentSlideChanged(object? sender, EventArgs e) => RefreshAdorner();

    /// <summary>Captures the selected source and arms source-then-target Format Painter mode.</summary>
    public bool BeginFormatPainter() => _editor.BeginFormatPainter();

    /// <summary>Disarms source-then-target Format Painter mode.</summary>
    public void CancelFormatPainter() => _editor.CancelFormatPainter();

    // ── Keyboard (called by MainWindow.KeyDown handler) ────────────────────────

    /// <summary>
    /// Process a key-down event forwarded from the main window.
    /// Returns true if the key was handled and the event should be marked as handled.
    /// </summary>
    public bool HandleKeyDown(Key key, KeyModifiers modifiers)
    {
        var plan = _gestureRouter.HandleKeyDown(
            ToGestureKey(key),
            ToGestureModifiers(modifiers));
        if (plan.Action == CanvasGestureKeyboardActionKind.CancelGesture)
            CancelActiveGesture(releaseCapture: true);
        return plan.Handled;
    }

    // ── Pointer capture lost ───────────────────────────────────────────────────

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Do NOT call e.Pointer.Capture(null) here: the framework has already released it.
        CancelActiveGesture(releaseCapture: false);
    }

    private void CompleteGesture(Point pt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        _gestureRouter.CompletePointer(
            ToGesturePoint(pt),
            xf,
            ToGestureModifiers(modifiers));
        ClearTransientInteractionVisuals();
    }

    private void CancelActiveGesture(bool releaseCapture)
    {
        IPointer? pointer = _capturedPointer;
        bool wasActive = _gestureRouter.IsActive;
        ClearGestureState();
        if (wasActive && releaseCapture)
            pointer?.Capture(null);
    }

    private void ClearGestureState()
    {
        _gestureRouter.Cancel();
        ClearTransientInteractionVisuals();
    }

    private void ClearTransientInteractionVisuals()
    {
        _capturedPointer = null;
        _canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        _adorner.UpdatePreview(null);
        _adorner.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        _adorner.UpdateGeometryPreview(null, null);
        _adorner.UpdateMarquee(null);
        _adorner.UpdateSnapGuides(null, SlideTransformCore.Identity);
    }

    // ── Pointer down ───────────────────────────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;

        _canvas.Focus();
        var pt    = e.GetPosition(_canvas);
        var xf    = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;

        if (slide is null || _editor.Presentation is null) return;

        var plan = _gestureRouter.HandlePointerPressed(CreatePressRequest(
            pt,
            xf,
            slide,
            e.ClickCount,
            ToGestureModifiers(e.KeyModifiers)));
        ApplyPressAction(plan);
        if (plan.CapturePointer)
            CapturePointer(e.Pointer);
        e.Handled = plan.Handled;
        return;

    }

    private CanvasGesturePressRequest CreatePressRequest(
        Point point,
        SlideTransformCore transform,
        Slide slide,
        int clickCount,
        CanvasGestureModifiers modifiers)
    {
        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            _editor.Presentation!,
            _editor.SelectedShapeIds,
            transform,
            EditPointsEnabled);
        return CanvasGestureInteractionPlanner.BuildPressRequest(
            ToGesturePoint(point),
            transform,
            projection,
            clickCount,
            modifiers,
            EditPointsEnabled,
            _onChartPointDoubleClick is not null);
    }

    private void ApplyPressAction(CanvasGesturePressPlan plan)
        => CanvasGesturePressActionDispatcher.Dispatch(
            plan,
            _onChartPointDoubleClick,
            shape => HandleOleDoubleClick(shape));

    private bool HandleOleDoubleClick(SlideShape shape) =>
        OleActivationCoordinator.TryActivate(
            shape,
            _tryOpenOleInPlace,
            _tryActivateOleExternally);

    internal static bool ShouldContinueDoubleClickSelection(SlideShape? shape) =>
        CanvasGesturePlanner.ShouldContinueDoubleClickSelection(shape);

    // ── Pointer move ───────────────────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pt = e.GetPosition(_canvas);

        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            UpdateCursor(pt);
            return;
        }

        var transform = _canvas.CurrentTransform;
        ApplyPreviewPlan(
            _gestureRouter.PreviewPointer(
                ToGesturePoint(pt),
                transform,
                ToGestureModifiers(e.KeyModifiers)),
            transform);
    }

    // ── Pointer up ─────────────────────────────────────────────────────────────

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        CompleteGesture(
            e.GetPosition(_canvas),
            _canvas.CurrentTransform,
            e.KeyModifiers);
        // Release pointer capture (capture-lost handler is guarded by _gesture == None check above).
        e.Pointer.Capture(null);
    }

    // ── Move gesture ───────────────────────────────────────────────────────────

    private void CapturePointer(IPointer pointer)
    {
        _capturedPointer = pointer;
        pointer.Capture(_canvas);
    }

    private void ApplyPreviewPlan(CanvasGesturePreviewPlan plan, SlideTransformCore transform)
    {
        var visual = CanvasGesturePreviewProjector.Project(
            plan,
            _editor.CurrentSlide,
            _editor.Presentation,
            transform);
        CanvasGesturePreviewDispatcher.Apply(visual, transform, _previewSurface);
    }

    // ── Resize gesture ─────────────────────────────────────────────────────────

    /// <summary>Computes new shape bounds in EMU given the current drag point.</summary>
    /// <param name="modifiers">
    /// Key modifiers at the time of the drag event.
    /// When <see cref="KeyModifiers.Alt"/> is set, snapping is bypassed
    /// (PowerPoint convention for precise off-grid placement).
    /// </param>
    public (long newX, long newY, long newCx, long newCy) ComputeResizeBounds(
        Point screenPt, SlideTransformCore xf, KeyModifiers modifiers = KeyModifiers.None)
    {
        var result = _gestureRouter.PlanResize(
            ToGesturePoint(screenPt),
            xf,
            ToGestureModifiers(modifiers));

        return (result.XEmu, result.YEmu, result.CxEmu, result.CyEmu);
    }

    // ── Rotate gesture ─────────────────────────────────────────────────────────

    // ── Preset geometry edit-point gesture ─────────────────────────────────────────────────────

    /// <summary>Computes new absolute rotation angle in degrees.</summary>
    public double ComputeRotationAngle(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        return _gestureRouter.PlanRotation(
            ToGesturePoint(screenPt),
            xf,
            ToGestureModifiers(modifiers));
    }

    // ── Marquee gesture ────────────────────────────────────────────────────────

    // ── Cursor feedback ────────────────────────────────────────────────────────

    private void UpdateCursor(Point screenPt)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
        {
            _canvas.Cursor = Cursor.Default;
            return;
        }

        var transform = _canvas.CurrentTransform;
        var projection = SelectionAdornerGeometry.BuildProjection(
            slide,
            _editor.Presentation,
            _editor.SelectedShapeIds,
            transform,
            EditPointsEnabled);
        _canvas.Cursor = CanvasGestureInteractionPlanner.PlanCursor(
            slide,
            _editor.Presentation,
            _editor.SelectedShapeIds,
            transform,
            projection,
            ToGesturePoint(screenPt),
            EditPointsEnabled) switch
        {
            CanvasGestureCursorKind.Pointer => new Cursor(StandardCursorType.Hand),
            CanvasGestureCursorKind.Move => new Cursor(StandardCursorType.SizeAll),
            CanvasGestureCursorKind.Rotate => new Cursor(StandardCursorType.Cross),
            CanvasGestureCursorKind.ResizeNorthSouth => new Cursor(StandardCursorType.SizeNorthSouth),
            CanvasGestureCursorKind.ResizeWestEast => new Cursor(StandardCursorType.SizeWestEast),
            CanvasGestureCursorKind.ResizeNorthEastSouthWest => new Cursor(StandardCursorType.TopRightCorner),
            CanvasGestureCursorKind.ResizeNorthWestSouthEast => new Cursor(StandardCursorType.TopLeftCorner),
            _ => Cursor.Default
        };
    }

    // ── Adorner refresh ────────────────────────────────────────────────────────

    private void RefreshAdorner()
    {
        var slide = _editor.CurrentSlide;
        var projection = slide is null || _editor.Presentation is not { } presentation
            ? SelectionAdornerProjectionPlan.Empty
            : SelectionAdornerGeometry.BuildProjection(
                slide,
                presentation,
                _editor.SelectedShapeIds,
                _canvas.CurrentTransform,
                EditPointsEnabled);

        _adorner.UpdateProjection(projection);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static CanvasGesturePoint ToGesturePoint(Point point)
        => new(point.X, point.Y);

    private static CanvasGestureKey ToGestureKey(Key key) =>
        CanvasGestureNativeInputMapper.MapKeyName(key.ToString());

    private static CanvasGestureModifiers ToGestureModifiers(KeyModifiers modifiers) =>
        CanvasGestureNativeInputMapper.MapModifiers(
            modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Alt),
            modifiers.HasFlag(KeyModifiers.Meta));

    private static Rect ToAvaloniaRect(SlideScreenRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect ToAvaloniaRect(SelectionAdornerRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

}
