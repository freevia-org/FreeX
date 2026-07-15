using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>PowerPoint-compatible linear-light color interpolation for DrawingML gradients.</summary>
public static class GradientColorInterpolation
{
    /// <summary>Applies PowerPoint's eased progression between two DrawingML gradient stops.</summary>
    public static double EasePowerPointPosition(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        return fraction * fraction * (3 - 2 * fraction);
    }

    public static SrgbColor InterpolateLinearLight(SrgbColor start, SrgbColor end, double fraction) =>
        new(
            InterpolateChannel(start.R, end.R, fraction),
            InterpolateChannel(start.G, end.G, fraction),
            InterpolateChannel(start.B, end.B, fraction));

    private static byte InterpolateChannel(byte start, byte end, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        double startLinear = Math.Pow(start / 255.0, 2.2);
        double endLinear = Math.Pow(end / 255.0, 2.2);
        return (byte)Math.Round(Math.Pow(startLinear + (endLinear - startLinear) * fraction, 1.0 / 2.2) * 255.0);
    }
}
