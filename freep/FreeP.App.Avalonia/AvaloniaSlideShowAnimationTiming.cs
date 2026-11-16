using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal static class AvaloniaSlideShowAnimationTiming
{
    private static readonly SlideShowAnimationEffectSpline FlashSpline = new(0.2, 0, 0.4, 1);

    internal sealed record DoubleFrame(
        double Value,
        double Progress,
        SlideShowAnimationEffectFrameInterpolation Interpolation,
        SlideShowAnimationEffectSpline? Spline = null);

    public static double Apply(double progress, int? acceleration, int? deceleration) =>
        SlideShowPlaybackPlanner.ApplyHostTimingEasing(progress, acceleration, deceleration);

    public static double RetimedKeyframeProgress(
        double progress,
        int? acceleration,
        int? deceleration) =>
        SlideShowPlaybackPlanner.InvertHostTimingEasing(progress, acceleration, deceleration);

    public static IReadOnlyList<DoubleFrame> BuildFlashTimeline(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var initialOpacity = plan.Animation.Kind == AnimationKind.Exit
            ? plan.FromOpacity
            : 0;
        return
        [
            new(
                initialOpacity,
                RetimedKeyframeProgress(0, plan.Acceleration, plan.Deceleration),
                SlideShowAnimationEffectFrameInterpolation.Discrete),
            new(
                0.7,
                RetimedKeyframeProgress(0.2, plan.Acceleration, plan.Deceleration),
                SlideShowAnimationEffectFrameInterpolation.Spline,
                FlashSpline),
            new(
                0.35,
                RetimedKeyframeProgress(0.55, plan.Acceleration, plan.Deceleration),
                SlideShowAnimationEffectFrameInterpolation.Spline,
                FlashSpline),
            new(
                plan.ToOpacity,
                RetimedKeyframeProgress(1, plan.Acceleration, plan.Deceleration),
                SlideShowAnimationEffectFrameInterpolation.Linear)
        ];
    }

    public static double SampleFlash(
        IReadOnlyList<DoubleFrame> timeline,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return Sample(
            timeline,
            progress,
            static frame => frame.Value,
            static frame => frame.Progress,
            static frame => frame.Interpolation,
            static frame => frame.Spline);
    }

    public static SlideShowAnimationEffectFramePlan BuildTrajectoryTimeline(
        SlideShowAnimationEffectFramePlan trajectory,
        int? acceleration,
        int? deceleration)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        return trajectory with
        {
            Frames = trajectory.Frames
                .Select(frame => frame with
                {
                    Progress = RetimedKeyframeProgress(frame.Progress, acceleration, deceleration)
                })
                .ToArray()
        };
    }

    public static (double NormalizedX, double NormalizedY) SampleTrajectory(
        SlideShowAnimationEffectFramePlan trajectory,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        if (trajectory.Frames.Count == 0)
            return default;

        return (
            Sample(
                trajectory.Frames,
                progress,
                static frame => frame.NormalizedX,
                static frame => frame.Progress,
                static frame => frame.StoryboardInterpolation,
                static frame => frame.StoryboardSpline),
            Sample(
                trajectory.Frames,
                progress,
                static frame => frame.NormalizedY,
                static frame => frame.Progress,
                static frame => frame.StoryboardInterpolation,
                static frame => frame.StoryboardSpline));
    }

    private static double Sample<TFrame>(
        IReadOnlyList<TFrame> frames,
        double progress,
        Func<TFrame, double> value,
        Func<TFrame, double> keyTime,
        Func<TFrame, SlideShowAnimationEffectFrameInterpolation> interpolation,
        Func<TFrame, SlideShowAnimationEffectSpline?> spline)
    {
        if (frames.Count == 0)
            return 0;

        var clamped = Math.Clamp(progress, 0, 1);
        if (clamped <= keyTime(frames[0]))
            return value(frames[0]);

        for (var index = 1; index < frames.Count; index++)
        {
            var current = frames[index];
            var currentTime = keyTime(current);
            if (clamped > currentTime)
                continue;

            var previous = frames[index - 1];
            var span = currentTime - keyTime(previous);
            var localProgress = span <= 0
                ? 1
                : Math.Clamp((clamped - keyTime(previous)) / span, 0, 1);
            var interpolationProgress = interpolation(current) switch
            {
                SlideShowAnimationEffectFrameInterpolation.Discrete => localProgress < 1 ? 0 : 1,
                SlideShowAnimationEffectFrameInterpolation.Linear => localProgress,
                SlideShowAnimationEffectFrameInterpolation.Spline when spline(current) is { } keySpline =>
                    SampleKeySpline(keySpline, localProgress),
                _ => localProgress
            };
            return value(previous) + (value(current) - value(previous)) * interpolationProgress;
        }

        return value(frames[^1]);
    }

    private static double SampleKeySpline(SlideShowAnimationEffectSpline spline, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress is 0 or 1)
            return progress;

        var low = 0.0;
        var high = 1.0;
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var parameter = (low + high) / 2;
            if (CubicBezier(parameter, spline.ControlPoint1X, spline.ControlPoint2X) < progress)
                low = parameter;
            else
                high = parameter;
        }

        return CubicBezier((low + high) / 2, spline.ControlPoint1Y, spline.ControlPoint2Y);
    }

    private static double CubicBezier(double parameter, double control1, double control2)
    {
        var inverse = 1 - parameter;
        return 3 * inverse * inverse * parameter * control1
            + 3 * inverse * parameter * parameter * control2
            + parameter * parameter * parameter;
    }

}
