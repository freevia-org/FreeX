using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;
using System.Collections.Generic;

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
///   <item>All coordinate work is delegated to the framework-free helpers
///         <see cref="SlideTransform"/> and <see cref="ShapeHitTester"/> so the logic
///         is fully unit-testable.</item>
/// </list>
/// </summary>
public sealed class CanvasGestureHandler : IDisposable
{
    // ── Wiring ────────────────────────────────────────────────────────────────────────────────

    private readonly SlideCanvas       _canvas;
    private readonly EditingSession    _editor;
    private readonly Func<SlideShape, bool>? _tryOpenOleInPlace;
    private readonly Action<ChartPointHit>? _onChartPointDoubleClick;
    private readonly SelectionAdorner  _adorner;
    private readonly AdornerLayer?     _adornerLayer;
    private bool                       _disposed;

    // ── Drag state ────────────────────────────────────────────────────────────────────────────

    private readonly CanvasGestureSession _gestureSession = new();

    // ── Move ──────────────────────────────────────────────────────────────────────────────────

    // ── Resize ────────────────────────────────────────────────────────────────────────────────

    // ── Rotate ────────────────────────────────────────────────────────────────────────────────

    // Preset geometry edit-point gesture

    // ── Marquee ───────────────────────────────────────────────────────────────────────────────

    // ── Small nudge step ──────────────────────────────────────────────────────────────────────
    // ── Wave 12B: Snap settings ───────────────────────────────────────────────────────────────
    // Both default on; holding Alt during drag disables snapping (PowerPoint convention).

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

    // ── Construction / attach ─────────────────────────────────────────────────────────────────

    public CanvasGestureHandler(
        SlideCanvas canvas,
        EditingSession editor,
        Func<SlideShape, bool>? tryOpenOleInPlace = null,
        Action<ChartPointHit>? onChartPointDoubleClick = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _tryOpenOleInPlace = tryOpenOleInPlace;
        _onChartPointDoubleClick = onChartPointDoubleClick;

        // Add adorner to the adorner layer
        _adornerLayer = AdornerLayer.GetAdornerLayer(_canvas);
        _adorner      = new SelectionAdorner(_canvas);
        _adornerLayer?.Add(_adorner);

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
        var pt   = e.GetPosition(_canvas);
        var xf   = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;

        if (slide is null || _editor.Presentation is null)
            return;

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
            if (_onChartPointDoubleClick is not null &&
                ChartPointHitTester.TryHitTest(
                    slide,
                    _editor.Presentation,
                    slidePoint.X,
                    slidePoint.Y,
                    out var chartPointHit))
            {
                _onChartPointDoubleClick(chartPointHit);
                e.Handled = true;
                return;
            }
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
                if (!(_tryOpenOleInPlace?.Invoke(shape) ?? false))
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

            // Text editing remains the responsibility of InCanvasTextEditor. Keep text-bearing
            // shapes out of the normal selection path while allowing textless shapes to select.
            if (!ShouldContinueDoubleClickSelection(shape))
                return;
        }

        // Determine what was hit first for the existing selection (handles take priority)
        if (_editor.SelectedShapeIds.Count > 0)
        {
            // A multi-selection has one group box. Its handles operate on every selected shape.
            if (_editor.SelectedShapeIds.Count > 1 && _adorner.SelectionBounds is { } groupRect)
            {
                var groupHandle = _adorner.HitTestHandle(groupRect, pt);
                if (groupHandle == CanvasGestureHandleKind.Rotate)
                {
                    StartMultiRotate(slide, pt);
                    e.Handled = true;
                    return;
                }
                if (groupHandle is not CanvasGestureHandleKind.None and not CanvasGestureHandleKind.Body)
                {
                    StartMultiResize(slide, groupHandle, pt);
                    e.Handled = true;
                    return;
                }
            }

            // Single-selection handles retain their existing shape-local behavior.
            if (_editor.SelectedShapeIds.Count == 1)
            {
                var selId   = _editor.SelectedShapeIds[0];
                var selRect = GetSelectionScreenRect(selId, slide, xf);
                if (selRect.HasValue)
                {
                    if (EditPointsEnabled)
                    {
                        var geometryHandle = _adorner.HitTestGeometryHandle(pt);
                        if (geometryHandle is not null)
                        {
                            StartGeometryAdjustment(selId, slide, xf, geometryHandle, pt);
                            e.Handled = true;
                            return;
                        }
                    }

                    var hitHandle = _adorner.HitTestHandle(selRect.Value, pt);
                    if (hitHandle == CanvasGestureHandleKind.Rotate)
                    {
                        StartRotate(selId, slide, xf, pt);
                        e.Handled = true;
                        return;
                    }
                    if (hitHandle != CanvasGestureHandleKind.None &&
                        hitHandle != CanvasGestureHandleKind.Body)
                    {
                        StartResize(selId, slide, hitHandle, pt);
                        e.Handled = true;
                        return;
                    }
                    if (hitHandle == CanvasGestureHandleKind.Body)
                    {
                        // Body of already-selected shape: start move
                        StartMove(slide, xf, pt);
                        e.Handled = true;
                        return;
                    }
                }
            }
            else
            {
                // Multi-selection: hit any selected body -> move
                var slidePt = xf.ScreenToSlide(pt.X, pt.Y);
                if (CanvasGesturePlanner.HitSelectedShapeBody(
                    slide,
                    _editor.Presentation,
                    _editor.SelectedShapeIds,
                    new CanvasGesturePoint(slidePt.X, slidePt.Y)))
                {
                    StartMove(slide, xf, pt);
                    e.Handled = true;
                    return;
                }
            }
        }

        // Hit-test slide shapes
        var slidePt2 = xf.ScreenToSlide(pt.X, pt.Y);
        var hitId    = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt2.X, slidePt2.Y);

        if (hitId.HasValue)
        {
            bool addToSelection = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            _editor.Select(hitId.Value, addToSelection);
            // If not multi-select, prepare for possible move
            if (!addToSelection || _editor.SelectedShapeIds.Count <= 1)
            {
                StartMove(slide, xf, pt);
            }
        }
        else
        {
            // Click on empty -> start marquee or clear
            _editor.ClearSelection();
            StartMarquee(xf, pt);
        }

        e.Handled = true;
    }

    internal static bool ShouldContinueDoubleClickSelection(SlideShape? shape) =>
        CanvasGesturePlanner.ShouldContinueDoubleClickSelection(shape);

    // ── Mouse move ────────────────────────────────────────────────────────────────────────────

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateCursor(e.GetPosition(_canvas));
            return;
        }

        var pt  = e.GetPosition(_canvas);
        var xf  = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;
        if (slide is null) return;

        switch (_gestureSession.Kind)
        {
            case CanvasGestureKind.Move:
                PreviewMove(pt, xf, slide);
                break;
            case CanvasGestureKind.Resize:
                PreviewResize(pt, xf);
                break;
            case CanvasGestureKind.Rotate:
                PreviewRotate(pt, xf);
                break;
            case CanvasGestureKind.GeometryAdjustment:
                PreviewGeometryAdjustment(pt, xf);
                break;
            case CanvasGestureKind.Marquee:
                PreviewMarquee(pt, xf);
                break;
        }
    }

    // ── Mouse up ──────────────────────────────────────────────────────────────────────────────

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        CompleteGesture(e.GetPosition(_canvas), _canvas.CurrentTransform);
        _canvas.ReleaseMouseCapture();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) =>
        CancelActiveGesture(releaseCapture: false);

    private void CompleteGesture(Point pt, SlideTransform xf)
    {
        switch (_gestureSession.Kind)
        {
            case CanvasGestureKind.Move:
                CommitMove(pt, xf);
                break;
            case CanvasGestureKind.Resize:
                CommitResize(pt, xf);
                break;
            case CanvasGestureKind.Rotate:
                CommitRotate(pt, xf);
                break;
            case CanvasGestureKind.GeometryAdjustment:
                CommitGeometryAdjustment(pt, xf);
                break;
            case CanvasGestureKind.Marquee:
                CommitMarquee(pt, xf);
                break;
        }

        ClearGestureState();
    }

    private void CancelActiveGesture(bool releaseCapture)
    {
        bool wasActive = _gestureSession.IsActive;
        ClearGestureState();
        if (wasActive && releaseCapture)
            _canvas.ReleaseMouseCapture();
    }

    private void ClearGestureState()
    {
        _gestureSession.Clear();
        _canvas.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        _adorner.UpdatePreview(null);
        _adorner.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        _adorner.UpdateGeometryPreview(null, null);
        _adorner.UpdateMarquee(null);
        _adorner.UpdateSnapGuides(null, SlideTransform.Identity);
    }

    // ── Move gesture ──────────────────────────────────────────────────────────────────────────

    private void StartMove(Slide slide, SlideTransform xf, Point screenPt)
    {
        _gestureSession.BeginMove(slide, _editor.SelectedShapeIds, ToGesturePoint(screenPt));
        _canvas.CaptureMouse();
    }

    private void PreviewMove(Point screenPt, SlideTransform xf, Slide slide)
    {
        var drag = _gestureSession.TrackDrag(ToGesturePoint(screenPt));
        if (!drag.DragStarted)
            return;

        var plan = _gestureSession.PlanMove(
            ToGesturePoint(screenPt),
            ToCoreTransform(xf),
            slide,
            SnapToGrid,
            SnapToShapes,
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0);

        _adorner.UpdatePreview(plan.PreviewBounds is { } bounds ? ToWpfRect(bounds) : null);
        _adorner.UpdateSnapGuides(plan.SnapGuides.Count > 0 ? plan.SnapGuides : null, xf);
    }

    private void CommitMove(Point screenPt, SlideTransform xf)
    {
        if (!_gestureSession.ShouldCommit(ToGesturePoint(screenPt)))
            return;

        var plan = _gestureSession.PlanMove(
            ToGesturePoint(screenPt),
            ToCoreTransform(xf),
            _editor.CurrentSlide,
            SnapToGrid,
            SnapToShapes,
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0);

        _editor.MoveSelected(plan.DeltaXEmu, plan.DeltaYEmu);
    }

    // ── Resize gesture ────────────────────────────────────────────────────────────────────────

    private void StartResize(uint shapeId, Slide slide, CanvasGestureHandleKind handle, Point screenPt)
    {
        if (_gestureSession.BeginResize(slide, shapeId, handle, ToGesturePoint(screenPt)))
            _canvas.CaptureMouse();
    }

    private void StartMultiResize(Slide slide, CanvasGestureHandleKind handle, Point screenPt)
    {
        if (_gestureSession.BeginMultiResize(
            slide,
            _editor.SelectedShapeIds,
            handle,
            ToGesturePoint(screenPt)))
        {
            _canvas.CaptureMouse();
        }
    }

    private void PreviewResize(Point screenPt, SlideTransform xf)
    {
        var drag = _gestureSession.TrackDrag(ToGesturePoint(screenPt));
        if (!drag.DragStarted)
            return;

        if (_gestureSession.MultiTransformStartShapes is not null)
        {
            var plan = _gestureSession.PlanMultiResize(
                ToGesturePoint(screenPt),
                ToCoreTransform(xf),
                _editor.CurrentSlide,
                SnapToGrid,
                SnapToShapes,
                (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            _adorner.UpdateTransformPreview(plan);
            _canvas.UpdateTransformPreview(plan);
            return;
        }

        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf);
        var r = SlideCanvasGeometryPlanner.EmuBoundsToScreen(nx, ny, ncx, ncy, ToCoreTransform(xf));
        _adorner.UpdatePreview(ToWpfRect(r));
    }

    private void CommitResize(Point screenPt, SlideTransform xf)
    {
        if (!_gestureSession.ShouldCommit(ToGesturePoint(screenPt)))
            return;

        if (_gestureSession.MultiTransformStartShapes is not null)
        {
            var plan = _gestureSession.PlanMultiResize(
                ToGesturePoint(screenPt),
                ToCoreTransform(xf),
                _editor.CurrentSlide,
                SnapToGrid,
                SnapToShapes,
                (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            _editor.ApplySelectedTransforms(plan.Shapes);
            return;
        }

        var (nx, ny, ncx, ncy) = ComputeResizeBounds(screenPt, xf);
        _editor.ResizeShape(_gestureSession.ResizeState!.Value.ShapeId, nx, ny, ncx, ncy);
    }

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
        var result = _gestureSession.PlanResize(
            ToGesturePoint(screenPt),
            ToCoreTransform(xf),
            _editor.CurrentSlide,
            SnapToGrid,
            SnapToShapes,
            (Keyboard.Modifiers & ModifierKeys.Alt) != 0);

        return (result.XEmu, result.YEmu, result.CxEmu, result.CyEmu);
    }

    // ── Rotate gesture ────────────────────────────────────────────────────────────────────────

    private void StartRotate(uint shapeId, Slide slide, SlideTransform xf, Point screenPt)
    {
        if (_gestureSession.BeginRotate(slide, shapeId, ToGesturePoint(screenPt)))
            _canvas.CaptureMouse();
    }

    private void StartMultiRotate(Slide slide, Point screenPt)
    {
        if (_gestureSession.BeginMultiRotate(
            slide,
            _editor.SelectedShapeIds,
            ToGesturePoint(screenPt)))
        {
            _canvas.CaptureMouse();
        }
    }

    private void PreviewRotate(Point screenPt, SlideTransform xf)
    {
        var drag = _gestureSession.TrackDrag(ToGesturePoint(screenPt));
        if (!drag.DragStarted)
            return;

        if (_gestureSession.MultiTransformStartShapes is not null)
        {
            var plan = _gestureSession.PlanMultiRotate(
                ToGesturePoint(screenPt),
                ToCoreTransform(xf),
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            _adorner.UpdateTransformPreview(plan);
            _canvas.UpdateTransformPreview(plan);
            return;
        }

        double angle = ComputeRotationAngle(screenPt, xf);
        // Show preview using selection rect with rotation hint
        if (_editor.CurrentSlide is not null && _editor.Presentation is not null)
        {
            var s = _editor.CurrentSlide.Shapes.FirstOrDefault(
                sh => sh.Id == _gestureSession.RotateShapeId);
            if (s is not null)
            {
                var r = GetSelectionScreenRect(
                    _gestureSession.RotateShapeId,
                    _editor.CurrentSlide,
                    xf);
                if (r.HasValue)
                    _adorner.UpdatePreview(r.Value, angle);
            }
        }
    }

    private void CommitRotate(Point screenPt, SlideTransform xf)
    {
        if (!_gestureSession.ShouldCommit(ToGesturePoint(screenPt)))
            return;

        if (_gestureSession.MultiTransformStartShapes is not null)
        {
            var plan = _gestureSession.PlanMultiRotate(
                ToGesturePoint(screenPt),
                ToCoreTransform(xf),
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            _editor.ApplySelectedTransforms(plan.Shapes);
            return;
        }

        double angle = ComputeRotationAngle(screenPt, xf);
        _editor.RotateShape(_gestureSession.RotateShapeId, angle);
    }

    // ── Preset geometry edit-point gesture ─────────────────────────────────────────────────────

    private void StartGeometryAdjustment(
        uint shapeId,
        Slide slide,
        SlideTransform xf,
        string handleName,
        Point screenPt)
    {
        if (_editor.Presentation is not null &&
            _gestureSession.TryBeginGeometryAdjustment(
                slide,
                _editor.Presentation,
                shapeId,
                handleName,
                ToGesturePoint(screenPt)))
        {
            _canvas.CaptureMouse();
        }
    }

    private void PreviewGeometryAdjustment(Point screenPt, SlideTransform xf)
    {
        var pointerSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        var plan = _gestureSession.PlanGeometryPreview(
            _editor.CurrentSlide,
            new CanvasGesturePoint(pointerSlide.X, pointerSlide.Y));
        if (plan is not { } preview)
            return;

        var previewScreen = xf.SlideToScreen(
            preview.PositionSlide.X,
            preview.PositionSlide.Y);
        _adorner.UpdateGeometryPreview(preview.HandleName, previewScreen);
    }

    private void CommitGeometryAdjustment(Point screenPt, SlideTransform xf)
    {
        if (_gestureSession.Geometry is not { } geometry ||
            _editor.CurrentSlide is null)
            return;

        var dx = screenPt.X - geometry.StartScreen.X;
        var dy = screenPt.Y - geometry.StartScreen.Y;
        if (dx * dx + dy * dy < 1.0)
            return;

        var pointerSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        _gestureSession.CommitGeometryAdjustment(
            _editor,
            _editor.CurrentSlide,
            new CanvasGesturePoint(pointerSlide.X, pointerSlide.Y));
    }

    /// <summary>
    /// Computes the new absolute rotation angle in degrees given the current drag position.
    /// Snaps to 15° increments when Shift is held.
    /// </summary>
    public double ComputeRotationAngle(Point screenPt, SlideTransform xf)
    {
        return _gestureSession.PlanRotation(
            ToGesturePoint(screenPt),
            ToCoreTransform(xf),
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    // ── Marquee gesture ───────────────────────────────────────────────────────────────────────

    private void StartMarquee(SlideTransform xf, Point screenPt)
    {
        var startSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        _gestureSession.BeginMarquee(
            ToGesturePoint(screenPt),
            ToGesturePoint(startSlide));
        _canvas.CaptureMouse();
    }

    private void PreviewMarquee(Point screenPt, SlideTransform xf)
    {
        var r = SlideCanvasGeometryPlanner.ScreenRectBetween(
            _gestureSession.DragStartScreen,
            ToGesturePoint(screenPt));
        _adorner.UpdateMarquee(ToWpfRect(r));
    }

    private void CommitMarquee(Point screenPt, SlideTransform xf)
    {
        _adorner.UpdateMarquee(null);
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var endSlide = xf.ScreenToSlide(screenPt.X, screenPt.Y);
        var ids = ShapeHitTester.MarqueeHitTest(
            slide, _editor.Presentation,
            _gestureSession.MarqueeStartSlide.X,
            _gestureSession.MarqueeStartSlide.Y,
            endSlide.X, endSlide.Y);

        if (ids.Count == 0) return;

        _editor.ClearSelection();
        foreach (var id in ids)
            _editor.Select(id, addToSelection: true);
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKeyDown(e.Key))
            e.Handled = true;
    }

    private bool HandleKeyDown(Key key)
    {
        switch (CanvasGesturePlanner.ResolveEscapeAction(
            _editor.IsFormatPainterActive,
            _gestureSession.IsActive))
        {
            case CanvasEscapeAction.CancelFormatPainter when key == Key.Escape:
                _editor.CancelFormatPainter();
                return true;
            case CanvasEscapeAction.CancelGesture when key == Key.Escape:
                CancelActiveGesture(releaseCapture: true);
                return true;
        }

        if (_editor.SelectedShapeIds.Count == 0) return false;

        if (TryHandleCustomGeometryKey(key))
            return true;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        long step = CanvasGesturePlanner.ResolveNudgeStep(shift);

        switch (key)
        {
            case Key.Left:
                _editor.MoveSelected(-step, 0);
                return true;
            case Key.Right:
                _editor.MoveSelected(step, 0);
                return true;
            case Key.Up:
                _editor.MoveSelected(0, -step);
                return true;
            case Key.Down:
                _editor.MoveSelected(0, step);
                return true;
            case Key.Delete:
            case Key.Back:
                _editor.DeleteSelected();
                return true;
        }

        return false;
    }

    internal bool IsGestureActiveForTests => _gestureSession.IsActive;

    internal bool HasPendingGestureStateForTests => _gestureSession.HasPendingState;

    internal bool HasTransientInteractionVisualsForTests =>
        _adorner.HasTransientInteractionVisualsForTests ||
        _canvas.HasLiveTransformPreviewForTests;

    internal SelectionAdorner AdornerForTests => _adorner;

    internal bool HandleEscapeForTests() => HandleKeyDown(Key.Escape);

    internal void SimulateStaleMouseUpForTests() =>
        CompleteGesture(new Point(0, 0), SlideTransform.Identity);

    internal void SeedTransientInteractionVisualsForTests()
    {
        if (_editor.CurrentSlide is { } slide)
        {
            var previewSession = new CanvasGestureSession();
            if (previewSession.BeginMultiResize(
                slide,
                _editor.SelectedShapeIds,
                CanvasGestureHandleKind.ResizeSE,
                new CanvasGesturePoint(0, 0)))
            {
                _canvas.UpdateTransformPreview(previewSession.PlanMultiResize(
                    new CanvasGesturePoint(1, 1),
                    ToCoreTransform(_canvas.CurrentTransform),
                    slide,
                    false,
                    false,
                    false));
            }
        }
        _adorner.UpdatePreview(new Rect(1, 1, 10, 10));
        _adorner.UpdateGeometryPreview("test", new Point(2, 2));
        _adorner.UpdateMarquee(new Rect(3, 3, 8, 8));
        _adorner.UpdateSnapGuides(
            [new SnapGuideLine { IsHorizontal = true, Position = 4, Label = "test" }],
            SlideTransform.Identity);
    }

    internal void SeedResizeStateForTests(
        Point startScreen,
        SlideShape shape,
        CanvasGestureHandleKind handle)
    {
        if (_editor.CurrentSlide is null ||
            !_gestureSession.BeginResize(
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

        _gestureSession.BeginMove(
            _editor.CurrentSlide,
            _editor.SelectedShapeIds,
            ToGesturePoint(startScreen));
    }

    internal void CompleteGestureForTests(Point currentScreen) =>
        CompleteGesture(currentScreen, _canvas.CurrentTransform);

    private bool TryHandleCustomGeometryKey(Key key)
    {
        if (!EditPointsEnabled ||
            _gestureSession.Geometry is not { HandleName: { } handleName } geometry ||
            _editor.SelectedShapeIds.Count != 1)
            return false;

        var shapeId = _editor.SelectedShapeIds[0];
        if (geometry.ShapeId != shapeId)
            return false;

        var handled = key switch
        {
            Key.Insert => _editor.TryInsertCustomGeometryPoint(shapeId, handleName),
            Key.Delete or Key.Back => _editor.TryDeleteCustomGeometryPoint(shapeId, handleName),
            _ => false,
        };
        if (handled)
            _gestureSession.ClearGeometryHandle();
        return handled;
    }

    // ── Cursor feedback ───────────────────────────────────────────────────────────────────────

    private void UpdateCursor(Point screenPt)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
        {
            _canvas.Cursor = Cursors.Arrow;
            return;
        }

        var xf = _canvas.CurrentTransform;

        if (_editor.SelectedShapeIds.Count > 1 && _adorner.SelectionBounds is { } groupRect)
        {
            var groupHandle = _adorner.HitTestHandle(groupRect, screenPt);
            _canvas.Cursor = groupHandle switch
            {
                CanvasGestureHandleKind.Rotate => Cursors.Cross,
                CanvasGestureHandleKind.ResizeN or CanvasGestureHandleKind.ResizeS => Cursors.SizeNS,
                CanvasGestureHandleKind.ResizeE or CanvasGestureHandleKind.ResizeW => Cursors.SizeWE,
                CanvasGestureHandleKind.ResizeNE or CanvasGestureHandleKind.ResizeSW => Cursors.SizeNESW,
                CanvasGestureHandleKind.ResizeNW or CanvasGestureHandleKind.ResizeSE => Cursors.SizeNWSE,
                _ => Cursors.Arrow
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
                    _canvas.Cursor = Cursors.Hand;
                    return;
                }

                var handle = _adorner.HitTestHandle(selRect.Value, screenPt);
                _canvas.Cursor = handle switch
                {
                    CanvasGestureHandleKind.Rotate              => Cursors.Cross,
                    CanvasGestureHandleKind.ResizeN or
                    CanvasGestureHandleKind.ResizeS             => Cursors.SizeNS,
                    CanvasGestureHandleKind.ResizeE or
                    CanvasGestureHandleKind.ResizeW             => Cursors.SizeWE,
                    CanvasGestureHandleKind.ResizeNE or
                    CanvasGestureHandleKind.ResizeSW            => Cursors.SizeNESW,
                    CanvasGestureHandleKind.ResizeNW or
                    CanvasGestureHandleKind.ResizeSE            => Cursors.SizeNWSE,
                    CanvasGestureHandleKind.Body                => Cursors.SizeAll,
                    _                                               => Cursors.Arrow
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
            _canvas.Cursor = Cursors.SizeAll;
            return;
        }

        // Check any shape for hover
        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        _canvas.Cursor = hitId.HasValue ? Cursors.Hand : Cursors.Arrow;
    }

    // ── Adorner refresh ───────────────────────────────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static CanvasGesturePoint ToGesturePoint(Point point)
        => new(point.X, point.Y);

    private static SlideTransformCore ToCoreTransform(SlideTransform xf)
        => xf.Core;

    private Rect? GetSelectionScreenRect(uint shapeId, Slide slide, SlideTransform xf)
    {
        if (_editor.Presentation is null) return null;
        var shape = ShapeHitTester.FindShape(slide, shapeId);
        var rect = shape is null
            ? (SlideScreenRect?)null
            : SlideCanvasGeometryPlanner.ShapeVisualBoundsToScreen(
                shape,
                _editor.Presentation,
                ToCoreTransform(xf));
        return rect is { } screenRect ? ToWpfRect(screenRect) : null;
    }

    private static Rect ToWpfRect(SlideScreenRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);
}
