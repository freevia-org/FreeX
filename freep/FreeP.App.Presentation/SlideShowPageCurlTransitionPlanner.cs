using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowPageCurlTransitionPlan(
    bool HorizontalAxis,
    bool CurlFromEnd,
    bool DoubleFold,
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

        return new(
            horizontal,
            curlFromEnd,
            transition.Kind is TransitionKind.PageCurlDouble or TransitionKind.Origami,
            DefaultFoldDepthFactor);
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
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var remaining = 1 - progress;
        if (plan.DoubleFold)
            return BuildDoubleFoldPolygons(width, height, remaining, plan);

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

    private static IReadOnlyList<SlideShowMaskPolygon> BuildDoubleFoldPolygons(
        double width,
        double height,
        double remaining,
        SlideShowPageCurlTransitionPlan plan)
    {
        var depth = Math.Min(width, height) * plan.FoldDepthFactor * remaining;
        if (plan.HorizontalAxis)
        {
            var leftEdge = width * 0.5 * remaining;
            var rightEdge = width - leftEdge;
            var centerY = height / 2;
            return new[]
            {
                new SlideShowMaskPolygon(new[]
                {
                    new SlideShowMaskPoint(0, 0),
                    new SlideShowMaskPoint(leftEdge, 0),
                    new SlideShowMaskPoint(Math.Min(width, leftEdge + depth), centerY),
                    new SlideShowMaskPoint(leftEdge, height),
                    new SlideShowMaskPoint(0, height)
                }),
                new SlideShowMaskPolygon(new[]
                {
                    new SlideShowMaskPoint(rightEdge, 0),
                    new SlideShowMaskPoint(width, 0),
                    new SlideShowMaskPoint(width, height),
                    new SlideShowMaskPoint(rightEdge, height),
                    new SlideShowMaskPoint(Math.Max(0, rightEdge - depth), centerY)
                })
            };
        }

        var topEdge = height * 0.5 * remaining;
        var bottomEdge = height - topEdge;
        var centerX = width / 2;
        return new[]
        {
            new SlideShowMaskPolygon(new[]
            {
                new SlideShowMaskPoint(0, 0),
                new SlideShowMaskPoint(width, 0),
                new SlideShowMaskPoint(width, Math.Min(height, topEdge + depth)),
                new SlideShowMaskPoint(centerX, topEdge),
                new SlideShowMaskPoint(0, topEdge)
            }),
            new SlideShowMaskPolygon(new[]
            {
                new SlideShowMaskPoint(0, bottomEdge),
                new SlideShowMaskPoint(centerX, Math.Max(0, bottomEdge - depth)),
                new SlideShowMaskPoint(width, bottomEdge),
                new SlideShowMaskPoint(width, height),
                new SlideShowMaskPoint(0, height)
            })
        };
    }

}
