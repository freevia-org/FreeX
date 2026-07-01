using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public static class WordArtWarpPlanner
{
    private const double DefaultAmplitudeFraction = 0.35;

    public static double? ComputeYOffset(string? preset, double horizontalPosition, LayoutRect shapeBounds) =>
        ComputeYOffset(preset, horizontalPosition, shapeBounds.Height);

    public static double? ComputeYOffset(string? preset, double horizontalPosition, double shapeHeightDip)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return null;

        double t = horizontalPosition;
        double h = shapeHeightDip;
        return preset.ToLowerInvariant() switch
        {
            "textarchup" or "textcirclecurve" =>
                -h * DefaultAmplitudeFraction * 4 * t * (1 - t),
            "textarchdown" or "textarchdownpour" =>
                h * DefaultAmplitudeFraction * 4 * t * (1 - t),
            "textcircle" =>
                -h * DefaultAmplitudeFraction * Math.Sin(t * Math.PI),
            "textwaveup" or "textwave1" or "textwaves" =>
                -h * 0.15 * Math.Sin(t * 2 * Math.PI),
            "textwave2" =>
                -h * 0.10 * Math.Sin(t * 4 * Math.PI),
            "texttriangle" or "texttrianglepour" =>
                h * DefaultAmplitudeFraction * (0.5 - t),
            "textinversetriangle" =>
                -h * DefaultAmplitudeFraction * (0.5 - t),
            "textslantup" =>
                -h * 0.3 * t,
            "textslantdown" =>
                h * 0.3 * t,
            "textcantop" or "textcan" =>
                -h * DefaultAmplitudeFraction * Math.Sin(t * Math.PI),
            _ => null
        };
    }
}
