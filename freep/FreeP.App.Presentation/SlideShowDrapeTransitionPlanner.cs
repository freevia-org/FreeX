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
            return new[] { new SlideShowMaskPolygon(BuildRectangle(width, height)) };

        var segments = Math.Max(2, plan.SegmentCount);
        var waveDepthFactor = Math.Clamp(plan.WaveDepthFactor, 0, 0.45);
        var polygons = new List<SlideShowMaskPolygon>(segments);

        if (plan.HorizontalAxis)
        {
            var segmentHeight = height / segments;
            for (var segment = 0; segment < segments; segment++)
            {
                var normalized = (segment + 0.5) / segments;
                var phase = normalized * plan.WaveCount * 2 * Math.PI;
                var local = SmoothStep(Math.Clamp(
                    (progress - normalized * 0.10) / 0.90,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var depth = width * waveDepthFactor * (1 - local);
                var wave = Math.Sin(phase) * depth;
                var edge = plan.Reverse ? width * (1 - local) : width * local;
                var topEdge = Math.Clamp(edge + wave, 0, width);
                var bottomEdge = Math.Clamp(edge - wave, 0, width);
                var y0 = segment * segmentHeight;
                var y1 = (segment + 1) * segmentHeight;
                polygons.Add(new(plan.Reverse
                    ? new[]
                    {
                        new SlideShowMaskPoint(topEdge, y0),
                        new SlideShowMaskPoint(width, y0),
                        new SlideShowMaskPoint(width, y1),
                        new SlideShowMaskPoint(bottomEdge, y1)
                    }
                    : new[]
                    {
                        new SlideShowMaskPoint(0, y0),
                        new SlideShowMaskPoint(topEdge, y0),
                        new SlideShowMaskPoint(bottomEdge, y1),
                        new SlideShowMaskPoint(0, y1)
                    }));
            }
        }
        else
        {
            var segmentWidth = width / segments;
            for (var segment = 0; segment < segments; segment++)
            {
                var normalized = (segment + 0.5) / segments;
                var phase = normalized * plan.WaveCount * 2 * Math.PI;
                var local = SmoothStep(Math.Clamp(
                    (progress - normalized * 0.10) / 0.90,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var depth = height * waveDepthFactor * (1 - local);
                var wave = Math.Sin(phase) * depth;
                var edge = plan.Reverse ? height * (1 - local) : height * local;
                var leadingEdge = Math.Clamp(edge + wave, 0, height);
                var trailingEdge = Math.Clamp(edge - wave, 0, height);
                var x0 = segment * segmentWidth;
                var x1 = (segment + 1) * segmentWidth;
                polygons.Add(new(plan.Reverse
                    ? new[]
                    {
                        new SlideShowMaskPoint(x0, leadingEdge),
                        new SlideShowMaskPoint(x1, leadingEdge),
                        new SlideShowMaskPoint(x1, height),
                        new SlideShowMaskPoint(x0, trailingEdge)
                    }
                    : new[]
                    {
                        new SlideShowMaskPoint(x0, 0),
                        new SlideShowMaskPoint(x1, 0),
                        new SlideShowMaskPoint(x1, trailingEdge),
                        new SlideShowMaskPoint(x0, leadingEdge)
                    }));
            }
        }

        return polygons;
    }

    private static double SmoothStep(double value) => value * value * (3 - 2 * value);

    private static IReadOnlyList<SlideShowMaskPoint> BuildRectangle(double width, double height) =>
        new[]
        {
            new SlideShowMaskPoint(0, 0),
            new SlideShowMaskPoint(width, 0),
            new SlideShowMaskPoint(width, height),
            new SlideShowMaskPoint(0, height)
        };
}
