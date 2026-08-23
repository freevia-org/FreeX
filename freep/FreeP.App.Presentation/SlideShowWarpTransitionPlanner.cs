using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowWarpTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int SegmentCount,
    double WaveCount,
    double WarpDepthFactor);

/// <summary>
/// Shared elastic-front geometry for the Warp transition.
/// Each strip follows a phase-shifted, tapering front so the incoming page
/// appears to bend as it is revealed, while both hosts consume identical
/// quadrilateral clip geometry.
/// </summary>
public static class SlideShowWarpTransitionPlanner
{
    public const int DefaultSegmentCount = 12;
    public const double DefaultWaveCount = 2.0;
    public const double DefaultWarpDepthFactor = 0.16;

    public static SlideShowWarpTransitionPlan Plan(SlideTransition transition)
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
            DefaultWarpDepthFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowWarpTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var waveCount = Math.Max(0, plan.WaveCount);
        var warpDepth = Math.Clamp(plan.WarpDepthFactor, 0, 0.45);
        return SlideShowTransitionGeometry.BuildSegmentedFront(
            width,
            height,
            plan.SegmentCount,
            plan.HorizontalAxis,
            plan.Reverse,
            normalized =>
            {
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - normalized * 0.08) / 0.92,
                    0,
                    1));
                if (local <= 0)
                    return null;

                var phase = normalized * waveCount * 2 * Math.PI;
                var extent = plan.HorizontalAxis ? width : height;
                var depth = extent * warpDepth * (1 - local) *
                    (0.55 + 0.45 * Math.Sin(Math.PI * local));
                var wave = Math.Sin(phase + progress * Math.PI * 0.75) * depth;
                var edge = extent * local;
                var leading = Math.Clamp(edge + wave, 0, extent);
                var trailing = Math.Clamp(edge - wave, 0, extent);
                if (plan.Reverse)
                    return new(extent - leading, extent - trailing);

                return new(leading, trailing);
            });
    }


}
