using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// One renderer-neutral polygon-mask transition, including its authored option plan and
/// common playback sampling. Hosts materialize the returned polygons as native geometry.
/// </summary>
public sealed class SlideShowPolygonClipTransitionPlan
{
    private readonly Func<double, double, double, IReadOnlyList<SlideShowMaskPolygon>> _buildPolygons;

    internal SlideShowPolygonClipTransitionPlan(
        SlideShowTransitionPlaybackActionKind actionKind,
        Func<double, double, double, IReadOnlyList<SlideShowMaskPolygon>> buildPolygons)
    {
        ActionKind = actionKind;
        _buildPolygons = buildPolygons ?? throw new ArgumentNullException(nameof(buildPolygons));
    }

    public SlideShowTransitionPlaybackActionKind ActionKind { get; }

    public IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress) =>
        _buildPolygons(
            Math.Max(0, width),
            Math.Max(0, height),
            Math.Clamp(progress, 0, 1));
}

public static class SlideShowPolygonClipTransitionPlanner
{
    public const int StoryboardFrameCount = 30;
    public const int TimerFrameIntervalMs = 16;

    public static SlideShowPolygonClipTransitionPlan Build(
        SlideShowTransitionPlaybackActionKind actionKind,
        SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        return actionKind switch
        {
            SlideShowTransitionPlaybackActionKind.Honeycomb => Wrap(
                actionKind,
                SlideShowHoneycombTransitionPlanner.Plan(transition),
                SlideShowHoneycombTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Glitter => Wrap(
                actionKind,
                SlideShowGlitterTransitionPlanner.Plan(transition),
                SlideShowGlitterTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Ripple => Wrap(
                actionKind,
                SlideShowRippleTransitionPlanner.Plan(transition),
                SlideShowRippleTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Wind => Wrap(
                actionKind,
                SlideShowWindTransitionPlanner.Plan(transition),
                SlideShowWindTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Curtains => Wrap(
                actionKind,
                SlideShowCurtainsTransitionPlanner.Plan(transition),
                SlideShowCurtainsTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Shred => Wrap(
                actionKind,
                SlideShowShredTransitionPlanner.Plan(transition),
                SlideShowShredTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Drape => Wrap(
                actionKind,
                SlideShowDrapeTransitionPlanner.Plan(transition),
                SlideShowDrapeTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Fracture => Wrap(
                actionKind,
                SlideShowFractureTransitionPlanner.Plan(transition),
                SlideShowFractureTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Crush => Wrap(
                actionKind,
                SlideShowCrushTransitionPlanner.Plan(transition),
                SlideShowCrushTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Prism => Wrap(
                actionKind,
                SlideShowPrismTransitionPlanner.Plan(transition),
                SlideShowPrismTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Prestige => Wrap(
                actionKind,
                SlideShowPrestigeTransitionPlanner.Plan(transition),
                SlideShowPrestigeTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Warp => Wrap(
                actionKind,
                SlideShowWarpTransitionPlanner.Plan(transition),
                SlideShowWarpTransitionPlanner.BuildPolygons),
            SlideShowTransitionPlaybackActionKind.Vortex => Wrap(
                actionKind,
                SlideShowVortexTransitionPlanner.Plan(transition),
                SlideShowVortexTransitionPlanner.BuildPolygons),
            _ => throw new ArgumentOutOfRangeException(
                nameof(actionKind),
                actionKind,
                "The transition does not use polygon-clip playback.")
        };
    }

    public static int ResolveTimerStepCount(int durationMs) =>
        Math.Max(1, Math.Max(0, durationMs) / TimerFrameIntervalMs);

    public static double ResolveFrameProgress(int frameIndex, int frameCount)
    {
        var count = Math.Max(1, frameCount);
        var progress = Math.Clamp(frameIndex, 0, count) / (double)count;
        return progress < 0.5
            ? 4 * progress * progress * progress
            : 1 - Math.Pow(-2 * progress + 2, 3) / 2;
    }

    private static SlideShowPolygonClipTransitionPlan Wrap<TPlan>(
        SlideShowTransitionPlaybackActionKind actionKind,
        TPlan plan,
        Func<double, double, double, TPlan, IReadOnlyList<SlideShowMaskPolygon>> buildPolygons) =>
        new(actionKind, (width, height, progress) =>
            buildPolygons(width, height, progress, plan));
}
