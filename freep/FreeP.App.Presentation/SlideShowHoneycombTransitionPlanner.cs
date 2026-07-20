using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowHoneycombTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int ColumnCount,
    double RevealWindow,
    double RadiusFactor);

/// <summary>
/// Shared deterministic hex-cell reveal geometry for the Honeycomb transition.
/// Hosts turn the returned polygons into their native clip geometry.
/// </summary>
public static class SlideShowHoneycombTransitionPlanner
{
    public const int DefaultColumnCount = 10;
    public const double DefaultRevealWindow = 0.24;
    public const double DefaultRadiusFactor = 0.98;

    public static SlideShowHoneycombTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var horizontal = transition.Direction is not (
            TransitionDirection.Up or
            TransitionDirection.Down or
            TransitionDirection.Vertical);
        var reverse = transition.Direction is
            TransitionDirection.Right or
            TransitionDirection.Down or
            TransitionDirection.RightUp or
            TransitionDirection.RightDown;

        return new(
            horizontal,
            reverse,
            DefaultColumnCount,
            DefaultRevealWindow,
            DefaultRadiusFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowHoneycombTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();

        var columns = Math.Max(1, plan.ColumnCount);
        var radius = width / (Math.Sqrt(3) * (columns + 0.5));
        var cellWidth = Math.Sqrt(3) * radius;
        var verticalStep = radius * 1.5;
        var rows = Math.Max(1, (int)Math.Ceiling(height / verticalStep) + 1);
        var polygons = new List<SlideShowMaskPolygon>(columns * rows);

        for (var column = -1; column <= columns; column++)
        {
            var centerX = cellWidth / 2 + column * cellWidth;
            var columnOffset = (column & 1) == 0 ? 0 : radius * 0.75;
            for (var row = -1; row <= rows; row++)
            {
                var centerY = radius + row * verticalStep + columnOffset;
                var normalizedX = width <= 0 ? 0.5 : Math.Clamp(centerX / width, 0, 1);
                var normalizedY = height <= 0 ? 0.5 : Math.Clamp(centerY / height, 0, 1);
                var order = plan.HorizontalAxis
                    ? normalizedX * 0.78 + normalizedY * 0.22
                    : normalizedY * 0.78 + normalizedX * 0.22;
                if (plan.Reverse)
                    order = 1 - order;

                var localProgress = Math.Clamp(
                    (progress - order) / Math.Max(0.01, plan.RevealWindow),
                    0,
                    1);
                if (localProgress <= 0)
                    continue;

                // Growing the cell slightly avoids a hard all-at-once grid while
                // retaining the recognizable honeycomb silhouette at full open.
                var eased = localProgress * localProgress * (3 - 2 * localProgress);
                var localRadius = radius * (0.08 + 0.92 * eased) * plan.RadiusFactor;
                polygons.Add(new(BuildHexagon(centerX, centerY, localRadius)));
            }
        }

        return polygons;
    }

    private static IReadOnlyList<SlideShowMaskPoint> BuildHexagon(
        double centerX,
        double centerY,
        double radius)
    {
        var points = new SlideShowMaskPoint[6];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = (-90 + index * 60) * Math.PI / 180;
            points[index] = new(
                centerX + radius * Math.Cos(angle),
                centerY + radius * Math.Sin(angle));
        }

        return points;
    }
}
