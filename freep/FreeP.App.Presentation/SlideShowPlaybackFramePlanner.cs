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
    string EvidenceSummary);

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
        var progress = Math.Clamp(localElapsedMs / (double)durationMs, 0, 1);
        var isBeforeStart = localElapsedMs < 0;
        var isComplete = localElapsedMs >= durationMs;
        var opacity = ResolveOpacity(plan, progress, isBeforeStart);
        var scale = ResolveScale(plan, progress);
        var rotation = ResolveRotation(plan, progress);
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
            BuildEvidenceSummary(plan, trackKind, progress, opacity, scale, rotation, translateXFactor, translateYFactor, clipKind, clipProgress));
    }

    private static double ResolveOpacity(SlideShowShapeAnimationPlaybackPlan plan, double progress, bool isBeforeStart)
    {
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

    private static double ResolveScale(SlideShowShapeAnimationPlaybackPlan plan, double progress) =>
        plan.EffectKind switch
        {
            SlideShowShapeAnimationEffectKind.Pulse or SlideShowShapeAnimationEffectKind.GrowShrink =>
                progress <= 0.5
                    ? Lerp(plan.FromScale, plan.PeakScale, progress * 2)
                    : Lerp(plan.PeakScale, plan.ToScale, (progress - 0.5) * 2),
            SlideShowShapeAnimationEffectKind.Zoom =>
                Lerp(plan.FromScale, plan.ToScale, progress),
            _ => 1
        };

    private static double ResolveRotation(SlideShowShapeAnimationPlaybackPlan plan, double progress) =>
        plan.EffectKind is SlideShowShapeAnimationEffectKind.Spin
            or SlideShowShapeAnimationEffectKind.Spiral
            or SlideShowShapeAnimationEffectKind.Swivel
            ? plan.RotationDegrees * progress
            : 0;

    private static (double X, double Y) ResolveTranslateFactors(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress)
    {
        if (plan.EffectKind == SlideShowShapeAnimationEffectKind.MotionPath)
        {
            return InterpolateMotionPath(plan.MotionKeyFrames, progress);
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
            SlideShowShapeAnimationEffectKind.Zoom
                or SlideShowShapeAnimationEffectKind.Pulse
                or SlideShowShapeAnimationEffectKind.GrowShrink => SlideShowAnimationVisualTrackKind.Scale,
            SlideShowShapeAnimationEffectKind.Spin
                or SlideShowShapeAnimationEffectKind.Spiral
                or SlideShowShapeAnimationEffectKind.Swivel => SlideShowAnimationVisualTrackKind.Rotate,
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
            SlideShowAnimationClipKind.RandomBars or SlideShowAnimationClipKind.Split => plan.WipeHorizontal,
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
            SlideShowAnimationClipKind.RandomBars => SlideShowPlaybackPlanner.StripsBandCount,
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
        double scale,
        double rotation,
        double translateX,
        double translateY,
        SlideShowAnimationClipKind clipKind,
        double clipProgress) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1}: shape {2}; progress {3:0.###}; opacity {4:0.###}; scale {5:0.###}; rotation {6:0.###}; translate ({7:0.###},{8:0.###}); clip {9} {10:0.###}",
            plan.EffectKind,
            trackKind,
            plan.Animation.ShapeId,
            progress,
            opacity,
            scale,
            rotation,
            translateX,
            translateY,
            clipKind,
            clipProgress);

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * Math.Clamp(progress, 0, 1);
}
