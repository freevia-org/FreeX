using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowAnimationVisualTrackKind
{
    Instant,
    Opacity,
    Translate,
    Scale,
    Rotate,
    Emphasis,
    Clip,
    MotionPath
}

public enum SlideShowAnimationClipKind
{
    None,
    Wipe,
    Split,
    RandomBars,
    Blinds,
    Box,
    Checkerboard,
    Circle,
    Diamond,
    Plus,
    Strips,
    Wedge,
    Wheel
}

public sealed record SlideShowShapeAnimationVisualFramePlan(
    uint ShapeId,
    SlideShowShapeAnimationEffectKind EffectKind,
    SlideShowAnimationVisualTrackKind TrackKind,
    int ElapsedMs,
    double Progress,
    bool IsBeforeStart,
    bool IsComplete,
    double Opacity,
    double Scale,
    double RotationDegrees,
    double TranslateXFactor,
    double TranslateYFactor,
    double TranslateXDip,
    double TranslateYDip,
    SlideShowAnimationClipKind ClipKind,
    double ClipProgress,
    bool ClipHorizontal,
    int ClipBandCount,
    int ClipSpokeCount,
    string EvidenceSummary)
{
    public double ScaleX { get; init; } = 1;
    public double ScaleY { get; init; } = 1;
    public double FromScaleX { get; init; } = 1;
    public double FromScaleY { get; init; } = 1;
    public double ToScaleX { get; init; } = 1;
    public double ToScaleY { get; init; } = 1;
    public double PeakScaleX { get; init; } = 1;
    public double PeakScaleY { get; init; } = 1;
    // Swivel is a 3-D vertical-axis effect.  Hosts expose the depth cue as a
    // horizontal 2-D projection while retaining the shared rotation track.
    public double HorizontalScale { get; init; } = 1;
    public bool ClipFromCenter { get; init; }
}

public sealed record SlideShowAnimationStepVisualCheckpointPlan(
    string Checkpoint,
    int ElapsedMs,
    IReadOnlyList<SlideShowShapeAnimationVisualFramePlan> Frames,
    string EvidenceSummary);

public enum SlideShowPlaybackReadinessHost
{
    Wpf,
    Avalonia
}

public sealed record SlideShowAnimationStepPlaybackHostEvidenceRow(
    SlideShowPlaybackReadinessHost Host,
    string EvidenceId,
    int SlideIndex,
    int StepIndex,
    int CheckpointCount,
    bool RequiresPowerPointCom,
    string EvidenceSummary);

public sealed record SlideShowAnimationStepPlaybackReadinessPlan(
    string ScenarioId,
    int SlideIndex,
    int StepIndex,
    int AnimationEntryCount,
    int CheckpointCount,
    int DelayedEntryCount,
    IReadOnlyList<SlideShowAnimationVisualTrackKind> TrackKinds,
    IReadOnlyList<SlideShowAnimationClipKind> ClipKinds,
    IReadOnlyList<SlideShowAnimationStepPlaybackHostEvidenceRow> HostRows,
    IReadOnlyList<string> EvidenceLines)
{
    public bool HasSharedHostParity =>
        HostRows.Any(row => row.Host == SlideShowPlaybackReadinessHost.Wpf) &&
        HostRows.Any(row => row.Host == SlideShowPlaybackReadinessHost.Avalonia) &&
        HostRows.All(row => !row.RequiresPowerPointCom);
}

public static class SlideShowPlaybackFramePlanner
{
    public static IReadOnlyList<SlideShowShapeAnimationVisualFramePlan> PlanAnimationStepFrames(
        AnimationStep step,
        int elapsedMs,
        double slideWidthDip,
        double slideHeightDip)
    {
        ArgumentNullException.ThrowIfNull(step);

        return SlideShowPlaybackPlanner.PlanAnimationStep(step)
            .Select(plan => PlanFrame(plan, elapsedMs, slideWidthDip, slideHeightDip))
            .ToArray();
    }

    public static IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> PlanAnimationStepCheckpoints(
        AnimationStep step,
        double slideWidthDip,
        double slideHeightDip)
    {
        ArgumentNullException.ThrowIfNull(step);

        var plans = SlideShowPlaybackPlanner.PlanAnimationStep(step);
        if (plans.Count == 0)
        {
            return Array.Empty<SlideShowAnimationStepVisualCheckpointPlan>();
        }

        var totalDurationMs = plans.Max(plan =>
            Math.Max(0, plan.DelayMs) + ResolvePlaybackDuration(plan));
        var elapsedTimes = new[]
        {
            ("start", 0),
            ("midpoint", totalDurationMs / 2),
            ("complete", totalDurationMs)
        };

        return elapsedTimes
            .Select(checkpoint =>
            {
                var frames = plans
                    .Select(plan => PlanFrame(plan, checkpoint.Item2, slideWidthDip, slideHeightDip))
                    .ToArray();
                return new SlideShowAnimationStepVisualCheckpointPlan(
                    checkpoint.Item1,
                    checkpoint.Item2,
                    frames,
                    BuildCheckpointEvidenceSummary(checkpoint.Item1, checkpoint.Item2, frames));
            })
            .ToArray();
    }

    public static SlideShowAnimationStepPlaybackReadinessPlan BuildAnimationStepPlaybackReadinessPlan(
        AnimationStep step,
        int slideIndex,
        int stepIndex,
        double slideWidthDip,
        double slideHeightDip,
        string scenarioId = "slideshow-playback")
    {
        ArgumentNullException.ThrowIfNull(step);

        var safeScenarioId = NormalizeScenarioId(scenarioId);
        var safeSlideIndex = Math.Max(0, slideIndex);
        var safeStepIndex = Math.Max(0, stepIndex);
        var playbackPlans = SlideShowPlaybackPlanner.PlanAnimationStep(step);
        var checkpoints = PlanAnimationStepCheckpoints(step, slideWidthDip, slideHeightDip);
        var trackKinds = checkpoints
            .SelectMany(checkpoint => checkpoint.Frames)
            .Select(frame => frame.TrackKind)
            .Distinct()
            .OrderBy(kind => kind.ToString(), StringComparer.Ordinal)
            .ToArray();
        var clipKinds = checkpoints
            .SelectMany(checkpoint => checkpoint.Frames)
            .Select(frame => frame.ClipKind)
            .Where(kind => kind != SlideShowAnimationClipKind.None)
            .Distinct()
            .OrderBy(kind => kind.ToString(), StringComparer.Ordinal)
            .ToArray();
        var delayedEntryCount = playbackPlans.Count(plan => plan.DelayMs > 0);
        var surfaceId = $"{safeScenarioId}-slide-{safeSlideIndex + 1}-step-{safeStepIndex + 1}";
        var summary =
            $"slide {safeSlideIndex + 1}; step {safeStepIndex + 1}; animations {playbackPlans.Count}; "
            + $"checkpoints {checkpoints.Count}; tracks {FormatEnumList(trackKinds)}; clips {FormatEnumList(clipKinds)}";
        var hostRows = new[]
        {
            BuildHostEvidenceRow(SlideShowPlaybackReadinessHost.Wpf, surfaceId, safeSlideIndex, safeStepIndex, checkpoints.Count, summary),
            BuildHostEvidenceRow(SlideShowPlaybackReadinessHost.Avalonia, surfaceId, safeSlideIndex, safeStepIndex, checkpoints.Count, summary)
        };
        var evidenceLines = new[]
        {
            $"Scenario {safeScenarioId}: slide {safeSlideIndex + 1}; step {safeStepIndex + 1}; animations {playbackPlans.Count}; checkpoints {checkpoints.Count}",
            $"Playback tracks: {FormatEnumList(trackKinds)}; clips: {FormatEnumList(clipKinds)}; delayed entries: {delayedEntryCount}",
            "Shared host rows: WPF/Avalonia; PowerPoint COM required: false"
        };

        return new SlideShowAnimationStepPlaybackReadinessPlan(
            safeScenarioId,
            safeSlideIndex,
            safeStepIndex,
            playbackPlans.Count,
            checkpoints.Count,
            delayedEntryCount,
            trackKinds,
            clipKinds,
            hostRows,
            evidenceLines);
    }

    public static SlideShowShapeAnimationVisualFramePlan PlanFrame(
        SlideShowShapeAnimationPlaybackPlan plan,
        int elapsedMs,
        double slideWidthDip,
        double slideHeightDip)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var safeElapsedMs = Math.Max(0, elapsedMs);
        var durationMs = Math.Max(SlideShowPlaybackPlanner.MinShapeAnimationDurationMs, plan.DurationMs);
        var localElapsedMs = safeElapsedMs - Math.Max(0, plan.DelayMs);
        var isBeforeStart = localElapsedMs < 0;
        var playbackDurationMs = ResolvePlaybackDuration(plan);
        var isComplete = !plan.RepeatIndefinitely && localElapsedMs >= playbackDurationMs;
        var progress = ResolvePlaybackProgress(plan, localElapsedMs, durationMs, isComplete);
        var opacity = ResolveOpacity(plan, progress, isBeforeStart);
        var (scaleX, scaleY) = ResolveScaleAxes(plan, progress);
        var scale = scaleX;
        var rotation = ResolveRotation(plan, progress);
        var horizontalScale = ResolveHorizontalScale(plan, progress);
        var (translateXFactor, translateYFactor) = ResolveTranslateFactors(plan, progress);
        var clipKind = ResolveClipKind(plan);
        var clipProgress = ResolveClipProgress(plan, progress, clipKind);
        var trackKind = ResolveTrackKind(plan, clipKind);
        var width = Math.Max(0, slideWidthDip);
        var height = Math.Max(0, slideHeightDip);

        return new SlideShowShapeAnimationVisualFramePlan(
            plan.Animation.ShapeId,
            plan.EffectKind,
            trackKind,
            safeElapsedMs,
            progress,
            isBeforeStart,
            isComplete,
            opacity,
            scale,
            rotation,
            translateXFactor,
            translateYFactor,
            translateXFactor * width,
            translateYFactor * height,
            clipKind,
            clipProgress,
            ResolveClipHorizontal(plan, clipKind),
            ResolveClipBandCount(plan, clipKind),
            ResolveClipSpokeCount(plan, clipKind),
            BuildEvidenceSummary(plan, trackKind, progress, opacity, scaleX, scaleY, horizontalScale,
                rotation, translateXFactor, translateYFactor, clipKind, clipProgress))
        {
            ScaleX = scaleX,
            ScaleY = scaleY,
            FromScaleX = plan.FromScaleX,
            FromScaleY = plan.FromScaleY,
            ToScaleX = plan.ToScaleX,
            ToScaleY = plan.ToScaleY,
            PeakScaleX = plan.PeakScaleX,
            PeakScaleY = plan.PeakScaleY,
            HorizontalScale = horizontalScale,
            ClipFromCenter = clipKind == SlideShowAnimationClipKind.Split && plan.SplitFromCenter
        };
    }

    private static int ResolvePlaybackDuration(SlideShowShapeAnimationPlaybackPlan plan)
    {
        var durationMs = Math.Max(SlideShowPlaybackPlanner.MinShapeAnimationDurationMs, plan.DurationMs);
        if (plan.RepeatIndefinitely)
            return durationMs;

        var repeatCount = Math.Max(1, plan.RepeatCount ?? 1);
        return checked(durationMs * repeatCount);
    }

    private static double ResolvePlaybackProgress(
        SlideShowShapeAnimationPlaybackPlan plan,
        int localElapsedMs,
        int durationMs,
        bool isComplete)
    {
        if (localElapsedMs < 0)
            return 0;

        if (isComplete)
        {
            var repeatCount = Math.Max(1, plan.RepeatCount ?? 1);
            return plan.AutoReverse && repeatCount % 2 == 0 ? 0 : 1;
        }

        var passIndex = localElapsedMs / durationMs;
        var passElapsedMs = localElapsedMs % durationMs;
        var progress = passElapsedMs / (double)durationMs;
        return plan.AutoReverse && passIndex % 2 == 1 ? 1 - progress : progress;
    }

    private static double ResolveOpacity(SlideShowShapeAnimationPlaybackPlan plan, double progress, bool isBeforeStart)
    {
        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.Blink)
        {
            if (isBeforeStart)
            {
                return 1;
            }

            var phase = progress * 4;
            return ((int)Math.Floor(phase) % 2) == 0 ? 1 : 0.15;
        }

        if (plan.EffectKind is SlideShowShapeAnimationEffectKind.ColorPulse
            or SlideShowShapeAnimationEffectKind.ChangeColor
            or SlideShowShapeAnimationEffectKind.GrowWithColor
            or SlideShowShapeAnimationEffectKind.Shimmer
            or SlideShowShapeAnimationEffectKind.Bold
            or SlideShowShapeAnimationEffectKind.Underline)
        {
            return progress <= 0.5
                ? Lerp(1, 0.65, progress * 2)
                : Lerp(0.65, 1, (progress - 0.5) * 2);
        }

        if (plan.Animation.Kind == AnimationKind.Emphasis)
        {
            return 1;
        }

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.Appear)
        {
            return isBeforeStart || progress <= 0 ? plan.FromOpacity : plan.ToOpacity;
        }

        return plan.FromOpacity + (plan.ToOpacity - plan.FromOpacity) * progress;
    }

    private static (double X, double Y) ResolveScaleAxes(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress)
    {
        if (plan.EffectKind is SlideShowShapeAnimationEffectKind.Pulse
            or SlideShowShapeAnimationEffectKind.GrowShrink)
        {
            var phase = progress <= 0.5;
            var phaseProgress = phase ? progress * 2 : (progress - 0.5) * 2;
            return phase
                ? (Lerp(plan.FromScaleX, plan.PeakScaleX, phaseProgress),
                   Lerp(plan.FromScaleY, plan.PeakScaleY, phaseProgress))
                : (Lerp(plan.PeakScaleX, plan.ToScaleX, phaseProgress),
                   Lerp(plan.PeakScaleY, plan.ToScaleY, phaseProgress));
        }

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.Zoom)
        {
            var scale = Lerp(plan.FromScale, plan.ToScale, progress);
            return (scale, scale);
        }

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.GrowWithColor)
        {
            var scale = progress <= 0.5
                ? Lerp(1, plan.PeakScale, progress * 2)
                : Lerp(plan.PeakScale, 1, (progress - 0.5) * 2);
            return (scale, scale);
        }

        return (1, 1);
    }

    private static double ResolveRotation(SlideShowShapeAnimationPlaybackPlan plan, double progress) =>
        plan.EffectKind == SlideShowShapeAnimationEffectKind.Teeter
            ? Math.Sin(progress * Math.PI * 4) * 10
            : plan.EffectKind == SlideShowShapeAnimationEffectKind.Spiral
            ? progress <= 0.7
                ? Lerp(0, plan.RotationDegrees * 0.82, progress / 0.7)
                : Lerp(plan.RotationDegrees * 0.82, plan.RotationDegrees, (progress - 0.7) / 0.3)
            : plan.EffectKind is SlideShowShapeAnimationEffectKind.Spin
            or SlideShowShapeAnimationEffectKind.Swivel
            ? plan.RotationDegrees * progress
            : 0;

    private static double ResolveHorizontalScale(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress) =>
        plan.EffectKind == SlideShowShapeAnimationEffectKind.Swivel
            ? ResolveSwivelHorizontalScale(progress)
            : 1;

    public static double ResolveSwivelHorizontalScale(double progress)
    {
        const double edgeOnScale = 0.04;
        progress = Math.Clamp(progress, 0, 1);
        return progress switch
        {
            <= 0.25 => Lerp(1, edgeOnScale, progress / 0.25),
            <= 0.5 => Lerp(edgeOnScale, 1, (progress - 0.25) / 0.25),
            <= 0.75 => Lerp(1, edgeOnScale, (progress - 0.5) / 0.25),
            _ => Lerp(edgeOnScale, 1, (progress - 0.75) / 0.25)
        };
    }

    private static (double X, double Y) ResolveTranslateFactors(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress)
    {
        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.MotionPath)
        {
            return InterpolateMotionPath(plan.MotionKeyFrames, progress);
        }

        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.Wave)
        {
            return (Math.Sin(progress * Math.PI * 4) * 0.00625, 0);
        }

        if (plan.EffectKind is not (SlideShowShapeAnimationEffectKind.FlyIn
            or SlideShowShapeAnimationEffectKind.Bounce
            or SlideShowShapeAnimationEffectKind.Float
            or SlideShowShapeAnimationEffectKind.Swoop
            or SlideShowShapeAnimationEffectKind.Boomerang
            or SlideShowShapeAnimationEffectKind.Peek
            or SlideShowShapeAnimationEffectKind.Crawl))
        {
            return (0, 0);
        }

        var factor = plan.Animation.Kind == AnimationKind.Exit
            ? progress
            : 1 - progress;
        return (plan.OffsetXFactor * factor, plan.OffsetYFactor * factor);
    }

    private static (double X, double Y) InterpolateMotionPath(
        IReadOnlyList<SlideShowMotionPathKeyFrame> keyFrames,
        double progress)
    {
        if (keyFrames.Count == 0)
        {
            return (0, 0);
        }

        if (progress <= keyFrames[0].Progress)
        {
            return (keyFrames[0].OffsetXFactor, keyFrames[0].OffsetYFactor);
        }

        for (var i = 1; i < keyFrames.Count; i++)
        {
            var previous = keyFrames[i - 1];
            var current = keyFrames[i];
            if (progress > current.Progress)
            {
                continue;
            }

            var range = Math.Max(0.0001, current.Progress - previous.Progress);
            var t = (progress - previous.Progress) / range;
            return (
                Lerp(previous.OffsetXFactor, current.OffsetXFactor, t),
                Lerp(previous.OffsetYFactor, current.OffsetYFactor, t));
        }

        var last = keyFrames[^1];
        return (last.OffsetXFactor, last.OffsetYFactor);
    }

    private static SlideShowAnimationClipKind ResolveClipKind(SlideShowShapeAnimationPlaybackPlan plan) =>
        plan.EffectKind switch
        {
            SlideShowShapeAnimationEffectKind.Wipe => SlideShowAnimationClipKind.Wipe,
            SlideShowShapeAnimationEffectKind.Split => SlideShowAnimationClipKind.Split,
            SlideShowShapeAnimationEffectKind.RandomBars => SlideShowAnimationClipKind.RandomBars,
            SlideShowShapeAnimationEffectKind.Blinds => SlideShowAnimationClipKind.Blinds,
            SlideShowShapeAnimationEffectKind.Box => SlideShowAnimationClipKind.Box,
            SlideShowShapeAnimationEffectKind.Checkerboard => SlideShowAnimationClipKind.Checkerboard,
            SlideShowShapeAnimationEffectKind.Circle => SlideShowAnimationClipKind.Circle,
            SlideShowShapeAnimationEffectKind.Diamond => SlideShowAnimationClipKind.Diamond,
            SlideShowShapeAnimationEffectKind.Plus => SlideShowAnimationClipKind.Plus,
            SlideShowShapeAnimationEffectKind.Strips => SlideShowAnimationClipKind.Strips,
            SlideShowShapeAnimationEffectKind.Wedge => SlideShowAnimationClipKind.Wedge,
            SlideShowShapeAnimationEffectKind.Wheel => SlideShowAnimationClipKind.Wheel,
            _ => SlideShowAnimationClipKind.None
        };

    private static double ResolveClipProgress(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress,
        SlideShowAnimationClipKind clipKind)
    {
        if (clipKind == SlideShowAnimationClipKind.None)
        {
            return 1;
        }

        if (clipKind is SlideShowAnimationClipKind.Circle
            or SlideShowAnimationClipKind.Diamond
            or SlideShowAnimationClipKind.Plus
            or SlideShowAnimationClipKind.Strips
            or SlideShowAnimationClipKind.Wedge
            or SlideShowAnimationClipKind.Wheel
            or SlideShowAnimationClipKind.Box)
        {
            return plan.GeometricMaskExpandsFromCenter || plan.BoxExpandsFromCenter
                ? progress
                : 1 - progress;
        }

        return plan.Animation.Kind == AnimationKind.Exit ? 1 - progress : progress;
    }

    private static SlideShowAnimationVisualTrackKind ResolveTrackKind(
        SlideShowShapeAnimationPlaybackPlan plan,
        SlideShowAnimationClipKind clipKind)
    {
        if (clipKind != SlideShowAnimationClipKind.None)
        {
            return SlideShowAnimationVisualTrackKind.Clip;
        }

        return plan.EffectKind switch
        {
            SlideShowShapeAnimationEffectKind.Appear => SlideShowAnimationVisualTrackKind.Instant,
            SlideShowShapeAnimationEffectKind.Fade
                or SlideShowShapeAnimationEffectKind.Dissolve
                or SlideShowShapeAnimationEffectKind.Flash => SlideShowAnimationVisualTrackKind.Opacity,
            SlideShowShapeAnimationEffectKind.FlyIn
                or SlideShowShapeAnimationEffectKind.Bounce
                or SlideShowShapeAnimationEffectKind.Float
                or SlideShowShapeAnimationEffectKind.Swoop
                or SlideShowShapeAnimationEffectKind.Boomerang
                or SlideShowShapeAnimationEffectKind.Peek
                or SlideShowShapeAnimationEffectKind.Crawl => SlideShowAnimationVisualTrackKind.Translate,
            SlideShowShapeAnimationEffectKind.Wave => SlideShowAnimationVisualTrackKind.Translate,
            SlideShowShapeAnimationEffectKind.Zoom
                or SlideShowShapeAnimationEffectKind.Pulse
                or SlideShowShapeAnimationEffectKind.GrowShrink => SlideShowAnimationVisualTrackKind.Scale,
            SlideShowShapeAnimationEffectKind.Spin
                or SlideShowShapeAnimationEffectKind.Spiral
                or SlideShowShapeAnimationEffectKind.Swivel
                or SlideShowShapeAnimationEffectKind.Teeter => SlideShowAnimationVisualTrackKind.Rotate,
            SlideShowShapeAnimationEffectKind.Blink => SlideShowAnimationVisualTrackKind.Opacity,
            SlideShowShapeAnimationEffectKind.ColorPulse
                or SlideShowShapeAnimationEffectKind.ChangeColor
                or SlideShowShapeAnimationEffectKind.GrowWithColor
                or SlideShowShapeAnimationEffectKind.Shimmer
                or SlideShowShapeAnimationEffectKind.Bold
                or SlideShowShapeAnimationEffectKind.Underline => SlideShowAnimationVisualTrackKind.Emphasis,
            SlideShowShapeAnimationEffectKind.MotionPath => SlideShowAnimationVisualTrackKind.MotionPath,
            _ => SlideShowAnimationVisualTrackKind.Instant
        };
    }

    private static bool ResolveClipHorizontal(
        SlideShowShapeAnimationPlaybackPlan plan,
        SlideShowAnimationClipKind clipKind) =>
        clipKind switch
        {
            SlideShowAnimationClipKind.Wipe => plan.WipeHorizontal,
            SlideShowAnimationClipKind.Blinds => plan.BlindsHorizontal,
            SlideShowAnimationClipKind.Checkerboard => plan.CheckerboardHorizontal,
            SlideShowAnimationClipKind.RandomBars => plan.WipeHorizontal,
            SlideShowAnimationClipKind.Split => plan.SplitHorizontal,
            _ => false
        };

    private static int ResolveClipBandCount(
        SlideShowShapeAnimationPlaybackPlan plan,
        SlideShowAnimationClipKind clipKind) =>
        clipKind switch
        {
            SlideShowAnimationClipKind.Blinds => plan.BlindsBandCount,
            SlideShowAnimationClipKind.Checkerboard => plan.CheckerboardRowCount * plan.CheckerboardColumnCount,
            SlideShowAnimationClipKind.Strips => plan.GeometricMaskStripCount,
            SlideShowAnimationClipKind.RandomBars => SlideShowPlaybackPlanner.RandomBarsBandCount,
            _ => 0
        };

    private static int ResolveClipSpokeCount(
        SlideShowShapeAnimationPlaybackPlan plan,
        SlideShowAnimationClipKind clipKind) =>
        clipKind == SlideShowAnimationClipKind.Wheel ? plan.GeometricMaskSpokeCount : 0;

    private static string BuildEvidenceSummary(
        SlideShowShapeAnimationPlaybackPlan plan,
        SlideShowAnimationVisualTrackKind trackKind,
        double progress,
        double opacity,
        double scaleX,
        double scaleY,
        double horizontalScale,
        double rotation,
        double translateX,
        double translateY,
        SlideShowAnimationClipKind clipKind,
        double clipProgress) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}: shape {2}; progress {3:0.###}; opacity {4:0.###}; scale {5:0.###}; scale-x {6:0.###}; scale-y {7:0.###}; horizontal-scale {8:0.###}; rotation {9:0.###}; translate ({10:0.###},{11:0.###}); clip {12} {13:0.###}",
            plan.EffectKind,
            trackKind,
            plan.Animation.ShapeId,
            progress,
            opacity,
            scaleX,
            scaleX,
            scaleY,
            horizontalScale,
            rotation,
            translateX,
            translateY,
            clipKind,
            clipProgress);

    private static string BuildCheckpointEvidenceSummary(
        string checkpoint,
        int elapsedMs,
        IReadOnlyList<SlideShowShapeAnimationVisualFramePlan> frames)
    {
        var activeCount = frames.Count(frame => !frame.IsBeforeStart && !frame.IsComplete);
        var completeCount = frames.Count(frame => frame.IsComplete);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} at {1}ms: {2} frame(s); {3} active; {4} complete",
            checkpoint,
            elapsedMs,
            frames.Count,
            activeCount,
            completeCount);
    }

    private static SlideShowAnimationStepPlaybackHostEvidenceRow BuildHostEvidenceRow(
        SlideShowPlaybackReadinessHost host,
        string surfaceId,
        int slideIndex,
        int stepIndex,
        int checkpointCount,
        string evidenceSummary)
    {
        var hostId = host.ToString().ToLowerInvariant();
        return new SlideShowAnimationStepPlaybackHostEvidenceRow(
            host,
            $"{surfaceId}-{hostId}",
            slideIndex,
            stepIndex,
            checkpointCount,
            RequiresPowerPointCom: false,
            evidenceSummary);
    }

    private static string NormalizeScenarioId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "slideshow-playback";
        }

        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = string.Join(
            "-",
            new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "slideshow-playback" : normalized;
    }

    private static string FormatEnumList<T>(IReadOnlyList<T> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * Math.Clamp(progress, 0, 1);
}
