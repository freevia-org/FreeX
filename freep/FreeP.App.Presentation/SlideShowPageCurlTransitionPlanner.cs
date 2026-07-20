using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowPageCurlTransitionPlan(
    bool HorizontalAxis,
    bool CurlFromEnd,
    double FoldDepthFactor);

/// <summary>
/// Shared folded-page clip geometry for the single-page curl transition.
/// </summary>
public static class SlideShowPageCurlTransitionPlanner
{
    public const double DefaultFoldDepthFactor = 0.30;

    public static SlideShowPageCurlTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var horizontal = transition.Direction is not (
            TransitionDirection.Up or
            TransitionDirection.Down or
            TransitionDirection.Vertical);
        var curlFromEnd = transition.Direction is
            TransitionDirection.Right or
            TransitionDirection.Down or
            TransitionDirection.RightUp or
            TransitionDirection.RightDown;

        return new(horizontal, curlFromEnd, DefaultFoldDepthFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowPageCurlTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress >= 1)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress <= 0)
            return new[] { new SlideShowMaskPolygon(BuildRectangle(width, height)) };

        var remaining = 1 - progress;
        if (plan.HorizontalAxis)
        {
            var edge = plan.CurlFromEnd ? width * remaining : width * progress;
            var depth = Math.Min(width, height) * plan.FoldDepthFactor * remaining;
            var centerY = height / 2;
            var points = plan.CurlFromEnd
                ? new[]
                {
                    new SlideShowMaskPoint(0, 0),
                    new SlideShowMaskPoint(edge, 0),
                    new SlideShowMaskPoint(Math.Min(width, edge + depth), centerY),
                    new SlideShowMaskPoint(edge, height),
                    new SlideShowMaskPoint(0, height)
                }
                : new[]
                {
                    new SlideShowMaskPoint(edge, 0),
                    new SlideShowMaskPoint(width, 0),
                    new SlideShowMaskPoint(width, height),
                    new SlideShowMaskPoint(edge, height),
                    new SlideShowMaskPoint(Math.Max(0, edge - depth), centerY)
                };
            return new[] { new SlideShowMaskPolygon(points) };
        }

        var edgeY = plan.CurlFromEnd ? height * remaining : height * progress;
        var verticalDepth = Math.Min(width, height) * plan.FoldDepthFactor * remaining;
        var centerX = width / 2;
        var verticalPoints = plan.CurlFromEnd
            ? new[]
            {
                new SlideShowMaskPoint(0, 0),
                new SlideShowMaskPoint(width, 0),
                new SlideShowMaskPoint(width, edgeY),
                new SlideShowMaskPoint(centerX, Math.Min(height, edgeY + verticalDepth)),
                new SlideShowMaskPoint(0, edgeY)
            }
            : new[]
            {
                new SlideShowMaskPoint(0, edgeY),
                new SlideShowMaskPoint(centerX, Math.Max(0, edgeY - verticalDepth)),
                new SlideShowMaskPoint(width, edgeY),
                new SlideShowMaskPoint(width, height),
                new SlideShowMaskPoint(0, height)
            };
        return new[] { new SlideShowMaskPolygon(verticalPoints) };
    }

    private static IReadOnlyList<SlideShowMaskPoint> BuildRectangle(double width, double height) =>
        new[]
        {
            new SlideShowMaskPoint(0, 0),
            new SlideShowMaskPoint(width, 0),
            new SlideShowMaskPoint(width, height),
            new SlideShowMaskPoint(0, height)
        };
}
