using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum GradientEndpointProfile
{
    CenteredDirection,
    AvaloniaTextCorners
}

public readonly record struct GradientRenderPoint(double X, double Y);

public readonly record struct GradientRenderStop(
    double Position,
    SrgbColor Color,
    byte Alpha);

public readonly record struct LinearGradientEndpointPlan(
    GradientRenderPoint Start,
    GradientRenderPoint End);

/// <summary>Expands DrawingML gradients into renderer-neutral stops and endpoints.</summary>
public static class GradientFillRenderPlanner
{
    public const int SegmentStepCount = 16;

    public static IReadOnlyList<GradientRenderStop> ExpandStops(
        ResolvedFill.Gradient gradient,
        bool easePositions = false)
    {
        ArgumentNullException.ThrowIfNull(gradient);

        var stops = new List<GradientRenderStop>(
            Math.Max(gradient.Stops.Count, 1) * SegmentStepCount);
        for (int index = 0; index < gradient.Stops.Count; index++)
        {
            var start = gradient.Stops[index];
            stops.Add(new GradientRenderStop(start.Position, start.Color, start.Alpha));
            if (index == gradient.Stops.Count - 1)
                continue;

            var end = gradient.Stops[index + 1];
            for (int step = 1; step < SegmentStepCount; step++)
            {
                double fraction = step / (double)SegmentStepCount;
                double colorFraction = easePositions
                    ? GradientColorInterpolation.EasePowerPointPosition(fraction)
                    : fraction;
                stops.Add(new GradientRenderStop(
                    start.Position + (end.Position - start.Position) * fraction,
                    GradientColorInterpolation.InterpolateLinearLight(
                        start.Color,
                        end.Color,
                        colorFraction),
                    (byte)Math.Round(start.Alpha + (end.Alpha - start.Alpha) * fraction)));
            }
        }

        return stops;
    }

    public static LinearGradientEndpointPlan PlanLinearEndpoints(
        double angleDegrees,
        GradientEndpointProfile profile = GradientEndpointProfile.CenteredDirection)
    {
        double angleRadians = angleDegrees * Math.PI / 180.0;
        double dx = Math.Cos(angleRadians);
        double dy = Math.Sin(angleRadians);

        if (profile == GradientEndpointProfile.AvaloniaTextCorners)
        {
            double startX = dx >= 0 ? 0 : 1;
            double startY = dy >= 0 ? 0 : 1;
            return new LinearGradientEndpointPlan(
                new GradientRenderPoint(startX, startY),
                new GradientRenderPoint(1 - startX, 1 - startY));
        }

        return new LinearGradientEndpointPlan(
            new GradientRenderPoint(0.5 - 0.5 * dx, 0.5 - 0.5 * dy),
            new GradientRenderPoint(0.5 + 0.5 * dx, 0.5 + 0.5 * dy));
    }
}
