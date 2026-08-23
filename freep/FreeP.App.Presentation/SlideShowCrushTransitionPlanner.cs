using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowCrushTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    double InitialLongitudinalFactor,
    double InitialCrossFactor,
    double InitialOffsetFactor);

/// <summary>
/// Shared center-compressing aperture geometry for the Crush transition.
/// The incoming slide starts as a narrow, slightly offset panel and expands
/// anisotropically toward the complete frame in both slideshow hosts.
/// </summary>
public static class SlideShowCrushTransitionPlanner
{
    public const double DefaultInitialLongitudinalFactor = 0.08;
    public const double DefaultInitialCrossFactor = 0.18;
    public const double DefaultInitialOffsetFactor = 0.10;

    public static SlideShowCrushTransitionPlan Plan(SlideTransition transition)
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
            DefaultInitialLongitudinalFactor,
            DefaultInitialCrossFactor,
            DefaultInitialOffsetFactor);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowCrushTransitionPlan plan)
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
        var longitudinal = Lerp(
            Math.Clamp(plan.InitialLongitudinalFactor, 0.02, 1),
            1,
            eased);
        var cross = Lerp(
            Math.Clamp(plan.InitialCrossFactor, 0.02, 1),
            1,
            eased);
        var offset = Math.Clamp(plan.InitialOffsetFactor, 0, 0.35)
            * (1 - eased) * (plan.Reverse ? 1 : -1);

        var panelWidth = plan.HorizontalAxis ? width * longitudinal : width * cross;
        var panelHeight = plan.HorizontalAxis ? height * cross : height * longitudinal;
        var centerX = 0.5 * width;
        var centerY = 0.5 * height;
        if (plan.HorizontalAxis)
            centerX += offset * width;
        else
            centerY += offset * height;

        var x0 = Math.Clamp(centerX - panelWidth * 0.5, 0, width);
        var y0 = Math.Clamp(centerY - panelHeight * 0.5, 0, height);
        var x1 = Math.Clamp(centerX + panelWidth * 0.5, 0, width);
        var y1 = Math.Clamp(centerY + panelHeight * 0.5, 0, height);
        return new[]
        {
            new SlideShowMaskPolygon(new[]
            {
                new SlideShowMaskPoint(x0, y0),
                new SlideShowMaskPoint(x1, y0),
                new SlideShowMaskPoint(x1, y1),
                new SlideShowMaskPoint(x0, y1)
            })
        };
    }

    private static double Lerp(double start, double end, double amount) =>
        start + (end - start) * amount;


}
