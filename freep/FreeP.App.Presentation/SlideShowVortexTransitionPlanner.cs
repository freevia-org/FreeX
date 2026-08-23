using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowVortexTransitionPlan(
    bool Reverse,
    int SectorCount,
    double TurnCount,
    double CoreFactor);

/// <summary>
/// Shared radial-spiral mask geometry for the Vortex transition.
/// A growing center core is surrounded by rotated quadrilateral sectors so
/// both hosts preserve the same spiral reveal without a host-specific mesh.
/// </summary>
public static class SlideShowVortexTransitionPlanner
{
    public const int DefaultSectorCount = 24;
    public const double DefaultTurnCount = 1.1;
    public const double DefaultCoreFactor = 0.18;

    public static SlideShowVortexTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var reverse = transition.Direction is
            TransitionDirection.Left or
            TransitionDirection.Up or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown;
        return new(
            reverse,
            DefaultSectorCount,
            DefaultTurnCount,
            DefaultCoreFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowVortexTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var sectors = Math.Max(4, plan.SectorCount);
        var eased = progress * progress * (3 - 2 * progress);
        var centerX = width / 2;
        var centerY = height / 2;
        var maximumRadius = Math.Sqrt(width * width + height * height) * 0.72;
        var radius = maximumRadius * eased;
        var innerRadius = radius * Math.Clamp(plan.CoreFactor, 0.05, 0.4);
        var rotationSign = plan.Reverse ? -1 : 1;
        var rotation = rotationSign * plan.TurnCount * 2 * Math.PI * (1 - progress);
        var polygons = new List<SlideShowMaskPolygon>(sectors + 1)
        {
            new(BuildCore(centerX, centerY, Math.Max(
                Math.Min(width, height) * 0.05,
                innerRadius)))
        };

        for (var sector = 0; sector < sectors; sector++)
        {
            var start = 2 * Math.PI * sector / sectors;
            var end = 2 * Math.PI * (sector + 1) / sectors;
            var innerStart = start + rotation;
            var innerEnd = end + rotation;
            var outerStart = start + rotation + rotationSign * 0.22 * (1 - progress);
            var outerEnd = end + rotation + rotationSign * 0.22 * (1 - progress);
            polygons.Add(new(new[]
            {
                PointOnCircle(centerX, centerY, innerRadius, innerStart),
                PointOnCircle(centerX, centerY, radius, outerStart),
                PointOnCircle(centerX, centerY, radius, outerEnd),
                PointOnCircle(centerX, centerY, innerRadius, innerEnd)
            }));
        }

        return polygons;
    }

    private static IReadOnlyList<SlideShowMaskPoint> BuildCore(
        double centerX,
        double centerY,
        double radius) =>
        new[]
        {
            new SlideShowMaskPoint(centerX - radius, centerY - radius),
            new SlideShowMaskPoint(centerX + radius, centerY - radius),
            new SlideShowMaskPoint(centerX + radius, centerY + radius),
            new SlideShowMaskPoint(centerX - radius, centerY + radius)
        };

    private static SlideShowMaskPoint PointOnCircle(
        double centerX,
        double centerY,
        double radius,
        double angle) =>
        new(
            centerX + Math.Cos(angle) * radius,
            centerY + Math.Sin(angle) * radius);

}
