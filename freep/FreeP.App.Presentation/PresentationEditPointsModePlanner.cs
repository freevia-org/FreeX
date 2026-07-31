namespace FreeP.App.Compositor;

public sealed record PresentationEditPointsModePlan(
    bool CurrentIsEnabled,
    bool NextIsEnabled);

/// <summary>Shared state transition for the FreeP Edit Points interaction mode.</summary>
public static class PresentationEditPointsModePlanner
{
    public const string CommandId = "freep.arrange.edit-points";

    public static PresentationEditPointsModePlan BuildTogglePlan(bool isEnabled) =>
        new(isEnabled, !isEnabled);
}
