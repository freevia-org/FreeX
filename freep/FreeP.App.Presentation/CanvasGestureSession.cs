using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum CanvasGestureKind
{
    None,
    Move,
    Resize,
    Rotate,
    GeometryAdjustment,
    Marquee,
}

public readonly record struct CanvasGeometryGestureState(
    uint ShapeId,
    string? HandleName,
    LayoutRect BoundsDip,
    CanvasGesturePoint StartScreen);

public readonly record struct CanvasGeometryPreviewPlan(
    string HandleName,
    CanvasGesturePoint PositionSlide);

/// <summary>
/// Owns the renderer-neutral state and planning inputs for one canvas pointer gesture.
/// Native hosts retain pointer capture, event routing, cursors, previews, and command dispatch.
/// </summary>
public sealed class CanvasGestureSession
{
    public CanvasGestureKind Kind { get; private set; }

    public CanvasGesturePoint DragStartScreen { get; private set; }

    public bool DragStarted { get; private set; }

    public IReadOnlyList<CanvasMoveShapeState>? MoveStartShapes { get; private set; }

    public CanvasResizeState? ResizeState { get; private set; }

    public IReadOnlyList<CanvasTransformShapeState>? MultiTransformStartShapes { get; private set; }

    public uint RotateShapeId { get; private set; }

    public double RotateOriginalDegrees { get; private set; }

    public CanvasGesturePoint RotateCenterSlide { get; private set; }

    public CanvasGeometryGestureState? Geometry { get; private set; }

    public CanvasGesturePoint MarqueeStartSlide { get; private set; }

    public bool IsActive => Kind != CanvasGestureKind.None;

    public bool HasPendingState =>
        MoveStartShapes is not null ||
        MultiTransformStartShapes is not null ||
        ResizeState is not null ||
        RotateShapeId != 0 ||
        Geometry is not null;

    public void BeginMove(
        Slide slide,
        IEnumerable<uint> selectedShapeIds,
        CanvasGesturePoint startScreen)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        Reset(CanvasGestureKind.Move, startScreen);
        MoveStartShapes = CanvasGesturePlanner.CaptureMoveState(slide, selectedShapeIds);
    }

    public bool BeginResize(
        Slide slide,
        uint shapeId,
        CanvasGestureHandleKind handle,
        CanvasGesturePoint startScreen)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape is null)
            return false;

        Reset(CanvasGestureKind.Resize, startScreen);
        ResizeState = new CanvasResizeState(
            shapeId,
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            shape.RotationDeg,
            handle);
        return true;
    }

    public bool BeginMultiResize(
        Slide slide,
        IEnumerable<uint> selectedShapeIds,
        CanvasGestureHandleKind handle,
        CanvasGesturePoint startScreen)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        var shapes = CanvasGesturePlanner.CaptureTransformState(slide, selectedShapeIds);
        if (shapes.Count == 0)
            return false;

        Reset(CanvasGestureKind.Resize, startScreen);
        MultiTransformStartShapes = shapes;
        ResizeState = new CanvasResizeState(0, 0, 0, 0, 0, 0, handle);
        return true;
    }

    public bool BeginRotate(
        Slide slide,
        uint shapeId,
        CanvasGesturePoint startScreen)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape is null)
            return false;

        Reset(CanvasGestureKind.Rotate, startScreen);
        RotateShapeId = shapeId;
        RotateOriginalDegrees = shape.RotationDeg;
        RotateCenterSlide = new CanvasGesturePoint(
            SlideTransformCore.EmuToDip(shape.OffsetXEmu + shape.ExtentCxEmu / 2),
            SlideTransformCore.EmuToDip(shape.OffsetYEmu + shape.ExtentCyEmu / 2));
        return true;
    }

    public bool BeginMultiRotate(
        Slide slide,
        IEnumerable<uint> selectedShapeIds,
        CanvasGesturePoint startScreen)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        var shapes = CanvasGesturePlanner.CaptureTransformState(slide, selectedShapeIds);
        if (shapes.Count == 0)
            return false;

        Reset(CanvasGestureKind.Rotate, startScreen);
        MultiTransformStartShapes = shapes;
        return true;
    }

    public void BeginGeometryAdjustment(
        uint shapeId,
        string handleName,
        LayoutRect boundsDip,
        CanvasGesturePoint startScreen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handleName);

        Reset(CanvasGestureKind.GeometryAdjustment, startScreen);
        Geometry = new CanvasGeometryGestureState(shapeId, handleName, boundsDip, startScreen);
    }

    public bool TryBeginGeometryAdjustment(
        Slide slide,
        Presentation presentation,
        uint shapeId,
        string handleName,
        CanvasGesturePoint startScreen)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape is null)
            return false;

        var bounds = ShapeHitTester.GetShapeBoundsDip(shape, presentation).ToLayoutRect();
        bool canEdit = shape.Kind == SlideShapeKind.Picture
            ? PictureCropAuthoringPlanner.Build(shape, bounds).Handles.Any(
                handle => handle.Name == handleName)
            : ShapeGeometryAdjustmentPlanner.Build(shape, bounds).Handles.Any(
                handle => handle.Name == handleName);
        if (!canEdit)
            return false;

        BeginGeometryAdjustment(shapeId, handleName, bounds, startScreen);
        return true;
    }

    public void BeginMarquee(
        CanvasGesturePoint startScreen,
        CanvasGesturePoint startSlide)
    {
        Reset(CanvasGestureKind.Marquee, startScreen);
        MarqueeStartSlide = startSlide;
    }

    public CanvasDragReducerPlan TrackDrag(CanvasGesturePoint currentScreen)
    {
        var plan = EvaluateDrag(currentScreen);
        DragStarted = plan.DragStarted;
        return plan;
    }

    public CanvasDragReducerPlan EvaluateDrag(CanvasGesturePoint currentScreen) =>
        CanvasGesturePlanner.ReduceDrag(new CanvasDragReducerRequest(
            DragStartScreen,
            currentScreen,
            DragStarted,
            CanvasGesturePlanner.DefaultDragStartThresholdPx,
            CanvasGesturePlanner.MeaningfulDragCommitThresholdPx));

    public bool ShouldCommit(CanvasGesturePoint currentScreen) =>
        DragStarted && EvaluateDrag(currentScreen).ShouldCommit;

    public CanvasMovePlan PlanMove(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        Slide? currentSlide,
        bool snapToGrid,
        bool snapToShapes,
        bool bypassSnap)
    {
        if (MoveStartShapes is null)
            return CanvasMovePlan.Empty;

        return CanvasGesturePlanner.PlanMove(new CanvasMoveRequest(
            DragStartScreen,
            currentScreen,
            transform,
            MoveStartShapes,
            currentSlide,
            snapToGrid,
            snapToShapes,
            bypassSnap));
    }

    public CanvasResizeBounds PlanResize(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        Slide? currentSlide,
        bool snapToGrid,
        bool snapToShapes,
        bool bypassSnap)
    {
        if (ResizeState is not { ShapeId: not 0 } state)
            throw new InvalidOperationException("A single-shape resize gesture is not active.");

        return CanvasGesturePlanner.ComputeResizeBounds(new CanvasResizeRequest(
            DragStartScreen,
            currentScreen,
            transform,
            state,
            currentSlide,
            snapToGrid,
            snapToShapes,
            bypassSnap));
    }

    public CanvasMultiTransformPlan PlanMultiResize(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        Slide? currentSlide,
        bool snapToGrid,
        bool snapToShapes,
        bool bypassSnap)
    {
        if (MultiTransformStartShapes is null || ResizeState is not { } resize)
            return CanvasMultiTransformPlan.Empty;

        return CanvasGesturePlanner.PlanMultiResize(new CanvasMultiResizeRequest(
            DragStartScreen,
            currentScreen,
            transform,
            resize.Handle,
            MultiTransformStartShapes,
            currentSlide,
            snapToGrid,
            snapToShapes,
            bypassSnap));
    }

    public double PlanRotation(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        bool snapToFifteenDegrees) =>
        CanvasGesturePlanner.ComputeRotationAngle(new CanvasRotationRequest(
            currentScreen,
            RotateCenterSlide,
            transform,
            RotateOriginalDegrees,
            snapToFifteenDegrees));

    public CanvasMultiTransformPlan PlanMultiRotate(
        CanvasGesturePoint currentScreen,
        SlideTransformCore transform,
        bool snapToFifteenDegrees)
    {
        if (MultiTransformStartShapes is null)
            return CanvasMultiTransformPlan.Empty;

        return CanvasGesturePlanner.PlanMultiRotate(new CanvasMultiRotateRequest(
            DragStartScreen,
            currentScreen,
            transform,
            MultiTransformStartShapes,
            snapToFifteenDegrees));
    }

    public CanvasGeometryPreviewPlan? PlanGeometryPreview(
        Slide? slide,
        CanvasGesturePoint pointerSlide)
    {
        if (slide is null || Geometry is not { HandleName: { } handleName } geometry)
            return null;

        var shape = ShapeHitTester.FindShape(slide, geometry.ShapeId);
        if (shape is null)
            return null;

        if (shape.Kind != SlideShapeKind.Picture)
            return new CanvasGeometryPreviewPlan(handleName, pointerSlide);

        var mutation = PictureCropAuthoringPlanner.BuildMutationPlan(
            shape,
            geometry.BoundsDip,
            handleName,
            new LayoutPoint(pointerSlide.X, pointerSlide.Y));
        if (mutation.Values is not { } values)
            return null;

        var position = PictureCropAuthoringPlanner.PositionFor(
            geometry.BoundsDip,
            values,
            handleName);
        return new CanvasGeometryPreviewPlan(
            handleName,
            new CanvasGesturePoint(position.X, position.Y));
    }

    public bool CommitGeometryAdjustment(
        EditingSession editor,
        Slide? slide,
        CanvasGesturePoint pointerSlide)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (slide is null || Geometry is not { HandleName: { } handleName } geometry)
            return false;

        var shape = ShapeHitTester.FindShape(slide, geometry.ShapeId);
        if (shape is null)
            return false;

        var pointer = new LayoutPoint(pointerSlide.X, pointerSlide.Y);
        if (shape.Kind == SlideShapeKind.Picture)
        {
            var crop = PictureCropAuthoringPlanner.BuildMutationPlan(
                shape,
                geometry.BoundsDip,
                handleName,
                pointer);
            if (!crop.ShouldApply || crop.Values is not { } values)
                return false;

            editor.SetPictureCrop(geometry.ShapeId, values);
            return true;
        }

        var mutation = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            geometry.BoundsDip,
            handleName,
            pointer);
        if (!mutation.ShouldApply)
            return false;

        if (mutation.CustomPoint is { } customPoint)
        {
            editor.SetCustomGeometryPoint(
                geometry.ShapeId,
                customPoint.PathIndex,
                customPoint.SegmentIndex,
                customPoint.X,
                customPoint.Y,
                customPoint.Slot);
            return true;
        }

        if (mutation.ArcPoint is { } arcPoint)
        {
            editor.SetCustomGeometryArcPoint(
                geometry.ShapeId,
                arcPoint.PathIndex,
                arcPoint.SegmentIndex,
                arcPoint.Value,
                arcPoint.Slot);
            return true;
        }

        if (mutation.Name is null || mutation.Value is not { } value)
            return false;

        editor.SetShapeGeometryAdjustment(geometry.ShapeId, mutation.Name, value);
        return true;
    }

    public void ClearGeometryHandle()
    {
        if (Geometry is { } geometry)
            Geometry = geometry with { HandleName = null };
    }

    public void Clear() => Reset(CanvasGestureKind.None, default);

    private void Reset(CanvasGestureKind kind, CanvasGesturePoint startScreen)
    {
        Kind = kind;
        DragStartScreen = startScreen;
        DragStarted = false;
        MoveStartShapes = null;
        ResizeState = null;
        MultiTransformStartShapes = null;
        RotateShapeId = 0;
        RotateOriginalDegrees = 0;
        RotateCenterSlide = default;
        Geometry = null;
        MarqueeStartSlide = default;
    }
}
