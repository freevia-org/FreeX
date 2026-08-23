using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowWindTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int BandCount,
    double StaggerWindow,
    double SkewFactor);

/// <summary>
/// Shared staggered swept-band geometry for PowerPoint's Wind transition.
/// Each band has a slightly delayed start and a skewed leading edge, keeping
/// the host implementations on the same deterministic mask surface.
/// </summary>
public static class SlideShowWindTransitionPlanner
{
    public const int DefaultBandCount = 8;
    public const double DefaultStaggerWindow = 0.24;
    public const double DefaultSkewFactor = 0.12;

    public static SlideShowWindTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var horizontal = transition.Direction is not (
            TransitionDirection.Up or
            TransitionDirection.Down or
            TransitionDirection.Vertical);
        var reverse = transition.Direction is
            TransitionDirection.Left or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown or
            TransitionDirection.Up;

        return new(
            horizontal,
            reverse,
            DefaultBandCount,
            DefaultStaggerWindow,
            DefaultSkewFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowWindTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var bands = Math.Max(1, plan.BandCount);
        var staggerWindow = Math.Clamp(plan.StaggerWindow, 0.01, 0.9);
        var skewFactor = Math.Max(0, plan.SkewFactor);
        var polygons = new List<SlideShowMaskPolygon>(bands);

        if (plan.HorizontalAxis)
        {
            var bandHeight = height / bands;
            for (var band = 0; band < bands; band++)
            {
                var normalized = (band + 0.5) / bands;
                var stagger = (plan.Reverse ? 1 - normalized : normalized) * staggerWindow;
                var local = Math.Clamp(
                    (progress - stagger) / (1 - staggerWindow),
                    0,
                    1);
                if (local <= 0)
                    continue;

                var eased = local * local * (3 - 2 * local);
                var edge = plan.Reverse ? width * (1 - eased) : width * eased;
                var skew = width * skewFactor * (1 - eased) * (normalized - 0.5);
                var topEdge = Math.Clamp(edge + skew, 0, width);
                var bottomEdge = Math.Clamp(edge - skew, 0, width);
                var y0 = band * bandHeight;
                var y1 = (band + 1) * bandHeight;

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
            var bandWidth = width / bands;
            for (var band = 0; band < bands; band++)
            {
                var normalized = (band + 0.5) / bands;
                var stagger = (plan.Reverse ? 1 - normalized : normalized) * staggerWindow;
                var local = Math.Clamp(
                    (progress - stagger) / (1 - staggerWindow),
                    0,
                    1);
                if (local <= 0)
                    continue;

                var eased = local * local * (3 - 2 * local);
                var edge = plan.Reverse ? height * (1 - eased) : height * eased;
                var skew = height * skewFactor * (1 - eased) * (normalized - 0.5);
                var leadingEdge = Math.Clamp(edge + skew, 0, height);
                var trailingEdge = Math.Clamp(edge - skew, 0, height);
                var x0 = band * bandWidth;
                var x1 = (band + 1) * bandWidth;

                polygons.Add(new(plan.Reverse
                    ? new[]
                    {
                        new SlideShowMaskPoint(x0, leadingEdge),
                        new SlideShowMaskPoint(x1, leadingEdge),
                        new SlideShowMaskPoint(x1, height),
                        new SlideShowMaskPoint(x0, height)
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

}
