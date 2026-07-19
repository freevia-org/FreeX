namespace FreeP.App.Compositor;

public readonly record struct PictureColorEffectPlan(
    bool Grayscale,
    double? BiLevelThreshold,
    double? Brightness,
    double? Contrast)
{
    public bool HasPixelEffects =>
        Grayscale ||
        BiLevelThreshold.HasValue ||
        Brightness.HasValue ||
        Contrast.HasValue;
}

public static class PictureColorEffectPlanner
{
    private const double RedLuminanceWeight = 0.2126;
    private const double GreenLuminanceWeight = 0.7152;
    private const double BlueLuminanceWeight = 0.0722;

    public static PictureColorEffectPlan Plan(DrawOp.Picture picture) => new(
        picture.Grayscale,
        picture.BiLevelThreshold,
        picture.Brightness,
        picture.Contrast);

    public static void ApplyToBgra32(Span<byte> pixels, PictureColorEffectPlan plan)
    {
        if (!plan.HasPixelEffects)
            return;

        bool doGray = plan.Grayscale;
        bool doBiLevel = plan.BiLevelThreshold.HasValue;
        double biThreshold = doBiLevel ? plan.BiLevelThreshold!.Value : 0;
        bool doLuminance = plan.Brightness.HasValue || plan.Contrast.HasValue;
        double brightness = plan.Brightness ?? 0;
        double contrast = plan.Contrast ?? 0;
        // DrawingML's combined lum transform keeps brightness additive, but the
        // contrast pass slightly scales that offset.  Applying the adjustment
        // after the centered contrast transform matches PowerPoint's raster path.
        double combinedBrightness = brightness * (1 + contrast / 2);

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            double b = pixels[i] / 255.0;
            double g = pixels[i + 1] / 255.0;
            double r = pixels[i + 2] / 255.0;

            if (doGray)
            {
                double lum = ComputeLuminance(r, g, b);
                r = g = b = lum;
            }

            if (doLuminance)
            {
                if (contrast > 0)
                {
                    double denominator = Math.Max(1.0 - contrast, 0.001);
                    r = (r - 0.5) / denominator + 0.5;
                    g = (g - 0.5) / denominator + 0.5;
                    b = (b - 0.5) / denominator + 0.5;
                }
                else if (contrast < 0)
                {
                    r = (r - 0.5) * (1 + contrast) + 0.5;
                    g = (g - 0.5) * (1 + contrast) + 0.5;
                    b = (b - 0.5) * (1 + contrast) + 0.5;
                }

                r = Math.Clamp(r + combinedBrightness, 0, 1);
                g = Math.Clamp(g + combinedBrightness, 0, 1);
                b = Math.Clamp(b + combinedBrightness, 0, 1);
            }

            if (doBiLevel)
            {
                double lum = ComputeLuminance(r, g, b);
                double bw = lum >= biThreshold ? 1.0 : 0.0;
                r = g = b = bw;
            }

            pixels[i] = ToByte(b);
            pixels[i + 1] = ToByte(g);
            pixels[i + 2] = ToByte(r);
        }
    }

    private static double ComputeLuminance(double r, double g, double b) =>
        RedLuminanceWeight * r + GreenLuminanceWeight * g + BlueLuminanceWeight * b;

    private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255);
}
