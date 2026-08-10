using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Owns renderer-neutral eligibility and fallback ordering for slide-level OLE activation.
/// Renderers supply native in-place activation and may inject an external route for testing.
/// </summary>
public static class OleActivationCoordinator
{
    public static bool IsEligible(SlideShape? shape) =>
        shape is { Kind: SlideShapeKind.Ole, OleObject: not null };

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
