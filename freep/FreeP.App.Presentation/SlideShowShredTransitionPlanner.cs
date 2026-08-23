using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowShredTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int FragmentCount,
    double RevealWindow,
    double TearFactor);

/// <summary>
/// Shared staggered fragment geometry for the Shred transition.
/// Fragments reveal in a stable interleaved order with alternating diagonal
/// leading edges, giving both hosts the same torn-band silhouette.
/// </summary>
public static class SlideShowShredTransitionPlanner
{
    public const int DefaultFragmentCount = 10;
    public const double DefaultRevealWindow = 0.42;
    public const double DefaultTearFactor = 0.14;

    public static SlideShowShredTransitionPlan Plan(SlideTransition transition)
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
            DefaultFragmentCount,
            DefaultRevealWindow,
            DefaultTearFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowShredTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var fragments = Math.Max(2, plan.FragmentCount);
        var revealWindow = Math.Clamp(plan.RevealWindow, 0.05, 0.9);
        var tearFactor = Math.Clamp(plan.TearFactor, 0, 0.45);
        var polygons = new List<SlideShowMaskPolygon>(fragments);

        if (plan.HorizontalAxis)
        {
            var bandHeight = height / fragments;
            for (var fragment = 0; fragment < fragments; fragment++)
            {
                var order = StableUnit(fragment, fragments);
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - order) / revealWindow,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var y0 = fragment * bandHeight;
                var y1 = (fragment + 1) * bandHeight;
                var tear = width * tearFactor * (1 - local)
                    * (fragment % 2 == 0 ? 1 : -1);
                var edge = width * local;
                if (plan.Reverse)
                {
                    var rightEdge = Math.Clamp(width - edge + tear, 0, width);
                    var lowerEdge = Math.Clamp(width - edge - tear, 0, width);
                    polygons.Add(new(new[]
                    {
                        new SlideShowMaskPoint(rightEdge, y0),
                        new SlideShowMaskPoint(width, y0),
                        new SlideShowMaskPoint(width, y1),
                        new SlideShowMaskPoint(lowerEdge, y1)
                    }));
                }
                else
                {
                    var topEdge = Math.Clamp(edge + tear, 0, width);
                    var bottomEdge = Math.Clamp(edge - tear, 0, width);
                    polygons.Add(new(new[]
                    {
                        new SlideShowMaskPoint(0, y0),
                        new SlideShowMaskPoint(topEdge, y0),
                        new SlideShowMaskPoint(bottomEdge, y1),
                        new SlideShowMaskPoint(0, y1)
                    }));
                }
            }
        }
        else
        {
            var bandWidth = width / fragments;
            for (var fragment = 0; fragment < fragments; fragment++)
            {
                var order = StableUnit(fragment, fragments);
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - order) / revealWindow,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var x0 = fragment * bandWidth;
                var x1 = (fragment + 1) * bandWidth;
                var tear = height * tearFactor * (1 - local)
                    * (fragment % 2 == 0 ? 1 : -1);
                var edge = height * local;
                if (plan.Reverse)
                {
                    var rightEdge = Math.Clamp(height - edge + tear, 0, height);
                    var lowerEdge = Math.Clamp(height - edge - tear, 0, height);
                    polygons.Add(new(new[]
                    {
                        new SlideShowMaskPoint(x0, rightEdge),
                        new SlideShowMaskPoint(x1, rightEdge),
                        new SlideShowMaskPoint(x1, height),
                        new SlideShowMaskPoint(x0, lowerEdge)
                    }));
                }
                else
                {
                    var leftEdge = Math.Clamp(edge + tear, 0, height);
                    var rightEdge = Math.Clamp(edge - tear, 0, height);
                    polygons.Add(new(new[]
                    {
                        new SlideShowMaskPoint(x0, 0),
                        new SlideShowMaskPoint(x1, 0),
                        new SlideShowMaskPoint(x1, rightEdge),
                        new SlideShowMaskPoint(x0, leftEdge)
                    }));
                }
            }
        }

        return polygons;
    }

    private static double StableUnit(int index, int count) =>
        ((index * 7) % count) / (double)count;


}
