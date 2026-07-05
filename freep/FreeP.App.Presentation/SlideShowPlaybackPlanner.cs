using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowTransitionPlaybackActionKind
{
    ShowInstant,
    Fade,
    Push
}

public sealed record SlideShowTransitionPlaybackPlan(
    SlideShowTransitionPlaybackActionKind ActionKind,
    int DurationMs,
    double IncomingOffsetX,
    double IncomingOffsetY,
    SlideShowTransitionPlaybackKind SourceKind);

public enum SlideShowShapeAnimationEffectKind
{
    Appear,
    Fade,
    FlyIn,
    Wipe,
    Split,
    RandomBars,
    Blinds,
    Box,
    Checkerboard,
    Circle,
    Diamond,
    Plus,
    Zoom,
    Pulse,
    GrowShrink,
    Spin,
    MotionPath
}

public enum SlideShowAnimationRevealTiming
{
    None,
    AtStart,
    OnComplete
}

public sealed record SlideShowMotionPathKeyFrame(
    double Progress,
    double OffsetXFactor,
    double OffsetYFactor);

public enum SlideShowGeometricMaskKind
{
    None,
    Circle,
    Diamond,
    Plus
}

public sealed record SlideShowShapeAnimationPlaybackPlan(
    ShapeAnimation Animation,
    SlideShowShapeAnimationEffectKind EffectKind,
    int DurationMs,
    int DelayMs,
    SlideShowAnimationRevealTiming RevealTiming,
    double FromOpacity,
    double ToOpacity,
    double FromScale,
    double ToScale,
    double PeakScale,
    double RotationDegrees,
    double OffsetXFactor,
    double OffsetYFactor,
    bool WipeHorizontal,
    bool BlindsHorizontal,
    int BlindsBandCount,
    bool BoxExpandsFromCenter,
    SlideShowGeometricMaskKind GeometricMaskKind,
    bool GeometricMaskExpandsFromCenter,
    bool CheckerboardHorizontal,
    int CheckerboardRowCount,
    int CheckerboardColumnCount,
    IReadOnlyList<SlideShowMotionPathKeyFrame> MotionKeyFrames);

public sealed record SlideShowFallbackAnimationPlaybackPlan(
    int DurationMs,
    int DelayMs,
    double FromOpacity,
    double FlashOpacity);

public static class SlideShowPlaybackPlanner
{
    public const int MinTransitionDurationMs = 50;
    public const int MinShapeAnimationDurationMs = 50;
    public const int MinFallbackAnimationDurationMs = 100;
    public const int MotionPathFrameCount = 30;
    public const int BlindsBandCount = 8;
    public const int CheckerboardRowCount = 4;
    public const int CheckerboardColumnCount = 6;

    public static SlideShowTransitionPlaybackPlan PlanTransition(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var transitionPlan = SlideShowTransitionPlanner.Plan(transition);
        var actionKind = transitionPlan.PlaybackKind switch
        {
            SlideShowTransitionPlaybackKind.Cut => SlideShowTransitionPlaybackActionKind.ShowInstant,
            SlideShowTransitionPlaybackKind.PushLike => SlideShowTransitionPlaybackActionKind.Push,
            _ => SlideShowTransitionPlaybackActionKind.Fade
        };

        return new SlideShowTransitionPlaybackPlan(
            actionKind,
            Math.Max(MinTransitionDurationMs, transition.DurationMs),
            transitionPlan.IncomingOffsetX,
            transitionPlan.IncomingOffsetY,
            transitionPlan.PlaybackKind);
    }

    public static IReadOnlyList<SlideShowShapeAnimationPlaybackPlan> PlanAnimationStep(AnimationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Entries
            .Select(entry => PlanShapeAnimation(entry.Animation, entry.StartDelayMs))
            .ToList();
    }

    public static SlideShowShapeAnimationPlaybackPlan PlanShapeAnimation(
        ShapeAnimation animation,
        int startDelayMs)
    {
        ArgumentNullException.ThrowIfNull(animation);

        var effectKind = ResolveEffectKind(animation);
        var (fromOpacity, toOpacity) = ResolveOpacity(animation);
        var (fromScale, toScale) = ResolveScale(animation);
        var (offsetX, offsetY) = ResolveFlyInOffset(animation.Direction);

        return new SlideShowShapeAnimationPlaybackPlan(
            animation,
            effectKind,
            Math.Max(MinShapeAnimationDurationMs, animation.DurationMs),
            Math.Max(0, startDelayMs),
            ResolveRevealTiming(animation, effectKind),
            fromOpacity,
            toOpacity,
            fromScale,
            toScale,
            ResolvePeakScale(animation),
            RotationDegrees: 360,
            offsetX,
            offsetY,
            IsHorizontalWipe(animation.Direction),
            IsHorizontalBlinds(animation.Direction),
            BlindsBandCount,
            BoxExpandsFromCenter(animation),
            ResolveGeometricMaskKind(animation),
            GeometricMaskExpandsFromCenter(animation),
            IsHorizontalCheckerboard(animation.Direction),
            CheckerboardRowCount,
            CheckerboardColumnCount,
            BuildMotionKeyFrames(animation.Motion));
    }

    public static SlideShowFallbackAnimationPlaybackPlan? PlanFallbackAnimation(
        ShapeAnimation animation,
        int startDelayMs)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return animation.Kind == AnimationKind.Emphasis
            ? new SlideShowFallbackAnimationPlaybackPlan(
                Math.Max(MinFallbackAnimationDurationMs, animation.DurationMs),
                Math.Max(0, startDelayMs),
                FromOpacity: 1,
                FlashOpacity: 0.5)
            : null;
    }

    private static SlideShowShapeAnimationEffectKind ResolveEffectKind(ShapeAnimation animation)
    {
        if (animation.Kind == AnimationKind.Motion && animation.Motion is not null)
        {
            return SlideShowShapeAnimationEffectKind.MotionPath;
        }

        return animation.Preset switch
        {
            AnimationPreset.Appear => SlideShowShapeAnimationEffectKind.Appear,
            AnimationPreset.Fade => SlideShowShapeAnimationEffectKind.Fade,
            AnimationPreset.FlyIn => SlideShowShapeAnimationEffectKind.FlyIn,
            AnimationPreset.Wipe => SlideShowShapeAnimationEffectKind.Wipe,
            AnimationPreset.Split => SlideShowShapeAnimationEffectKind.Split,
            AnimationPreset.RandomBars => SlideShowShapeAnimationEffectKind.RandomBars,
            AnimationPreset.Blinds => SlideShowShapeAnimationEffectKind.Blinds,
            AnimationPreset.Box => SlideShowShapeAnimationEffectKind.Box,
            AnimationPreset.Checkerboard => SlideShowShapeAnimationEffectKind.Checkerboard,
            AnimationPreset.Circle => SlideShowShapeAnimationEffectKind.Circle,
            AnimationPreset.Diamond => SlideShowShapeAnimationEffectKind.Diamond,
            AnimationPreset.Plus => SlideShowShapeAnimationEffectKind.Plus,
            AnimationPreset.Zoom => SlideShowShapeAnimationEffectKind.Zoom,
            AnimationPreset.Pulse => SlideShowShapeAnimationEffectKind.Pulse,
            AnimationPreset.Grow or AnimationPreset.Shrink => SlideShowShapeAnimationEffectKind.GrowShrink,
            AnimationPreset.Spin => SlideShowShapeAnimationEffectKind.Spin,
            _ => SlideShowShapeAnimationEffectKind.Appear
        };
    }

    private static SlideShowAnimationRevealTiming ResolveRevealTiming(
        ShapeAnimation animation,
        SlideShowShapeAnimationEffectKind effectKind)
    {
        if (effectKind == SlideShowShapeAnimationEffectKind.MotionPath ||
            animation.Kind is AnimationKind.Emphasis or AnimationKind.Exit)
        {
            return SlideShowAnimationRevealTiming.AtStart;
        }

        return SlideShowAnimationRevealTiming.OnComplete;
    }

    private static (double From, double To) ResolveOpacity(ShapeAnimation animation) =>
        animation.Kind == AnimationKind.Exit
            ? (1, 0)
            : (0, 1);

    private static (double From, double To) ResolveScale(ShapeAnimation animation)
    {
        if (animation.Kind == AnimationKind.Exit)
        {
            return (1, 0);
        }

        return animation.Preset is AnimationPreset.Grow or AnimationPreset.Shrink
            ? (1, 1)
            : (0, 1);
    }

    private static double ResolvePeakScale(ShapeAnimation animation) =>
        animation.Preset == AnimationPreset.Shrink
            ? 0.8
            : 1.2;

    private static (double X, double Y) ResolveFlyInOffset(AnimationDirection? direction) =>
        direction switch
        {
            AnimationDirection.FromLeft => (-1, 0),
            AnimationDirection.FromRight => (1, 0),
            AnimationDirection.FromTop => (0, -1),
            AnimationDirection.FromBottom => (0, 1),
            AnimationDirection.FromTopLeft => (-1, -1),
            AnimationDirection.FromTopRight => (1, -1),
            AnimationDirection.FromBottomLeft => (-1, 1),
            AnimationDirection.FromBottomRight => (1, 1),
            AnimationDirection.Left => (-1, 0),
            AnimationDirection.Right => (1, 0),
            AnimationDirection.Up => (0, -1),
            AnimationDirection.Down => (0, 1),
            _ => (0, 1)
        };

    private static bool IsHorizontalWipe(AnimationDirection? direction) =>
        direction is AnimationDirection.Left
            or AnimationDirection.Right
            or AnimationDirection.FromLeft
            or AnimationDirection.FromRight
            or AnimationDirection.Horizontal
            or null;

    private static bool IsHorizontalBlinds(AnimationDirection? direction) =>
        direction is not AnimationDirection.Vertical;

    private static bool IsHorizontalCheckerboard(AnimationDirection? direction) =>
        direction is not AnimationDirection.Vertical;

    private static bool BoxExpandsFromCenter(ShapeAnimation animation) =>
        ExpandsFromCenter(animation);

    private static SlideShowGeometricMaskKind ResolveGeometricMaskKind(ShapeAnimation animation) =>
        animation.Preset switch
        {
            AnimationPreset.Circle => SlideShowGeometricMaskKind.Circle,
            AnimationPreset.Diamond => SlideShowGeometricMaskKind.Diamond,
            AnimationPreset.Plus => SlideShowGeometricMaskKind.Plus,
            _ => SlideShowGeometricMaskKind.None
        };

    private static bool GeometricMaskExpandsFromCenter(ShapeAnimation animation) =>
        ExpandsFromCenter(animation);

    private static bool ExpandsFromCenter(ShapeAnimation animation) =>
        animation.Direction switch
        {
            AnimationDirection.In => true,
            AnimationDirection.Out => false,
            _ => animation.Kind != AnimationKind.Exit
        };

    private static IReadOnlyList<SlideShowMotionPathKeyFrame> BuildMotionKeyFrames(MotionPath? path)
    {
        if (path is null)
        {
            return Array.Empty<SlideShowMotionPathKeyFrame>();
        }

        var frames = new List<SlideShowMotionPathKeyFrame>(MotionPathFrameCount + 1);
        for (var frame = 0; frame <= MotionPathFrameCount; frame++)
        {
            var progress = frame / (double)MotionPathFrameCount;
            var (dx, dy) = MotionPathEvaluator.Sample(path, progress);
            frames.Add(new SlideShowMotionPathKeyFrame(progress, dx, dy));
        }

        return frames;
    }
}
