using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowAnimationEffectFrameInterpolation
{
    Discrete,
    Spline,
    Linear
}

public sealed record SlideShowAnimationEffectSpline(
    double ControlPoint1X,
    double ControlPoint1Y,
    double ControlPoint2X,
    double ControlPoint2Y);

public sealed record SlideShowAnimationEffectFrame(
    double Progress,
    double NormalizedX,
    double NormalizedY,
    SlideShowAnimationEffectFrameInterpolation StoryboardInterpolation,
    SlideShowAnimationEffectSpline? StoryboardSpline = null);

public sealed record SlideShowAnimationEffectFramePlan(
    SlideShowShapeAnimationEffectKind EffectKind,
    IReadOnlyList<SlideShowAnimationEffectFrame> Frames)
{
    public SlideShowAnimationEffectFrame Start => Frames[0];
    public SlideShowAnimationEffectFrame End => Frames[^1];
}

/// <summary>
/// Plans renderer-neutral, slide-normalized trajectories for effects whose geometry is shared by
/// WPF storyboards and Avalonia timer animation. Native animation primitives remain host-owned.
/// </summary>
public static class SlideShowAnimationEffectFramePlanner
{
    private const double DirectionThreshold = 0.01;

    public static SlideShowAnimationEffectFramePlan Build(
        SlideShowShapeAnimationEffectKind effectKind,
        AnimationKind animationKind,
        double offsetXFactor,
        double offsetYFactor)
    {
        var isExit = animationKind == AnimationKind.Exit;
        var startX = isExit ? 0 : offsetXFactor;
        var startY = isExit ? 0 : offsetYFactor;
        var endX = isExit ? offsetXFactor : 0;
        var endY = isExit ? offsetYFactor : 0;

        return effectKind switch
        {
            SlideShowShapeAnimationEffectKind.Float => BuildCurved(
                effectKind,
                startX,
                startY,
                endX,
                endY,
                ArcX(offsetYFactor, 0.06),
                ArcY(offsetXFactor, 0.06),
                middleProgress: 0.72,
                new SlideShowAnimationEffectSpline(0.2, 0, 0.4, 1),
                new SlideShowAnimationEffectSpline(0.2, 0, 0.2, 1)),
            SlideShowShapeAnimationEffectKind.Swoop => BuildCurved(
                effectKind,
                startX,
                startY,
                endX,
                endY,
                ArcX(offsetYFactor, 0.14),
                ArcY(offsetXFactor, 0.14),
                middleProgress: 0.55,
                new SlideShowAnimationEffectSpline(0.1, 0, 0.25, 1),
                new SlideShowAnimationEffectSpline(0.25, 0, 0.2, 1)),
            SlideShowShapeAnimationEffectKind.Boomerang => BuildBoomerang(
                startX,
                startY,
                endX,
                endY,
                offsetXFactor,
                offsetYFactor,
                isExit),
            SlideShowShapeAnimationEffectKind.Bounce => BuildBounce(
                startX,
                startY,
                endX,
                endY,
                offsetXFactor,
                offsetYFactor,
                isExit),
            _ => throw new ArgumentOutOfRangeException(
                nameof(effectKind),
                effectKind,
                "The effect does not use a shared trajectory frame plan.")
        };
    }

    public static (double NormalizedX, double NormalizedY) SampleSmooth(
        SlideShowAnimationEffectFramePlan plan,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Frames.Count == 0)
            return default;

        var clamped = Math.Clamp(progress, 0, 1);
        if (clamped <= plan.Start.Progress)
            return (plan.Start.NormalizedX, plan.Start.NormalizedY);

        for (var index = 1; index < plan.Frames.Count; index++)
        {
            var current = plan.Frames[index];
            if (clamped > current.Progress)
                continue;

            var previous = plan.Frames[index - 1];
            var span = current.Progress - previous.Progress;
            var local = span <= 0 ? 1 : (clamped - previous.Progress) / span;
            var eased = SmoothStep(Math.Clamp(local, 0, 1));
            return (
                previous.NormalizedX + (current.NormalizedX - previous.NormalizedX) * eased,
                previous.NormalizedY + (current.NormalizedY - previous.NormalizedY) * eased);
        }

        return (plan.End.NormalizedX, plan.End.NormalizedY);
    }

    private static SlideShowAnimationEffectFramePlan BuildCurved(
        SlideShowShapeAnimationEffectKind effectKind,
        double startX,
        double startY,
        double endX,
        double endY,
        double arcX,
        double arcY,
        double middleProgress,
        SlideShowAnimationEffectSpline middleSpline,
        SlideShowAnimationEffectSpline endSpline) =>
        new(
            effectKind,
            [
                Start(startX, startY),
                Spline(middleProgress, (startX + endX) / 2 + arcX, (startY + endY) / 2 + arcY, middleSpline),
                Spline(1, endX, endY, endSpline)
            ]);

    private static SlideShowAnimationEffectFramePlan BuildBoomerang(
        double startX,
        double startY,
        double endX,
        double endY,
        double offsetX,
        double offsetY,
        bool isExit)
    {
        var overshootX = isExit ? endX + offsetX * 0.08 : endX - offsetX * 0.08;
        var overshootY = isExit ? endY + offsetY * 0.08 : endY - offsetY * 0.08;
        return new SlideShowAnimationEffectFramePlan(
            SlideShowShapeAnimationEffectKind.Boomerang,
            [
                Start(startX, startY),
                Spline(0.78, overshootX, overshootY, new SlideShowAnimationEffectSpline(0.2, 0, 0.3, 1)),
                Spline(1, endX, endY, new SlideShowAnimationEffectSpline(0.2, 0, 0.2, 1))
            ]);
    }

    private static SlideShowAnimationEffectFramePlan BuildBounce(
        double startX,
        double startY,
        double endX,
        double endY,
        double offsetX,
        double offsetY,
        bool isExit)
    {
        var overshootX = isExit ? endX + offsetX * 0.08 : -offsetX * 0.08;
        var overshootY = isExit ? endY + offsetY * 0.08 : -offsetY * 0.08;
        var reboundX = isExit ? endX - offsetX * 0.04 : offsetX * 0.04;
        var reboundY = isExit ? endY - offsetY * 0.04 : offsetY * 0.04;
        var spline = new SlideShowAnimationEffectSpline(0.2, 0, 0.4, 1);
        return new SlideShowAnimationEffectFramePlan(
            SlideShowShapeAnimationEffectKind.Bounce,
            [
                Start(startX, startY),
                Spline(0.55, endX, endY, spline),
                Spline(0.72, overshootX, overshootY, spline),
                Spline(0.86, reboundX, reboundY, spline),
                new SlideShowAnimationEffectFrame(
                    1,
                    endX,
                    endY,
                    SlideShowAnimationEffectFrameInterpolation.Linear)
            ]);
    }

    private static SlideShowAnimationEffectFrame Start(double x, double y) =>
        new(0, x, y, SlideShowAnimationEffectFrameInterpolation.Discrete);

    private static SlideShowAnimationEffectFrame Spline(
        double progress,
        double x,
        double y,
        SlideShowAnimationEffectSpline spline) =>
        new(progress, x, y, SlideShowAnimationEffectFrameInterpolation.Spline, spline);

    private static double ArcX(double perpendicularOffset, double magnitude) =>
        Math.Abs(perpendicularOffset) > DirectionThreshold
            ? -Math.Sign(perpendicularOffset) * magnitude
            : 0;

    private static double ArcY(double perpendicularOffset, double magnitude) =>
        Math.Abs(perpendicularOffset) > DirectionThreshold
            ? Math.Sign(perpendicularOffset) * magnitude
            : 0;

    private static double SmoothStep(double value) => value * value * (3 - 2 * value);
}
