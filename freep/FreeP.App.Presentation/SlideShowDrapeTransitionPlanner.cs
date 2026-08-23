using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowDrapeTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int SegmentCount,
    double WaveCount,
    double WaveDepthFactor);

/// <summary>
/// Shared wavy fold-front geometry for the Drape transition.
/// The leading edge is segmented across the orthogonal axis so both hosts
/// preserve the same draped silhouette without a host-specific curve model.
/// </summary>
public static class SlideShowDrapeTransitionPlanner
{
    public const int DefaultSegmentCount = 10;
    public const double DefaultWaveCount = 1.5;
    public const double DefaultWaveDepthFactor = 0.10;

    public static SlideShowDrapeTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var horizontal = transition.Direction is not (
            TransitionDirection.Up or
            TransitionDirection.Down or
            TransitionDirection.Vertical);
        var reverse = transition.Direction is
            TransitionDirection.Left or
            TransitionDirection.Up or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown;

        return new(
            horizontal,
            reverse,
            DefaultSegmentCount,
            DefaultWaveCount,
            DefaultWaveDepthFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowDrapeTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var waveDepthFactor = Math.Clamp(plan.WaveDepthFactor, 0, 0.45);
        return SlideShowTransitionGeometry.BuildSegmentedFront(
            width,
            height,
            plan.SegmentCount,
            plan.HorizontalAxis,
            plan.Reverse,
            normalized =>
            {
                var phase = normalized * plan.WaveCount * 2 * Math.PI;
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - normalized * 0.10) / 0.90,
                    0,
                    1));
                if (local <= 0)
                    return null;

                var extent = plan.HorizontalAxis ? width : height;
                var depth = extent * waveDepthFactor * (1 - local);
                var wave = Math.Sin(phase) * depth;
                var edge = plan.Reverse ? extent * (1 - local) : extent * local;
                return new SlideShowSegmentedFrontEdges(
                    Math.Clamp(edge + wave, 0, extent),
                    Math.Clamp(edge - wave, 0, extent));
            });
    }


}
