using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowTransitionPlaybackKind
{
    Cut,
    Fade,
    Split,
    Blinds,
    PushLike,
    FadeFallback
}

public sealed record SlideShowTransitionPlan(
    SlideShowTransitionPlaybackKind PlaybackKind,
    double IncomingOffsetX,
    double IncomingOffsetY,
    bool SplitHorizontal,
    bool SplitOut,
    bool BlindsHorizontal);

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
            ResolveBlindsHorizontal(transition));
    }

    public static SlideShowTransitionPlaybackKind PlanPlaybackKind(TransitionKind kind) =>
        kind switch
        {
            TransitionKind.None or
            TransitionKind.Cut => SlideShowTransitionPlaybackKind.Cut,

            TransitionKind.Fade or
            TransitionKind.Dissolve or
            TransitionKind.Flash => SlideShowTransitionPlaybackKind.Fade,

            TransitionKind.Split => SlideShowTransitionPlaybackKind.Split,

            TransitionKind.Blinds => SlideShowTransitionPlaybackKind.Blinds,

            TransitionKind.Push or
            TransitionKind.Cover or
            TransitionKind.Wipe or
            TransitionKind.Uncover or
            TransitionKind.Gallery or
            TransitionKind.Conveyor or
            TransitionKind.Pan or
            TransitionKind.Reveal or
            TransitionKind.Comb or
            TransitionKind.Doors or
            TransitionKind.Window => SlideShowTransitionPlaybackKind.PushLike,

            _ => SlideShowTransitionPlaybackKind.FadeFallback
        };

    private static bool ResolveSplitHorizontal(SlideTransition transition) =>
        transition.SplitOrientation == TransitionDirection.Horizontal
        || (transition.SplitOrientation is null
            && transition.Direction != TransitionDirection.Vertical);

    private static bool ResolveSplitOut(SlideTransition transition) =>
        transition.Direction != TransitionDirection.In;

    private static bool ResolveBlindsHorizontal(SlideTransition transition) =>
        transition.Direction != TransitionDirection.Vertical;

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
