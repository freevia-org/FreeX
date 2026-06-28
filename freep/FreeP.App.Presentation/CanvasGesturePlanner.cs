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

public readonly record struct CanvasGesturePoint(double X, double Y);

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

public readonly record struct CanvasRotationRequest(
    CanvasGesturePoint CurrentScreen,
    CanvasGesturePoint CenterSlide,
    SlideTransformCore Transform,
    double OriginalRotationDeg,
    bool SnapToFifteenDegrees);

public static class CanvasGesturePlanner
{
    public const long MinimumShapeSizeEmu = 91440L;

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

    public static double ComputeRotationAngle(CanvasRotationRequest request)
    {
        double cx = request.CenterSlide.X * request.Transform.Scale + request.Transform.OffsetX;
        double cy = request.CenterSlide.Y * request.Transform.Scale + request.Transform.OffsetY;

        double angle = Math.Atan2(request.CurrentScreen.Y - cy, request.CurrentScreen.X - cx) *
            (180.0 / Math.PI) + 90.0;
        angle = ((angle % 360) + 360) % 360;
        angle = request.OriginalRotationDeg + (angle - request.OriginalRotationDeg);

        if (request.SnapToFifteenDegrees)
            angle = Math.Round(angle / 15.0) * 15.0;

        return angle;
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
        (double anchorLocalX, double anchorLocalY) = GetOriginalAnchorLocal(state);

        double origCenterX = SlideTransformCore.EmuToDip(state.XEmu) +
            SlideTransformCore.EmuToDip(state.CxEmu) / 2.0;
        double origCenterY = SlideTransformCore.EmuToDip(state.YEmu) +
            SlideTransformCore.EmuToDip(state.CyEmu) / 2.0;

        var (anchorWorldX, anchorWorldY) =
            SlideTransformCore.RotatePoint(anchorLocalX, anchorLocalY, origCenterX, origCenterY, rotation);

        double newXDip = SlideTransformCore.EmuToDip(bounds.XEmu);
        double newYDip = SlideTransformCore.EmuToDip(bounds.YEmu);
        double newCxDip = SlideTransformCore.EmuToDip(bounds.CxEmu);
        double newCyDip = SlideTransformCore.EmuToDip(bounds.CyEmu);

        (double newAnchorLocalX, double newAnchorLocalY) =
            GetNewAnchorLocal(state.Handle, newXDip, newYDip, newCxDip, newCyDip);

        double newCenterLocalX = newXDip + newCxDip / 2.0;
        double newCenterLocalY = newYDip + newCyDip / 2.0;

        var (newAnchorWorldX, newAnchorWorldY) =
            SlideTransformCore.RotatePoint(
                newAnchorLocalX,
                newAnchorLocalY,
                newCenterLocalX,
                newCenterLocalY,
                rotation);

        double shiftX = anchorWorldX - newAnchorWorldX;
        double shiftY = anchorWorldY - newAnchorWorldY;

        return bounds with
        {
            XEmu = SlideTransformCore.DipToEmu(newXDip + shiftX),
            YEmu = SlideTransformCore.DipToEmu(newYDip + shiftY)
        };
    }

    private static (double X, double Y) GetOriginalAnchorLocal(CanvasResizeState state)
    {
        double x = SlideTransformCore.EmuToDip(state.XEmu);
        double y = SlideTransformCore.EmuToDip(state.YEmu);
        double cx = SlideTransformCore.EmuToDip(state.CxEmu);
        double cy = SlideTransformCore.EmuToDip(state.CyEmu);

        return state.Handle switch
        {
            CanvasGestureHandleKind.ResizeSE => (x, y),
            CanvasGestureHandleKind.ResizeNW => (x + cx, y + cy),
            CanvasGestureHandleKind.ResizeNE => (x, y + cy),
            CanvasGestureHandleKind.ResizeSW => (x + cx, y),
            CanvasGestureHandleKind.ResizeN => (x + cx / 2, y + cy),
            CanvasGestureHandleKind.ResizeS => (x + cx / 2, y),
            CanvasGestureHandleKind.ResizeW => (x + cx, y + cy / 2),
            CanvasGestureHandleKind.ResizeE => (x, y + cy / 2),
            _ => (x, y)
        };
    }

    private static (double X, double Y) GetNewAnchorLocal(
        CanvasGestureHandleKind handle,
        double x,
        double y,
        double cx,
        double cy)
        => handle switch
        {
            CanvasGestureHandleKind.ResizeSE => (x, y),
            CanvasGestureHandleKind.ResizeNW => (x + cx, y + cy),
            CanvasGestureHandleKind.ResizeNE => (x, y + cy),
            CanvasGestureHandleKind.ResizeSW => (x + cx, y),
            CanvasGestureHandleKind.ResizeN => (x + cx / 2, y + cy),
            CanvasGestureHandleKind.ResizeS => (x + cx / 2, y),
            CanvasGestureHandleKind.ResizeW => (x + cx, y + cy / 2),
            CanvasGestureHandleKind.ResizeE => (x, y + cy / 2),
            _ => (x, y)
        };
}
