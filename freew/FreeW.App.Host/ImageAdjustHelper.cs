using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Non-destructive pixel-adjustment pipeline for Picture Format > Adjust (Corrections / Color / Transparency).
/// Takes a decoded <see cref="BitmapSource"/> plus the four adjustment parameters from an
/// <see cref="InlineImage"/> and returns an adjusted, frozen <see cref="BitmapSource"/> without modifying
/// the original bytes. If all adjustments are at neutral the input source is returned as-is.
///
/// Algorithm (applied in order):
///   1. Saturation — HSL-space chroma scaling (grey when 0, boosted when &gt;100).
///   2. Brightness — linear luminance shift in [0,1].
///   3. Contrast — scale around the mid-point (0.5).
///   4. Transparency — written into alpha channel (Opacity on the WPF element handles this, but we also
///      bake it so the returned BitmapSource carries the correct alpha for callers that need a bitmap).
///
/// The transform operates on a 32-bit Pbgra32 intermediate WriteableBitmap so it runs on any WPF thread.
/// </summary>
internal static class ImageAdjustHelper
{
    /// <summary>
    /// Apply the adjust parameters from <paramref name="image"/> to <paramref name="source"/> and return
    /// the adjusted bitmap. Returns <paramref name="source"/> unchanged when <see cref="InlineImage.HasAdjustments"/>
    /// is false (neutral values).
    /// </summary>
    public static BitmapSource Apply(BitmapSource source, InlineImage image)
    {
        if (!image.HasAdjustments)
            return source;

        return ApplyCore(source,
            image.BrightnessPct,
            image.ContrastPct,
            image.SaturationPct,
            image.TransparencyPct);
    }

    /// <summary>
    /// Core adjustment: operates on normalized params directly. Public for unit-testing without an
    /// <see cref="InlineImage"/> model object.
    /// brightnessPct in [-100, 100] (0=neutral), contrastPct in [-100,100] (0=neutral),
    /// saturationPct in [0, 400] (100=neutral), transparencyPct in [0, 100] (0=fully opaque).
    /// </summary>
    public static BitmapSource ApplyCore(
        BitmapSource source,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct)
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
        double brightShift  = brightnessPct  / 100.0; // -1..1
        double contrastScale = (100.0 + contrastPct) / 100.0; // 0..2 (contrast multiplier around 0.5)
        double satScale      = saturationPct / 100.0; // 0..4
        double opacity       = 1.0 - transparencyPct / 100.0; // 0..1

        bool doBC  = brightnessPct != 0 || contrastPct != 0;
        bool doSat = saturationPct != 100;
        bool doA   = transparencyPct != 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i    ] / 255.0;
            double g = pixels[i + 1] / 255.0;
            double r = pixels[i + 2] / 255.0;
            double a = pixels[i + 3] / 255.0;

            // 1. Saturation (HSL-space chroma scaling via grey desaturate/mix).
            if (doSat)
            {
                // Perceived luminance (Rec.709 coefficients).
                double grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = Clamp(grey + (r - grey) * satScale);
                g = Clamp(grey + (g - grey) * satScale);
                b = Clamp(grey + (b - grey) * satScale);
            }

            // 2+3. Brightness (shift) then Contrast (scale around mid-point).
            if (doBC)
            {
                r = Clamp(r + brightShift);
                g = Clamp(g + brightShift);
                b = Clamp(b + brightShift);

                r = Clamp((r - 0.5) * contrastScale + 0.5);
                g = Clamp((g - 0.5) * contrastScale + 0.5);
                b = Clamp((b - 0.5) * contrastScale + 0.5);
            }

            // 4. Transparency: scale alpha (Pbgra32 stores pre-multiplied, so scale channels too).
            if (doA && a > 0)
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
