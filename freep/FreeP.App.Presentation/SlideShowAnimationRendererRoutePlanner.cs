using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowAnimationRendererRouteKind
{
    Instant,
    MotionPath,
    Opacity,
    Fly,
    WipeMask,
    SplitMask,
    RandomBarsMask,
    BlindsMask,
    BoxMask,
    CheckerboardMask,
    GeometricMask,
    DissolveMask,
    Flash,
    ScalarTrack,
    Trajectory,
    Peek,
    Crawl,
    Zoom,
    TextStyle,
    FontSize,
    LineColor,
    FillColor,
}

public enum SlideShowAnimationInstantVisibilityKind
{
    Show,
    Hide,
}

public sealed record SlideShowAnimationRendererRoutePlan(
    SlideShowAnimationRendererRouteKind Kind,
    SlideShowAnimationInstantVisibilityKind InstantVisibility,
    SlideShowAnimationRevealTiming RevealTiming,
    bool IsFallback);

/// <summary>
/// Maps authored animation effects to the small set of primitives that native renderers execute.
/// WPF and Avalonia retain their animation objects, timers, masks, and drawing mechanics.
/// </summary>
public static class SlideShowAnimationRendererRoutePlanner
{
    public static SlideShowAnimationRendererRoutePlan Build(
        SlideShowShapeAnimationPlaybackPlan playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        return Build(playback.EffectKind, playback.Animation.Kind, playback.RevealTiming);
    }

    public static SlideShowAnimationRendererRoutePlan Build(
        SlideShowShapeAnimationEffectKind effectKind,
        AnimationKind animationKind,
        SlideShowAnimationRevealTiming revealTiming = SlideShowAnimationRevealTiming.None)
    {
        var route = effectKind switch
        {
            SlideShowShapeAnimationEffectKind.Appear =>
                SlideShowAnimationRendererRouteKind.Instant,
            SlideShowShapeAnimationEffectKind.MotionPath =>
                SlideShowAnimationRendererRouteKind.MotionPath,
            SlideShowShapeAnimationEffectKind.Fade =>
                SlideShowAnimationRendererRouteKind.Opacity,
            SlideShowShapeAnimationEffectKind.FlyIn =>
                SlideShowAnimationRendererRouteKind.Fly,
            SlideShowShapeAnimationEffectKind.Wipe =>
                SlideShowAnimationRendererRouteKind.WipeMask,
            SlideShowShapeAnimationEffectKind.Split =>
                SlideShowAnimationRendererRouteKind.SplitMask,
            SlideShowShapeAnimationEffectKind.RandomBars =>
                SlideShowAnimationRendererRouteKind.RandomBarsMask,
            SlideShowShapeAnimationEffectKind.Blinds =>
                SlideShowAnimationRendererRouteKind.BlindsMask,
            SlideShowShapeAnimationEffectKind.Box =>
                SlideShowAnimationRendererRouteKind.BoxMask,
            SlideShowShapeAnimationEffectKind.Checkerboard =>
                SlideShowAnimationRendererRouteKind.CheckerboardMask,
            SlideShowShapeAnimationEffectKind.Circle
                or SlideShowShapeAnimationEffectKind.Diamond
                or SlideShowShapeAnimationEffectKind.Plus
                or SlideShowShapeAnimationEffectKind.Strips
                or SlideShowShapeAnimationEffectKind.Wedge
                or SlideShowShapeAnimationEffectKind.Wheel =>
                SlideShowAnimationRendererRouteKind.GeometricMask,
            SlideShowShapeAnimationEffectKind.Dissolve =>
                SlideShowAnimationRendererRouteKind.DissolveMask,
            SlideShowShapeAnimationEffectKind.Flash =>
                SlideShowAnimationRendererRouteKind.Flash,
            SlideShowShapeAnimationEffectKind.Spiral
                or SlideShowShapeAnimationEffectKind.Swivel
                or SlideShowShapeAnimationEffectKind.Pulse
                or SlideShowShapeAnimationEffectKind.GrowShrink
                or SlideShowShapeAnimationEffectKind.Spin
                or SlideShowShapeAnimationEffectKind.Teeter
                or SlideShowShapeAnimationEffectKind.Blink
                or SlideShowShapeAnimationEffectKind.FlashBulb
                or SlideShowShapeAnimationEffectKind.Flicker
                or SlideShowShapeAnimationEffectKind.ColorPulse
                or SlideShowShapeAnimationEffectKind.ColorWave
                or SlideShowShapeAnimationEffectKind.ChangeColor
                or SlideShowShapeAnimationEffectKind.GrowWithColor
                or SlideShowShapeAnimationEffectKind.Wave
                or SlideShowShapeAnimationEffectKind.Shimmer =>
                SlideShowAnimationRendererRouteKind.ScalarTrack,
            SlideShowShapeAnimationEffectKind.Bounce
                or SlideShowShapeAnimationEffectKind.Float
                or SlideShowShapeAnimationEffectKind.Swoop
                or SlideShowShapeAnimationEffectKind.Boomerang =>
                SlideShowAnimationRendererRouteKind.Trajectory,
            SlideShowShapeAnimationEffectKind.Peek =>
                SlideShowAnimationRendererRouteKind.Peek,
            SlideShowShapeAnimationEffectKind.Crawl =>
                SlideShowAnimationRendererRouteKind.Crawl,
            SlideShowShapeAnimationEffectKind.Zoom =>
                SlideShowAnimationRendererRouteKind.Zoom,
            SlideShowShapeAnimationEffectKind.ChangeFontStyle
                or SlideShowShapeAnimationEffectKind.Bold
                or SlideShowShapeAnimationEffectKind.Underline =>
                SlideShowAnimationRendererRouteKind.TextStyle,
            SlideShowShapeAnimationEffectKind.ChangeFontSize =>
                SlideShowAnimationRendererRouteKind.FontSize,
            SlideShowShapeAnimationEffectKind.ChangeLineColor =>
                SlideShowAnimationRendererRouteKind.LineColor,
            SlideShowShapeAnimationEffectKind.ChangeFillColor =>
                SlideShowAnimationRendererRouteKind.FillColor,
            _ => SlideShowAnimationRendererRouteKind.Instant,
        };

        var isFallback = !Enum.IsDefined(typeof(SlideShowShapeAnimationEffectKind), effectKind);
        var instantVisibility = route == SlideShowAnimationRendererRouteKind.Instant
            && !isFallback
            && animationKind == AnimationKind.Exit
                ? SlideShowAnimationInstantVisibilityKind.Hide
                : SlideShowAnimationInstantVisibilityKind.Show;
        return new SlideShowAnimationRendererRoutePlan(
            route,
            instantVisibility,
            revealTiming,
            isFallback);
    }
}
