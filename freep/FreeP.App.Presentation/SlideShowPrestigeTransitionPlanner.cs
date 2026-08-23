using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowPrestigeTransitionPlan(
    bool Reverse,
    double InitialRadiusFactor,
    double MaximumRadiusFactor,
    double DiagonalShiftFactor);

/// <summary>
/// Shared expanding-diamond geometry for the Prestige transition.
/// The aperture begins as a compact diagonal reveal and grows toward the
/// complete frame with a direction-sensitive offset.
/// </summary>
public static class SlideShowPrestigeTransitionPlanner
{
    public const double DefaultInitialRadiusFactor = 0.08;
    public const double DefaultMaximumRadiusFactor = 0.62;
    public const double DefaultDiagonalShiftFactor = 0.10;

    public static SlideShowPrestigeTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var reverse = transition.Direction is
            TransitionDirection.Left or
            TransitionDirection.Up or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown;
        return new(
            reverse,
            DefaultInitialRadiusFactor,
            DefaultMaximumRadiusFactor,
            DefaultDiagonalShiftFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowPrestigeTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var eased = SlideShowTransitionGeometry.SmoothStep(progress);
        var initial = Math.Clamp(plan.InitialRadiusFactor, 0.02, 0.45);
        var maximum = Math.Clamp(plan.MaximumRadiusFactor, initial, 1);
        var radiusFactor = Lerp(initial, maximum, eased);
        var shift = Math.Clamp(plan.DiagonalShiftFactor, 0, 0.35)
            * (1 - eased) * (plan.Reverse ? -1 : 1);
        var centerX = width * (0.5 + shift * 0.5);
        var centerY = height * (0.5 - shift * 0.5);
        var radiusX = width * radiusFactor;
        var radiusY = height * radiusFactor;

        return new[]
        {
            new SlideShowMaskPolygon(new[]
            {
                new SlideShowMaskPoint(centerX, Math.Clamp(centerY - radiusY, 0, height)),
                new SlideShowMaskPoint(Math.Clamp(centerX + radiusX, 0, width), centerY),
                new SlideShowMaskPoint(centerX, Math.Clamp(centerY + radiusY, 0, height)),
                new SlideShowMaskPoint(Math.Clamp(centerX - radiusX, 0, width), centerY)
            })
        };
    }

    private static double Lerp(double start, double end, double amount) =>
        start + (end - start) * amount;


}
