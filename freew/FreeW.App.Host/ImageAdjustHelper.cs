using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Non-destructive pixel-adjustment pipeline for Picture Format > Adjust (Corrections / Color / Transparency)
/// and Recolor (Grayscale, Sepia, Washout, Black&amp;White) + Color Tone (temperature) + Artistic Effects.
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
///   6. Artistic effect — applied AFTER the recolor/adjust pipeline (on the already-adjusted pixels).
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
        if (!image.HasAdjustments && !image.HasRecolor && !image.HasArtisticEffect)
            return source;

        var adjusted = (image.HasAdjustments || image.HasRecolor)
            ? ApplyCore(source,
                image.BrightnessPct,
                image.ContrastPct,
                image.SaturationPct,
                image.TransparencyPct,
                image.RecolorMode,
                image.ColorTemperature)
            : source;

        if (image.RequiresArtisticEffectRendering)
            adjusted = ApplyArtistic(adjusted, image.ArtisticEffect);

        return adjusted;
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

    // ── Artistic Effects pipeline ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply an <see cref="ImageArtisticEffect"/> to <paramref name="source"/> and return a new frozen
    /// bitmap. All effects are implemented as per-pixel or small-kernel convolution operations on a
    /// Pbgra32 intermediate, preserving the original source unchanged. Returns <paramref name="source"/>
    /// when <paramref name="effect"/> is <see cref="ImageArtisticEffect.None"/>.
    /// </summary>
    public static BitmapSource ApplyArtistic(BitmapSource source, ImageArtisticEffect effect)
    {
        if (effect == ImageArtisticEffect.None) return source;

        // Convert to Pbgra32.
        var src = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        int w = src.PixelWidth;
        int h = src.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        src.CopyPixels(pixels, stride, 0);

        byte[] result;
        switch (effect)
        {
            case ImageArtisticEffect.Blur:
                result = BoxBlur(pixels, w, h, stride, radius: 5);
                break;

            case ImageArtisticEffect.GlowDiffused:
                // Strong blur + additive 25% mix back of original bright areas.
                result = BoxBlur(pixels, w, h, stride, radius: 8);
                for (int i = 0; i < result.Length; i += 4)
                {
                    // additive blend: bright pixels get pushed toward white
                    double lum = 0.2126 * pixels[i + 2] / 255.0 + 0.7152 * pixels[i + 1] / 255.0 + 0.0722 * pixels[i] / 255.0;
                    double glow = lum * 0.3;
                    result[i    ] = Clamp255(result[i    ] + (int)(glow * 255));
                    result[i + 1] = Clamp255(result[i + 1] + (int)(glow * 255));
                    result[i + 2] = Clamp255(result[i + 2] + (int)(glow * 255));
                }
                break;

            case ImageArtisticEffect.GlowEdges:
            {
                // Edge-detect then invert: bright edges on dark background.
                var edges = Sobel(pixels, w, h, stride);
                result = new byte[pixels.Length];
                for (int i = 0; i < result.Length; i += 4)
                {
                    byte e = edges[i / 4];
                    // Colour the edges with the original hue, fill background black.
                    double factor = e / 255.0;
                    result[i    ] = (byte)(pixels[i    ] * factor);
                    result[i + 1] = (byte)(pixels[i + 1] * factor);
                    result[i + 2] = (byte)(pixels[i + 2] * factor);
                    result[i + 3] = pixels[i + 3];
                }
                break;
            }

            case ImageArtisticEffect.PencilGrayscale:
            {
                // Edge-detect in greyscale, soft overlay on a white background.
                var edges = Sobel(pixels, w, h, stride);
                result = new byte[pixels.Length];
                for (int i = 0; i < result.Length; i += 4)
                {
                    byte e = edges[i / 4];
                    // Invert (dark pencil on white paper).
                    byte v = (byte)(255 - e);
                    result[i    ] = v;
                    result[i + 1] = v;
                    result[i + 2] = v;
                    result[i + 3] = pixels[i + 3];
                }
                break;
            }

            case ImageArtisticEffect.PencilSketch:
            {
                // Colour pencil: edge-detect + boost saturation.
                var grey = ToGrey(pixels, w, h, stride);
                var edges = Sobel(pixels, w, h, stride);
                result = new byte[pixels.Length];
                for (int i = 0; i < result.Length; i += 4)
                {
                    // Mix original colour with the greyscale inversion.
                    double t = 1.0 - edges[i / 4] / 255.0;
                    double b = pixels[i    ] / 255.0;
                    double g = pixels[i + 1] / 255.0;
                    double r = pixels[i + 2] / 255.0;
                    double paper = 1.0; // white paper
                    double br = Clamp(paper * t + b * (1 - t));
                    double gr = Clamp(paper * t + g * (1 - t));
                    double rr = Clamp(paper * t + r * (1 - t));
                    // Boost saturation.
                    double lum = 0.2126 * rr + 0.7152 * gr + 0.0722 * br;
                    double sat = 1.6;
                    result[i    ] = (byte)(Clamp(lum + (br - lum) * sat) * 255 + 0.5);
                    result[i + 1] = (byte)(Clamp(lum + (gr - lum) * sat) * 255 + 0.5);
                    result[i + 2] = (byte)(Clamp(lum + (rr - lum) * sat) * 255 + 0.5);
                    result[i + 3] = pixels[i + 3];
                }
                break;
            }

            case ImageArtisticEffect.LineDrawing:
            {
                // Hard edge-detect → pure black on white.
                var edges = Sobel(pixels, w, h, stride);
                result = new byte[pixels.Length];
                for (int i = 0; i < result.Length; i += 4)
                {
                    byte v = edges[i / 4] > 60 ? (byte)0 : (byte)255; // threshold to B&W
                    result[i    ] = v;
                    result[i + 1] = v;
                    result[i + 2] = v;
                    result[i + 3] = pixels[i + 3];
                }
                break;
            }

            case ImageArtisticEffect.Paintbrush:
                // Median-ish approximation: box-blur with radius 4, then saturate boost.
                result = BoxBlur(pixels, w, h, stride, radius: 4);
                SaturateInPlace(result, satScale: 1.4);
                break;

            case ImageArtisticEffect.PaintStrokes:
            {
                // Stronger blur + very strong saturation boost.
                result = BoxBlur(pixels, w, h, stride, radius: 7);
                SaturateInPlace(result, satScale: 2.0);
                break;
            }

            case ImageArtisticEffect.Photocopy:
            {
                // Greyscale then threshold + high contrast → black/white like a bad photocopy.
                result = new byte[pixels.Length];
                for (int i = 0; i < result.Length; i += 4)
                {
                    double grey = 0.2126 * pixels[i + 2] / 255.0 + 0.7152 * pixels[i + 1] / 255.0 + 0.0722 * pixels[i] / 255.0;
                    // Apply S-curve for photocopy look.
                    double pc = grey < 0.4 ? (grey * 0.2) : grey > 0.6 ? (0.92 + (grey - 0.6) * 0.4) : (0.1 + (grey - 0.4) * 4.1);
                    byte v = (byte)(Clamp(pc) * 255 + 0.5);
                    result[i    ] = v;
                    result[i + 1] = v;
                    result[i + 2] = v;
                    result[i + 3] = pixels[i + 3];
                }
                break;
            }

            case ImageArtisticEffect.Posterize:
            {
                // Colour level-quantise to 4 steps per channel.
                result = new byte[pixels.Length];
                const int levels = 4;
                for (int i = 0; i < result.Length; i += 4)
                {
                    result[i    ] = (byte)(Math.Round(pixels[i    ] / 255.0 * (levels - 1)) / (levels - 1) * 255 + 0.5);
                    result[i + 1] = (byte)(Math.Round(pixels[i + 1] / 255.0 * (levels - 1)) / (levels - 1) * 255 + 0.5);
                    result[i + 2] = (byte)(Math.Round(pixels[i + 2] / 255.0 * (levels - 1)) / (levels - 1) * 255 + 0.5);
                    result[i + 3] = pixels[i + 3];
                }
                break;
            }

            case ImageArtisticEffect.Pastels:
            {
                // Soft blur + desaturate toward pastel (mix with white, reduce chroma).
                result = BoxBlur(pixels, w, h, stride, radius: 3);
                for (int i = 0; i < result.Length; i += 4)
                {
                    double b = result[i    ] / 255.0;
                    double g = result[i + 1] / 255.0;
                    double r = result[i + 2] / 255.0;
                    // Desaturate + lighten toward pastel.
                    double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    double sat = 0.5;
                    double light = 0.25; // add brightness
                    result[i    ] = (byte)(Clamp(light + lum + (b - lum) * sat) * 255 + 0.5);
                    result[i + 1] = (byte)(Clamp(light + lum + (g - lum) * sat) * 255 + 0.5);
                    result[i + 2] = (byte)(Clamp(light + lum + (r - lum) * sat) * 255 + 0.5);
                }
                break;
            }

            case ImageArtisticEffect.Watercolor:
            {
                // Gentle blur + slight saturation lift.
                result = BoxBlur(pixels, w, h, stride, radius: 3);
                SaturateInPlace(result, satScale: 1.25);
                // Slightly brighten shadows.
                for (int i = 0; i < result.Length; i += 4)
                {
                    result[i    ] = Clamp255(result[i    ] + 10);
                    result[i + 1] = Clamp255(result[i + 1] + 10);
                    result[i + 2] = Clamp255(result[i + 2] + 10);
                }
                break;
            }

            case ImageArtisticEffect.FilmGrain:
            {
                // Add per-pixel random luminance noise (reproducible via deterministic RNG keyed by position).
                result = (byte[])pixels.Clone();
                var rng = new Random(12345);
                for (int i = 0; i < result.Length; i += 4)
                {
                    int noise = (int)((rng.NextDouble() - 0.5) * 60); // ±30 noise
                    result[i    ] = Clamp255(result[i    ] + noise);
                    result[i + 1] = Clamp255(result[i + 1] + noise);
                    result[i + 2] = Clamp255(result[i + 2] + noise);
                }
                break;
            }

            case ImageArtisticEffect.Mosaic:
            {
                // Block-average: divide into NxN blocks, fill with the block average colour.
                int blockSize = Math.Max(1, Math.Min(w, h) / 20); // ~5% of shortest dimension
                result = new byte[pixels.Length];
                for (int py = 0; py < h; py++)
                {
                    for (int px = 0; px < w; px++)
                    {
                        // Find the top-left of the block containing (px,py).
                        int bx = (px / blockSize) * blockSize;
                        int by = (py / blockSize) * blockSize;
                        int bx2 = Math.Min(bx + blockSize, w);
                        int by2 = Math.Min(by + blockSize, h);
                        // Compute block average.
                        long sumB = 0, sumG = 0, sumR = 0, count = 0;
                        for (int sy = by; sy < by2; sy++)
                            for (int sx = bx; sx < bx2; sx++)
                            {
                                int si = (sy * stride) + (sx * 4);
                                sumB += pixels[si    ];
                                sumG += pixels[si + 1];
                                sumR += pixels[si + 2];
                                count++;
                            }
                        int di = (py * stride) + (px * 4);
                        result[di    ] = (byte)(sumB / count);
                        result[di + 1] = (byte)(sumG / count);
                        result[di + 2] = (byte)(sumR / count);
                        result[di + 3] = pixels[di + 3];
                    }
                }
                break;
            }

            default:
                return source; // Unknown — pass through unchanged.
        }

        var wb = new WriteableBitmap(w, h, src.DpiX, src.DpiY, PixelFormats.Pbgra32, null);
        wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), result, stride, 0);
        wb.Freeze();
        return wb;
    }

    // ── Artistic-effect helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Single-pass 2D box blur with the given <paramref name="radius"/>. Returns a new pixel buffer;
    /// <paramref name="pixels"/> is not modified.
    /// </summary>
    private static byte[] BoxBlur(byte[] pixels, int w, int h, int stride, int radius)
    {
        // Two-pass: horizontal then vertical.
        var tmp = new byte[pixels.Length];
        var result = new byte[pixels.Length];

        // Horizontal pass.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                long sumB = 0, sumG = 0, sumR = 0, sumA = 0, count = 0;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int sx = x + dx;
                    if (sx < 0) sx = 0;
                    if (sx >= w) sx = w - 1;
                    int si = y * stride + sx * 4;
                    sumB += pixels[si    ];
                    sumG += pixels[si + 1];
                    sumR += pixels[si + 2];
                    sumA += pixels[si + 3];
                    count++;
                }
                int di = y * stride + x * 4;
                tmp[di    ] = (byte)(sumB / count);
                tmp[di + 1] = (byte)(sumG / count);
                tmp[di + 2] = (byte)(sumR / count);
                tmp[di + 3] = (byte)(sumA / count);
            }
        }

        // Vertical pass.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                long sumB = 0, sumG = 0, sumR = 0, sumA = 0, count = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int sy = y + dy;
                    if (sy < 0) sy = 0;
                    if (sy >= h) sy = h - 1;
                    int si = sy * stride + x * 4;
                    sumB += tmp[si    ];
                    sumG += tmp[si + 1];
                    sumR += tmp[si + 2];
                    sumA += tmp[si + 3];
                    count++;
                }
                int di = y * stride + x * 4;
                result[di    ] = (byte)(sumB / count);
                result[di + 1] = (byte)(sumG / count);
                result[di + 2] = (byte)(sumR / count);
                result[di + 3] = (byte)(sumA / count);
            }
        }
        return result;
    }

    /// <summary>
    /// Sobel edge-detection: returns a greyscale magnitude map (one byte per pixel, range 0–255).
    /// </summary>
    private static byte[] Sobel(byte[] pixels, int w, int h, int stride)
    {
        var grey = ToGrey(pixels, w, h, stride);
        var edges = new byte[w * h];
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int p(int dx, int dy) => grey[(y + dy) * w + (x + dx)];
                int gx = -p(-1,-1) - 2*p(0,-1) - p(1,-1)
                         + p(-1, 1) + 2*p(0, 1) + p(1, 1);
                int gy = -p(-1,-1) - 2*p(-1,0) - p(-1, 1)
                         + p( 1,-1) + 2*p( 1,0) + p( 1, 1);
                int mag = (int)Math.Sqrt(gx * (long)gx + gy * (long)gy);
                edges[y * w + x] = (byte)Math.Min(mag, 255);
            }
        }
        return edges;
    }

    /// <summary>Converts Pbgra32 pixel buffer to a greyscale magnitude array (one byte per pixel).</summary>
    private static byte[] ToGrey(byte[] pixels, int w, int h, int stride)
    {
        var grey = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int si = y * stride + x * 4;
                grey[y * w + x] = (byte)(
                    0.2126 * pixels[si + 2] +
                    0.7152 * pixels[si + 1] +
                    0.0722 * pixels[si    ] + 0.5);
            }
        return grey;
    }

    /// <summary>In-place saturation boost on a mutable result buffer.</summary>
    private static void SaturateInPlace(byte[] result, double satScale)
    {
        for (int i = 0; i < result.Length; i += 4)
        {
            double b = result[i    ] / 255.0;
            double g = result[i + 1] / 255.0;
            double r = result[i + 2] / 255.0;
            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            result[i    ] = (byte)(Clamp(lum + (b - lum) * satScale) * 255 + 0.5);
            result[i + 1] = (byte)(Clamp(lum + (g - lum) * satScale) * 255 + 0.5);
            result[i + 2] = (byte)(Clamp(lum + (r - lum) * satScale) * 255 + 0.5);
        }
    }

    private static byte Clamp255(int v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
}
