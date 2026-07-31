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
    private IPointer? _capturedPointer;
    private bool _disposed;

    // ── Drag state ─────────────────────────────────────────────────────────────

    private enum GestureKind { None, Move, Resize, Rotate, GeometryAdjustment, Marquee }

    private GestureKind _gesture = GestureKind.None;
    private Point       _dragStartScreen;
    private bool        _dragStarted;   // true once pointer has moved > threshold

    // ── Move ───────────────────────────────────────────────────────────────────
    private IReadOnlyList<CanvasMoveShapeState>? _moveStartShapes;

    // ── Resize ─────────────────────────────────────────────────────────────────
    private uint                              _resizeShapeId;
    private long                              _resizeOrigX, _resizeOrigY, _resizeOrigCx, _resizeOrigCy;
    private double                            _resizeOrigRotationDeg;
    private CanvasGestureHandleKind           _resizeHandle;
    private IReadOnlyList<CanvasTransformShapeState>? _multiTransformStartShapes;

    // ── Rotate ─────────────────────────────────────────────────────────────────
    private uint   _rotateShapeId;
    private double _rotateOrigDeg;
    private Point  _rotateCenterSlide; // shape center in slide DIP

    // Preset geometry edit-point gesture
    private uint _geometryShapeId;
    private string? _geometryHandleName;
    private LayoutRect _geometryBoundsDip;
    private Point _geometryDragStartScreen;

    // ── Marquee ────────────────────────────────────────────────────────────────
    private Point _marqueeStartSlide;

    // ── Nudge steps ────────────────────────────────────────────────────────────
    private const long SmallNudgeEmu = 91440L;   // ~0.1 inch
    private const long LargeNudgeEmu = 914400L;  // 1 inch

    // ── Snap settings ──────────────────────────────────────────────────────────
    /// <summary>When true (default), shapes snap to the background grid during move/resize.</summary>
    public bool SnapToGrid   { get; set; } = true;

    /// <summary>When true (default), shapes snap to other shapes' edges and centers during move/resize.</summary>
    public bool SnapToShapes { get; set; } = true;

    private bool _editPointsEnabled = true;

    /// <summary>When enabled, supported preset shapes expose draggable edit points.</summary>
    public bool EditPointsEnabled
    {
        get => _editPointsEnabled;
        set
        {
            if (_editPointsEnabled == value)
                return;
            _editPointsEnabled = value;
            RefreshAdorner();
        }
    }

    // ── Construction / attach ──────────────────────────────────────────────────

    public AvaloniaCanvasGestureHandler(SlideCanvas canvas, EditingSession editor,
                                         SelectionAdornerLayer adorner)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _adorner = adorner ?? throw new ArgumentNullException(nameof(adorner));

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
        if (key == Key.Escape)
        {
            switch (CanvasGesturePlanner.ResolveEscapeAction(
                _editor.IsFormatPainterActive,
                _gesture != GestureKind.None))
            {
                case CanvasEscapeAction.CancelFormatPainter:
                    _editor.CancelFormatPainter();
                    return true;
                case CanvasEscapeAction.CancelGesture:
                    CancelActiveGesture(releaseCapture: true);
                    return true;
            }
        }

        if (_editor.SelectedShapeIds.Count == 0) return false;

        if (TryHandleCustomGeometryKey(key))
            return true;

        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        long step  = shift ? LargeNudgeEmu : SmallNudgeEmu;

        switch (key)
        {
            case Key.Left:  _editor.MoveSelected(-step, 0);  return true;
            case Key.Right: _editor.MoveSelected( step, 0);  return true;
            case Key.Up:    _editor.MoveSelected(0, -step);  return true;
            case Key.Down:  _editor.MoveSelected(0,  step);  return true;
            case Key.Delete:
            case Key.Back:
                _editor.DeleteSelected();
                return true;
        }
        return false;
    }

    private bool TryHandleCustomGeometryKey(Key key)
    {
        if (!EditPointsEnabled || _geometryHandleName is null || _editor.SelectedShapeIds.Count != 1)
            return false;

        var shapeId = _editor.SelectedShapeIds[0];
        if (_geometryShapeId != shapeId)
            return false;

        var handled = key switch
        {
            Key.Insert => _editor.TryInsertCustomGeometryPoint(shapeId, _geometryHandleName),
            Key.Delete or Key.Back => _editor.TryDeleteCustomGeometryPoint(shapeId, _geometryHandleName),
            _ => false,
        };
        if (handled)
            _geometryHandleName = null;
        return handled;
    }

    // ── Pointer capture lost ───────────────────────────────────────────────────

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Do NOT call e.Pointer.Capture(null) here: the framework has already released it.
        CancelActiveGesture(releaseCapture: false);
    }

    private void CompleteGesture(Point pt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        switch (_gesture)
        {
            case GestureKind.Move:    CommitMove(pt, xf, modifiers);    break;
            case GestureKind.Resize:  CommitResize(pt, xf, modifiers);  break;
            case GestureKind.Rotate:  CommitRotate(pt, xf, modifiers);  break;
            case GestureKind.GeometryAdjustment: CommitGeometryAdjustment(pt, xf); break;
            case GestureKind.Marquee: CommitMarquee(pt, xf);            break;
        }

        ClearGestureState();
    }

    private void CancelActiveGesture(bool releaseCapture)
    {
        IPointer? pointer = _capturedPointer;
        bool wasActive = _gesture != GestureKind.None;
        ClearGestureState();
        if (wasActive && releaseCapture)
            pointer?.Capture(null);
    }

    private void ClearGestureState()
    {
        _gesture = GestureKind.None;
        _dragStarted = false;
        _capturedPointer = null;

        _moveStartShapes = null;
        _multiTransformStartShapes = null;
        _dragStartScreen = default;
        _resizeShapeId = 0;
        _resizeOrigX = _resizeOrigY = _resizeOrigCx = _resizeOrigCy = 0;
        _resizeOrigRotationDeg = 0;
        _resizeHandle = CanvasGestureHandleKind.None;
        _rotateShapeId = 0;
        _rotateOrigDeg = 0;
        _rotateCenterSlide = default;
        _geometryShapeId = 0;
        _geometryHandleName = null;
        _geometryBoundsDip = default;
        _geometryDragStartScreen = default;
        _marqueeStartSlide = default;
        _adorner.UpdatePreview(null);
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

        // PowerPoint's single-click Format Painter waits for the next shape hit instead of
        // turning that click into a move, resize, marquee, OLE activation, or zoom action.
        if (_editor.IsFormatPainterActive)
        {
            var painterSlidePoint = xf.ScreenToSlide(pt.X, pt.Y);
            var painterHitId = ShapeHitTester.HitTest(
                slide,
                _editor.Presentation,
                painterSlidePoint.X,
                painterSlidePoint.Y);
            if (painterHitId.HasValue)
                _editor.TryApplyFormatPainterToShape(painterHitId.Value);

            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            var slidePoint = xf.ScreenToSlide(pt.X, pt.Y);
            var oleHitId = ShapeHitTester.HitTest(
                slide,
                _editor.Presentation,
                slidePoint.X,
                slidePoint.Y);
            var shape = oleHitId.HasValue
                ? ShapeHitTester.FindShape(slide, oleHitId.Value)
                : null;
            if (shape?.Kind == SlideShapeKind.Ole)
            {
                OleActivationService.TryActivate(shape.OleObject);
                e.Handled = true;
                return;
            }
            if (shape?.Kind == SlideShapeKind.Zoom &&
                ZoomNavigationService.TryGetTargetSlideIndex(
                    _editor.Presentation,
                    shape.PreservedObject,
                    out var targetSlideIndex))
            {
                _editor.SelectSlide(targetSlideIndex);
                e.Handled = true;
                return;
            }

            // Text editing remains the responsibility of InCanvasTextEditor. A textless
            // double-click must continue through the normal selection path, matching WPF.
            if (!ShouldContinueDoubleClickSelection(shape))
                return;
        }

        // A multi-selection has one group box. Its handles operate on every selected shape.
        if (_editor.SelectedShapeIds.Count > 1 && _adorner.SelectionBounds is { } groupRect)
        {
            var groupHandle = _adorner.HitTestHandle(groupRect, pt);
            if (groupHandle == CanvasGestureHandleKind.Rotate)
            {
                StartMultiRotate(slide, pt, e.Pointer);
                e.Handled = true;
                return;
            }
            if (groupHandle is not CanvasGestureHandleKind.None and not CanvasGestureHandleKind.Body)
            {
                StartMultiResize(slide, groupHandle, pt, e.Pointer);
                e.Handled = true;
                return;
            }
        }

        // Handle single selection: check handles first.
        if (_editor.SelectedShapeIds.Count == 1)
        {
            var selId   = _editor.SelectedShapeIds[0];
            var selRect = GetSelectionScreenRect(selId, slide, xf);
            if (selRect.HasValue)
            {
                if (EditPointsEnabled && _adorner.HitTestGeometryHandle(pt) is { } geometryHandle)
                {
                    StartGeometryAdjustment(selId, slide, xf, geometryHandle, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }

                var hitHandle = _adorner.HitTestHandle(selRect.Value, pt);
                if (hitHandle == CanvasGestureHandleKind.Rotate)
                {
                    StartRotate(selId, slide, xf, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }
                if (hitHandle != CanvasGestureHandleKind.None &&
                    hitHandle != CanvasGestureHandleKind.Body)
                {
                    StartResize(selId, slide, hitHandle, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }
                if (hitHandle == CanvasGestureHandleKind.Body)
                {
                    StartMove(slide, xf, pt, e.Pointer);
                    e.Handled = true;
                    return;
                }
            }
        }
        else if (_editor.SelectedShapeIds.Count > 1)
        {
            // Multi-select: hit any selected body → move
            var slidePt = xf.ScreenToSlide(pt.X, pt.Y);
            bool hitAny = false;
            foreach (var sid in _editor.SelectedShapeIds)
            {
                var b = ShapeHitTester.GetShapeBoundsDip(
                    slide,
                    _editor.Presentation,
                    sid);
                if (b is { } bounds &&
                    slidePt.X >= bounds.Left && slidePt.X <= bounds.Right &&
                    slidePt.Y >= bounds.Top  && slidePt.Y <= bounds.Bottom)
                { hitAny = true; break; }
            }
            if (hitAny)
            {
                StartMove(slide, xf, pt, e.Pointer);
                e.Handled = true;
                return;
            }
        }

        // Hit-test slide shapes
        var slidePt2 = xf.ScreenToSlide(pt.X, pt.Y);
        var hitId    = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt2.X, slidePt2.Y);

        if (hitId.HasValue)
        {
            bool addToSelection = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) != 0;
            _editor.Select(hitId.Value, addToSelection);
            if (!addToSelection || _editor.SelectedShapeIds.Count <= 1)
                StartMove(slide, xf, pt, e.Pointer);
        }
        else
        {
            _editor.ClearSelection();
            StartMarquee(xf, pt, e.Pointer);
        }

        e.Handled = true;
    }

    internal static bool ShouldContinueDoubleClickSelection(SlideShape? shape) =>
        shape?.TextBody is null;

    // ── Pointer move ───────────────────────────────────────────────────────────

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pt = e.GetPosition(_canvas);

        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            UpdateCursor(pt);
            return;
        }

        var xf        = _canvas.CurrentTransform;
        var slide     = _editor.CurrentSlide;
        var modifiers = e.KeyModifiers;
        if (slide is null) return;

        switch (_gesture)
        {
            case GestureKind.Move:    PreviewMove(pt, xf, slide, modifiers);    break;
            case GestureKind.Resize:  PreviewResize(pt, xf, modifiers);         break;
            case GestureKind.Rotate:  PreviewRotate(pt, xf, modifiers);         break;
            case GestureKind.GeometryAdjustment: PreviewGeometryAdjustment(pt, xf); break;
            case GestureKind.Marquee: PreviewMarquee(pt, xf);                   break;
        }
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

    private void StartMove(Slide slide, SlideTransformCore xf, Point screenPt, IPointer pointer)
    {
        _gesture          = GestureKind.Move;
        _dragStartScreen  = screenPt;
        _dragStarted      = false;
        _moveStartShapes = CanvasGesturePlanner.CaptureMoveState(slide, _editor.SelectedShapeIds);
        CapturePointer(pointer);
    }

    private void PreviewMove(Point screenPt, SlideTransformCore xf, Slide slide, KeyModifiers modifiers)
    {
        var drag = ReduceDrag(screenPt);
        if (!drag.DragStarted) return;
        _dragStarted = drag.DragStarted;

        if (_moveStartShapes is null)
            return;

        var plan = CanvasGesturePlanner.PlanMove(new CanvasMoveRequest(
            StartScreen: ToGesturePoint(_dragStartScreen),
            CurrentScreen: ToGesturePoint(screenPt),
            Transform: xf,
            Shapes: _moveStartShapes,
            CurrentSlide: slide,
            SnapToGrid: SnapToGrid,
            SnapToShapes: SnapToShapes,
            BypassSnap: (modifiers & KeyModifiers.Alt) != 0));

        _adorner.UpdatePreview(plan.PreviewBounds is { } bounds ? ToAvaloniaRect(bounds) : null);
        _adorner.UpdateSnapGuides(plan.SnapGuides.Count > 0 ? plan.SnapGuides : null, xf);
    }

    private void CommitMove(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        var drag = ReduceDrag(screenPt);
        if (_moveStartShapes is null || !_dragStarted || !drag.ShouldCommit) return;

        var plan = CanvasGesturePlanner.PlanMove(new CanvasMoveRequest(
            StartScreen: ToGesturePoint(_dragStartScreen),
            CurrentScreen: ToGesturePoint(screenPt),
            Transform: xf,
            Shapes: _moveStartShapes,
            CurrentSlide: _editor.CurrentSlide,
            SnapToGrid: SnapToGrid,
            SnapToShapes: SnapToShapes,
            BypassSnap: (modifiers & KeyModifiers.Alt) != 0));

        _editor.MoveSelected(plan.DeltaXEmu, plan.DeltaYEmu);
    }

    // ── Resize gesture ─────────────────────────────────────────────────────────

    private void StartResize(uint shapeId, Slide slide, CanvasGestureHandleKind handle, Point screenPt, IPointer pointer)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null) return;
        _gesture                = GestureKind.Resize;
        _dragStartScreen        = screenPt;
        _dragStarted            = false;
        _resizeShapeId          = shapeId;
        _resizeOrigX            = s.OffsetXEmu;
        _resizeOrigY            = s.OffsetYEmu;
        _resizeOrigCx           = s.ExtentCxEmu;
        _resizeOrigCy           = s.ExtentCyEmu;
        _resizeOrigRotationDeg  = s.RotationDeg;
        _resizeHandle           = handle;
        CapturePointer(pointer);
    }

    private void StartMultiResize(
        Slide slide,
        CanvasGestureHandleKind handle,
        Point screenPt,
        IPointer pointer)
    {
        _multiTransformStartShapes = CanvasGesturePlanner.CaptureTransformState(
            slide,
            _editor.SelectedShapeIds);
        if (_multiTransformStartShapes.Count == 0)
            return;

        _gesture = GestureKind.Resize;
        _dragStartScreen = screenPt;
        _dragStarted = false;
        _resizeHandle = handle;
        CapturePointer(pointer);
    }

    private void PreviewResize(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        var drag = ReduceDrag(screenPt);
        if (!drag.DragStarted) return;
        _dragStarted = drag.DragStarted;

        if (_multiTransformStartShapes is not null)
        {
            var plan = CanvasGesturePlanner.PlanMultiResize(new CanvasMultiResizeRequest(
                ToGesturePoint(_dragStartScreen),
                ToGesturePoint(screenPt),
                xf,
                _resizeHandle,
                _multiTransformStartShapes,
                _editor.CurrentSlide,
                SnapToGrid,
                SnapToShapes,
                (modifiers & KeyModifiers.Alt) != 0));
            _adorner.UpdatePreview(
                plan.PreviewBounds is { } groupBounds ? ToAvaloniaRect(groupBounds) : null);
            return;
        }

        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf, modifiers);
        var r = SlideCanvasGeometryPlanner.EmuBoundsToScreen(nx, ny, ncx, ncy, xf);
        _adorner.UpdatePreview(ToAvaloniaRect(r));
    }

    private void CommitResize(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        var drag = ReduceDrag(screenPt);
        if (!_dragStarted || !drag.ShouldCommit) return;

        if (_multiTransformStartShapes is not null)
        {
            var plan = CanvasGesturePlanner.PlanMultiResize(new CanvasMultiResizeRequest(
                ToGesturePoint(_dragStartScreen),
                ToGesturePoint(screenPt),
                xf,
                _resizeHandle,
                _multiTransformStartShapes,
                _editor.CurrentSlide,
                SnapToGrid,
                SnapToShapes,
                (modifiers & KeyModifiers.Alt) != 0));
            _editor.ApplySelectedTransforms(plan.Shapes);
            return;
        }

        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf, modifiers);
        _editor.ResizeShape(_resizeShapeId, nx, ny, ncx, ncy);
    }

    /// <summary>Computes new shape bounds in EMU given the current drag point.</summary>
    /// <param name="modifiers">
    /// Key modifiers at the time of the drag event.
    /// When <see cref="KeyModifiers.Alt"/> is set, snapping is bypassed
    /// (PowerPoint convention for precise off-grid placement).
    /// </param>
    public (long newX, long newY, long newCx, long newCy) ComputeResizeBounds(
        Point screenPt, SlideTransformCore xf, KeyModifiers modifiers = KeyModifiers.None)
    {
        var result = CanvasGesturePlanner.ComputeResizeBounds(new CanvasResizeRequest(
            StartScreen: ToGesturePoint(_dragStartScreen),
            CurrentScreen: ToGesturePoint(screenPt),
            Transform: xf,
            State: new CanvasResizeState(
                _resizeShapeId,
                _resizeOrigX,
                _resizeOrigY,
                _resizeOrigCx,
                _resizeOrigCy,
                _resizeOrigRotationDeg,
                _resizeHandle),
            CurrentSlide: _editor.CurrentSlide,
            SnapToGrid: SnapToGrid,
            SnapToShapes: SnapToShapes,
            BypassSnap: (modifiers & KeyModifiers.Alt) != 0));

        return (result.XEmu, result.YEmu, result.CxEmu, result.CyEmu);
    }

    // ── Rotate gesture ─────────────────────────────────────────────────────────

    private void StartRotate(uint shapeId, Slide slide, SlideTransformCore xf, Point screenPt, IPointer pointer)
    {
        var s = slide.Shapes.FirstOrDefault(sh => sh.Id == shapeId);
        if (s is null) return;
        _gesture         = GestureKind.Rotate;
        _dragStartScreen = screenPt;
        _dragStarted     = false;
        _rotateShapeId   = shapeId;
        _rotateOrigDeg   = s.RotationDeg;
        double cx = SlideTransformCore.EmuToDip(s.OffsetXEmu + s.ExtentCxEmu / 2);
        double cy = SlideTransformCore.EmuToDip(s.OffsetYEmu + s.ExtentCyEmu / 2);
        _rotateCenterSlide = new Point(cx, cy);
        CapturePointer(pointer);
    }

    private void StartMultiRotate(
        Slide slide,
        Point screenPt,
        IPointer pointer)
    {
        _multiTransformStartShapes = CanvasGesturePlanner.CaptureTransformState(
            slide,
            _editor.SelectedShapeIds);
        if (_multiTransformStartShapes.Count == 0)
            return;

        _gesture = GestureKind.Rotate;
        _dragStartScreen = screenPt;
        _dragStarted = false;
        CapturePointer(pointer);
    }

    private void PreviewRotate(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        var drag = ReduceDrag(screenPt);
        if (!drag.DragStarted)
            return;
        _dragStarted = drag.DragStarted;

        if (_multiTransformStartShapes is not null)
        {
            var plan = CanvasGesturePlanner.PlanMultiRotate(new CanvasMultiRotateRequest(
                ToGesturePoint(_dragStartScreen),
                ToGesturePoint(screenPt),
                xf,
                _multiTransformStartShapes,
                (modifiers & KeyModifiers.Shift) != 0));
            _adorner.UpdatePreview(
                plan.PreviewBounds is { } groupBounds ? ToAvaloniaRect(groupBounds) : null,
                plan.PreviewRotationDeg);
            return;
        }

        double angle = ComputeRotationAngle(screenPt, xf, modifiers);
        if (_editor.CurrentSlide is not null && _editor.Presentation is not null)
        {
            var r = GetSelectionScreenRect(_rotateShapeId, _editor.CurrentSlide, xf);
            if (r.HasValue)
                _adorner.UpdatePreview(r.Value, angle);
        }
    }

    private void CommitRotate(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        var drag = ReduceDrag(screenPt);
        if (!_dragStarted || !drag.ShouldCommit) return;

        if (_multiTransformStartShapes is not null)
        {
            var plan = CanvasGesturePlanner.PlanMultiRotate(new CanvasMultiRotateRequest(
                ToGesturePoint(_dragStartScreen),
                ToGesturePoint(screenPt),
                xf,
                _multiTransformStartShapes,
                (modifiers & KeyModifiers.Shift) != 0));
            _editor.ApplySelectedTransforms(plan.Shapes);
            return;
        }

        double angle = ComputeRotationAngle(screenPt, xf, modifiers);
        _editor.RotateShape(_rotateShapeId, angle);
    }

    // ── Preset geometry edit-point gesture ─────────────────────────────────────────────────────

    private void StartGeometryAdjustment(
        uint shapeId,
        Slide slide,
        SlideTransformCore xf,
        string handleName,
        Point screenPt,
        IPointer pointer)
    {
        if (_editor.Presentation is null)
            return;

        var shape = slide.Shapes.FirstOrDefault(candidate => candidate.Id == shapeId);
        if (shape is null)
            return;

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, _editor.Presentation).ToLayoutRect();
        if (shape.Kind == SlideShapeKind.Picture)
        {
            var cropPlan = PictureCropAuthoringPlanner.Build(shape, bounds);
            if (!cropPlan.CanEdit || cropPlan.Handles.All(handle => handle.Name != handleName))
                return;
        }
        else
        {
            var plan = ShapeGeometryAdjustmentPlanner.Build(shape, bounds);
            if (!plan.CanEdit || plan.Handles.All(handle => handle.Name != handleName))
                return;
        }

        _gesture = GestureKind.GeometryAdjustment;
        _geometryShapeId = shapeId;
        _geometryHandleName = handleName;
        _geometryBoundsDip = bounds;
        _geometryDragStartScreen = screenPt;
        _dragStarted = false;
        CapturePointer(pointer);
    }

    private void PreviewGeometryAdjustment(Point screenPt, SlideTransformCore xf)
    {
        if (_geometryHandleName is null)
            return;

        var drag = ReduceDrag(screenPt);
        if (!drag.DragStarted)
            return;
        _dragStarted = true;
        var pointerSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        if (_editor.CurrentSlide is { } slide &&
            slide.Shapes.FirstOrDefault(candidate => candidate.Id == _geometryShapeId) is { Kind: SlideShapeKind.Picture } picture)
        {
            var cropMutation = PictureCropAuthoringPlanner.BuildMutationPlan(
                picture,
                _geometryBoundsDip,
                _geometryHandleName,
                new LayoutPoint(pointerSlide.X, pointerSlide.Y));
            if (cropMutation.Values is { } cropValues)
            {
                var cropPosition = PictureCropAuthoringPlanner.PositionFor(
                    _geometryBoundsDip,
                    cropValues,
                    _geometryHandleName);
                var cropScreen = xf.SlideToScreen(cropPosition.X, cropPosition.Y);
                _adorner.UpdateGeometryPreview(
                    _geometryHandleName,
                    new Point(cropScreen.X, cropScreen.Y));
            }
            return;
        }

        var previewScreen = xf.SlideToScreen(pointerSlide.X, pointerSlide.Y);
        _adorner.UpdateGeometryPreview(_geometryHandleName, new Point(previewScreen.X, previewScreen.Y));
    }

    private void CommitGeometryAdjustment(Point screenPt, SlideTransformCore xf)
    {
        if (!_dragStarted || _geometryHandleName is null || _editor.CurrentSlide is null)
            return;

        var shape = _editor.CurrentSlide.Shapes.FirstOrDefault(candidate => candidate.Id == _geometryShapeId);
        if (shape is null)
            return;

        var pointerSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        if (shape.Kind == SlideShapeKind.Picture)
        {
            var cropMutation = PictureCropAuthoringPlanner.BuildMutationPlan(
                shape,
                _geometryBoundsDip,
                _geometryHandleName,
                new LayoutPoint(pointerSlide.X, pointerSlide.Y));
            if (cropMutation.ShouldApply && cropMutation.Values is { } cropValues)
                _editor.SetPictureCrop(_geometryShapeId, cropValues);
            return;
        }

        var mutation = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            _geometryBoundsDip,
            _geometryHandleName,
            new LayoutPoint(pointerSlide.X, pointerSlide.Y));
        if (mutation.ShouldApply && mutation.CustomPoint is { } customPoint)
        {
            _editor.SetCustomGeometryPoint(
                _geometryShapeId,
                customPoint.PathIndex,
                customPoint.SegmentIndex,
                customPoint.X,
                customPoint.Y,
                customPoint.Slot);
        }
        else if (mutation.ShouldApply && mutation.ArcPoint is { } arcPoint)
        {
            _editor.SetCustomGeometryArcPoint(
                _geometryShapeId,
                arcPoint.PathIndex,
                arcPoint.SegmentIndex,
                arcPoint.Value,
                arcPoint.Slot);
        }
        else if (mutation.ShouldApply && mutation.Name is not null && mutation.Value is { } value)
            _editor.SetShapeGeometryAdjustment(_geometryShapeId, mutation.Name, value);
    }

    /// <summary>Computes new absolute rotation angle in degrees.</summary>
    public double ComputeRotationAngle(Point screenPt, SlideTransformCore xf, KeyModifiers modifiers)
    {
        return CanvasGesturePlanner.ComputeRotationAngle(new CanvasRotationRequest(
            CurrentScreen: ToGesturePoint(screenPt),
            CenterSlide: ToGesturePoint(_rotateCenterSlide),
            Transform: xf,
            OriginalRotationDeg: _rotateOrigDeg,
            SnapToFifteenDegrees: (modifiers & KeyModifiers.Shift) != 0));
    }

    // ── Marquee gesture ────────────────────────────────────────────────────────

    private void StartMarquee(SlideTransformCore xf, Point screenPt, IPointer pointer)
    {
        _gesture           = GestureKind.Marquee;
        _dragStartScreen   = screenPt;
        _dragStarted       = false;
        var slidePoint = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        _marqueeStartSlide = new Point(slidePoint.X, slidePoint.Y);
        CapturePointer(pointer);
    }

    private void PreviewMarquee(Point screenPt, SlideTransformCore xf)
    {
        var drag = ReduceDrag(screenPt);
        if (!drag.DragStarted) return;
        _dragStarted = drag.DragStarted;

        var rect = SlideCanvasGeometryPlanner.ScreenRectBetween(
            ToGesturePoint(_dragStartScreen),
            ToGesturePoint(screenPt));
        _adorner.UpdateMarquee(ToAvaloniaRect(rect));
    }

    private void CommitMarquee(Point screenPt, SlideTransformCore xf)
    {
        _adorner.UpdateMarquee(null);
        var drag = ReduceDrag(screenPt);
        if (!_dragStarted || !drag.ShouldCommit) return;
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var endSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        var ids = ShapeHitTester.MarqueeHitTest(
            slide, _editor.Presentation,
            _marqueeStartSlide.X, _marqueeStartSlide.Y,
            endSlide.X, endSlide.Y);

        if (ids.Count == 0) return;
        _editor.ClearSelection();
        foreach (var id in ids)
            _editor.Select(id, addToSelection: true);
    }

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
        foreach (var id in _editor.SelectedShapeIds)
        {
            var s = slide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            var b = ShapeHitTester.GetShapeBoundsDip(s, _editor.Presentation);
            if (slidePt.X >= b.Left && slidePt.X <= b.Right &&
                slidePt.Y >= b.Top  && slidePt.Y <= b.Bottom)
            {
                _canvas.Cursor = new Cursor(StandardCursorType.SizeAll);
                return;
            }
        }

        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        _canvas.Cursor = hitId.HasValue ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    // ── Adorner refresh ────────────────────────────────────────────────────────

    private void RefreshAdorner()
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
        {
            _adorner.UpdateSelection(Array.Empty<(uint, Rect)>());
            _adorner.UpdateGeometryHandles(Array.Empty<(string Name, Point Position)>());
            return;
        }

        var xf    = _canvas.CurrentTransform;
        var rects = new List<(uint, Rect)>();
        foreach (var id in _editor.SelectedShapeIds)
        {
            var r = GetSelectionScreenRect(id, slide, xf);
            if (r.HasValue)
                rects.Add((id, r.Value));
        }
        _adorner.UpdateSelection(rects);

        if (EditPointsEnabled && _editor.SelectedShapeIds.Count == 1)
        {
            var id = _editor.SelectedShapeIds[0];
            var shape = slide.Shapes.FirstOrDefault(candidate => candidate.Id == id);
            if (shape is not null)
            {
                var bounds = ShapeHitTester.GetShapeBoundsDip(shape, _editor.Presentation).ToLayoutRect();
                IEnumerable<(string Name, Point Position)> handles;
                if (shape.Kind == SlideShapeKind.Picture)
                {
                    var cropPlan = PictureCropAuthoringPlanner.Build(shape, bounds);
                    handles = cropPlan.CanEdit
                        ? cropPlan.Handles.Select(handle =>
                        {
                            var screen = xf.SlideToScreen(handle.PositionDip.X, handle.PositionDip.Y);
                            return (handle.Name, new Point(screen.X, screen.Y));
                        })
                        : Enumerable.Empty<(string Name, Point Position)>();
                }
                else
                {
                    var plan = ShapeGeometryAdjustmentPlanner.Build(shape, bounds);
                    handles = plan.CanEdit
                        ? plan.Handles.Select(handle =>
                        {
                            var screen = xf.SlideToScreen(handle.PositionDip.X, handle.PositionDip.Y);
                            return (handle.Name, new Point(screen.X, screen.Y));
                        })
                        : Enumerable.Empty<(string Name, Point Position)>();
                }
                _adorner.UpdateGeometryHandles(handles);
                return;
            }
        }

        _adorner.UpdateGeometryHandles(Array.Empty<(string Name, Point Position)>());
    }

    // ── Test seeding (InternalsVisibleTo test project) ─────────────────────────

    /// <summary>
    /// Seeds the internal resize state so that
    /// <see cref="ComputeResizeBounds"/> can be exercised in unit tests
    /// without requiring live pointer events.
    /// </summary>
    internal void SeedResizeState(Point startScreen, SlideShape shape, CanvasGestureHandleKind handle)
    {
        _dragStartScreen       = startScreen;
        _dragStarted           = true;
        _resizeShapeId         = shape.Id;
        _resizeOrigX           = shape.OffsetXEmu;
        _resizeOrigY           = shape.OffsetYEmu;
        _resizeOrigCx          = shape.ExtentCxEmu;
        _resizeOrigCy          = shape.ExtentCyEmu;
        _resizeOrigRotationDeg = shape.RotationDeg;
        _resizeHandle          = handle;
        _gesture               = GestureKind.Resize;
    }

    internal void SeedMoveStateForTests(Point startScreen)
    {
        if (_editor.CurrentSlide is null)
            throw new InvalidOperationException("A current slide is required to seed a move gesture.");

        _dragStartScreen = startScreen;
        _dragStarted = false;
        _moveStartShapes = CanvasGesturePlanner.CaptureMoveState(
            _editor.CurrentSlide,
            _editor.SelectedShapeIds);
        _gesture = GestureKind.Move;
    }

    internal void CompleteGestureForTests(Point currentScreen) =>
        CompleteGesture(currentScreen, _canvas.CurrentTransform, KeyModifiers.None);

    // ── Helpers ────────────────────────────────────────────────────────────────

    internal bool IsGestureActiveForTests => _gesture != GestureKind.None;

    internal bool HasPendingGestureStateForTests =>
        _moveStartShapes is not null ||
        _multiTransformStartShapes is not null ||
        _resizeShapeId != 0 ||
        _rotateShapeId != 0 ||
        _geometryShapeId != 0 ||
        _geometryHandleName is not null;

    internal bool HasTransientInteractionVisualsForTests =>
        _adorner.HasTransientInteractionVisualsForTests;

    internal void SimulateCaptureLossForTests() => CancelActiveGesture(releaseCapture: false);

    internal void SimulateStalePointerUpForTests() =>
        CompleteGesture(new Point(0, 0), SlideTransformCore.Identity, KeyModifiers.None);

    internal void SeedTransientInteractionVisualsForTests()
    {
        _adorner.UpdatePreview(new Rect(1, 1, 10, 10));
        _adorner.UpdateGeometryPreview("test", new Point(2, 2));
        _adorner.UpdateMarquee(new Rect(3, 3, 8, 8));
        _adorner.UpdateSnapGuides(
            [new SnapGuideLine { IsHorizontal = true, Position = 4, Label = "test" }],
            SlideTransformCore.Identity);
    }

    private static CanvasGesturePoint ToGesturePoint(Point point)
        => new(point.X, point.Y);

    private CanvasDragReducerPlan ReduceDrag(Point screenPt)
        => CanvasGesturePlanner.ReduceDrag(new CanvasDragReducerRequest(
            StartScreen: ToGesturePoint(_dragStartScreen),
            CurrentScreen: ToGesturePoint(screenPt),
            DragStarted: _dragStarted,
            StartThresholdPx: CanvasGesturePlanner.DefaultDragStartThresholdPx,
            CommitThresholdPx: CanvasGesturePlanner.MeaningfulDragCommitThresholdPx));

    private Rect? GetSelectionScreenRect(uint shapeId, Slide slide, SlideTransformCore xf)
    {
        if (_editor.Presentation is null) return null;
        var rect = SlideCanvasGeometryPlanner.ShapeBoundsToScreen(
            slide,
            _editor.Presentation,
            shapeId,
            xf);
        return rect is { } screenRect ? ToAvaloniaRect(screenRect) : null;
    }

    private static Rect ToAvaloniaRect(SlideScreenRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

}
