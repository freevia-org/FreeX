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
///   <item>All coordinate work uses the framework-free helpers
///         <see cref="SlideTransformCore"/> and <see cref="ShapeHitTester"/>.</item>
/// </list>
/// </summary>
public sealed class AvaloniaCanvasGestureHandler : IDisposable
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
        var selectionHandle = CanvasGestureHandleKind.None;
        string? geometryHandle = null;
        bool hasSingleSelectionFrame = false;

        if (_editor.SelectedShapeIds.Count > 1 && _adorner.SelectionBounds is { } groupRect)
        {
            selectionHandle = _adorner.HitTestHandle(groupRect, point);
        }
        else if (_editor.SelectedShapeIds.Count == 1)
        {
            var selectionRect = GetSelectionScreenRect(
                _editor.SelectedShapeIds[0],
                slide,
                transform);
            if (selectionRect.HasValue)
            {
                hasSingleSelectionFrame = true;
                selectionHandle = _adorner.HitTestHandle(selectionRect.Value, point);
                if (EditPointsEnabled)
                    geometryHandle = _adorner.HitTestGeometryHandle(point);
            }
        }

        var slidePoint = transform.ScreenToSlide(point.X, point.Y);
        return new CanvasGesturePressRequest(
            ToGesturePoint(point),
            new CanvasGesturePoint(slidePoint.X, slidePoint.Y),
            clickCount,
            modifiers,
            selectionHandle,
            geometryHandle,
            hasSingleSelectionFrame,
            _onChartPointDoubleClick is not null);
    }

    private void ApplyPressAction(CanvasGesturePressPlan plan)
    {
        switch (plan.Action)
        {
            case CanvasGesturePressActionKind.NotifyChartPointDoubleClick
                when plan.ChartPoint is { } chartPoint:
                _onChartPointDoubleClick?.Invoke(chartPoint);
                break;
            case CanvasGesturePressActionKind.ActivateOle when plan.Shape is { } shape:
                HandleOleDoubleClick(shape);
                break;
        }
    }

    /// <summary>
    /// Applies the same in-place-first OLE activation order as the WPF canvas.
    /// The optional external callback is a test seam; production falls back to the
    /// shared activation service when no native in-place host can be created.
    /// </summary>
    private bool HandleOleDoubleClick(SlideShape shape)
    {
        if (_tryOpenOleInPlace?.Invoke(shape) == true)
            return true;

        return _tryActivateOleExternally?.Invoke(shape.OleObject)
            ?? OleActivationService.TryActivate(shape.OleObject);
    }

    internal bool HandleOleDoubleClickForTests(SlideShape shape) => HandleOleDoubleClick(shape);

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
        switch (plan.Kind)
        {
            case CanvasGestureKind.Move when plan.Move is { } move:
                _adorner.UpdatePreview(
                    move.PreviewBounds is { } bounds ? ToAvaloniaRect(bounds) : null);
                _adorner.UpdateSnapGuides(
                    move.SnapGuides.Count > 0 ? move.SnapGuides : null,
                    transform);
                break;

            case CanvasGestureKind.Resize when plan.MultiTransform is { } multiResize:
                _adorner.UpdateTransformPreview(multiResize);
                _canvas.UpdateTransformPreview(multiResize);
                break;

            case CanvasGestureKind.Resize when plan.Resize is { } resize:
                var resizeRect = SlideCanvasGeometryPlanner.EmuBoundsToScreen(
                    resize.XEmu,
                    resize.YEmu,
                    resize.CxEmu,
                    resize.CyEmu,
                    transform);
                _adorner.UpdatePreview(ToAvaloniaRect(resizeRect));
                break;

            case CanvasGestureKind.Rotate when plan.MultiTransform is { } multiRotate:
                _adorner.UpdateTransformPreview(multiRotate);
                _canvas.UpdateTransformPreview(multiRotate);
                break;

            case CanvasGestureKind.Rotate when plan.RotationDegrees is { } angle:
                if (_editor.CurrentSlide is { } slide &&
                    GetSelectionScreenRect(plan.ShapeId, slide, transform) is { } selectionRect)
                {
                    _adorner.UpdatePreview(selectionRect, angle);
                }
                break;

            case CanvasGestureKind.GeometryAdjustment when plan.Geometry is { } geometry:
                var geometryScreen = transform.SlideToScreen(
                    geometry.PositionSlide.X,
                    geometry.PositionSlide.Y);
                _adorner.UpdateGeometryPreview(
                    geometry.HandleName,
                    new Point(geometryScreen.X, geometryScreen.Y));
                break;

            case CanvasGestureKind.Marquee when plan.Marquee is { } marquee:
                _adorner.UpdateMarquee(ToAvaloniaRect(marquee));
                break;
        }
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

        var xf = _canvas.CurrentTransform;

        if (_editor.SelectedShapeIds.Count > 1 && _adorner.SelectionBounds is { } groupRect)
        {
            var groupHandle = _adorner.HitTestHandle(groupRect, screenPt);
            _canvas.Cursor = groupHandle switch
            {
                CanvasGestureHandleKind.Rotate => new Cursor(StandardCursorType.Cross),
                CanvasGestureHandleKind.ResizeN or CanvasGestureHandleKind.ResizeS => new Cursor(StandardCursorType.SizeNorthSouth),
                CanvasGestureHandleKind.ResizeE or CanvasGestureHandleKind.ResizeW => new Cursor(StandardCursorType.SizeWestEast),
                CanvasGestureHandleKind.ResizeNE or CanvasGestureHandleKind.ResizeSW => new Cursor(StandardCursorType.TopRightCorner),
                CanvasGestureHandleKind.ResizeNW or CanvasGestureHandleKind.ResizeSE => new Cursor(StandardCursorType.TopLeftCorner),
                _ => Cursor.Default
            };
            if (groupHandle != CanvasGestureHandleKind.None)
                return;
        }

        if (_editor.SelectedShapeIds.Count == 1)
        {
            var selId = _editor.SelectedShapeIds[0];
            var selRect = GetSelectionScreenRect(selId, slide, xf);
            if (selRect.HasValue)
            {
                if (EditPointsEnabled && _adorner.HitTestGeometryHandle(screenPt) is not null)
                {
                    _canvas.Cursor = new Cursor(StandardCursorType.Hand);
                    return;
                }

                var handle = _adorner.HitTestHandle(selRect.Value, screenPt);
                _canvas.Cursor = handle switch
                {
                    CanvasGestureHandleKind.Rotate              => new Cursor(StandardCursorType.Cross),
                    CanvasGestureHandleKind.ResizeN or
                    CanvasGestureHandleKind.ResizeS             => new Cursor(StandardCursorType.SizeNorthSouth),
                    CanvasGestureHandleKind.ResizeE or
                    CanvasGestureHandleKind.ResizeW             => new Cursor(StandardCursorType.SizeWestEast),
                    CanvasGestureHandleKind.ResizeNE or
                    CanvasGestureHandleKind.ResizeSW            => new Cursor(StandardCursorType.TopRightCorner),
                    CanvasGestureHandleKind.ResizeNW or
                    CanvasGestureHandleKind.ResizeSE            => new Cursor(StandardCursorType.TopLeftCorner),
                    CanvasGestureHandleKind.Body                => new Cursor(StandardCursorType.SizeAll),
                    _                                                    => Cursor.Default
                };
                return;
            }
        }

        // Check if hovering over any selected body
        var slidePt = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        if (CanvasGesturePlanner.HitSelectedShapeBody(
            slide,
            _editor.Presentation,
            _editor.SelectedShapeIds,
            new CanvasGesturePoint(slidePt.X, slidePt.Y),
            includeNestedShapes: false))
        {
            _canvas.Cursor = new Cursor(StandardCursorType.SizeAll);
            return;
        }

        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        _canvas.Cursor = hitId.HasValue ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
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

        _adorner.UpdateSelection(projection.Selections.Select(selection =>
            (selection.ShapeId, ToAvaloniaRect(selection.ScreenRect))));
        _adorner.UpdateGeometryHandles(projection.GeometryHandles.Select(handle =>
            (handle.Name, new Point(handle.ScreenPosition.X, handle.ScreenPosition.Y))));
    }

    // ── Test seeding (InternalsVisibleTo test project) ─────────────────────────

    /// <summary>
    /// Seeds the internal resize state so that
    /// <see cref="ComputeResizeBounds"/> can be exercised in unit tests
    /// without requiring live pointer events.
    /// </summary>
    internal void SeedResizeState(Point startScreen, SlideShape shape, CanvasGestureHandleKind handle)
    {
        if (_editor.CurrentSlide is null ||
            !_gestureRouter.BeginResize(
                _editor.CurrentSlide,
                shape.Id,
                handle,
                ToGesturePoint(startScreen)))
        {
            throw new InvalidOperationException("The shape must belong to the current slide.");
        }
    }

    internal void SeedMoveStateForTests(Point startScreen)
    {
        if (_editor.CurrentSlide is null)
            throw new InvalidOperationException("A current slide is required to seed a move gesture.");

        _gestureRouter.BeginMove(
            _editor.CurrentSlide,
            _editor.SelectedShapeIds,
            ToGesturePoint(startScreen));
    }

    internal void CompleteGestureForTests(Point currentScreen) =>
        CompleteGesture(currentScreen, _canvas.CurrentTransform, KeyModifiers.None);

    // ── Helpers ────────────────────────────────────────────────────────────────

    internal bool IsGestureActiveForTests => _gestureRouter.IsActive;

    internal bool HasPendingGestureStateForTests => _gestureRouter.HasPendingState;

    internal bool HasTransientInteractionVisualsForTests =>
        _adorner.HasTransientInteractionVisualsForTests ||
        _canvas.HasLiveTransformPreviewForTests;

    internal void SimulateCaptureLossForTests() => CancelActiveGesture(releaseCapture: false);

    internal void SimulateStalePointerUpForTests() =>
        CompleteGesture(new Point(0, 0), SlideTransformCore.Identity, KeyModifiers.None);

    internal void SeedTransientInteractionVisualsForTests()
    {
        if (_editor.CurrentSlide is { } slide)
        {
            var previewRouter = new CanvasGestureRouter(_editor)
            {
                SnapToGrid = false,
                SnapToShapes = false,
            };
            if (previewRouter.BeginMultiResize(
                slide,
                _editor.SelectedShapeIds,
                CanvasGestureHandleKind.ResizeSE,
                new CanvasGesturePoint(0, 0)))
            {
                var preview = previewRouter.PreviewPointer(
                    new CanvasGesturePoint(4, 4),
                    _canvas.CurrentTransform,
                    CanvasGestureModifiers.None);
                if (preview.MultiTransform is { } transform)
                    _canvas.UpdateTransformPreview(transform);
            }
        }
        _adorner.UpdatePreview(new Rect(1, 1, 10, 10));
        _adorner.UpdateGeometryPreview("test", new Point(2, 2));
        _adorner.UpdateMarquee(new Rect(3, 3, 8, 8));
        _adorner.UpdateSnapGuides(
            [new SnapGuideLine { IsHorizontal = true, Position = 4, Label = "test" }],
            SlideTransformCore.Identity);
    }

    private static CanvasGesturePoint ToGesturePoint(Point point)
        => new(point.X, point.Y);

    private static CanvasGestureKey ToGestureKey(Key key) => key switch
    {
        Key.Escape => CanvasGestureKey.Escape,
        Key.Left => CanvasGestureKey.Left,
        Key.Right => CanvasGestureKey.Right,
        Key.Up => CanvasGestureKey.Up,
        Key.Down => CanvasGestureKey.Down,
        Key.Delete => CanvasGestureKey.Delete,
        Key.Back => CanvasGestureKey.Backspace,
        Key.Insert => CanvasGestureKey.Insert,
        _ => CanvasGestureKey.None,
    };

    private static CanvasGestureModifiers ToGestureModifiers(KeyModifiers modifiers)
    {
        var result = CanvasGestureModifiers.None;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= CanvasGestureModifiers.Shift;
        if ((modifiers & KeyModifiers.Control) != 0)
            result |= CanvasGestureModifiers.Control;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= CanvasGestureModifiers.Alt;
        if ((modifiers & KeyModifiers.Meta) != 0)
            result |= CanvasGestureModifiers.Meta;
        return result;
    }

    private Rect? GetSelectionScreenRect(uint shapeId, Slide slide, SlideTransformCore xf)
    {
        if (_editor.Presentation is null) return null;
        var shape = ShapeHitTester.FindShape(slide, shapeId);
        var rect = shape is null
            ? (SlideScreenRect?)null
            : SlideCanvasGeometryPlanner.ShapeVisualBoundsToScreen(
                shape,
                _editor.Presentation,
                xf);
        return rect is { } screenRect ? ToAvaloniaRect(screenRect) : null;
    }

    private static Rect ToAvaloniaRect(SlideScreenRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect ToAvaloniaRect(SelectionAdornerRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

}
