using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowTransitionPlaybackKind
{
    Cut,
    Fade,
    Dissolve,
    Box,
    Reveal,
    Uncover,
    Cover,
    Push,
    Split,
    Blinds,
    RandomBars,
    Strips,
    Wheel,
    Zoom,
    PushLike,
    FadeFallback
}

public sealed record SlideShowTransitionPlan(
    SlideShowTransitionPlaybackKind PlaybackKind,
    double IncomingOffsetX,
    double IncomingOffsetY,
    bool SplitHorizontal,
    bool SplitOut,
    bool BlindsHorizontal,
    bool RandomBarsHorizontal,
    bool StripsSlopeDown,
    int WheelSpokeCount,
    bool WheelReverse,
    bool ZoomIn,
    bool BoxExpandsFromCenter);

public static class SlideShowTransitionPlanner
{
    public static SlideShowTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var (x, y) = ResolveIncomingOffset(transition.Direction);
        return new SlideShowTransitionPlan(
            PlanPlaybackKind(transition.Kind),
            x,
            y,
            ResolveSplitHorizontal(transition),
            ResolveSplitOut(transition),
            ResolveBlindsHorizontal(transition),
            ResolveRandomBarsHorizontal(transition),
            ResolveStripsSlopeDown(transition),
            ResolveWheelSpokeCount(transition),
            transition.Kind == TransitionKind.WheelReverse,
            transition.Direction != TransitionDirection.Out,
            ResolveBoxExpandsFromCenter(transition));
    }

    public static SlideShowTransitionPlaybackKind PlanPlaybackKind(TransitionKind kind) =>
        kind switch
        {
            TransitionKind.None or
            TransitionKind.Cut => SlideShowTransitionPlaybackKind.Cut,

            TransitionKind.Fade or
            TransitionKind.Flash => SlideShowTransitionPlaybackKind.Fade,

            TransitionKind.Dissolve => SlideShowTransitionPlaybackKind.Dissolve,

            TransitionKind.Box => SlideShowTransitionPlaybackKind.Box,

            TransitionKind.Reveal => SlideShowTransitionPlaybackKind.Reveal,

            TransitionKind.Wipe => SlideShowTransitionPlaybackKind.Reveal,

            TransitionKind.Uncover => SlideShowTransitionPlaybackKind.Uncover,

            TransitionKind.Cover => SlideShowTransitionPlaybackKind.Cover,

            TransitionKind.Push => SlideShowTransitionPlaybackKind.Push,

            TransitionKind.Doors => SlideShowTransitionPlaybackKind.Split,

            TransitionKind.Split => SlideShowTransitionPlaybackKind.Split,

            TransitionKind.Blinds => SlideShowTransitionPlaybackKind.Blinds,

            // OOXML comb is a directional bar wipe; reuse the renderer-neutral
            // blinds geometry so both slideshow hosts preserve horz/vert axes.
            TransitionKind.Comb => SlideShowTransitionPlaybackKind.Blinds,

            TransitionKind.RandomBar => SlideShowTransitionPlaybackKind.RandomBars,

            TransitionKind.Strips => SlideShowTransitionPlaybackKind.Strips,

            TransitionKind.Wheel or
            TransitionKind.WheelReverse => SlideShowTransitionPlaybackKind.Wheel,

            TransitionKind.Zoom => SlideShowTransitionPlaybackKind.Zoom,

            TransitionKind.Gallery or
            TransitionKind.Conveyor or
            TransitionKind.Pan or
            TransitionKind.Window => SlideShowTransitionPlaybackKind.PushLike,

            _ => SlideShowTransitionPlaybackKind.FadeFallback
        };

    private static bool ResolveSplitHorizontal(SlideTransition transition) =>
        transition.Kind == TransitionKind.Doors
        || transition.SplitOrientation == TransitionDirection.Horizontal
        || (transition.SplitOrientation is null
            && transition.Direction != TransitionDirection.Vertical);

    private static bool ResolveSplitOut(SlideTransition transition) =>
        transition.Kind == TransitionKind.Doors
        || transition.Direction != TransitionDirection.In;

    private static bool ResolveBlindsHorizontal(SlideTransition transition) =>
        transition.Direction != TransitionDirection.Vertical;

    private static bool ResolveRandomBarsHorizontal(SlideTransition transition) =>
        transition.Direction != TransitionDirection.Vertical;

    private static bool ResolveStripsSlopeDown(SlideTransition transition) =>
        transition.Direction is TransitionDirection.LeftDown or TransitionDirection.RightUp;

    private static int ResolveWheelSpokeCount(SlideTransition transition) =>
        Math.Clamp(transition.WheelSpokeCount is > 0 ? transition.WheelSpokeCount.Value : 4, 1, 32);

    private static bool ResolveBoxExpandsFromCenter(SlideTransition transition) =>
        transition.Direction switch
        {
            TransitionDirection.In => true,
            TransitionDirection.Out => false,
            _ => true
        };

    public static (double X, double Y) ResolveIncomingOffset(TransitionDirection? direction) =>
        direction switch
        {
            TransitionDirection.Right => (-1, 0),
            TransitionDirection.Left => (1, 0),
            TransitionDirection.Down => (0, -1),
            TransitionDirection.Up => (0, 1),
            _ => (1, 0)
        };
}
