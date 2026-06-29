namespace FreeP.Core.Model;

/// <summary>
/// Applies DrawingML theme color transforms to resolved sRGB colors.
/// </summary>
public static class ThemeColorTransform
{
    /// <summary>
    /// Applies the DrawingML transform order used by scheme colors: lumMod/lumOff, then tint, then shade.
    /// </summary>
    public static SrgbColor Apply(
        SrgbColor baseColor,
        double lumMod = 1.0,
        double lumOff = 0.0,
        double tint = 1.0,
        double shade = 1.0)
    {
        var resolved = ApplyLuminance(baseColor, lumMod, lumOff);
        resolved = ApplyTint(resolved, tint);
        return ApplyShade(resolved, shade);
    }

    /// <summary>
    /// Applies lumMod/lumOff by converting to HLS, computing L' = clamp(L * lumMod + lumOff), and converting back.
    /// </summary>
    public static SrgbColor ApplyLuminance(SrgbColor baseColor, double lumMod, double lumOff)
    {
        if (lumMod == 1.0 && lumOff == 0.0)
            return baseColor;

        RgbToHls(baseColor, out var h, out var l, out var s);
        l = Math.Clamp(l * lumMod + lumOff, 0.0, 1.0);
        return HlsToRgb(h, l, s);
    }

    /// <summary>
    /// Applies DrawingML tint, where 1.0 preserves the color and 0.0 blends fully to white.
    /// </summary>
    public static SrgbColor ApplyTint(SrgbColor baseColor, double tintFraction)
    {
        if (tintFraction >= 1.0) return baseColor;
        if (tintFraction <= 0.0) return SrgbColor.White;

        double r = baseColor.R * tintFraction + 255.0 * (1.0 - tintFraction);
        double g = baseColor.G * tintFraction + 255.0 * (1.0 - tintFraction);
        double b = baseColor.B * tintFraction + 255.0 * (1.0 - tintFraction);
        return new SrgbColor(
            (byte)Math.Clamp(Math.Round(r), 0, 255),
            (byte)Math.Clamp(Math.Round(g), 0, 255),
            (byte)Math.Clamp(Math.Round(b), 0, 255));
    }

    /// <summary>
    /// Applies DrawingML shade, where 1.0 preserves the color and 0.0 blends fully to black.
    /// </summary>
    public static SrgbColor ApplyShade(SrgbColor baseColor, double shadeFraction)
    {
        if (shadeFraction >= 1.0) return baseColor;
        if (shadeFraction <= 0.0) return SrgbColor.Black;

        double r = baseColor.R * shadeFraction;
        double g = baseColor.G * shadeFraction;
        double b = baseColor.B * shadeFraction;
        return new SrgbColor(
            (byte)Math.Clamp(Math.Round(r), 0, 255),
            (byte)Math.Clamp(Math.Round(g), 0, 255),
            (byte)Math.Clamp(Math.Round(b), 0, 255));
    }

    private static void RgbToHls(SrgbColor color, out double h, out double l, out double s)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta < 1e-10)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

        if (max == r)
            h = ((g - b) / delta % 6.0) / 6.0;
        else if (max == g)
            h = ((b - r) / delta + 2.0) / 6.0;
        else
            h = ((r - g) / delta + 4.0) / 6.0;

        if (h < 0)
            h += 1.0;
    }

    private static SrgbColor HlsToRgb(double h, double l, double s)
    {
        if (s < 1e-10)
        {
            var v = (byte)Math.Clamp(Math.Round(l * 255), 0, 255);
            return new SrgbColor(v, v, v);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;

        return new SrgbColor(
            HueToRgb(p, q, h + 1.0 / 3.0),
            HueToRgb(p, q, h),
            HueToRgb(p, q, h - 1.0 / 3.0));
    }

    private static byte HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;

        double value = t < 1.0 / 6.0 ? p + (q - p) * 6.0 * t
            : t < 1.0 / 2.0 ? q
            : t < 2.0 / 3.0 ? p + (q - p) * (2.0 / 3.0 - t) * 6.0
            : p;

        return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }
}
