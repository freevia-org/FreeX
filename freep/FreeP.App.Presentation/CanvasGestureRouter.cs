using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

[Flags]
public enum CanvasGestureModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3,
}

public enum CanvasGestureKey
{
    None,
    Escape,
    Left,
    Right,
    Up,
    Down,
    Delete,
    Backspace,
    Insert,
}

public enum CanvasGesturePressActionKind
{
    None,
    NotifyChartPointDoubleClick,
    ActivateOle,
}

public enum CanvasGestureKeyboardActionKind
{
    None,
    CancelGesture,
}

public readonly record struct CanvasGesturePressRequest(
    CanvasGesturePoint ScreenPoint,
    CanvasGesturePoint SlidePoint,
    int ClickCount,
    CanvasGestureModifiers Modifiers,
    CanvasGestureHandleKind SelectionHandle,
    string? GeometryHandle,
    bool HasSingleSelectionFrame,
    bool CanNotifyChartPointDoubleClick);

public readonly record struct CanvasGesturePressPlan(
    bool Handled,
    bool CapturePointer,
    CanvasGesturePressActionKind Action,
    ChartPointHit? ChartPoint,
    SlideShape? Shape)
{
    public static readonly CanvasGesturePressPlan Unhandled = new(
        false,
        false,
        CanvasGesturePressActionKind.None,
        null,
        null);

    public static readonly CanvasGesturePressPlan HandledOnly = new(
        true,
        false,
        CanvasGesturePressActionKind.None,
        null,
        null);
}

public readonly record struct CanvasGestureKeyboardPlan(
    bool Handled,
    CanvasGestureKeyboardActionKind Action)
{
    public static readonly CanvasGestureKeyboardPlan Unhandled = new(
        false,
        CanvasGestureKeyboardActionKind.None);
}

public readonly record struct CanvasGesturePreviewPlan(
    CanvasGestureKind Kind,
    uint ShapeId,
    CanvasMovePlan? Move,
    CanvasResizeBounds? Resize,
    CanvasMultiTransformPlan? MultiTransform,
    double? RotationDegrees,
    CanvasGeometryPreviewPlan? Geometry,
    SlideScreenRect? Marquee)
{
    public static readonly CanvasGesturePreviewPlan Empty = new(
        CanvasGestureKind.None,
        0,
        null,
        null,
        null,
        null,
        null,
        null);
}

public readonly record struct CanvasGestureVisualPreviewPlan(
    CanvasGestureKind Kind,
    SlideScreenRect? PreviewBounds,
    IReadOnlyList<SnapGuideLine> SnapGuides,
    CanvasMultiTransformPlan? MultiTransform,
    double? RotationDegrees,
    string? GeometryHandleName,
    CanvasGesturePoint? GeometryScreenPoint)
{
    public static readonly CanvasGestureVisualPreviewPlan Empty = new(
        CanvasGestureKind.None,
        null,
        [],
        null,
        null,
        null,
        null);
}

public static class CanvasGesturePreviewProjector
{
    public static CanvasGestureVisualPreviewPlan Project(
        CanvasGesturePreviewPlan plan,
        Slide? slide,
        Presentation? presentation,
        SlideTransformCore transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        return plan.Kind switch
        {
            CanvasGestureKind.Move when plan.Move is { } move => new(
                plan.Kind,
                move.PreviewBounds,
                move.SnapGuides,
                null,
                null,
                null,
                null),
            CanvasGestureKind.Resize when plan.MultiTransform is { } multiResize => new(
                plan.Kind,
                null,
                [],
                multiResize,
                null,
                null,
                null),
            CanvasGestureKind.Resize when plan.Resize is { } resize => new(
                plan.Kind,
                SlideCanvasGeometryPlanner.EmuBoundsToScreen(
                    resize.XEmu,
                    resize.YEmu,
                    resize.CxEmu,
                    resize.CyEmu,
                    transform),
                [],
                null,
                null,
                null,
                null),
            CanvasGestureKind.Rotate when plan.MultiTransform is { } multiRotate => new(
                plan.Kind,
                null,
                [],
                multiRotate,
                null,
                null,
                null),
            CanvasGestureKind.Rotate when plan.RotationDegrees is { } angle => new(
                plan.Kind,
                ResolveShapeBounds(plan.ShapeId, slide, presentation, transform),
                [],
                null,
                angle,
                null,
                null),
            CanvasGestureKind.GeometryAdjustment when plan.Geometry is { } geometry => ProjectGeometry(
                plan.Kind,
                geometry,
                transform),
            CanvasGestureKind.Marquee => new(
                plan.Kind,
                plan.Marquee,
                [],
                null,
                null,
                null,
                null),
            _ => CanvasGestureVisualPreviewPlan.Empty,
        };
    }

    private static CanvasGestureVisualPreviewPlan ProjectGeometry(
        CanvasGestureKind kind,
        CanvasGeometryPreviewPlan geometry,
        SlideTransformCore transform)
    {
        var screen = transform.SlideToScreen(
            geometry.PositionSlide.X,
            geometry.PositionSlide.Y);
        return new CanvasGestureVisualPreviewPlan(
            kind,
            null,
            [],
            null,
            null,
            geometry.HandleName,
            new CanvasGesturePoint(screen.X, screen.Y));
    }

    private static SlideScreenRect? ResolveShapeBounds(
        uint shapeId,
        Slide? slide,
        Presentation? presentation,
        SlideTransformCore transform)
    {
        if (slide is null || presentation is null)
            return null;

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        return shape is null
            ? null
            : SlideCanvasGeometryPlanner.ShapeVisualBoundsToScreen(
                shape,
                slide,
                presentation,
                transform);
    }
}

/// <summary>
/// Owns renderer-neutral canvas input routing and model command decisions. Native hosts translate
/// framework events and draw the returned preview plans, while this router keeps gesture behavior
/// identical across renderers.
/// </summary>
public sealed class CanvasGestureRouter
{
    private readonly EditingSession _editor;
    private readonly CanvasGestureSession _session = new();

    public CanvasGestureRouter(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    public bool SnapToGrid { get; set; } = true;

    public bool SnapToShapes { get; set; } = true;

    public bool EditPointsEnabled { get; set; } = true;

    public CanvasGestureKind Kind => _session.Kind;

    public bool IsActive => _session.IsActive;

    public bool HasPendingState => _session.HasPendingState;

    public CanvasGesturePressPlan HandlePointerPressed(CanvasGesturePressRequest request)
    {
        var slide = _editor.CurrentSlide;
        var presentation = _editor.Presentation;
        if (slide is null || presentation is null)
            return CanvasGesturePressPlan.Unhandled;

        if (_editor.IsFormatPainterActive)
        {
            var painterHitId = ShapeHitTester.HitTest(
                slide,
                presentation,
                request.SlidePoint.X,
                request.SlidePoint.Y);
            if (painterHitId.HasValue)
                _editor.TryApplyFormatPainterToShape(painterHitId.Value);

            return CanvasGesturePressPlan.HandledOnly;
        }

        if (request.ClickCount >= 2)
        {
            if (request.CanNotifyChartPointDoubleClick &&
                ChartPointHitTester.TryHitTest(
                    slide,
                    presentation,
                    request.SlidePoint.X,
                    request.SlidePoint.Y,
                    out var chartPointHit))
            {
                return new CanvasGesturePressPlan(
                    true,
                    false,
                    CanvasGesturePressActionKind.NotifyChartPointDoubleClick,
                    chartPointHit,
                    null);
            }

            var shape = HitShape(slide, presentation, request.SlidePoint);
            if (shape?.Kind == SlideShapeKind.Ole)
            {
                return new CanvasGesturePressPlan(
                    true,
                    false,
                    CanvasGesturePressActionKind.ActivateOle,
                    null,
                    shape);
            }

            if (shape?.Kind == SlideShapeKind.Zoom &&
                ZoomNavigationService.TryGetTargetSlideIndex(
                    presentation,
                    shape.PreservedObject,
                    out var targetSlideIndex))
            {
                _editor.SelectSlide(targetSlideIndex);
                return CanvasGesturePressPlan.HandledOnly;
            }

            if (!CanvasGesturePlanner.ShouldContinueDoubleClickSelection(shape))
                return CanvasGesturePressPlan.Unhandled;
        }

        if (_editor.SelectedShapeIds.Count > 1)
        {
            if (request.SelectionHandle == CanvasGestureHandleKind.Rotate)
            {
                bool began = _session.BeginMultiRotate(
                    slide,
                    _editor.SelectedShapeIds,
                    request.ScreenPoint);
                return GestureBeginPlan(began);
            }

            if (IsResizeHandle(request.SelectionHandle))
            {
                bool began = _session.BeginMultiResize(
                    slide,
                    _editor.SelectedShapeIds,
                    request.SelectionHandle,
                    request.ScreenPoint);
                return GestureBeginPlan(began);
            }
        }

        if (_editor.SelectedShapeIds.Count == 1 && request.HasSingleSelectionFrame)
        {
            uint selectedShapeId = _editor.SelectedShapeIds[0];
            if (EditPointsEnabled && request.GeometryHandle is { } geometryHandle)
            {
                bool began = _session.TryBeginGeometryAdjustment(
                    slide,
                    presentation,
                    selectedShapeId,
                    geometryHandle,
                    request.ScreenPoint);
                return GestureBeginPlan(began);
            }

            if (request.SelectionHandle == CanvasGestureHandleKind.Rotate)
            {
                bool began = _session.BeginRotate(
                    slide,
                    selectedShapeId,
                    request.ScreenPoint);
                return GestureBeginPlan(began);
            }

            if (IsResizeHandle(request.SelectionHandle))
            {
                bool began = _session.BeginResize(
                    slide,
                    selectedShapeId,
                    request.SelectionHandle,
                    request.ScreenPoint);
                return GestureBeginPlan(began);
            }

            if (request.SelectionHandle == CanvasGestureHandleKind.Body)
            {
                _session.BeginMove(slide, _editor.SelectedShapeIds, request.ScreenPoint);
                return GestureBeginPlan(began: true);
            }
        }
        else if (_editor.SelectedShapeIds.Count > 1 &&
            CanvasGesturePlanner.HitSelectedShapeBody(
                slide,
                presentation,
                _editor.SelectedShapeIds,
                request.SlidePoint))
        {
            _session.BeginMove(slide, _editor.SelectedShapeIds, request.ScreenPoint);
            return GestureBeginPlan(began: true);
        }

        var hitShape = HitShape(slide, presentation, request.SlidePoint);
        if (hitShape is not null)
        {
            bool addToSelection = HasAdditiveSelectionModifier(request.Modifiers);
            _editor.Select(hitShape.Id, addToSelection);
            if (!addToSelection || _editor.SelectedShapeIds.Count <= 1)
            {
                _session.BeginMove(slide, _editor.SelectedShapeIds, request.ScreenPoint);
                return GestureBeginPlan(began: true);
            }

            return CanvasGesturePressPlan.HandledOnly;
        }

        _editor.ClearSelection();
        _session.BeginMarquee(request.ScreenPoint, request.SlidePoint);
        return GestureBeginPlan(began: true);
    }

    public CanvasGesturePreviewPlan PreviewPointer(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var slide = _editor.CurrentSlide;
        if (slide is null || !_session.IsActive)
            return CanvasGesturePreviewPlan.Empty;

        if (!_session.TrackDrag(currentScreen).DragStarted)
            return CanvasGesturePreviewPlan.Empty;

        switch (_session.Kind)
        {
            case CanvasGestureKind.Move:
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.Move,
                    0,
                    PlanMove(currentScreen, transform, modifiers),
                    null,
                    null,
                    null,
                    null,
                    null);

            case CanvasGestureKind.Resize when _session.MultiTransformStartShapes is not null:
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.Resize,
                    0,
                    null,
                    null,
                    PlanMultiResize(currentScreen, transform, modifiers),
                    null,
                    null,
                    null);

            case CanvasGestureKind.Resize:
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.Resize,
                    _session.ResizeState?.ShapeId ?? 0,
                    null,
                    PlanResize(currentScreen, transform, modifiers),
                    null,
                    null,
                    null,
                    null);

            case CanvasGestureKind.Rotate when _session.MultiTransformStartShapes is not null:
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.Rotate,
                    0,
                    null,
                    null,
                    PlanMultiRotate(currentScreen, transform, modifiers),
                    null,
                    null,
                    null);

            case CanvasGestureKind.Rotate:
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.Rotate,
                    _session.RotateShapeId,
                    null,
                    null,
                    null,
                    PlanRotation(currentScreen, transform, modifiers),
                    null,
                    null);

            case CanvasGestureKind.GeometryAdjustment:
                var geometryPoint = transform.ScreenToSlide(currentScreen.X, currentScreen.Y);
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.GeometryAdjustment,
                    _session.Geometry?.ShapeId ?? 0,
                    null,
                    null,
                    null,
                    null,
                    _session.PlanGeometryPreview(
                        slide,
                        new CanvasGesturePoint(geometryPoint.X, geometryPoint.Y)),
                    null);

            case CanvasGestureKind.Marquee:
                return new CanvasGesturePreviewPlan(
                    CanvasGestureKind.Marquee,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    SlideCanvasGeometryPlanner.ScreenRectBetween(
                        _session.DragStartScreen,
                        currentScreen));

            default:
                return CanvasGesturePreviewPlan.Empty;
        }
    }

    public bool CompletePointer(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!_session.IsActive)
            return false;

        try
        {
            bool shouldCommit = _session.ShouldCommit(currentScreen);
            switch (_session.Kind)
            {
                case CanvasGestureKind.Move when shouldCommit:
                    var move = PlanMove(currentScreen, transform, modifiers);
                    _editor.MoveSelected(move.DeltaXEmu, move.DeltaYEmu);
                    break;

                case CanvasGestureKind.Resize when shouldCommit:
                    if (_session.MultiTransformStartShapes is not null)
                    {
                        var multiResize = PlanMultiResize(currentScreen, transform, modifiers);
                        _editor.ApplySelectedTransforms(multiResize.Shapes);
                    }
                    else if (_session.ResizeState is { ShapeId: not 0 } resizeState)
                    {
                        var resize = PlanResize(currentScreen, transform, modifiers);
                        _editor.ResizeShape(
                            resizeState.ShapeId,
                            resize.XEmu,
                            resize.YEmu,
                            resize.CxEmu,
                            resize.CyEmu);
                    }
                    break;

                case CanvasGestureKind.Rotate when shouldCommit:
                    if (_session.MultiTransformStartShapes is not null)
                    {
                        var multiRotate = PlanMultiRotate(currentScreen, transform, modifiers);
                        _editor.ApplySelectedTransforms(multiRotate.Shapes);
                    }
                    else if (_session.RotateShapeId != 0)
                    {
                        _editor.RotateShape(
                            _session.RotateShapeId,
                            PlanRotation(currentScreen, transform, modifiers));
                    }
                    break;

                case CanvasGestureKind.GeometryAdjustment when shouldCommit:
                    var geometryPoint = transform.ScreenToSlide(currentScreen.X, currentScreen.Y);
                    _session.CommitGeometryAdjustment(
                        _editor,
                        _editor.CurrentSlide,
                        new CanvasGesturePoint(geometryPoint.X, geometryPoint.Y));
                    break;

                case CanvasGestureKind.Marquee when shouldCommit:
                    CommitMarquee(currentScreen, transform);
                    break;
            }

            return true;
        }
        finally
        {
            _session.Clear();
        }
    }

    public CanvasGestureKeyboardPlan HandleKeyDown(
        CanvasGestureKey key,
        CanvasGestureModifiers modifiers)
    {
        if (key == CanvasGestureKey.Escape)
        {
            switch (CanvasGesturePlanner.ResolveEscapeAction(
                _editor.IsFormatPainterActive,
                _session.IsActive))
            {
                case CanvasEscapeAction.CancelFormatPainter:
                    _editor.CancelFormatPainter();
                    return new CanvasGestureKeyboardPlan(
                        true,
                        CanvasGestureKeyboardActionKind.None);
                case CanvasEscapeAction.CancelGesture:
                    return new CanvasGestureKeyboardPlan(
                        true,
                        CanvasGestureKeyboardActionKind.CancelGesture);
            }
        }

        if (_editor.SelectedShapeIds.Count == 0)
            return CanvasGestureKeyboardPlan.Unhandled;

        if (TryHandleCustomGeometryKey(key))
        {
            return new CanvasGestureKeyboardPlan(
                true,
                CanvasGestureKeyboardActionKind.None);
        }

        long step = CanvasGesturePlanner.ResolveNudgeStep(
            (modifiers & CanvasGestureModifiers.Shift) != 0);
        switch (key)
        {
            case CanvasGestureKey.Left:
                _editor.MoveSelected(-step, 0);
                break;
            case CanvasGestureKey.Right:
                _editor.MoveSelected(step, 0);
                break;
            case CanvasGestureKey.Up:
                _editor.MoveSelected(0, -step);
                break;
            case CanvasGestureKey.Down:
                _editor.MoveSelected(0, step);
                break;
            case CanvasGestureKey.Delete:
            case CanvasGestureKey.Backspace:
                _editor.DeleteSelected();
                break;
            default:
                return CanvasGestureKeyboardPlan.Unhandled;
        }

        return new CanvasGestureKeyboardPlan(
            true,
            CanvasGestureKeyboardActionKind.None);
    }

    public void BeginMove(
        Slide slide,
        IEnumerable<uint> selectedShapeIds,
        CanvasGesturePoint startScreen) =>
        _session.BeginMove(slide, selectedShapeIds, startScreen);

    public bool BeginResize(
        Slide slide,
        uint shapeId,
        CanvasGestureHandleKind handle,
        CanvasGesturePoint startScreen) =>
        _session.BeginResize(slide, shapeId, handle, startScreen);

    public bool BeginMultiResize(
        Slide slide,
        IEnumerable<uint> selectedShapeIds,
        CanvasGestureHandleKind handle,
        CanvasGesturePoint startScreen) =>
        _session.BeginMultiResize(slide, selectedShapeIds, handle, startScreen);

    public CanvasResizeBounds PlanResize(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers) =>
        _session.PlanResize(
            currentScreen,
            transform,
            _editor.CurrentSlide,
            SnapToGrid,
            SnapToShapes,
            (modifiers & CanvasGestureModifiers.Alt) != 0);

    public double PlanRotation(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers) =>
        _session.PlanRotation(
            currentScreen,
            transform,
            (modifiers & CanvasGestureModifiers.Shift) != 0);

    public void Cancel() => _session.Clear();

    private CanvasMovePlan PlanMove(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers) =>
        _session.PlanMove(
            currentScreen,
            transform,
            _editor.CurrentSlide,
            SnapToGrid,
            SnapToShapes,
            (modifiers & CanvasGestureModifiers.Alt) != 0);

    private CanvasMultiTransformPlan PlanMultiResize(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers) =>
        _session.PlanMultiResize(
            currentScreen,
            transform,
            _editor.CurrentSlide,
            SnapToGrid,
            SnapToShapes,
            (modifiers & CanvasGestureModifiers.Alt) != 0);

    private CanvasMultiTransformPlan PlanMultiRotate(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        CanvasGestureModifiers modifiers) =>
        _session.PlanMultiRotate(
            currentScreen,
            transform,
            (modifiers & CanvasGestureModifiers.Shift) != 0);

    private void CommitMarquee(CanvasGesturePoint currentScreen, SlideTransformCore transform)
    {
        var slide = _editor.CurrentSlide;
        var presentation = _editor.Presentation;
        if (slide is null || presentation is null)
            return;

        var endSlide = transform.ScreenToSlide(currentScreen.X, currentScreen.Y);
        var ids = ShapeHitTester.MarqueeHitTest(
            slide,
            presentation,
            _session.MarqueeStartSlide.X,
            _session.MarqueeStartSlide.Y,
            endSlide.X,
            endSlide.Y);
        if (ids.Count == 0)
            return;

        _editor.ClearSelection();
        foreach (var id in ids)
            _editor.Select(id, addToSelection: true);
    }

    private bool TryHandleCustomGeometryKey(CanvasGestureKey key)
    {
        if (!EditPointsEnabled ||
            _session.Geometry is not { HandleName: { } handleName } geometry ||
            _editor.SelectedShapeIds.Count != 1)
        {
            return false;
        }

        uint shapeId = _editor.SelectedShapeIds[0];
        if (geometry.ShapeId != shapeId)
            return false;

        bool handled = key switch
        {
            CanvasGestureKey.Insert => _editor.TryInsertCustomGeometryPoint(shapeId, handleName),
            CanvasGestureKey.Delete or CanvasGestureKey.Backspace =>
                _editor.TryDeleteCustomGeometryPoint(shapeId, handleName),
            _ => false,
        };
        if (handled)
            _session.ClearGeometryHandle();
        return handled;
    }

    private static CanvasGesturePressPlan GestureBeginPlan(bool began) => new(
        true,
        began,
        CanvasGesturePressActionKind.None,
        null,
        null);

    private static bool HasAdditiveSelectionModifier(CanvasGestureModifiers modifiers) =>
        (modifiers & (
            CanvasGestureModifiers.Control |
            CanvasGestureModifiers.Shift |
            CanvasGestureModifiers.Meta)) != 0;

    private static bool IsResizeHandle(CanvasGestureHandleKind handle) =>
        handle is not CanvasGestureHandleKind.None
            and not CanvasGestureHandleKind.Body
            and not CanvasGestureHandleKind.Rotate;

    private static SlideShape? HitShape(
        Slide slide,
        Presentation presentation,
        CanvasGesturePoint slidePoint)
    {
        var hitId = ShapeHitTester.HitTest(
            slide,
            presentation,
            slidePoint.X,
            slidePoint.Y);
        return hitId.HasValue ? ShapeHitTester.FindShape(slide, hitId.Value) : null;
    }
}
