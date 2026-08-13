using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// WPF adapter for the non-destructive picture adjustment and artistic-effect pipeline.
/// Portable BGRA transforms live in <see cref="PremultipliedBgraRasterEffects"/>; this type owns
/// WPF format conversion, DPI propagation, and frozen <see cref="BitmapSource"/> materialization.
/// </summary>
internal static class ImageAdjustHelper
{
    public static BitmapSource Apply(BitmapSource source, InlineImage image)
    {
        if (!image.HasAdjustments && !image.HasRecolor && !image.HasArtisticEffect)
            return source;

        var adjusted = image.HasAdjustments || image.HasRecolor
            ? ApplyCore(
                source,
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

    public static BitmapSource ApplyCore(
        BitmapSource source,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct,
        ImageRecolorMode recolorMode = ImageRecolorMode.None,
        double colorTemperature = 0)
    {
        var nativeSource = AsPremultipliedBgra32(source);
        var pixels = ReadPixels(nativeSource, out var stride);
        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct,
            contrastPct,
            saturationPct,
            transparencyPct,
            recolorMode,
            colorTemperature,
            PremultipliedTransparencyChannelSource.AdjustedChannels);

        return CreateBitmap(nativeSource, pixels, stride);
    }

    public static BitmapSource ApplyArtistic(BitmapSource source, ImageArtisticEffect effect)
    {
        if (effect == ImageArtisticEffect.None)
            return source;

        var nativeSource = AsPremultipliedBgra32(source);
        var pixels = ReadPixels(nativeSource, out var stride);
        var width = nativeSource.PixelWidth;
        var height = nativeSource.PixelHeight;
        if (!PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
                pixels,
                width,
                height,
                stride,
                effect,
                out var result))
        {
            switch (effect)
            {
                // These branches preserve legacy WPF arithmetic that is observably different from
                // Avalonia's historical implementation.
                case ImageArtisticEffect.GlowDiffused:
                    result = ApplyGlowDiffused(pixels, width, height, stride);
                    break;
                case ImageArtisticEffect.GlowEdges:
                    result = ApplyGlowEdges(pixels, width, height, stride);
                    break;
                case ImageArtisticEffect.Photocopy:
                    result = ApplyPhotocopy(pixels);
                    break;
                default:
                    return source;
            }
        }

        return CreateBitmap(nativeSource, result, stride);
    }

    private static byte[] ApplyGlowDiffused(byte[] pixels, int width, int height, int stride)
    {
        var result = PremultipliedBgraRasterEffects.BoxBlur(pixels, width, height, stride, 8);
        for (var i = 0; i < result.Length; i += 4)
        {
            var luminance = 0.2126 * pixels[i + 2] / 255.0
                + 0.7152 * pixels[i + 1] / 255.0
                + 0.0722 * pixels[i] / 255.0;
            var glow = luminance * 0.3;
            result[i] = PremultipliedBgraRasterEffects.ClampByte(result[i] + (int)(glow * 255));
            result[i + 1] = PremultipliedBgraRasterEffects.ClampByte(result[i + 1] + (int)(glow * 255));
            result[i + 2] = PremultipliedBgraRasterEffects.ClampByte(result[i + 2] + (int)(glow * 255));
        }

        return result;
    }

    private static byte[] ApplyGlowEdges(byte[] pixels, int width, int height, int stride)
    {
        var edges = PremultipliedBgraRasterEffects.Sobel(pixels, width, height, stride);
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var factor = edges[i / 4] / 255.0;
            result[i] = (byte)(pixels[i] * factor);
            result[i + 1] = (byte)(pixels[i + 1] * factor);
            result[i + 2] = (byte)(pixels[i + 2] * factor);
            result[i + 3] = pixels[i + 3];
        }

        return result;
    }

    private static byte[] ApplyPhotocopy(byte[] pixels)
    {
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var grey = 0.2126 * pixels[i + 2] / 255.0
                + 0.7152 * pixels[i + 1] / 255.0
                + 0.0722 * pixels[i] / 255.0;
            var value = grey < 0.4
                ? grey * 0.2
                : grey > 0.6
                    ? 0.92 + (grey - 0.6) * 0.4
                    : 0.1 + (grey - 0.4) * 4.1;
            var channel = PremultipliedBgraRasterEffects.ToByte(value);
            result[i] = result[i + 1] = result[i + 2] = channel;
            result[i + 3] = pixels[i + 3];
        }

        return result;
    }

    private static BitmapSource AsPremultipliedBgra32(BitmapSource source) =>
        source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

    private static byte[] ReadPixels(BitmapSource source, out int stride)
    {
        stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static BitmapSource CreateBitmap(BitmapSource source, byte[] pixels, int stride)
    {
        var bitmap = new WriteableBitmap(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Pbgra32,
            null);
        bitmap.WritePixels(
            new System.Windows.Int32Rect(0, 0, source.PixelWidth, source.PixelHeight),
            pixels,
            stride,
            0);
        bitmap.Freeze();
        return bitmap;
    }
}
