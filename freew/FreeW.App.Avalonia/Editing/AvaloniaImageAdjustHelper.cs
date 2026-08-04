using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Non-destructive Avalonia pixel pipeline for Picture Format correction and color presets.
/// The operation order mirrors the WPF host's ImageAdjustHelper so both hosts render the same
/// model values without modifying InlineImage.PngBytes.
/// </summary>
internal static partial class AvaloniaImageAdjustHelper
{
    public static Bitmap Apply(Bitmap source, InlineImage image)
        => ApplyWithBounds(source, image).Bitmap;

    internal static AvaloniaImageApplyResult ApplyWithBounds(Bitmap source, InlineImage image)
    {
        if (!image.HasAdjustments && !image.HasRecolor && !image.HasArtisticEffect && !HasRasterEffects(image))
            return new(source, new PixelRect(0, 0, source.PixelSize.Width, source.PixelSize.Height));

        var current = source;
        Bitmap? owned = null;
        var sourcePixelRect = new PixelRect(0, 0, source.PixelSize.Width, source.PixelSize.Height);
        try
        {
            if (image.HasAdjustments || image.HasRecolor)
            {
                current = ApplyCore(
                    current,
                    image.BrightnessPct,
                    image.ContrastPct,
                    image.SaturationPct,
                    image.TransparencyPct,
                    image.RecolorMode,
                    image.ColorTemperature);
                owned = current;
            }

            if (image.RequiresArtisticEffectRendering)
            {
                var next = ApplyArtistic(current, image.ArtisticEffect);
                if (owned is not null)
                    owned.Dispose();
                current = next;
                owned = current;
            }

            if (HasRasterEffects(image))
            {
                var next = ApplyPictureEffects(current, image);
                if (owned is not null)
                    owned.Dispose();
                current = next.Bitmap;
                sourcePixelRect = next.SourcePixelRect;
                owned = current;
            }

            // Ownership transfers to DocumentView's bitmap cache. The local variable is retained only
            // to make the transfer explicit; disposing it here would invalidate the cached result.
            owned = null;
            return new(current, sourcePixelRect);
        }
        catch
        {
            owned?.Dispose();
            throw;
        }
    }

    internal static Bitmap ApplyCore(
        Bitmap source,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct,
        ImageRecolorMode recolorMode = ImageRecolorMode.None,
        double colorTemperature = 0)
    {
        var size = source.PixelSize;
        var width = size.Width;
        var height = size.Height;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        // Match WPF's Pbgra32 pipeline. Use the pointer overload to preserve the source
        // channel bytes; the framebuffer overload may transcode through the active backend.
        var sourcePixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(
                new PixelRect(0, 0, width, height),
                sourcePixels.AddrOfPinnedObject(),
                pixels.Length,
                stride);
        }
        finally
        {
            sourcePixels.Free();
        }

        ApplyPixels(
            pixels,
            brightnessPct,
            contrastPct,
            saturationPct,
            transparencyPct,
            recolorMode,
            colorTemperature);

        var output = new WriteableBitmap(size, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var framebuffer = output.Lock())
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * stride, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), stride);
            }
        }

        return output;
    }

    internal static void ApplyPixels(
        byte[] pixels,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct,
        ImageRecolorMode recolorMode,
        double colorTemperature)
    {
        var brightShift = brightnessPct / 100.0;
        var contrastScale = (100.0 + contrastPct) / 100.0;
        var saturationScale = saturationPct / 100.0;
        var opacity = 1.0 - transparencyPct / 100.0;
        var temperatureScale = colorTemperature / 100.0;
        var adjustBrightnessContrast = brightnessPct != 0 || contrastPct != 0;
        var adjustSaturation = saturationPct != 100;
        var adjustAlpha = transparencyPct != 0;
        var adjustTemperature = colorTemperature != 0 && recolorMode == ImageRecolorMode.None;

        const double sepiaR1 = 0.482;
        const double sepiaG1 = 0.251;
        const double sepiaB1 = 0.071;
        const double sepiaR2 = 0.992;
        const double sepiaG2 = 0.941;
        const double sepiaB2 = 0.878;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i] / 255.0;
            var g = pixels[i + 1] / 255.0;
            var r = pixels[i + 2] / 255.0;
            var a = pixels[i + 3] / 255.0;

            switch (recolorMode)
            {
                case ImageRecolorMode.Grayscale:
                {
                    var grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = g = b = grey;
                    break;
                }
                case ImageRecolorMode.Sepia:
                {
                    var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = Clamp(sepiaR1 + (sepiaR2 - sepiaR1) * luminance);
                    g = Clamp(sepiaG1 + (sepiaG2 - sepiaG1) * luminance);
                    b = Clamp(sepiaB1 + (sepiaB2 - sepiaB1) * luminance);
                    break;
                }
                case ImageRecolorMode.Washout:
                {
                    r = Clamp(r + 0.40 + brightShift);
                    g = Clamp(g + 0.40 + brightShift);
                    b = Clamp(b + 0.40 + brightShift);
                    var washOpacity = 0.5 * opacity;
                    if (a > 0)
                    {
                        var newAlpha = Clamp(a * washOpacity);
                        var ratio = newAlpha / a;
                        r *= ratio;
                        g *= ratio;
                        b *= ratio;
                        a = newAlpha;
                    }
                    break;
                }
                case ImageRecolorMode.BlackWhite:
                {
                    var grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = g = b = grey >= 0.5 ? 1.0 : 0.0;
                    break;
                }
            }

            if (adjustTemperature)
            {
                if (temperatureScale > 0)
                {
                    r = Clamp(r + temperatureScale * 0.15);
                    b = Clamp(b - temperatureScale * 0.10);
                }
                else
                {
                    b = Clamp(b - temperatureScale * 0.15);
                    r = Clamp(r + temperatureScale * 0.10);
                }
            }

            if (adjustSaturation && recolorMode == ImageRecolorMode.None)
            {
                var grey = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = Clamp(grey + (r - grey) * saturationScale);
                g = Clamp(grey + (g - grey) * saturationScale);
                b = Clamp(grey + (b - grey) * saturationScale);
            }

            if (adjustBrightnessContrast && recolorMode is not (ImageRecolorMode.Washout or ImageRecolorMode.BlackWhite))
            {
                r = Clamp((Clamp(r + brightShift) - 0.5) * contrastScale + 0.5);
                g = Clamp((Clamp(g + brightShift) - 0.5) * contrastScale + 0.5);
                b = Clamp((Clamp(b + brightShift) - 0.5) * contrastScale + 0.5);
            }

            if (adjustAlpha && recolorMode != ImageRecolorMode.Washout && a > 0)
            {
                ApplyPremultipliedTransparency(pixels, i, opacity);
                r = pixels[i + 2] / 255.0;
                g = pixels[i + 1] / 255.0;
                b = pixels[i] / 255.0;
                a = Clamp(a * opacity);
            }

            pixels[i] = ToByte(b);
            pixels[i + 1] = ToByte(g);
            pixels[i + 2] = ToByte(r);
            pixels[i + 3] = ToByte(a);
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    private static byte ToByte(double value) => (byte)Math.Clamp(value * 255 + 0.5, 0, 255);

    private static void ApplyPremultipliedTransparency(byte[] pixels, int offset, double opacity)
    {
        var alpha = pixels[offset + 3] / 255.0;
        if (alpha <= 0)
            return;

        var newAlpha = Clamp(alpha * opacity);
        var ratio = newAlpha / alpha;
        pixels[offset] = ToByte(pixels[offset] / 255.0 * ratio);
        pixels[offset + 1] = ToByte(pixels[offset + 1] / 255.0 * ratio);
        pixels[offset + 2] = ToByte(pixels[offset + 2] / 255.0 * ratio);
        pixels[offset + 3] = ToByte(newAlpha);
    }

}
