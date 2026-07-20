using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowTransitionPlaybackKind
{
    Cut,
    Fade,
    Flash,
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
    Pan,
    Gallery,
    Conveyor,
    Window,
    Morph,
    Flip,
    Cube,
    Rotate,
    Honeycomb,
    Switch,
    Orbit,
    Ferris,
    Flythrough,
    Glitter,
    Ripple,
    Wind,
    Curtains,
    Shred,
    Drape,
    Fracture,
    Crush,
    Prism,
    Warp,
    Vortex,
    PageCurl,
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

            TransitionKind.Fade => SlideShowTransitionPlaybackKind.Fade,

            // PresentationML p14:flash is a distinct transition. Keep it
            // renderer-neutral so both slideshow hosts can produce a brief
            // white flash instead of silently reducing it to a cross-fade.
            TransitionKind.Flash => SlideShowTransitionPlaybackKind.Flash,

            TransitionKind.Dissolve => SlideShowTransitionPlaybackKind.Dissolve,

            TransitionKind.Box => SlideShowTransitionPlaybackKind.Box,

            TransitionKind.Reveal => SlideShowTransitionPlaybackKind.Reveal,

            TransitionKind.Wipe => SlideShowTransitionPlaybackKind.Reveal,

            TransitionKind.Uncover => SlideShowTransitionPlaybackKind.Uncover,

            TransitionKind.Cover => SlideShowTransitionPlaybackKind.Cover,

            TransitionKind.Push => SlideShowTransitionPlaybackKind.Push,

            // There is no standard PresentationML p:fly element. The package
            // writer emits Fly as push, so playback must follow that same
            // interoperable representation instead of falling back to fade.
            TransitionKind.Fly => SlideShowTransitionPlaybackKind.Push,

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

            TransitionKind.Pan => SlideShowTransitionPlaybackKind.Pan,

            // Gallery is a two-surface exchange: unlike Cover, the outgoing
            // slide participates in the motion and the incoming slide starts
            // as a centered, reduced panel.
            TransitionKind.Gallery => SlideShowTransitionPlaybackKind.Gallery,

            TransitionKind.Conveyor => SlideShowTransitionPlaybackKind.Conveyor,

            // Window opens the incoming slide through a centered aperture;
            // unlike Box it starts partially open and carries a subtle scale.
            TransitionKind.Window => SlideShowTransitionPlaybackKind.Window,

            // Morph is object-aware when both slides expose stable ids or
            // unique names; the host falls back only when no match exists.
            TransitionKind.Morph => SlideShowTransitionPlaybackKind.Morph,

            TransitionKind.Flip => SlideShowTransitionPlaybackKind.Flip,

            TransitionKind.Cube => SlideShowTransitionPlaybackKind.Cube,

            TransitionKind.Rotate => SlideShowTransitionPlaybackKind.Rotate,

            TransitionKind.Honeycomb => SlideShowTransitionPlaybackKind.Honeycomb,

            TransitionKind.Switch => SlideShowTransitionPlaybackKind.Switch,

            TransitionKind.Orbit => SlideShowTransitionPlaybackKind.Orbit,

            TransitionKind.Ferris => SlideShowTransitionPlaybackKind.Ferris,

            TransitionKind.Flythrough => SlideShowTransitionPlaybackKind.Flythrough,

            TransitionKind.Glitter => SlideShowTransitionPlaybackKind.Glitter,

            TransitionKind.Ripple => SlideShowTransitionPlaybackKind.Ripple,

            TransitionKind.Wind => SlideShowTransitionPlaybackKind.Wind,

            TransitionKind.Curtains => SlideShowTransitionPlaybackKind.Curtains,

            TransitionKind.Shred => SlideShowTransitionPlaybackKind.Shred,

            // Peel Off is the single-sheet page-peel family. Reuse the
            // shared folded-page projection instead of reducing it to fade.
            TransitionKind.PeelOff => SlideShowTransitionPlaybackKind.PageCurl,

            TransitionKind.Drape => SlideShowTransitionPlaybackKind.Drape,

            // Airplane is a motion-through-space transition; use the
            // direction-aware Flythrough projection rather than a fade.
            TransitionKind.Airplane => SlideShowTransitionPlaybackKind.Flythrough,

            // Origami is a multi-fold paper transition; use the shared
            // double-fold page projection instead of reducing it to fade.
            TransitionKind.Origami => SlideShowTransitionPlaybackKind.PageCurl,

            TransitionKind.Vortex => SlideShowTransitionPlaybackKind.Vortex,

            TransitionKind.Warp => SlideShowTransitionPlaybackKind.Warp,

            TransitionKind.Fracture => SlideShowTransitionPlaybackKind.Fracture,

            TransitionKind.Crush => SlideShowTransitionPlaybackKind.Crush,

            TransitionKind.Prism => SlideShowTransitionPlaybackKind.Prism,

            TransitionKind.PageCurlSingle or
            TransitionKind.PageCurlDouble => SlideShowTransitionPlaybackKind.PageCurl,

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
