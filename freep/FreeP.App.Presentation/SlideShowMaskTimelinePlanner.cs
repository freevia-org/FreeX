using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowMaskElementTimeline(
    int Order,
    int StartOffsetMs,
    int DurationMs);

public sealed record SlideShowRandomBarsMaskTimelinePlan(
    int DelayMs,
    int DurationMs,
    int StaggerMs,
    double InitialOpacity,
    IReadOnlyList<SlideShowMaskElementTimeline> Bars,
    SlideShowAnimationScalarTrackPlan OpacityTrack);

public sealed record SlideShowCheckerboardMaskTimelinePlan(
    int DelayMs,
    int DurationMs,
    int PhaseDelayMs,
    int CellDurationMs)
{
    public SlideShowMaskElementTimeline ResolveCell(bool isSecondPhase) =>
        new(
            isSecondPhase ? 1 : 0,
            isSecondPhase ? PhaseDelayMs : 0,
            CellDurationMs);
}

/// <summary>
/// Owns the renderer-neutral schedules for slideshow mask effects. Hosts retain
/// native geometry, storyboard, and timer construction while consuming the same
/// delays, durations, and opacity trajectory.
/// </summary>
public static class SlideShowMaskTimelinePlanner
{
    public static SlideShowRandomBarsMaskTimelinePlan BuildRandomBars(
        SlideShowShapeAnimationPlaybackPlan playback,
        IReadOnlyList<SlideShowMaskRandomBarPlan> bars)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(bars);

        var delayMs = Math.Max(0, playback.DelayMs);
        var durationMs = Math.Max(1, playback.DurationMs);
        var staggerMs = durationMs / Math.Max(1, bars.Count + 1);
        var timelines = bars
            .Select(bar => new SlideShowMaskElementTimeline(
                bar.Order,
                bar.Order * staggerMs,
                Math.Max(1, durationMs - bar.Order * staggerMs)))
            .ToArray();
        var isExit = playback.Animation.Kind == AnimationKind.Exit;
        var opacityTrack = new SlideShowAnimationScalarTrackPlan(
            SlideShowAnimationScalarPropertyKind.Opacity,
            isExit
                ? [
                    Frame(playback.FromOpacity, 0, SlideShowAnimationScalarInterpolationKind.Discrete),
                    Frame(0.7, 0.2, SlideShowAnimationScalarInterpolationKind.Discrete),
                    Frame(0.35, 0.55, SlideShowAnimationScalarInterpolationKind.Discrete),
                    Frame(playback.ToOpacity, 1, SlideShowAnimationScalarInterpolationKind.Linear)
                ]
                : [
                    Frame(0, 0, SlideShowAnimationScalarInterpolationKind.Discrete),
                    Frame(0.35, 0.2, SlideShowAnimationScalarInterpolationKind.Discrete),
                    Frame(0.7, 0.55, SlideShowAnimationScalarInterpolationKind.Discrete),
                    Frame(playback.ToOpacity, 1, SlideShowAnimationScalarInterpolationKind.Linear)
                ]);

        return new SlideShowRandomBarsMaskTimelinePlan(
            delayMs,
            durationMs,
            staggerMs,
            opacityTrack.KeyFrames[0].Value,
            timelines,
            opacityTrack);
    }

    public static SlideShowCheckerboardMaskTimelinePlan BuildCheckerboard(
        SlideShowShapeAnimationPlaybackPlan playback)
    {
        ArgumentNullException.ThrowIfNull(playback);

        var durationMs = Math.Max(1, playback.DurationMs);
        var phaseDelayMs = Math.Max(0, durationMs / 3);
        return new SlideShowCheckerboardMaskTimelinePlan(
            Math.Max(0, playback.DelayMs),
            durationMs,
            phaseDelayMs,
            Math.Max(1, durationMs - phaseDelayMs));
    }

    public static double SampleOpacity(
        SlideShowRandomBarsMaskTimelinePlan timeline,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return SlideShowAnimationEffectTrackPlanner.Sample(timeline.OpacityTrack, progress);
    }

    private static SlideShowAnimationScalarKeyFrame Frame(
        double value,
        double progress,
        SlideShowAnimationScalarInterpolationKind interpolationKind) =>
        new(value, progress, interpolationKind);
}
