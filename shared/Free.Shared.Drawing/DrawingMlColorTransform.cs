namespace Free.Shared.Drawing;

public static class DrawingMlColorTransform
{
    public static DrawingMlRgbColor Apply(
        DrawingMlRgbColor baseColor,
        double lumMod = 1.0,
        double lumOff = 0.0,
        double tint = 1.0,
        double shade = 1.0,
        double satMod = 1.0,
        double hueMod = 1.0)
    {
        var resolved = ApplyLuminance(baseColor, lumMod, lumOff);
        resolved = ApplySaturation(resolved, satMod);
        resolved = ApplyHue(resolved, hueMod);
        resolved = ApplyTint(resolved, tint);
        return ApplyShade(resolved, shade);
    }

    public static DrawingMlRgbColor ApplyLuminance(DrawingMlRgbColor baseColor, double lumMod, double lumOff)
    {
        if (lumMod == 1.0 && lumOff == 0.0)
            return baseColor;

        RgbToHls(baseColor, out var h, out var l, out var s);
        l = Math.Clamp(l * lumMod + lumOff, 0.0, 1.0);
        return HlsToRgb(h, l, s);
    }

    /// <summary>
    /// Applies the DrawingML &lt;a:satMod&gt; scheme-color modifier: the HSL saturation channel is
    /// multiplied by <paramref name="satModFraction"/> and clamped to [0,1] (ECMA-376 §20.1.2.3.32;
    /// ordinal formula mirrors the reference OOXML consumer's saturation-modulate transform:
    /// s' = clamp(s * satMod, 0, 1)). A value of 1.0 (100%) is a no-op.
    /// </summary>
    public static DrawingMlRgbColor ApplySaturation(DrawingMlRgbColor baseColor, double satModFraction)
    {
        if (satModFraction == 1.0)
            return baseColor;

        RgbToHls(baseColor, out var h, out var l, out var s);
        s = Math.Clamp(s * satModFraction, 0.0, 1.0);
        return HlsToRgb(h, l, s);
    }

    /// <summary>
    /// Applies the DrawingML &lt;a:hueMod&gt; scheme-color modifier: the normalized hue channel
    /// (fraction of 360 degrees) is multiplied by <paramref name="hueModFraction"/> and clamped to
    /// [0,1] (ECMA-376 §20.1.2.3.25; mirrors the reference OOXML consumer's hue-modulate transform:
    /// h' = clamp(h * hueMod, 0, 1), clamped rather than wrapped). A value of 1.0 (100%) is a no-op.
    /// </summary>
    public static DrawingMlRgbColor ApplyHue(DrawingMlRgbColor baseColor, double hueModFraction)
    {
        if (hueModFraction == 1.0)
            return baseColor;

        RgbToHls(baseColor, out var h, out var l, out var s);
        h = Math.Clamp(h * hueModFraction, 0.0, 1.0);
        return HlsToRgb(h, l, s);
    }

    public static DrawingMlRgbColor ApplyTint(DrawingMlRgbColor baseColor, double tintFraction)
    {
        if (tintFraction >= 1.0)
            return baseColor;
        if (tintFraction <= 0.0)
            return DrawingMlRgbColor.White;

        var r = baseColor.R * tintFraction + 255.0 * (1.0 - tintFraction);
        var g = baseColor.G * tintFraction + 255.0 * (1.0 - tintFraction);
        var b = baseColor.B * tintFraction + 255.0 * (1.0 - tintFraction);
        return new DrawingMlRgbColor(ClampByte(r), ClampByte(g), ClampByte(b));
    }

    public static DrawingMlRgbColor ApplyShade(DrawingMlRgbColor baseColor, double shadeFraction)
    {
        if (shadeFraction >= 1.0)
            return baseColor;
        if (shadeFraction <= 0.0)
            return DrawingMlRgbColor.Black;

        return new DrawingMlRgbColor(
            ClampByte(baseColor.R * shadeFraction),
            ClampByte(baseColor.G * shadeFraction),
            ClampByte(baseColor.B * shadeFraction));
    }

    private static void RgbToHls(DrawingMlRgbColor color, out double h, out double l, out double s)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

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

    private static DrawingMlRgbColor HlsToRgb(double h, double l, double s)
    {
        if (s < 1e-10)
        {
            var v = ClampByte(l * 255);
            return new DrawingMlRgbColor(v, v, v);
        }

        var q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        var p = 2.0 * l - q;

        return new DrawingMlRgbColor(
            HueToRgb(p, q, h + 1.0 / 3.0),
            HueToRgb(p, q, h),
            HueToRgb(p, q, h - 1.0 / 3.0));
    }

    private static byte HueToRgb(double p, double q, double t)
    {
        if (t < 0)
            t += 1;
        if (t > 1)
            t -= 1;

        var value = t < 1.0 / 6.0 ? p + (q - p) * 6.0 * t
            : t < 1.0 / 2.0 ? q
            : t < 2.0 / 3.0 ? p + (q - p) * (2.0 / 3.0 - t) * 6.0
            : p;

        return ClampByte(value * 255);
    }

    private static byte ClampByte(double value) =>
        (byte)Math.Clamp(Math.Round(value), 0, 255);
}
