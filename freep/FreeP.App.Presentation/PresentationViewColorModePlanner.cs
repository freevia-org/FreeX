namespace FreeP.App.Compositor;

/// <summary>
/// Non-destructive display treatment selected from the PowerPoint-compatible
/// View &gt; Color/Grayscale group. The value belongs to a window, never to the
/// presentation package, print output, or exported slide.
/// </summary>
public enum PresentationViewColorMode
{
    Color,
    Grayscale,
    BlackAndWhite,
}

public readonly record struct PresentationViewColorModeState(PresentationViewColorMode Mode)
{
    public static PresentationViewColorModeState Color { get; } = new(PresentationViewColorMode.Color);
}

public readonly record struct PresentationViewColorModeCommandPlan(
    string CommandId,
    PresentationViewColorMode Mode,
    bool IsChecked);

public static class PresentationViewColorModePlanner
{
    public const string ColorCommandId = "freep.view.color";
    public const string GrayscaleCommandId = "freep.view.grayscale";
    public const string BlackAndWhiteCommandId = "freep.view.black-and-white";

    public static IReadOnlyList<PresentationViewColorModeCommandPlan> BuildPlans(
        PresentationViewColorModeState state) =>
        [
            BuildPlan(PresentationViewColorMode.Color, state),
            BuildPlan(PresentationViewColorMode.Grayscale, state),
            BuildPlan(PresentationViewColorMode.BlackAndWhite, state),
        ];

    public static PresentationViewColorModeCommandPlan BuildPlan(
        PresentationViewColorMode mode,
        PresentationViewColorModeState state) =>
        new(CommandIdFor(mode), mode, state.Mode == mode);

    public static PresentationViewColorModeState Select(
        PresentationViewColorModeState _,
        PresentationViewColorModeCommandPlan plan) => new(plan.Mode);

    public static string CommandIdFor(PresentationViewColorMode mode) => mode switch
    {
        PresentationViewColorMode.Color => ColorCommandId,
        PresentationViewColorMode.Grayscale => GrayscaleCommandId,
        PresentationViewColorMode.BlackAndWhite => BlackAndWhiteCommandId,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static bool TryGetMode(string commandId, out PresentationViewColorMode mode)
    {
        switch (commandId)
        {
            case ColorCommandId:
                mode = PresentationViewColorMode.Color;
                return true;
            case GrayscaleCommandId:
                mode = PresentationViewColorMode.Grayscale;
                return true;
            case BlackAndWhiteCommandId:
                mode = PresentationViewColorMode.BlackAndWhite;
                return true;
            default:
                mode = default;
                return false;
        }
    }
}
