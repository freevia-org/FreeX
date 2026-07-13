namespace Free.Shared.Pdf;

internal static class PdfImageColorEffectPixels
{
    private const double RedLuminanceWeight = 0.2126;
    private const double GreenLuminanceWeight = 0.7152;
    private const double BlueLuminanceWeight = 0.0722;

    public static void ApplyToRgb24(Span<byte> pixels, PdfImageColorEffects effects)
    {
        if (!effects.HasPixelEffects)
            return;

        for (var i = 0; i + 2 < pixels.Length; i += 3)
        {
            var (r, g, b) = TransformRgb(pixels[i], pixels[i + 1], pixels[i + 2], effects);
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
        }
    }

    public static void ApplyToGray8(Span<byte> pixels, PdfImageColorEffects effects)
    {
        if (!effects.HasPixelEffects)
            return;

        for (var i = 0; i < pixels.Length; i++)
        {
            var (r, _, _) = TransformRgb(pixels[i], pixels[i], pixels[i], effects);
            pixels[i] = r;
        }
    }

    public static void ApplyToBgra32(Span<byte> pixels, PdfImageColorEffects effects)
    {
        if (!effects.HasPixelEffects)
            return;

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var (r, g, b) = TransformRgb(pixels[i + 2], pixels[i + 1], pixels[i], effects);
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
        }
    }

    public static (byte R, byte G, byte B) TransformRgb(
        byte red,
        byte green,
        byte blue,
        PdfImageColorEffects effects)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;

        if (effects.Grayscale)
        {
            var lum = ComputeLuminance(r, g, b);
            r = g = b = lum;
        }

        if (effects.Brightness.HasValue || effects.Contrast.HasValue)
        {
            var brightness = NormalizeUnit(effects.Brightness ?? 0);
            var contrast = NormalizeUnit(effects.Contrast ?? 0);

            r = Math.Clamp(r + brightness, 0, 1);
            g = Math.Clamp(g + brightness, 0, 1);
            b = Math.Clamp(b + brightness, 0, 1);

            if (contrast > 0)
            {
                var denominator = Math.Max(1.0 - contrast, 0.001);
                r = Math.Clamp((r - 0.5) / denominator + 0.5, 0, 1);
                g = Math.Clamp((g - 0.5) / denominator + 0.5, 0, 1);
                b = Math.Clamp((b - 0.5) / denominator + 0.5, 0, 1);
            }
            else if (contrast < 0)
            {
                r = Math.Clamp((r - 0.5) * (1 + contrast) + 0.5, 0, 1);
                g = Math.Clamp((g - 0.5) * (1 + contrast) + 0.5, 0, 1);
                b = Math.Clamp((b - 0.5) * (1 + contrast) + 0.5, 0, 1);
            }
        }

        if (effects.BiLevelThreshold.HasValue)
        {
            var threshold = Math.Clamp(
                double.IsFinite(effects.BiLevelThreshold.Value) ? effects.BiLevelThreshold.Value : 0,
                0,
                1);
            var lum = ComputeLuminance(r, g, b);
            var bw = lum >= threshold ? 1.0 : 0.0;
            r = g = b = bw;
        }

        return (ToByte(r), ToByte(g), ToByte(b));
    }

    private static double ComputeLuminance(double r, double g, double b) =>
        RedLuminanceWeight * r + GreenLuminanceWeight * g + BlueLuminanceWeight * b;

    private static double NormalizeUnit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;

    private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255);
}
