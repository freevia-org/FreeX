using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum PremultipliedTransparencyChannelSource
{
    AdjustedChannels,
    SourceChannels,
}

/// <summary>
/// UI-free pixel transforms for premultiplied BGRA buffers. Renderer projects retain ownership of
/// native bitmap decoding, buffer transfer, DPI, and output materialization.
/// </summary>
public static class PremultipliedBgraRasterEffects
{
    public static void ApplyAdjustmentsInPlace(
        byte[] pixels,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct,
        ImageRecolorMode recolorMode,
        double colorTemperature,
        PremultipliedTransparencyChannelSource transparencyChannelSource)
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
            var sourceB = b;
            var sourceG = g;
            var sourceR = r;

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
                var newAlpha = Clamp(a * opacity);
                var ratio = newAlpha / a;
                if (transparencyChannelSource == PremultipliedTransparencyChannelSource.SourceChannels)
                {
                    b = sourceB;
                    g = sourceG;
                    r = sourceR;
                }
                r *= ratio;
                g *= ratio;
                b *= ratio;
                a = newAlpha;
            }

            pixels[i] = ToByte(b);
            pixels[i + 1] = ToByte(g);
            pixels[i + 2] = ToByte(r);
            pixels[i + 3] = ToByte(a);
        }
    }

    /// <summary>
    /// Applies an artistic effect whose byte transform is shared exactly by both renderers.
    /// Returns false for effects that intentionally retain renderer-specific legacy behavior.
    /// </summary>
    public static bool TryApplySharedArtisticEffect(
        byte[] pixels,
        int width,
        int height,
        int stride,
        ImageArtisticEffect effect,
        out byte[] result)
    {
        switch (effect)
        {
            case ImageArtisticEffect.None:
                result = pixels;
                return true;
            case ImageArtisticEffect.Blur:
                result = BoxBlur(pixels, width, height, stride, 5);
                return true;
            case ImageArtisticEffect.PencilGrayscale:
                result = ApplyPencilGrayscale(pixels, width, height, stride);
                return true;
            case ImageArtisticEffect.PencilSketch:
                result = ApplyPencilSketch(pixels, width, height, stride);
                return true;
            case ImageArtisticEffect.LineDrawing:
                result = ApplyLineDrawing(pixels, width, height, stride);
                return true;
            case ImageArtisticEffect.Paintbrush:
                result = BoxBlur(pixels, width, height, stride, 4);
                SaturateInPlace(result, 1.4);
                return true;
            case ImageArtisticEffect.PaintStrokes:
                result = BoxBlur(pixels, width, height, stride, 7);
                SaturateInPlace(result, 2.0);
                return true;
            case ImageArtisticEffect.Posterize:
                result = ApplyPosterize(pixels);
                return true;
            case ImageArtisticEffect.Pastels:
                result = ApplyPastels(pixels, width, height, stride);
                return true;
            case ImageArtisticEffect.Watercolor:
                result = ApplyWatercolor(pixels, width, height, stride);
                return true;
            case ImageArtisticEffect.FilmGrain:
                result = ApplyFilmGrain(pixels);
                return true;
            case ImageArtisticEffect.Mosaic:
                result = ApplyMosaic(pixels, width, height, stride);
                return true;
            default:
                result = pixels;
                return false;
        }
    }

    public static byte[] BoxBlur(byte[] pixels, int width, int height, int stride, int radius)
    {
        if (radius <= 0)
            return (byte[])pixels.Clone();

        var temp = new byte[pixels.Length];
        var result = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            long sumB = 0;
            long sumG = 0;
            long sumR = 0;
            long sumA = 0;
            var count = 0;
            for (var dx = -radius; dx <= radius; dx++)
            {
                var sourceX = Math.Clamp(x + dx, 0, width - 1);
                var sourceIndex = y * stride + sourceX * 4;
                sumB += pixels[sourceIndex];
                sumG += pixels[sourceIndex + 1];
                sumR += pixels[sourceIndex + 2];
                sumA += pixels[sourceIndex + 3];
                count++;
            }

            var destinationIndex = y * stride + x * 4;
            temp[destinationIndex] = (byte)(sumB / count);
            temp[destinationIndex + 1] = (byte)(sumG / count);
            temp[destinationIndex + 2] = (byte)(sumR / count);
            temp[destinationIndex + 3] = (byte)(sumA / count);
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            long sumB = 0;
            long sumG = 0;
            long sumR = 0;
            long sumA = 0;
            var count = 0;
            for (var dy = -radius; dy <= radius; dy++)
            {
                var sourceY = Math.Clamp(y + dy, 0, height - 1);
                var sourceIndex = sourceY * stride + x * 4;
                sumB += temp[sourceIndex];
                sumG += temp[sourceIndex + 1];
                sumR += temp[sourceIndex + 2];
                sumA += temp[sourceIndex + 3];
                count++;
            }

            var destinationIndex = y * stride + x * 4;
            result[destinationIndex] = (byte)(sumB / count);
            result[destinationIndex + 1] = (byte)(sumG / count);
            result[destinationIndex + 2] = (byte)(sumR / count);
            result[destinationIndex + 3] = (byte)(sumA / count);
        }

        return result;
    }

    public static byte[] Sobel(byte[] pixels, int width, int height, int stride)
    {
        var grey = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            grey[y * width + x] = (byte)Luminance(pixels, y * stride + x * 4);

        var edges = new byte[width * height];
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            int Pixel(int dx, int dy) => grey[(y + dy) * width + x + dx];
            var gx = -Pixel(-1, -1) - 2 * Pixel(0, -1) - Pixel(1, -1)
                + Pixel(-1, 1) + 2 * Pixel(0, 1) + Pixel(1, 1);
            var gy = -Pixel(-1, -1) - 2 * Pixel(-1, 0) - Pixel(-1, 1)
                + Pixel(1, -1) + 2 * Pixel(1, 0) + Pixel(1, 1);
            edges[y * width + x] = (byte)Math.Min(255, Math.Sqrt(gx * (long)gx + gy * (long)gy));
        }

        return edges;
    }

    public static int Luminance(byte[] pixels, int offset) =>
        (int)(0.2126 * pixels[offset + 2]
            + 0.7152 * pixels[offset + 1]
            + 0.0722 * pixels[offset]
            + 0.5);

    public static byte ToByte(double value) =>
        (byte)Math.Clamp(value * 255 + 0.5, 0, 255);

    public static byte ClampByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    private static byte[] ApplyPencilGrayscale(byte[] pixels, int width, int height, int stride)
    {
        var edges = Sobel(pixels, width, height, stride);
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var value = (byte)(255 - edges[i / 4]);
            result[i] = result[i + 1] = result[i + 2] = value;
            result[i + 3] = pixels[i + 3];
        }

        return result;
    }

    private static byte[] ApplyPencilSketch(byte[] pixels, int width, int height, int stride)
    {
        var edges = Sobel(pixels, width, height, stride);
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var paperMix = 1 - edges[i / 4] / 255.0;
            var b = pixels[i] / 255.0;
            var g = pixels[i + 1] / 255.0;
            var r = pixels[i + 2] / 255.0;
            var mixedB = Clamp(paperMix + b * (1 - paperMix));
            var mixedG = Clamp(paperMix + g * (1 - paperMix));
            var mixedR = Clamp(paperMix + r * (1 - paperMix));
            var luminance = 0.2126 * mixedR + 0.7152 * mixedG + 0.0722 * mixedB;
            result[i] = ToByte(luminance + (mixedB - luminance) * 1.6);
            result[i + 1] = ToByte(luminance + (mixedG - luminance) * 1.6);
            result[i + 2] = ToByte(luminance + (mixedR - luminance) * 1.6);
            result[i + 3] = pixels[i + 3];
        }

        return result;
    }

    private static byte[] ApplyLineDrawing(byte[] pixels, int width, int height, int stride)
    {
        var edges = Sobel(pixels, width, height, stride);
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var value = edges[i / 4] > 60 ? (byte)0 : (byte)255;
            result[i] = result[i + 1] = result[i + 2] = value;
            result[i + 3] = pixels[i + 3];
        }

        return result;
    }

    private static byte[] ApplyPosterize(byte[] pixels)
    {
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = Posterize(pixels[i]);
            result[i + 1] = Posterize(pixels[i + 1]);
            result[i + 2] = Posterize(pixels[i + 2]);
            result[i + 3] = pixels[i + 3];
        }

        return result;
    }

    private static byte[] ApplyPastels(byte[] pixels, int width, int height, int stride)
    {
        var result = BoxBlur(pixels, width, height, stride, 3);
        for (var i = 0; i < result.Length; i += 4)
        {
            var b = result[i] / 255.0;
            var g = result[i + 1] / 255.0;
            var r = result[i + 2] / 255.0;
            var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            result[i] = ToByte(0.25 + luminance + (b - luminance) * 0.5);
            result[i + 1] = ToByte(0.25 + luminance + (g - luminance) * 0.5);
            result[i + 2] = ToByte(0.25 + luminance + (r - luminance) * 0.5);
        }

        return result;
    }

    private static byte[] ApplyWatercolor(byte[] pixels, int width, int height, int stride)
    {
        var result = BoxBlur(pixels, width, height, stride, 3);
        SaturateInPlace(result, 1.25);
        for (var i = 0; i < result.Length; i += 4)
        {
            result[i] = ClampByte(result[i] + 10);
            result[i + 1] = ClampByte(result[i + 1] + 10);
            result[i + 2] = ClampByte(result[i + 2] + 10);
        }

        return result;
    }

    private static byte[] ApplyFilmGrain(byte[] pixels)
    {
        var result = (byte[])pixels.Clone();
        var random = new Random(12345);
        for (var i = 0; i < result.Length; i += 4)
        {
            var noise = (int)((random.NextDouble() - 0.5) * 60);
            result[i] = ClampByte(result[i] + noise);
            result[i + 1] = ClampByte(result[i + 1] + noise);
            result[i + 2] = ClampByte(result[i + 2] + noise);
        }

        return result;
    }

    private static byte[] ApplyMosaic(byte[] pixels, int width, int height, int stride)
    {
        var blockSize = Math.Max(1, Math.Min(width, height) / 20);
        var result = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var blockX = x / blockSize * blockSize;
            var blockY = y / blockSize * blockSize;
            var blockRight = Math.Min(blockX + blockSize, width);
            var blockBottom = Math.Min(blockY + blockSize, height);
            long sumB = 0;
            long sumG = 0;
            long sumR = 0;
            var count = 0;
            for (var sourceY = blockY; sourceY < blockBottom; sourceY++)
            for (var sourceX = blockX; sourceX < blockRight; sourceX++)
            {
                var sourceIndex = sourceY * stride + sourceX * 4;
                sumB += pixels[sourceIndex];
                sumG += pixels[sourceIndex + 1];
                sumR += pixels[sourceIndex + 2];
                count++;
            }

            var destinationIndex = y * stride + x * 4;
            result[destinationIndex] = (byte)(sumB / count);
            result[destinationIndex + 1] = (byte)(sumG / count);
            result[destinationIndex + 2] = (byte)(sumR / count);
            result[destinationIndex + 3] = pixels[destinationIndex + 3];
        }

        return result;
    }

    private static void SaturateInPlace(byte[] pixels, double scale)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i] / 255.0;
            var g = pixels[i + 1] / 255.0;
            var r = pixels[i + 2] / 255.0;
            var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            pixels[i] = ToByte(luminance + (b - luminance) * scale);
            pixels[i + 1] = ToByte(luminance + (g - luminance) * scale);
            pixels[i + 2] = ToByte(luminance + (r - luminance) * scale);
        }
    }

    private static byte Posterize(byte value) =>
        (byte)(Math.Round(value / 255.0 * 3) / 3 * 255 + 0.5);

    private static double Clamp(double value) =>
        Math.Clamp(value, 0, 1);
}
