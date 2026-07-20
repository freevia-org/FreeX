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
            return new[] { new SlideShowMaskPolygon(BuildRectangle(width, height)) };

        var segments = Math.Max(2, plan.SegmentCount);
        var waveCount = Math.Max(0, plan.WaveCount);
        var warpDepth = Math.Clamp(plan.WarpDepthFactor, 0, 0.45);
        var polygons = new List<SlideShowMaskPolygon>(segments);

        if (plan.HorizontalAxis)
        {
            var segmentHeight = height / segments;
            for (var segment = 0; segment < segments; segment++)
            {
                var normalized = (segment + 0.5) / segments;
                var local = SmoothStep(Math.Clamp(
                    (progress - normalized * 0.08) / 0.92,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var phase = normalized * waveCount * 2 * Math.PI;
                var depth = width * warpDepth * (1 - local) *
                    (0.55 + 0.45 * Math.Sin(Math.PI * local));
                var wave = Math.Sin(phase + progress * Math.PI * 0.75) * depth;
                var edge = width * local;
                var topEdge = Math.Clamp(edge + wave, 0, width);
                var bottomEdge = Math.Clamp(edge - wave, 0, width);
                var y0 = segment * segmentHeight;
                var y1 = (segment + 1) * segmentHeight;

                polygons.Add(new(plan.Reverse
                    ? new[]
                    {
                        new SlideShowMaskPoint(width - topEdge, y0),
                        new SlideShowMaskPoint(width, y0),
                        new SlideShowMaskPoint(width, y1),
                        new SlideShowMaskPoint(width - bottomEdge, y1)
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
                var local = SmoothStep(Math.Clamp(
                    (progress - normalized * 0.08) / 0.92,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var phase = normalized * waveCount * 2 * Math.PI;
                var depth = height * warpDepth * (1 - local) *
                    (0.55 + 0.45 * Math.Sin(Math.PI * local));
                var wave = Math.Sin(phase + progress * Math.PI * 0.75) * depth;
                var edge = height * local;
                var leadingEdge = Math.Clamp(edge + wave, 0, height);
                var trailingEdge = Math.Clamp(edge - wave, 0, height);
                var x0 = segment * segmentWidth;
                var x1 = (segment + 1) * segmentWidth;

                polygons.Add(new(plan.Reverse
                    ? new[]
                    {
                        new SlideShowMaskPoint(x0, height - leadingEdge),
                        new SlideShowMaskPoint(x1, height - leadingEdge),
                        new SlideShowMaskPoint(x1, height),
                        new SlideShowMaskPoint(x0, height - trailingEdge)
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
