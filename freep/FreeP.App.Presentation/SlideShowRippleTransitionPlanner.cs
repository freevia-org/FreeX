using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowRippleTransitionPlan(
    int SegmentCount,
    double WaveCount,
    double WaveAmplitudeFactor,
    double PhaseRadians);

/// <summary>
/// Shared radial wavefront geometry for the Ripple transition.
/// The incoming slide is revealed through one deterministic, wavy boundary.
/// </summary>
public static class SlideShowRippleTransitionPlanner
{
    public const int DefaultSegmentCount = 48;
    public const double DefaultWaveCount = 5;
    public const double DefaultWaveAmplitudeFactor = 0.08;

    public static SlideShowRippleTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var phase = transition.Direction switch
        {
            TransitionDirection.Right or TransitionDirection.RightDown => 0.35,
            TransitionDirection.Left or TransitionDirection.LeftUp => 1.25,
            TransitionDirection.Down or TransitionDirection.RightUp => 2.15,
            TransitionDirection.Up or TransitionDirection.LeftDown => 2.85,
            _ => 0
        };

        return new(
            DefaultSegmentCount,
            DefaultWaveCount,
            DefaultWaveAmplitudeFactor,
            phase);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowRippleTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var segments = Math.Max(12, plan.SegmentCount);
        var centerX = width / 2;
        var centerY = height / 2;
        var maximumRadius = Math.Sqrt(width * width + height * height) * 0.62;
        var radius = maximumRadius * progress;
        var amplitude = Math.Min(width, height)
            * Math.Max(0, plan.WaveAmplitudeFactor)
            * (1 - progress);
        var points = new SlideShowMaskPoint[segments];

        for (var index = 0; index < segments; index++)
        {
            var angle = 2 * Math.PI * index / segments;
            var wave = Math.Sin(angle * plan.WaveCount + plan.PhaseRadians) * amplitude;
            var localRadius = Math.Max(0, radius + wave);
            points[index] = new(
                centerX + Math.Cos(angle) * localRadius,
                centerY + Math.Sin(angle) * localRadius);
        }

        return new[] { new SlideShowMaskPolygon(points) };
    }

}
