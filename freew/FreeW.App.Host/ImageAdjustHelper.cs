using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Non-destructive pixel-adjustment pipeline for Picture Format > Adjust (Corrections / Color / Transparency)
/// and Recolor (Grayscale, Sepia, Washout, Black&amp;White) + Color Tone (temperature).
/// Takes a decoded <see cref="BitmapSource"/> plus parameters from an <see cref="InlineImage"/> and returns
/// an adjusted, frozen <see cref="BitmapSource"/> without modifying the original bytes.
/// Returns the input source unchanged when all values are at neutral defaults.
///
/// Algorithm (applied in order):
///   0. Recolor — greyscale/sepia/washout/black-and-white.
///   1. Color temperature — warm (orange) or cool (blue) overlay.
///   2. Saturation — HSL-space chroma scaling (grey when 0, boosted when &gt;100).
///   3. Brightness — linear luminance shift in [0,1].
///   4. Contrast — scale around the mid-point (0.5).
///   5. Transparency — written into alpha channel.
///
/// The transform operates on a 32-bit Pbgra32 intermediate WriteableBitmap so it runs on any WPF thread.
/// </summary>
internal static class ImageAdjustHelper
{
    /// <summary>
    /// Apply the adjust/recolor parameters from <paramref name="image"/> to <paramref name="source"/> and
    /// return the adjusted bitmap. Returns <paramref name="source"/> unchanged when all fields are neutral.
    /// </summary>
    public static BitmapSource Apply(BitmapSource source, InlineImage image)
    {
        if (!image.HasAdjustments && !image.HasRecolor)
            return source;

        return ApplyCore(source,
            image.BrightnessPct,
            image.ContrastPct,
            image.SaturationPct,
            image.TransparencyPct,
            image.RecolorMode,
            image.ColorTemperature);
    }

    /// <summary>
    /// Core adjustment: operates on normalized params directly. Public for unit-testing without an
    /// <see cref="InlineImage"/> model object.
    /// brightnessPct in [-100, 100] (0=neutral), contrastPct in [-100,100] (0=neutral),
    /// saturationPct in [0, 400] (100=neutral), transparencyPct in [0, 100] (0=fully opaque),
    /// recolorMode = recolor preset, colorTemperature in [-100, 100] (0=neutral).
    /// </summary>
    public static BitmapSource ApplyCore(
        BitmapSource source,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct,
        ImageRecolorMode recolorMode = ImageRecolorMode.None,
        double colorTemperature = 0)
    {
        // Convert to Pbgra32 for per-pixel access.
        var src = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        int width  = src.PixelWidth;
        int height = src.PixelHeight;
        int stride = width * 4; // 4 bytes per pixel (B,G,R,A) in Pbgra32

        var pixels = new byte[stride * height];
        src.CopyPixels(pixels, stride, 0);

        // Pre-compute scalars.
        double brightShift   = brightnessPct  / 100.0; // -1..1
        double contrastScale = (100.0 + contrastPct) / 100.0; // 0..2 (contrast multiplier around 0.5)
        double satScale      = saturationPct / 100.0; // 0..4
        double opacity       = 1.0 - transparencyPct / 100.0; // 0..1
        double tempScale     = colorTemperature / 100.0; // -1..1

        bool doBC   = brightnessPct != 0 || contrastPct != 0;
        bool doSat  = saturationPct != 100;
        bool doA    = transparencyPct != 0;
        bool doTemp = colorTemperature != 0 && recolorMode == ImageRecolorMode.None;

        // Sepia palette targets (Rec.709-weighted grey → sepia tones).
        // dark brown (#7B4012) normalised: r≈0.482, g≈0.251, b≈0.071
        // near-white (#FDF0E0) normalised: r≈0.992, g≈0.941, b≈0.878
        const double sepiaR1 = 0.482, sepiaG1 = 0.251, sepiaB1 = 0.071;
        const double sepiaR2 = 0.992, sepiaG2 = 0.941, sepiaB2 = 0.878;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i    ] / 255.0;
            double g = pixels[i + 1] / 255.0;
            double r = pixels[i + 2] / 255.0;
            double a = pixels[i + 3] / 255.0;

            // 0. Recolor.
            switch (recolorMode)
            {
                case ImageRecolorMode.Grayscale:
                {
                    double grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = g = b = grey;
                    break;
                }
                case ImageRecolorMode.Sepia:
                {
                    // Linear interpolation between dark-brown and near-white based on luminance.
                    double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = Clamp(sepiaR1 + (sepiaR2 - sepiaR1) * lum);
                    g = Clamp(sepiaG1 + (sepiaG2 - sepiaG1) * lum);
                    b = Clamp(sepiaB1 + (sepiaB2 - sepiaB1) * lum);
                    break;
                }
                case ImageRecolorMode.Washout:
                {
                    // Brighten strongly + reduce alpha to 50%.
                    r = Clamp(r + 0.40 + brightShift);
                    g = Clamp(g + 0.40 + brightShift);
                    b = Clamp(b + 0.40 + brightShift);
                    // Combine washout opacity (50%) with any existing transparency.
                    double washOpacity = 0.5 * opacity;
                    if (a > 0)
                    {
                        double newA = Clamp(a * washOpacity);
                        double ratio = newA / a;
                        r *= ratio; g *= ratio; b *= ratio; a = newA;
                    }
                    break;
                }
                case ImageRecolorMode.BlackWhite:
                {
                    // Greyscale then high contrast: values go to 0 or 1 only (threshold at 0.5).
                    double grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = g = b = grey >= 0.5 ? 1.0 : 0.0;
                    break;
                }
            }

            // 1. Color temperature: shift red/blue channels (warm = +red/-blue; cool = +blue/-red).
            if (doTemp)
            {
                if (tempScale > 0) // warm
                {
                    r = Clamp(r + tempScale * 0.15);
                    b = Clamp(b - tempScale * 0.10);
                }
                else // cool
                {
                    b = Clamp(b - tempScale * 0.15); // tempScale is negative, so this adds blue
                    r = Clamp(r + tempScale * 0.10); // subtracts red
                }
            }

            // 2. Saturation (HSL-space chroma scaling via grey desaturate/mix) — skip when recolor done.
            if (doSat && recolorMode == ImageRecolorMode.None)
            {
                // Perceived luminance (Rec.709 coefficients).
                double grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = Clamp(grey + (r - grey) * satScale);
                g = Clamp(grey + (g - grey) * satScale);
                b = Clamp(grey + (b - grey) * satScale);
            }

            // 3+4. Brightness (shift) then Contrast (scale around mid-point) — skip for Washout/BlackWhite.
            if (doBC && recolorMode is not (ImageRecolorMode.Washout or ImageRecolorMode.BlackWhite))
            {
                r = Clamp(r + brightShift);
                g = Clamp(g + brightShift);
                b = Clamp(b + brightShift);

                r = Clamp((r - 0.5) * contrastScale + 0.5);
                g = Clamp((g - 0.5) * contrastScale + 0.5);
                b = Clamp((b - 0.5) * contrastScale + 0.5);
            }

            // 5. Transparency: scale alpha (skip when Washout already baked it above).
            if (doA && recolorMode != ImageRecolorMode.Washout && a > 0)
            {
                double newA = Clamp(a * opacity);
                double ratio = newA / a; // = opacity (when original alpha > 0)
                r *= ratio;
                g *= ratio;
                b *= ratio;
                a  = newA;
            }

            pixels[i    ] = (byte)(b * 255 + 0.5);
            pixels[i + 1] = (byte)(g * 255 + 0.5);
            pixels[i + 2] = (byte)(r * 255 + 0.5);
            pixels[i + 3] = (byte)(a * 255 + 0.5);
        }

        var wb = new WriteableBitmap(width, height, src.DpiX, src.DpiY, PixelFormats.Pbgra32, null);
        wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
