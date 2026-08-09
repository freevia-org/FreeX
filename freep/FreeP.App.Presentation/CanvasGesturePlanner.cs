using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum CanvasGestureHandleKind
{
    None,
    Body,
    ResizeN,
    ResizeNE,
    ResizeE,
    ResizeSE,
    ResizeS,
    ResizeSW,
    ResizeW,
    ResizeNW,
    Rotate
}

public enum CanvasEscapeAction
{
    None,
    CancelFormatPainter,
    CancelGesture
}

public readonly record struct CanvasGesturePoint(double X, double Y);

public readonly record struct CanvasDragReducerRequest(
    CanvasGesturePoint StartScreen,
    CanvasGesturePoint CurrentScreen,
    bool DragStarted,
    double StartThresholdPx,
    double CommitThresholdPx);

public readonly record struct CanvasDragReducerPlan(
    bool DragStarted,
    bool ShouldCommit);

public readonly record struct CanvasResizeState(
    uint ShapeId,
    long XEmu,
    long YEmu,
    long CxEmu,
    long CyEmu,
    double RotationDeg,
    CanvasGestureHandleKind Handle);

public readonly record struct CanvasResizeRequest(
    CanvasGesturePoint StartScreen,
    CanvasGesturePoint CurrentScreen,
    SlideTransformCore Transform,
    CanvasResizeState State,
    Slide? CurrentSlide,
    bool SnapToGrid,
    bool SnapToShapes,
    bool BypassSnap);

public readonly record struct CanvasResizeBounds(
    long XEmu,
    long YEmu,
    long CxEmu,
    long CyEmu);

public readonly record struct CanvasTransformShapeState(
    uint ShapeId,
    long XEmu,
    long YEmu,
    long CxEmu,
    long CyEmu,
    double RotationDeg);

public readonly record struct CanvasShapeTransform(
    uint ShapeId,
    long XEmu,
    long YEmu,
    long CxEmu,
    long CyEmu,
    double RotationDeg);

/// <summary>
/// Screen-space geometry for one member of a live multi-selection transform preview.
/// The bounds remain the member's unrotated frame; hosts apply <see cref="RotationDeg"/>
/// around its center when drawing the preview outline.
/// </summary>
public readonly record struct CanvasShapeTransformPreview(
    uint ShapeId,
    SlideScreenRect ScreenBounds,
    double RotationDeg);

public readonly record struct CanvasMultiResizeRequest(
    CanvasGesturePoint StartScreen,
    CanvasGesturePoint CurrentScreen,
    SlideTransformCore Transform,
    CanvasGestureHandleKind Handle,
    IReadOnlyList<CanvasTransformShapeState> Shapes,
    Slide? CurrentSlide,
    bool SnapToGrid,
    bool SnapToShapes,
    bool BypassSnap);

public readonly record struct CanvasMultiRotateRequest(
    CanvasGesturePoint StartScreen,
    CanvasGesturePoint CurrentScreen,
    SlideTransformCore Transform,
    IReadOnlyList<CanvasTransformShapeState> Shapes,
    bool SnapToFifteenDegrees);

public readonly record struct CanvasMultiTransformPlan(
    IReadOnlyList<CanvasShapeTransform> Shapes,
    IReadOnlyList<CanvasShapeTransformPreview> PreviewShapes,
    SlideScreenRect? PreviewBounds,
    double PreviewRotationDeg)
{
    public static readonly CanvasMultiTransformPlan Empty = new(
        Array.Empty<CanvasShapeTransform>(),
        Array.Empty<CanvasShapeTransformPreview>(),
        null,
        0);
}

public readonly record struct CanvasRotationRequest(
    CanvasGesturePoint CurrentScreen,
    CanvasGesturePoint CenterSlide,
    SlideTransformCore Transform,
    double OriginalRotationDeg,
    bool SnapToFifteenDegrees);

public readonly record struct CanvasMoveShapeState(
    uint ShapeId,
    long OffsetXEmu,
    long OffsetYEmu,
    long ExtentCxEmu,
    long ExtentCyEmu);

public readonly record struct CanvasMoveRequest(
    CanvasGesturePoint StartScreen,
    CanvasGesturePoint CurrentScreen,
    SlideTransformCore Transform,
    IReadOnlyList<CanvasMoveShapeState> Shapes,
    Slide? CurrentSlide,
    bool SnapToGrid,
    bool SnapToShapes,
    bool BypassSnap);

public readonly record struct CanvasMovePreviewRect(
    uint ShapeId,
    SlideScreenRect ScreenRect);

public readonly record struct CanvasMovePlan(
    long DeltaXEmu,
    long DeltaYEmu,
    IReadOnlyList<CanvasMovePreviewRect> PreviewRects,
    SlideScreenRect? PreviewBounds,
    IReadOnlyList<SnapGuideLine> SnapGuides)
{
    public static readonly CanvasMovePlan Empty = new(
        0,
        0,
        Array.Empty<CanvasMovePreviewRect>(),
        null,
        Array.Empty<SnapGuideLine>());
}

public static class CanvasGesturePlanner
{
    public const long MinimumShapeSizeEmu = DrawingMlCoordinateUnits.EmuPerInch / 10;
    public const long SmallNudgeEmu = DrawingMlCoordinateUnits.EmuPerInch / 10;
    public const long LargeNudgeEmu = DrawingMlCoordinateUnits.EmuPerInch;
    public const double DefaultDragStartThresholdPx = 3;
    public const double MeaningfulDragCommitThresholdPx = 1;

    public static long ResolveNudgeStep(bool useLargeStep) =>
        useLargeStep ? LargeNudgeEmu : SmallNudgeEmu;

    public static bool ShouldContinueDoubleClickSelection(SlideShape? shape) =>
        shape?.TextBody is null;

    public static bool HitSelectedShapeBody(
        Slide slide,
        Presentation presentation,
        IEnumerable<uint> selectedShapeIds,
        CanvasGesturePoint slidePoint,
        bool includeNestedShapes = true)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        foreach (var shapeId in selectedShapeIds)
        {
            LayoutRect? bounds;
            if (includeNestedShapes)
            {
                bounds = ShapeHitTester.GetShapeBoundsDip(slide, presentation, shapeId)
                    ?.ToLayoutRect();
            }
            else
            {
                var shape = slide.Shapes.FirstOrDefault(candidate => candidate.Id == shapeId);
                bounds = shape is null
                    ? null
                    : ShapeHitTester.GetShapeBoundsDip(shape, slide, presentation).ToLayoutRect();
            }
            if (bounds is { } hit &&
                slidePoint.X >= hit.Left && slidePoint.X <= hit.Right &&
                slidePoint.Y >= hit.Top && slidePoint.Y <= hit.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps Escape precedence identical in the WPF and Avalonia hosts. Format Painter is a
    /// separate armed mode and therefore retains its existing first-priority cancellation.
    /// </summary>
    public static CanvasEscapeAction ResolveEscapeAction(
        bool formatPainterActive,
        bool gestureActive)
        => formatPainterActive
            ? CanvasEscapeAction.CancelFormatPainter
            : gestureActive
                ? CanvasEscapeAction.CancelGesture
                : CanvasEscapeAction.None;

    public static CanvasDragReducerPlan ReduceDrag(CanvasDragReducerRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.StartThresholdPx);
        ArgumentOutOfRangeException.ThrowIfNegative(request.CommitThresholdPx);

        bool dragStarted = request.DragStarted ||
            HasMovedAtLeast(request.StartScreen, request.CurrentScreen, request.StartThresholdPx);
        bool shouldCommit = dragStarted &&
            HasMovedAtLeast(request.StartScreen, request.CurrentScreen, request.CommitThresholdPx);

        return new CanvasDragReducerPlan(dragStarted, shouldCommit);
    }

    public static IReadOnlyList<CanvasMoveShapeState> CaptureMoveState(
        Slide slide,
        IEnumerable<uint> selectedShapeIds)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        var states = new List<CanvasMoveShapeState>();
        foreach (var id in selectedShapeIds)
        {
            var shape = ShapeHitTester.FindShape(slide, id);
            if (shape is null)
                continue;

            states.Add(new CanvasMoveShapeState(
                id,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu));
        }

        return states;
    }

    public static IReadOnlyList<CanvasTransformShapeState> CaptureTransformState(
        Slide slide,
        IEnumerable<uint> selectedShapeIds)
    {
        ArgumentNullException.ThrowIfNull(slide);
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        var states = new List<CanvasTransformShapeState>();
        foreach (var id in selectedShapeIds)
        {
            var shape = ShapeHitTester.FindShape(slide, id);
            if (shape is null)
                continue;

            states.Add(new CanvasTransformShapeState(
                id,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                shape.RotationDeg));
        }

        return states;
    }

    public static CanvasMovePlan PlanMove(CanvasMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Transform);
        ArgumentNullException.ThrowIfNull(request.Shapes);

        if (request.Shapes.Count == 0)
            return CanvasMovePlan.Empty;

        double dxPx = request.CurrentScreen.X - request.StartScreen.X;
        double dyPx = request.CurrentScreen.Y - request.StartScreen.Y;
        double dxDip = request.Transform.ScaleScreenToDip(dxPx);
        double dyDip = request.Transform.ScaleScreenToDip(dyPx);
        var snap = ComputeMoveSnap(request, dxDip, dyDip);

        double snappedDxDip = dxDip + snap.SnapDx;
        double snappedDyDip = dyDip + snap.SnapDy;
        var previewRects = request.Shapes
            .Select(shape => new CanvasMovePreviewRect(
                shape.ShapeId,
                SlideCanvasGeometryPlanner.DipBoundsToScreen(
                    SlideTransformCore.EmuToDip(shape.OffsetXEmu) + snappedDxDip,
                    SlideTransformCore.EmuToDip(shape.OffsetYEmu) + snappedDyDip,
                    SlideTransformCore.EmuToDip(shape.ExtentCxEmu),
                    SlideTransformCore.EmuToDip(shape.ExtentCyEmu),
                    request.Transform)))
            .ToArray();

        double snapDxPx = snap.SnapDx * request.Transform.Scale;
        double snapDyPx = snap.SnapDy * request.Transform.Scale;
        return new CanvasMovePlan(
            request.Transform.ScreenDeltaToEmu(dxPx + snapDxPx),
            request.Transform.ScreenDeltaToEmu(dyPx + snapDyPx),
            previewRects,
            SlideCanvasGeometryPlanner.Union(previewRects.Select(r => r.ScreenRect)),
            snap.Guides);
    }

    public static CanvasResizeBounds ComputeResizeBounds(CanvasResizeRequest request)
    {
        var state = request.State;
        double dxPx = request.CurrentScreen.X - request.StartScreen.X;
        double dyPx = request.CurrentScreen.Y - request.StartScreen.Y;

        double origXDip = SlideTransformCore.EmuToDip(state.XEmu);
        double origYDip = SlideTransformCore.EmuToDip(state.YEmu);
        double origCxDip = SlideTransformCore.EmuToDip(state.CxEmu);
        double origCyDip = SlideTransformCore.EmuToDip(state.CyEmu);
        double dxDip = request.Transform.ScaleScreenToDip(dxPx);
        double dyDip = request.Transform.ScaleScreenToDip(dyPx);

        double rotation = state.RotationDeg;
        if (rotation != 0)
            (dxDip, dyDip) = SlideTransformCore.UnRotateDelta(dxDip, dyDip, rotation);

        bool snapEnabled = (request.SnapToGrid || request.SnapToShapes) && !request.BypassSnap;
        if (snapEnabled && request.CurrentSlide is not null)
        {
            var candidates = request.SnapToShapes
                ? SnapEngine.BuildShapeCandidates(request.CurrentSlide, new[] { state.ShapeId })
                : null;
            double slideW = request.Transform.SlideWidthDip;
            double slideH = request.Transform.SlideHeightDip;
            double pitch = request.SnapToGrid ? SnapEngine.DefaultGridPitchDip : 0;

            ApplyResizeSnap(
                state.Handle,
                origXDip,
                origYDip,
                origCxDip,
                origCyDip,
                ref dxDip,
                ref dyDip,
                candidates,
                slideW,
                slideH,
                pitch);
        }

        long dx = request.Transform.ScreenDeltaToEmu(dxDip * request.Transform.Scale);
        long dy = request.Transform.ScreenDeltaToEmu(dyDip * request.Transform.Scale);

        var bounds = ApplyResizeDelta(state, dx, dy);
        if (rotation != 0)
            bounds = KeepRotatedAnchorFixed(state, bounds, rotation);

        return bounds;
    }

    public static CanvasMultiTransformPlan PlanMultiResize(CanvasMultiResizeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Transform);
        ArgumentNullException.ThrowIfNull(request.Shapes);

        if (request.Shapes.Count == 0)
            return CanvasMultiTransformPlan.Empty;

        var original = GetTransformBounds(request.Shapes);
        double dxDip = request.Transform.ScaleScreenToDip(
            request.CurrentScreen.X - request.StartScreen.X);
        double dyDip = request.Transform.ScaleScreenToDip(
            request.CurrentScreen.Y - request.StartScreen.Y);

        bool snapEnabled = (request.SnapToGrid || request.SnapToShapes) && !request.BypassSnap;
        if (snapEnabled && request.CurrentSlide is not null)
        {
            var candidates = request.SnapToShapes
                ? SnapEngine.BuildShapeCandidates(
                    request.CurrentSlide,
                    request.Shapes.Select(shape => shape.ShapeId))
                : null;
            ApplyResizeSnap(
                request.Handle,
                original.X,
                original.Y,
                original.Width,
                original.Height,
                ref dxDip,
                ref dyDip,
                candidates,
                request.Transform.SlideWidthDip,
                request.Transform.SlideHeightDip,
                request.SnapToGrid ? SnapEngine.DefaultGridPitchDip : 0);
        }

        var groupState = new CanvasResizeState(
            0,
            SlideTransformCore.DipToEmu(original.X),
            SlideTransformCore.DipToEmu(original.Y),
            SlideTransformCore.DipToEmu(original.Width),
            SlideTransformCore.DipToEmu(original.Height),
            0,
            request.Handle);
        var deltaXEmu = request.Transform.ScreenDeltaToEmu(dxDip * request.Transform.Scale);
        var deltaYEmu = request.Transform.ScreenDeltaToEmu(dyDip * request.Transform.Scale);
        var resized = ApplyResizeDelta(groupState, deltaXEmu, deltaYEmu);
        double newX = SlideTransformCore.EmuToDip(resized.XEmu);
        double newY = SlideTransformCore.EmuToDip(resized.YEmu);
        double newWidth = SlideTransformCore.EmuToDip(resized.CxEmu);
        double newHeight = SlideTransformCore.EmuToDip(resized.CyEmu);
        double scaleX = original.Width == 0 ? 1 : newWidth / original.Width;
        double scaleY = original.Height == 0 ? 1 : newHeight / original.Height;

        var transforms = request.Shapes
            .Select(shape => new CanvasShapeTransform(
                shape.ShapeId,
                SlideTransformCore.DipToEmu(newX +
                    (SlideTransformCore.EmuToDip(shape.XEmu) - original.X) * scaleX),
                SlideTransformCore.DipToEmu(newY +
                    (SlideTransformCore.EmuToDip(shape.YEmu) - original.Y) * scaleY),
                SlideTransformCore.DipToEmu(SlideTransformCore.EmuToDip(shape.CxEmu) * scaleX),
                SlideTransformCore.DipToEmu(SlideTransformCore.EmuToDip(shape.CyEmu) * scaleY),
                shape.RotationDeg))
            .ToArray();

        return CreateMultiTransformPlan(
            transforms,
            request.Transform,
            SlideCanvasGeometryPlanner.DipBoundsToScreen(
                newX,
                newY,
                newWidth,
                newHeight,
                request.Transform),
            0);
    }

    public static CanvasMultiTransformPlan PlanMultiRotate(CanvasMultiRotateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Transform);
        ArgumentNullException.ThrowIfNull(request.Shapes);

        if (request.Shapes.Count == 0)
            return CanvasMultiTransformPlan.Empty;

        var group = GetTransformBounds(request.Shapes);
        var centerSlide = new LayoutPoint(
            group.X + group.Width / 2.0,
            group.Y + group.Height / 2.0);
        var centerScreen = request.Transform.SlideToScreen(centerSlide.X, centerSlide.Y);
        double delta = DrawingObjectInteractionPlanner.CalculateRotationDelta(
            new LayoutPoint(centerScreen.X, centerScreen.Y),
            new LayoutPoint(request.StartScreen.X, request.StartScreen.Y),
            new LayoutPoint(request.CurrentScreen.X, request.CurrentScreen.Y));
        if (request.SnapToFifteenDegrees)
            delta = Math.Round(delta / 15.0) * 15.0;

        var transforms = request.Shapes
            .Select(shape =>
            {
                double width = SlideTransformCore.EmuToDip(shape.CxEmu);
                double height = SlideTransformCore.EmuToDip(shape.CyEmu);
                double shapeCenterX = SlideTransformCore.EmuToDip(shape.XEmu) + width / 2.0;
                double shapeCenterY = SlideTransformCore.EmuToDip(shape.YEmu) + height / 2.0;
                var rotatedCenter = DrawingObjectInteractionPlanner.RotatePoint(
                    new LayoutPoint(shapeCenterX, shapeCenterY),
                    centerSlide,
                    delta);

                return new CanvasShapeTransform(
                    shape.ShapeId,
                    SlideTransformCore.DipToEmu(rotatedCenter.X - width / 2.0),
                    SlideTransformCore.DipToEmu(rotatedCenter.Y - height / 2.0),
                    shape.CxEmu,
                    shape.CyEmu,
                    shape.RotationDeg + delta);
            })
            .ToArray();

        return CreateMultiTransformPlan(
            transforms,
            request.Transform,
            SlideCanvasGeometryPlanner.DipBoundsToScreen(
                group.X,
                group.Y,
                group.Width,
                group.Height,
                request.Transform),
            delta);
    }

    private static CanvasMultiTransformPlan CreateMultiTransformPlan(
        IReadOnlyList<CanvasShapeTransform> transforms,
        SlideTransformCore transform,
        SlideScreenRect? previewBounds,
        double previewRotationDeg)
    {
        var previews = transforms
            .Select(shape => new CanvasShapeTransformPreview(
                shape.ShapeId,
                SlideCanvasGeometryPlanner.EmuBoundsToScreen(
                    shape.XEmu,
                    shape.YEmu,
                    shape.CxEmu,
                    shape.CyEmu,
                    transform),
                shape.RotationDeg))
            .ToArray();

        return new CanvasMultiTransformPlan(
            transforms,
            previews,
            previewBounds,
            previewRotationDeg);
    }

    public static double ComputeRotationAngle(CanvasRotationRequest request)
    {
        double cx = request.CenterSlide.X * request.Transform.Scale + request.Transform.OffsetX;
        double cy = request.CenterSlide.Y * request.Transform.Scale + request.Transform.OffsetY;

        double angle = DrawingObjectInteractionPlanner.CalculateRotationDegrees(
            new LayoutPoint(cx, cy),
            new LayoutPoint(request.CurrentScreen.X, request.CurrentScreen.Y));

        if (request.SnapToFifteenDegrees)
            angle = Math.Round(angle / 15.0) * 15.0;

        return angle;
    }

    private static LayoutRect GetTransformBounds(IReadOnlyList<CanvasTransformShapeState> shapes)
    {
        double left = double.PositiveInfinity;
        double top = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double bottom = double.NegativeInfinity;

        foreach (var shape in shapes)
        {
            double x = SlideTransformCore.EmuToDip(shape.XEmu);
            double y = SlideTransformCore.EmuToDip(shape.YEmu);
            double rightEdge = x + SlideTransformCore.EmuToDip(shape.CxEmu);
            double bottomEdge = y + SlideTransformCore.EmuToDip(shape.CyEmu);
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, rightEdge);
            bottom = Math.Max(bottom, bottomEdge);
        }

        return new LayoutRect(left, top, right - left, bottom - top);
    }

    private static SnapResult ComputeMoveSnap(
        CanvasMoveRequest request,
        double dxDip,
        double dyDip)
    {
        bool snapEnabled = (request.SnapToGrid || request.SnapToShapes) && !request.BypassSnap;
        if (!snapEnabled || request.CurrentSlide is null || request.Shapes.Count == 0)
            return SnapResult.None;

        var anchor = request.Shapes[0];
        double newLeftDip = SlideTransformCore.EmuToDip(anchor.OffsetXEmu) + dxDip;
        double newTopDip = SlideTransformCore.EmuToDip(anchor.OffsetYEmu) + dyDip;
        double newRightDip = newLeftDip + SlideTransformCore.EmuToDip(anchor.ExtentCxEmu);
        double newBottomDip = newTopDip + SlideTransformCore.EmuToDip(anchor.ExtentCyEmu);
        var candidates = request.SnapToShapes
            ? SnapEngine.BuildShapeCandidates(
                request.CurrentSlide,
                request.Shapes.Select(shape => shape.ShapeId))
            : null;

        return SnapEngine.Snap(
            (newLeftDip, newTopDip, newRightDip, newBottomDip),
            candidates,
            request.Transform.SlideWidthDip,
            request.Transform.SlideHeightDip,
            snapEnabled: true,
            gridPitchDip: request.SnapToGrid ? SnapEngine.DefaultGridPitchDip : 0);
    }

    private static void ApplyResizeSnap(
        CanvasGestureHandleKind handle,
        double origXDip,
        double origYDip,
        double origCxDip,
        double origCyDip,
        ref double dxDip,
        ref double dyDip,
        IEnumerable<SnapCandidate>? candidates,
        double slideW,
        double slideH,
        double pitch)
    {
        switch (handle)
        {
            case CanvasGestureHandleKind.ResizeN:
            {
                double draggedY = origYDip + dyDip;
                var snap = SnapEngine.Snap(
                    (origXDip, draggedY, origXDip + origCxDip, draggedY),
                    candidates, slideW, slideH, true, pitch);
                dyDip += snap.SnapDy;
                break;
            }
            case CanvasGestureHandleKind.ResizeS:
            {
                double draggedY = origYDip + origCyDip + dyDip;
                var snap = SnapEngine.Snap(
                    (origXDip, draggedY, origXDip + origCxDip, draggedY),
                    candidates, slideW, slideH, true, pitch);
                dyDip += snap.SnapDy;
                break;
            }
            case CanvasGestureHandleKind.ResizeW:
            {
                double draggedX = origXDip + dxDip;
                var snap = SnapEngine.Snap(
                    (draggedX, origYDip, draggedX, origYDip + origCyDip),
                    candidates, slideW, slideH, true, pitch);
                dxDip += snap.SnapDx;
                break;
            }
            case CanvasGestureHandleKind.ResizeE:
            {
                double draggedX = origXDip + origCxDip + dxDip;
                var snap = SnapEngine.Snap(
                    (draggedX, origYDip, draggedX, origYDip + origCyDip),
                    candidates, slideW, slideH, true, pitch);
                dxDip += snap.SnapDx;
                break;
            }
            case CanvasGestureHandleKind.ResizeNE:
            {
                double draggedX = origXDip + origCxDip + dxDip;
                double draggedY = origYDip + dyDip;
                var snap = SnapEngine.Snap(
                    (draggedX, draggedY, draggedX, draggedY),
                    candidates, slideW, slideH, true, pitch);
                dxDip += snap.SnapDx;
                dyDip += snap.SnapDy;
                break;
            }
            case CanvasGestureHandleKind.ResizeNW:
            {
                double draggedX = origXDip + dxDip;
                double draggedY = origYDip + dyDip;
                var snap = SnapEngine.Snap(
                    (draggedX, draggedY, draggedX, draggedY),
                    candidates, slideW, slideH, true, pitch);
                dxDip += snap.SnapDx;
                dyDip += snap.SnapDy;
                break;
            }
            case CanvasGestureHandleKind.ResizeSE:
            {
                double draggedX = origXDip + origCxDip + dxDip;
                double draggedY = origYDip + origCyDip + dyDip;
                var snap = SnapEngine.Snap(
                    (draggedX, draggedY, draggedX, draggedY),
                    candidates, slideW, slideH, true, pitch);
                dxDip += snap.SnapDx;
                dyDip += snap.SnapDy;
                break;
            }
            case CanvasGestureHandleKind.ResizeSW:
            {
                double draggedX = origXDip + dxDip;
                double draggedY = origYDip + origCyDip + dyDip;
                var snap = SnapEngine.Snap(
                    (draggedX, draggedY, draggedX, draggedY),
                    candidates, slideW, slideH, true, pitch);
                dxDip += snap.SnapDx;
                dyDip += snap.SnapDy;
                break;
            }
        }
    }

    private static CanvasResizeBounds ApplyResizeDelta(CanvasResizeState state, long dx, long dy)
    {
        long x = state.XEmu;
        long y = state.YEmu;
        long cx = state.CxEmu;
        long cy = state.CyEmu;

        switch (state.Handle)
        {
            case CanvasGestureHandleKind.ResizeN:
                y = state.YEmu + dy;
                cy = Math.Max(MinimumShapeSizeEmu, state.CyEmu - dy);
                break;
            case CanvasGestureHandleKind.ResizeS:
                cy = Math.Max(MinimumShapeSizeEmu, state.CyEmu + dy);
                break;
            case CanvasGestureHandleKind.ResizeW:
                x = state.XEmu + dx;
                cx = Math.Max(MinimumShapeSizeEmu, state.CxEmu - dx);
                break;
            case CanvasGestureHandleKind.ResizeE:
                cx = Math.Max(MinimumShapeSizeEmu, state.CxEmu + dx);
                break;
            case CanvasGestureHandleKind.ResizeNE:
                y = state.YEmu + dy;
                cy = Math.Max(MinimumShapeSizeEmu, state.CyEmu - dy);
                cx = Math.Max(MinimumShapeSizeEmu, state.CxEmu + dx);
                break;
            case CanvasGestureHandleKind.ResizeNW:
                x = state.XEmu + dx;
                y = state.YEmu + dy;
                cx = Math.Max(MinimumShapeSizeEmu, state.CxEmu - dx);
                cy = Math.Max(MinimumShapeSizeEmu, state.CyEmu - dy);
                break;
            case CanvasGestureHandleKind.ResizeSE:
                cx = Math.Max(MinimumShapeSizeEmu, state.CxEmu + dx);
                cy = Math.Max(MinimumShapeSizeEmu, state.CyEmu + dy);
                break;
            case CanvasGestureHandleKind.ResizeSW:
                x = state.XEmu + dx;
                cx = Math.Max(MinimumShapeSizeEmu, state.CxEmu - dx);
                cy = Math.Max(MinimumShapeSizeEmu, state.CyEmu + dy);
                break;
        }

        return new CanvasResizeBounds(x, y, cx, cy);
    }

    private static CanvasResizeBounds KeepRotatedAnchorFixed(
        CanvasResizeState state,
        CanvasResizeBounds bounds,
        double rotation)
    {
        var originalAnchorLocal = GetOriginalAnchorLocal(state);

        double origCenterX = SlideTransformCore.EmuToDip(state.XEmu) +
            SlideTransformCore.EmuToDip(state.CxEmu) / 2.0;
        double origCenterY = SlideTransformCore.EmuToDip(state.YEmu) +
            SlideTransformCore.EmuToDip(state.CyEmu) / 2.0;

        var anchorWorld = DrawingObjectInteractionPlanner.RotatePoint(
            originalAnchorLocal,
            new LayoutPoint(origCenterX, origCenterY),
            rotation);

        double newXDip = SlideTransformCore.EmuToDip(bounds.XEmu);
        double newYDip = SlideTransformCore.EmuToDip(bounds.YEmu);
        double newCxDip = SlideTransformCore.EmuToDip(bounds.CxEmu);
        double newCyDip = SlideTransformCore.EmuToDip(bounds.CyEmu);

        var newAnchorLocal = GetNewAnchorLocal(state.Handle, newXDip, newYDip, newCxDip, newCyDip);

        double newCenterLocalX = newXDip + newCxDip / 2.0;
        double newCenterLocalY = newYDip + newCyDip / 2.0;

        var newAnchorWorld = DrawingObjectInteractionPlanner.RotatePoint(
            newAnchorLocal,
            new LayoutPoint(newCenterLocalX, newCenterLocalY),
            rotation);

        double shiftX = anchorWorld.X - newAnchorWorld.X;
        double shiftY = anchorWorld.Y - newAnchorWorld.Y;

        return bounds with
        {
            XEmu = SlideTransformCore.DipToEmu(newXDip + shiftX),
            YEmu = SlideTransformCore.DipToEmu(newYDip + shiftY)
        };
    }

    private static LayoutPoint GetOriginalAnchorLocal(CanvasResizeState state)
    {
        double x = SlideTransformCore.EmuToDip(state.XEmu);
        double y = SlideTransformCore.EmuToDip(state.YEmu);
        double cx = SlideTransformCore.EmuToDip(state.CxEmu);
        double cy = SlideTransformCore.EmuToDip(state.CyEmu);

        return DrawingObjectInteractionPlanner.GetFixedResizeAnchor(
            ToSharedHandle(state.Handle),
            new LayoutRect(x, y, cx, cy));
    }

    private static LayoutPoint GetNewAnchorLocal(
        CanvasGestureHandleKind handle,
        double x,
        double y,
        double cx,
        double cy)
        => DrawingObjectInteractionPlanner.GetFixedResizeAnchor(
            ToSharedHandle(handle),
            new LayoutRect(x, y, cx, cy));

    private static DrawingObjectInteractionKind ToSharedHandle(CanvasGestureHandleKind handle) =>
        handle switch
        {
            CanvasGestureHandleKind.Body => DrawingObjectInteractionKind.Body,
            CanvasGestureHandleKind.ResizeN => DrawingObjectInteractionKind.ResizeN,
            CanvasGestureHandleKind.ResizeNE => DrawingObjectInteractionKind.ResizeNE,
            CanvasGestureHandleKind.ResizeE => DrawingObjectInteractionKind.ResizeE,
            CanvasGestureHandleKind.ResizeSE => DrawingObjectInteractionKind.ResizeSE,
            CanvasGestureHandleKind.ResizeS => DrawingObjectInteractionKind.ResizeS,
            CanvasGestureHandleKind.ResizeSW => DrawingObjectInteractionKind.ResizeSW,
            CanvasGestureHandleKind.ResizeW => DrawingObjectInteractionKind.ResizeW,
            CanvasGestureHandleKind.ResizeNW => DrawingObjectInteractionKind.ResizeNW,
            CanvasGestureHandleKind.Rotate => DrawingObjectInteractionKind.Rotate,
            _ => DrawingObjectInteractionKind.None
        };

    private static bool HasMovedAtLeast(
        CanvasGesturePoint start,
        CanvasGesturePoint current,
        double thresholdPx)
        => Math.Abs(current.X - start.X) >= thresholdPx ||
           Math.Abs(current.Y - start.Y) >= thresholdPx;
}
