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
    private readonly Func<OleObjectInfo?, bool>? _tryActivateOleExternally;
    private readonly Action<ChartPointHit>? _onChartPointDoubleClick;
    private readonly SelectionAdorner  _adorner;
    private readonly AdornerLayer?     _adornerLayer;
    private bool                       _disposed;

    // ── Drag state ────────────────────────────────────────────────────────────────────────────

    private readonly CanvasGestureRouter _gestureRouter;

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
        EditingSession editor,
        Func<SlideShape, bool>? tryOpenOleInPlace = null,
        Action<ChartPointHit>? onChartPointDoubleClick = null,
        Func<OleObjectInfo?, bool>? tryActivateOleExternally = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _gestureRouter = new CanvasGestureRouter(_editor);
        _tryOpenOleInPlace = tryOpenOleInPlace;
        _onChartPointDoubleClick = onChartPointDoubleClick;
        _tryActivateOleExternally = tryActivateOleExternally;

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
        var pt = e.GetPosition(_canvas);
        var xf = _canvas.CurrentTransform;
        var slide = _editor.CurrentSlide;

        if (slide is null || _editor.Presentation is null)
            return;

        var plan = _gestureRouter.HandlePointerPressed(CreatePressRequest(
            pt,
            xf,
            slide,
            e.ClickCount,
            ToGestureModifiers(Keyboard.Modifiers)));
        ApplyPressAction(plan);
        if (plan.CapturePointer)
            _canvas.CaptureMouse();
        e.Handled = plan.Handled;
    }

    internal static bool ShouldContinueDoubleClickSelection(SlideShape? shape) =>
        CanvasGesturePlanner.ShouldContinueDoubleClickSelection(shape);

    private CanvasGesturePressRequest CreatePressRequest(
        Point point,
        SlideTransform transform,
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
        ApplyPreviewPlan(
            _gestureRouter.PreviewPointer(
                ToGesturePoint(point),
                ToCoreTransform(transform),
                ToGestureModifiers(Keyboard.Modifiers)),
            transform);
    }

    internal bool HandleOleDoubleClickForTests(SlideShape shape) => HandleOleDoubleClick(shape);

    private bool HandleOleDoubleClick(SlideShape shape)
    {
        if (shape.Kind != SlideShapeKind.Ole)
            return false;
        if (_tryOpenOleInPlace?.Invoke(shape) == true)
            return true;
        return _tryActivateOleExternally?.Invoke(shape.OleObject)
            ?? OleActivationService.TryActivate(shape.OleObject);
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
        CanvasGestureModifiers modifiers)
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
        _adorner.UpdateSnapGuides(null, SlideTransform.Identity);
    }

    // ── Move gesture ──────────────────────────────────────────────────────────────────────────

    private void ApplyPreviewPlan(CanvasGesturePreviewPlan plan, SlideTransform transform)
    {
        switch (plan.Kind)
        {
            case CanvasGestureKind.Move when plan.Move is { } move:
                _adorner.UpdatePreview(
                    move.PreviewBounds is { } bounds ? ToWpfRect(bounds) : null);
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
                    ToCoreTransform(transform));
                _adorner.UpdatePreview(ToWpfRect(resizeRect));
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
                _adorner.UpdateMarquee(ToWpfRect(marquee));
                break;
        }
    }

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

    internal bool IsGestureActiveForTests => _gestureRouter.IsActive;

    internal bool HasPendingGestureStateForTests => _gestureRouter.HasPendingState;

    internal bool HasTransientInteractionVisualsForTests =>
        _adorner.HasTransientInteractionVisualsForTests ||
        _canvas.HasLiveTransformPreviewForTests;

    internal SelectionAdorner AdornerForTests => _adorner;

    internal bool HandleEscapeForTests() => HandleKeyDown(Key.Escape, ModifierKeys.None);

    internal bool HandleKeyDownForTests(Key key, ModifierKeys modifiers) =>
        HandleKeyDown(key, modifiers);

    internal void SimulateStaleMouseUpForTests() =>
        CompleteGesture(
            new Point(0, 0),
            SlideTransform.Identity,
            CanvasGestureModifiers.None);

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
                    ToCoreTransform(_canvas.CurrentTransform),
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
            SlideTransform.Identity);
    }

    internal void SeedResizeStateForTests(
        Point startScreen,
        SlideShape shape,
        CanvasGestureHandleKind handle)
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
        CompleteGesture(
            currentScreen,
            _canvas.CurrentTransform,
            CanvasGestureModifiers.None);

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
        var projection = slide is null || _editor.Presentation is not { } presentation
            ? SelectionAdornerProjectionPlan.Empty
            : SelectionAdornerGeometry.BuildProjection(
                slide,
                presentation,
                _editor.SelectedShapeIds,
                ToCoreTransform(_canvas.CurrentTransform),
                EditPointsEnabled);

        _adorner.UpdateSelection(projection.Selections.Select(selection =>
            (selection.ShapeId, ToWpfRect(selection.ScreenRect))));
        _adorner.UpdateGeometryHandles(projection.GeometryHandles.Select(handle =>
            (handle.Name, new Point(handle.ScreenPosition.X, handle.ScreenPosition.Y))));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

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

    private static CanvasGestureModifiers ToGestureModifiers(ModifierKeys modifiers)
    {
        var result = CanvasGestureModifiers.None;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= CanvasGestureModifiers.Shift;
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= CanvasGestureModifiers.Control;
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= CanvasGestureModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= CanvasGestureModifiers.Meta;
        return result;
    }

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
                slide,
                _editor.Presentation,
                ToCoreTransform(xf));
        return rect is { } screenRect ? ToWpfRect(screenRect) : null;
    }

    private static Rect ToWpfRect(SlideScreenRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static Rect ToWpfRect(SelectionAdornerRect rect)
        => new(rect.Left, rect.Top, rect.Width, rect.Height);
}
