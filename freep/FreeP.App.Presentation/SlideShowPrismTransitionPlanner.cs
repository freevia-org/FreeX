using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowPrismTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int FacetCount,
    double RevealWindow,
    double TiltFactor);

/// <summary>
/// Shared three-facet geometry for the Prism transition.
/// The center facet leads, side facets follow, and each partial facet has a
/// small angled edge before settling into the complete frame.
/// </summary>
public static class SlideShowPrismTransitionPlanner
{
    public const int DefaultFacetCount = 3;
    public const double DefaultRevealWindow = 0.62;
    public const double DefaultTiltFactor = 0.12;

    public static SlideShowPrismTransitionPlan Plan(SlideTransition transition)
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
            DefaultFacetCount,
            DefaultRevealWindow,
            DefaultTiltFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowPrismTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var facets = Math.Max(3, plan.FacetCount);
        var revealWindow = Math.Clamp(plan.RevealWindow, 0.20, 0.95);
        var tiltFactor = Math.Clamp(plan.TiltFactor, 0, 0.40);
        var polygons = new List<SlideShowMaskPolygon>(facets);

        if (plan.HorizontalAxis)
        {
            var facetWidth = width / facets;
            for (var facet = 0; facet < facets; facet++)
            {
                var centerDistance = Math.Abs(facet - (facets - 1) * 0.5)
                    / Math.Max(1, (facets - 1) * 0.5);
                var order = plan.Reverse ? 1 - centerDistance : centerDistance;
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - order * (1 - revealWindow)) / revealWindow,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var x0 = facet * facetWidth;
                var x1 = (facet + 1) * facetWidth;
                var tilt = (facet - (facets - 1) * 0.5)
                    * height * tiltFactor * (1 - local)
                    * (plan.Reverse ? -1 : 1);
                polygons.Add(new(new[]
                {
                    new SlideShowMaskPoint(Math.Clamp(x0, 0, width), 0),
                    new SlideShowMaskPoint(Math.Clamp(x1, 0, width), 0),
                    new SlideShowMaskPoint(Math.Clamp(x1 + tilt, 0, width), height),
                    new SlideShowMaskPoint(Math.Clamp(x0 + tilt, 0, width), height)
                }));
            }
        }
        else
        {
            var facetHeight = height / facets;
            for (var facet = 0; facet < facets; facet++)
            {
                var centerDistance = Math.Abs(facet - (facets - 1) * 0.5)
                    / Math.Max(1, (facets - 1) * 0.5);
                var order = plan.Reverse ? 1 - centerDistance : centerDistance;
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp(
                    (progress - order * (1 - revealWindow)) / revealWindow,
                    0,
                    1));
                if (local <= 0)
                    continue;

                var y0 = facet * facetHeight;
                var y1 = (facet + 1) * facetHeight;
                var tilt = (facet - (facets - 1) * 0.5)
                    * width * tiltFactor * (1 - local)
                    * (plan.Reverse ? -1 : 1);
                polygons.Add(new(new[]
                {
                    new SlideShowMaskPoint(0, Math.Clamp(y0, 0, height)),
                    new SlideShowMaskPoint(width, Math.Clamp(y0 + tilt, 0, height)),
                    new SlideShowMaskPoint(width, Math.Clamp(y1 + tilt, 0, height)),
                    new SlideShowMaskPoint(0, Math.Clamp(y1, 0, height))
                }));
            }
        }

        return polygons;
    }


}
