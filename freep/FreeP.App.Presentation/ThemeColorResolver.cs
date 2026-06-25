using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves a <see cref="ThemeAwareColor"/> to a concrete <see cref="SrgbColor"/>.
/// When a <see cref="SchemeColorRef"/> is present, the color is looked up from the live theme
/// and luminance modifiers (lumMod / lumOff) are applied using HLS arithmetic.
/// When the scheme ref is absent the pre-resolved <see cref="ThemeAwareColor.Resolved"/> is used.
/// </summary>
public static class ThemeColorResolver
{
    /// <summary>
    /// Resolves <paramref name="color"/> against <paramref name="theme"/>.
    /// If <paramref name="theme"/> is null the pre-resolved value is returned as-is.
    /// </summary>
    public static SrgbColor Resolve(ThemeAwareColor color, PresentationTheme? theme)
    {
        if (color.SchemeColor is { } schemeRef && theme is not null)
        {
            var baseColor = theme.ColorScheme[schemeRef.Slot];
            return ApplyLumModOff(baseColor, schemeRef.LumMod, schemeRef.LumOff);
        }
        return color.Resolved;
    }

    // â”€â”€â”€ HLS luminance adjustments â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Applies lumMod and lumOff to an sRGB color by converting to HLS, computing
    ///   L' = clamp(L * lumMod + lumOff, 0, 1)
    /// and converting back.
    /// Both lumMod and lumOff are in [0, 1] (already normalized from OOXML's 100 000 scale).
    /// </summary>
    private static SrgbColor ApplyLumModOff(SrgbColor rgb, double lumMod, double lumOff)
    {
        // Fast path: identity modifiers.
        if (lumMod is 1.0 && lumOff is 0.0)
            return rgb;

        RgbToHls(rgb, out double h, out double l, out double s);

        l = Math.Clamp(l * lumMod + lumOff, 0.0, 1.0);

        return HlsToRgb(h, l, s);
    }

    /// <summary>Converts sRGB [0,255] to HLS [0,1].</summary>
    private static void RgbToHls(SrgbColor rgb, out double h, out double l, out double s)
    {
        double r = rgb.R / 255.0;
        double g = rgb.G / 255.0;
        double b = rgb.B / 255.0;

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
            h = ((g - b) / delta + (g < b ? 6.0 : 0.0)) / 6.0;
        else if (max == g)
            h = ((b - r) / delta + 2.0) / 6.0;
        else
            h = ((r - g) / delta + 4.0) / 6.0;
    }

    /// <summary>Converts HLS [0,1] to sRGB [0,255].</summary>
    private static SrgbColor HlsToRgb(double h, double l, double s)
    {
        if (s < 1e-10)
        {
            byte v = (byte)Math.Round(l * 255);
            return new SrgbColor(v, v, v);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;

        return new SrgbColor(
            (byte)Math.Round(Hue(p, q, h + 1.0 / 3.0) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255),
            (byte)Math.Round(Hue(p, q, h - 1.0 / 3.0) * 255));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1.0;
        if (t > 1) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }
}

