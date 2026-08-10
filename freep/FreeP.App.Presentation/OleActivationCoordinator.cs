using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record OleInPlaceActivationPlan(
    OleObjectInfo OleObject,
    SlideScreenRect Bounds);

/// <summary>
/// Owns renderer-neutral eligibility and fallback ordering for slide-level OLE activation.
/// Renderers supply native in-place activation and may inject an external route for testing.
/// </summary>
public static class OleActivationCoordinator
{
    public const double InPlaceRotationToleranceDegrees = 0.01;

    public static bool IsEligible(SlideShape? shape) =>
        shape is { Kind: SlideShapeKind.Ole, OleObject: not null };

    public static OleInPlaceActivationPlan? PlanInPlaceActivation(
        SlideShape? shape,
        SlideTransformCore transform,
        double overlayOffsetX = 0,
        double overlayOffsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(transform);

        if (!IsEligible(shape))
            return null;

        var eligibleShape = shape!;
        if (!double.IsFinite(eligibleShape.RotationDeg)
            || Math.Abs(eligibleShape.RotationDeg) > InPlaceRotationToleranceDegrees
            || eligibleShape.FlipH
            || eligibleShape.FlipV
            || eligibleShape.ExtentCxEmu <= 0
            || eligibleShape.ExtentCyEmu <= 0
            || !double.IsFinite(overlayOffsetX)
            || !double.IsFinite(overlayOffsetY))
        {
            return null;
        }

        var bounds = SlideCanvasGeometryPlanner.EmuBoundsToScreen(
            eligibleShape.OffsetXEmu,
            eligibleShape.OffsetYEmu,
            eligibleShape.ExtentCxEmu,
            eligibleShape.ExtentCyEmu,
            transform);
        bounds = bounds with
        {
            Left = bounds.Left + overlayOffsetX,
            Top = bounds.Top + overlayOffsetY,
        };

        if (!double.IsFinite(bounds.Left)
            || !double.IsFinite(bounds.Top)
            || !double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height)
            || bounds.Width <= 0
            || bounds.Height <= 0)
        {
            return null;
        }

        return new OleInPlaceActivationPlan(eligibleShape.OleObject!, bounds);
    }

    public static bool TryActivate(
        SlideShape? shape,
        Func<SlideShape, bool>? tryActivateInPlace = null,
        Func<OleObjectInfo?, bool>? tryActivateInjected = null,
        Func<OleObjectInfo?, bool>? tryActivateDefault = null)
    {
        if (!IsEligible(shape))
            return false;

        var eligibleShape = shape!;
        var oleObject = eligibleShape.OleObject!;

        if (tryActivateInPlace?.Invoke(eligibleShape) == true)
            return true;

        if (tryActivateInjected?.Invoke(oleObject) == true)
            return true;

        return tryActivateDefault?.Invoke(oleObject)
            ?? OleActivationService.TryActivate(oleObject);
    }
}
