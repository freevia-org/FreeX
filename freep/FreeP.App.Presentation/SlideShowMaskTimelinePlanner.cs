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

public sealed record SlideShowMaskRectAnimationPlan(
    SlideShowMaskRect From,
    SlideShowMaskRect To,
    int StartOffsetMs,
    int DurationMs);

public sealed record SlideShowRectMaskRendererPlan(
    int DelayMs,
    int DurationMs,
    double InitialOpacity,
    IReadOnlyList<SlideShowMaskRectAnimationPlan> Elements,
    SlideShowAnimationScalarTrackPlan? OpacityTrack = null);

/// <summary>
/// Owns the renderer-neutral schedules for slideshow mask effects. Hosts retain
/// native geometry, storyboard, and timer construction while consuming the same
/// delays, durations, and opacity trajectory.
/// </summary>
public static class SlideShowMaskTimelinePlanner
{
    public static SlideShowRectMaskRendererPlan BuildRandomBarsRendererPlan(
        SlideShowShapeAnimationPlaybackPlan playback,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(playback);

        var bars = SlideShowMaskGeometryPlanner.BuildRandomBars(
            width,
            height,
            SlideShowPlaybackPlanner.RandomBarsBandCount,
            playback.WipeHorizontal);
        var timeline = BuildRandomBars(playback, bars);
        var isExit = playback.Animation.Kind == AnimationKind.Exit;
        var elements = bars
            .Select((bar, index) => new SlideShowMaskRectAnimationPlan(
                isExit ? bar.Geometry.Open : bar.Geometry.Closed,
                isExit ? bar.Geometry.Closed : bar.Geometry.Open,
                timeline.Bars[index].StartOffsetMs,
                timeline.Bars[index].DurationMs))
            .ToArray();

        return new SlideShowRectMaskRendererPlan(
            timeline.DelayMs,
            timeline.DurationMs,
            timeline.InitialOpacity,
            elements,
            timeline.OpacityTrack);
    }

    public static SlideShowRectMaskRendererPlan BuildBlindsRendererPlan(
        SlideShowShapeAnimationPlaybackPlan playback,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(playback);

        var opens = playback.ToOpacity >= playback.FromOpacity;
        var bandCount = Math.Max(1, playback.BlindsBandCount);
        var elements = Enumerable.Range(0, bandCount)
            .Select(index =>
            {
                var band = SlideShowMaskGeometryPlanner.BuildBlindsBand(
                    width,
                    height,
                    bandCount,
                    index,
                    playback.BlindsHorizontal);
                return new SlideShowMaskRectAnimationPlan(
                    opens ? band.Closed : band.Open,
                    opens ? band.Open : band.Closed,
                    StartOffsetMs: 0,
                    DurationMs: Math.Max(1, playback.DurationMs));
            })
            .ToArray();

        return new SlideShowRectMaskRendererPlan(
            Math.Max(0, playback.DelayMs),
            Math.Max(1, playback.DurationMs),
            InitialOpacity: 1,
            elements);
    }

    public static SlideShowRectMaskRendererPlan BuildCheckerboardRendererPlan(
        SlideShowShapeAnimationPlaybackPlan playback,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(playback);

        var opens = playback.ToOpacity >= playback.FromOpacity;
        var rowCount = Math.Max(1, playback.CheckerboardRowCount);
        var columnCount = Math.Max(1, playback.CheckerboardColumnCount);
        var timeline = BuildCheckerboard(playback);
        var elements = new List<SlideShowMaskRectAnimationPlan>(rowCount * columnCount);
        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                var cell = SlideShowMaskGeometryPlanner.BuildCheckerboardCell(
                    width,
                    height,
                    rowCount,
                    columnCount,
                    row,
                    column,
                    playback.CheckerboardHorizontal);
                var cellTimeline = timeline.ResolveCell(
                    SlideShowMaskGeometryPlanner.IsSecondCheckerboardPhase(row, column));
                elements.Add(new SlideShowMaskRectAnimationPlan(
                    opens ? cell.Closed : cell.Open,
                    opens ? cell.Open : cell.Closed,
                    cellTimeline.StartOffsetMs,
                    cellTimeline.DurationMs));
            }
        }

        return new SlideShowRectMaskRendererPlan(
            timeline.DelayMs,
            timeline.DurationMs,
            InitialOpacity: 1,
            elements);
    }

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

    public static double SampleOpacity(
        SlideShowRectMaskRendererPlan plan,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.OpacityTrack is null
            ? plan.InitialOpacity
            : SlideShowAnimationEffectTrackPlanner.Sample(plan.OpacityTrack, progress);
    }

    private static SlideShowAnimationScalarKeyFrame Frame(
        double value,
        double progress,
        SlideShowAnimationScalarInterpolationKind interpolationKind) =>
        new(value, progress, interpolationKind);
}
