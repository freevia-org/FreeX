namespace FreeP.App.Compositor;

public enum SlideShowAnimationScalarPropertyKind
{
    Opacity,
    ScaleX,
    ScaleY,
    RotationDegrees,
    HorizontalScale,
    TranslateXFactor
}

public enum SlideShowAnimationScalarInterpolationKind
{
    Linear,
    Discrete
}

public sealed record SlideShowAnimationScalarKeyFrame(
    double Value,
    double Progress,
    SlideShowAnimationScalarInterpolationKind InterpolationKind);

public sealed record SlideShowAnimationScalarTrackPlan(
    SlideShowAnimationScalarPropertyKind PropertyKind,
    IReadOnlyList<SlideShowAnimationScalarKeyFrame> KeyFrames);

public sealed record SlideShowAnimationEffectTrackPlan(
    SlideShowShapeAnimationEffectKind EffectKind,
    int DelayMs,
    int DurationMs,
    IReadOnlyList<SlideShowAnimationScalarTrackPlan> Tracks,
    bool AddAuthoredColorOverlay)
{
    public SlideShowAnimationScalarTrackPlan? FindTrack(
        SlideShowAnimationScalarPropertyKind propertyKind) =>
        Tracks.FirstOrDefault(track => track.PropertyKind == propertyKind);
}

public sealed record SlideShowAnimationEffectScalarState(
    double? Opacity = null,
    double? ScaleX = null,
    double? ScaleY = null,
    double? RotationDegrees = null,
    double? HorizontalScale = null,
    double? TranslateXFactor = null);

/// <summary>
/// Projects the established slideshow frame behavior into portable scalar tracks.
/// Renderers only materialize these values as native transforms and animations.
/// </summary>
public static class SlideShowAnimationEffectTrackPlanner
{
    public const int StoryboardFrameCount = 30;
    public const int TimerFrameIntervalMs = 16;

    public static SlideShowAnimationEffectTrackPlan? Build(
        SlideShowShapeAnimationPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var properties = ResolveProperties(plan.EffectKind);
        if (properties.Count == 0)
            return null;

        var interpolation = UsesDiscreteSampling(plan.EffectKind)
            ? SlideShowAnimationScalarInterpolationKind.Discrete
            : SlideShowAnimationScalarInterpolationKind.Linear;
        var progressPoints = BuildProgressPoints(plan.EffectKind);
        var tracks = properties
            .Select(property => new SlideShowAnimationScalarTrackPlan(
                property,
                progressPoints
                    .Select(progress =>
                    {
                        var easedProgress = SlideShowPlaybackPlanner.ApplyTimingEasing(
                            progress,
                            plan.Acceleration,
                            plan.Deceleration);
                        return new SlideShowAnimationScalarKeyFrame(
                            ResolveValue(Sample(plan, easedProgress), property),
                            progress,
                            interpolation);
                    })
                    .ToArray()))
            .ToArray();

        return new SlideShowAnimationEffectTrackPlan(
            plan.EffectKind,
            Math.Max(0, plan.DelayMs),
            Math.Max(1, plan.DurationMs),
            tracks,
            RequiresAuthoredColorOverlay(plan.EffectKind));
    }

    public static SlideShowAnimationEffectScalarState Sample(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress,
        bool isBeforeStart = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        progress = Math.Clamp(progress, 0, 1);

        return plan.EffectKind switch
        {
            SlideShowShapeAnimationEffectKind.Pulse
                or SlideShowShapeAnimationEffectKind.GrowShrink =>
                SampleScaleAxes(plan, progress),
            SlideShowShapeAnimationEffectKind.Spin =>
                new(RotationDegrees: plan.RotationDegrees * progress),
            SlideShowShapeAnimationEffectKind.Spiral =>
                new(RotationDegrees: SampleSpiralRotation(plan.RotationDegrees, progress)),
            SlideShowShapeAnimationEffectKind.Swivel =>
                new(
                    RotationDegrees: plan.RotationDegrees * progress,
                    HorizontalScale: ResolveSwivelHorizontalScale(progress)),
            SlideShowShapeAnimationEffectKind.Teeter =>
                new(RotationDegrees: Math.Sin(progress * Math.PI * 4) * 10),
            SlideShowShapeAnimationEffectKind.Blink =>
                new(Opacity: SampleBlinkOpacity(progress, isBeforeStart)),
            SlideShowShapeAnimationEffectKind.FlashBulb =>
                new(Opacity: SampleFlashBulbOpacity(progress, isBeforeStart)),
            SlideShowShapeAnimationEffectKind.Flicker =>
                new(Opacity: SampleFlickerOpacity(progress, isBeforeStart)),
            SlideShowShapeAnimationEffectKind.Wave =>
                new(TranslateXFactor: Math.Sin(progress * Math.PI * 4) * 0.00625),
            SlideShowShapeAnimationEffectKind.ColorWave =>
                new(Opacity: SampleColorWaveOpacity(progress)),
            SlideShowShapeAnimationEffectKind.ColorPulse
                or SlideShowShapeAnimationEffectKind.ChangeColor
                or SlideShowShapeAnimationEffectKind.Shimmer =>
                new(Opacity: SampleEmphasisOpacity(progress)),
            SlideShowShapeAnimationEffectKind.GrowWithColor =>
                SampleGrowWithColor(plan, progress),
            _ => new()
        };
    }

    public static double Sample(
        SlideShowAnimationScalarTrackPlan track,
        double progress)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.KeyFrames.Count == 0)
            throw new ArgumentException("The scalar track has no keyframes.", nameof(track));

        progress = Math.Clamp(progress, 0, 1);
        var previous = track.KeyFrames[0];
        if (progress <= previous.Progress)
            return previous.Value;

        for (var index = 1; index < track.KeyFrames.Count; index++)
        {
            var current = track.KeyFrames[index];
            if (progress > current.Progress)
            {
                previous = current;
                continue;
            }

            if (current.InterpolationKind == SlideShowAnimationScalarInterpolationKind.Discrete)
                return progress < current.Progress ? previous.Value : current.Value;

            var segmentProgress = (progress - previous.Progress) /
                Math.Max(0.0001, current.Progress - previous.Progress);
            return Lerp(previous.Value, current.Value, segmentProgress);
        }

        return track.KeyFrames[^1].Value;
    }

    public static int ResolveTimerStepCount(int durationMs) =>
        Math.Max(1, Math.Max(0, durationMs) / TimerFrameIntervalMs);

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

    private static IReadOnlyList<SlideShowAnimationScalarPropertyKind> ResolveProperties(
        SlideShowShapeAnimationEffectKind effectKind) =>
        effectKind switch
        {
            SlideShowShapeAnimationEffectKind.Pulse
                or SlideShowShapeAnimationEffectKind.GrowShrink =>
                [SlideShowAnimationScalarPropertyKind.ScaleX, SlideShowAnimationScalarPropertyKind.ScaleY],
            SlideShowShapeAnimationEffectKind.GrowWithColor =>
                [
                    SlideShowAnimationScalarPropertyKind.Opacity,
                    SlideShowAnimationScalarPropertyKind.ScaleX,
                    SlideShowAnimationScalarPropertyKind.ScaleY
                ],
            SlideShowShapeAnimationEffectKind.Spin
                or SlideShowShapeAnimationEffectKind.Spiral
                or SlideShowShapeAnimationEffectKind.Teeter =>
                [SlideShowAnimationScalarPropertyKind.RotationDegrees],
            SlideShowShapeAnimationEffectKind.Swivel =>
                [
                    SlideShowAnimationScalarPropertyKind.RotationDegrees,
                    SlideShowAnimationScalarPropertyKind.HorizontalScale
                ],
            SlideShowShapeAnimationEffectKind.Blink
                or SlideShowShapeAnimationEffectKind.FlashBulb
                or SlideShowShapeAnimationEffectKind.Flicker
                or SlideShowShapeAnimationEffectKind.ColorPulse
                or SlideShowShapeAnimationEffectKind.ChangeColor
                or SlideShowShapeAnimationEffectKind.ColorWave
                or SlideShowShapeAnimationEffectKind.Shimmer =>
                [SlideShowAnimationScalarPropertyKind.Opacity],
            SlideShowShapeAnimationEffectKind.Wave =>
                [SlideShowAnimationScalarPropertyKind.TranslateXFactor],
            _ => Array.Empty<SlideShowAnimationScalarPropertyKind>()
        };

    private static IReadOnlyList<double> BuildProgressPoints(
        SlideShowShapeAnimationEffectKind effectKind)
    {
        var points = Enumerable.Range(0, StoryboardFrameCount + 1)
            .Select(frame => frame / (double)StoryboardFrameCount)
            .Concat([0.25, 0.5, 0.7, 0.75])
            .Concat(effectKind switch
            {
                SlideShowShapeAnimationEffectKind.Blink => [0.25, 0.5, 0.75],
                SlideShowShapeAnimationEffectKind.FlashBulb => [0.08, 0.16, 0.30, 0.31],
                SlideShowShapeAnimationEffectKind.Flicker => [0.20, 0.35, 0.50, 0.65, 0.80, 0.90],
                _ => Array.Empty<double>()
            })
            .Distinct()
            .OrderBy(progress => progress)
            .ToArray();
        return points;
    }

    private static bool UsesDiscreteSampling(SlideShowShapeAnimationEffectKind effectKind) =>
        effectKind is SlideShowShapeAnimationEffectKind.Blink
            or SlideShowShapeAnimationEffectKind.FlashBulb
            or SlideShowShapeAnimationEffectKind.Flicker;

    private static bool RequiresAuthoredColorOverlay(SlideShowShapeAnimationEffectKind effectKind) =>
        effectKind is SlideShowShapeAnimationEffectKind.ColorPulse
            or SlideShowShapeAnimationEffectKind.ChangeColor
            or SlideShowShapeAnimationEffectKind.ColorWave
            or SlideShowShapeAnimationEffectKind.GrowWithColor
            or SlideShowShapeAnimationEffectKind.Shimmer;

    private static SlideShowAnimationEffectScalarState SampleScaleAxes(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress)
    {
        var firstHalf = progress <= 0.5;
        var phaseProgress = firstHalf ? progress * 2 : (progress - 0.5) * 2;
        return firstHalf
            ? new(
                ScaleX: Lerp(plan.FromScaleX, plan.PeakScaleX, phaseProgress),
                ScaleY: Lerp(plan.FromScaleY, plan.PeakScaleY, phaseProgress))
            : new(
                ScaleX: Lerp(plan.PeakScaleX, plan.ToScaleX, phaseProgress),
                ScaleY: Lerp(plan.PeakScaleY, plan.ToScaleY, phaseProgress));
    }

    private static SlideShowAnimationEffectScalarState SampleGrowWithColor(
        SlideShowShapeAnimationPlaybackPlan plan,
        double progress)
    {
        var scale = progress <= 0.5
            ? Lerp(1, plan.PeakScale, progress * 2)
            : Lerp(plan.PeakScale, 1, (progress - 0.5) * 2);
        return new(
            Opacity: SampleEmphasisOpacity(progress),
            ScaleX: scale,
            ScaleY: scale);
    }

    private static double SampleSpiralRotation(double rotationDegrees, double progress) =>
        progress <= 0.7
            ? Lerp(0, rotationDegrees * 0.82, progress / 0.7)
            : Lerp(rotationDegrees * 0.82, rotationDegrees, (progress - 0.7) / 0.3);

    private static double SampleBlinkOpacity(double progress, bool isBeforeStart)
    {
        if (isBeforeStart)
            return 1;

        var phase = progress * 4;
        return (int)Math.Floor(phase) % 2 == 0 ? 1 : 0.15;
    }

    private static double SampleFlashBulbOpacity(double progress, bool isBeforeStart)
    {
        if (isBeforeStart)
            return 1;

        return progress switch
        {
            < 0.08 => 1,
            < 0.16 => 0.05,
            < 0.30 => 1,
            < 0.31 => 0.70,
            _ => 1
        };
    }

    private static double SampleFlickerOpacity(double progress, bool isBeforeStart)
    {
        if (isBeforeStart)
            return 1;

        return progress switch
        {
            < 0.20 => 1,
            < 0.35 => 0.20,
            < 0.50 => 0.80,
            < 0.65 => 0.15,
            < 0.80 => 0.65,
            < 0.90 => 0.25,
            _ => 1
        };
    }

    private static double SampleColorWaveOpacity(double progress)
    {
        if (progress <= 0.25)
            return Lerp(1, 0.65, progress / 0.25);
        if (progress <= 0.50)
            return Lerp(0.65, 1, (progress - 0.25) / 0.25);
        if (progress <= 0.75)
            return Lerp(1, 0.65, (progress - 0.50) / 0.25);
        return Lerp(0.65, 1, (progress - 0.75) / 0.25);
    }

    private static double SampleEmphasisOpacity(double progress) =>
        progress <= 0.5
            ? Lerp(1, 0.65, progress * 2)
            : Lerp(0.65, 1, (progress - 0.5) * 2);

    private static double ResolveValue(
        SlideShowAnimationEffectScalarState state,
        SlideShowAnimationScalarPropertyKind propertyKind) =>
        propertyKind switch
        {
            SlideShowAnimationScalarPropertyKind.Opacity => state.Opacity,
            SlideShowAnimationScalarPropertyKind.ScaleX => state.ScaleX,
            SlideShowAnimationScalarPropertyKind.ScaleY => state.ScaleY,
            SlideShowAnimationScalarPropertyKind.RotationDegrees => state.RotationDegrees,
            SlideShowAnimationScalarPropertyKind.HorizontalScale => state.HorizontalScale,
            SlideShowAnimationScalarPropertyKind.TranslateXFactor => state.TranslateXFactor,
            _ => null
        } ?? throw new InvalidOperationException(
            $"Effect state does not define {propertyKind}.");

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * Math.Clamp(progress, 0, 1);
}
