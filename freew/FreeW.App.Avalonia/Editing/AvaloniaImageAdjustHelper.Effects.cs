using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Free.Shared.Drawing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

internal readonly record struct AvaloniaImageApplyResult(Bitmap Bitmap, PixelRect SourcePixelRect);

internal readonly record struct AvaloniaPictureEffectRaster(
    byte[] Pixels,
    int Width,
    int Height,
    int Stride,
    PixelRect SourcePixelRect);

internal sealed class AvaloniaRenderedImage : IDisposable
{
    public AvaloniaRenderedImage(Bitmap bitmap, PixelRect sourcePixelRect)
    {
        Bitmap = bitmap;
        SourcePixelRect = sourcePixelRect;
    }

    public Bitmap Bitmap { get; }
    public PixelRect SourcePixelRect { get; }

    public Rect VisualRect(Rect sourceRect)
    {
        var scaleX = sourceRect.Width / Math.Max(1, SourcePixelRect.Width);
        var scaleY = sourceRect.Height / Math.Max(1, SourcePixelRect.Height);
        return new Rect(
            sourceRect.X - SourcePixelRect.X * scaleX,
            sourceRect.Y - SourcePixelRect.Y * scaleY,
            Bitmap.PixelSize.Width * scaleX,
            Bitmap.PixelSize.Height * scaleY);
    }

    public void Dispose() => Bitmap.Dispose();
}

internal static partial class AvaloniaImageAdjustHelper
{
    private static bool HasRasterEffects(InlineImage image) =>
        image.ShadowPreset > 0 || image.GlowSizePt > 0 || image.SoftEdgePt > 0 || image.BevelPreset > 0;

    /// <summary>
    /// Applies the WPF image-effect precedence to a premultiplied BGRA raster. WPF exposes one Effect
    /// slot on the image root, so shadow wins over glow, then soft edge, then bevel. Reflection is kept
    /// out of this bitmap because it changes the visual bounds and is composed by DocumentView.
    /// </summary>
    private static AvaloniaImageApplyResult ApplyPictureEffects(Bitmap source, InlineImage image)
    {
        ReadPixels(source, out var pixels, out var width, out var height, out var stride);
        var result = ApplyPictureEffectRaster(pixels, width, height, stride, image);

        return new(
            CreateBitmap(new PixelSize(result.Width, result.Height), source.Dpi, result.Pixels, result.Stride),
            result.SourcePixelRect);
    }

    internal static byte[] ApplyPictureEffectPixels(byte[] pixels, int width, int height, int stride, InlineImage image)
    {
        byte[] result;
        if (image.ShadowPreset > 0)
        {
            var shadow = PictureEffectVisualPlanner.BuildShadowPlan(image);
            result = CompositeHalo(
                pixels, width, height, stride, shadow.BlurPoints,
                shadow.OffsetXPoints, shadow.OffsetYPoints, shadow.Opacity,
                ParseColor(shadow.ColorHex, Color.FromRgb(0, 0, 0)),
                image);
        }
        else if (image.GlowSizePt > 0)
        {
            result = CompositeHalo(pixels, width, height, stride, image.GlowSizePt, 0, 0,
                PictureEffectVisualPlanner.ResolveGlowOpacity(image),
                ParseColor(image.GlowColorHex, Color.FromRgb(0x44, 0x72, 0xC4)), image);
        }
        else if (image.SoftEdgePt > 0)
        {
            var radius = EffectPixels(image.SoftEdgePt * 0.5, image, width, height);
            result = PremultipliedBgraRasterEffects.BoxBlur(pixels, width, height, stride, radius);
        }
        else
        {
            result = ApplyBevel(pixels, width, height, stride, image.BevelPreset);
        }

        return result;
    }

    internal static AvaloniaPictureEffectRaster ApplyPictureEffectRaster(
        byte[] pixels,
        int width,
        int height,
        int stride,
        InlineImage image)
    {
        if (image.ShadowPreset > 0)
        {
            var shadow = PictureEffectVisualPlanner.BuildShadowPlan(image);
            return CompositeHaloExpanded(
                pixels, width, height, stride, shadow.BlurPoints,
                shadow.OffsetXPoints, shadow.OffsetYPoints, shadow.Opacity,
                ParseColor(shadow.ColorHex, Color.FromRgb(0, 0, 0)),
                image);
        }

        if (image.GlowSizePt > 0)
        {
            return CompositeHaloExpanded(
                pixels, width, height, stride, image.GlowSizePt, 0, 0,
                PictureEffectVisualPlanner.ResolveGlowOpacity(image),
                ParseColor(image.GlowColorHex, Color.FromRgb(0x44, 0x72, 0xC4)), image);
        }

        var result = ApplyPictureEffectPixels(pixels, width, height, stride, image);
        return new(result, width, height, stride, new PixelRect(0, 0, width, height));
    }

    /// <summary>Port of the WPF ImageAdjustHelper artistic pipeline to Avalonia's premultiplied BGRA buffer.</summary>
    private static Bitmap ApplyArtistic(Bitmap source, ImageArtisticEffect effect)
    {
        if (effect == ImageArtisticEffect.None)
            return source;

        ReadPixels(source, out var pixels, out var width, out var height, out var stride);
        var result = ApplyArtisticPixels(pixels, width, height, stride, effect);
        return CreateBitmap(new PixelSize(width, height), source.Dpi, result, stride);
    }

    internal static byte[] ApplyArtisticPixels(
        byte[] pixels,
        int width,
        int height,
        int stride,
        ImageArtisticEffect effect)
    {
        if (PremultipliedBgraRasterEffects.TryApplySharedArtisticEffect(
                pixels,
                width,
                height,
                stride,
                effect,
                out var result))
        {
            return result;
        }

        switch (effect)
        {
            // Keep Avalonia's established arithmetic for the three effects whose legacy output
            // differs from WPF at byte level.
            case ImageArtisticEffect.GlowDiffused:
                result = PremultipliedBgraRasterEffects.BoxBlur(pixels, width, height, stride, 8);
                for (var i = 0; i < result.Length; i += 4)
                {
                    var luminance = Luminance(pixels, i);
                    var lift = (int)(luminance * 0.30 * 255);
                    result[i] = Clamp255(result[i] + lift);
                    result[i + 1] = Clamp255(result[i + 1] + lift);
                    result[i + 2] = Clamp255(result[i + 2] + lift);
                }
                return result;
            case ImageArtisticEffect.GlowEdges:
                return EdgeColor(pixels, width, height, stride, invert: false, threshold: 0);
            case ImageArtisticEffect.Photocopy:
                result = new byte[pixels.Length];
                for (var i = 0; i < result.Length; i += 4)
                {
                    var grey = Luminance(pixels, i) / 255.0;
                    var value = grey < 0.4
                        ? grey * 0.2
                        : grey > 0.6
                            ? 0.92 + (grey - 0.6) * 0.4
                            : 0.1 + (grey - 0.4) * 4.1;
                    var channel = ToByte(value);
                    result[i] = result[i + 1] = result[i + 2] = channel;
                    result[i + 3] = pixels[i + 3];
                }
                return result;
            default:
                return pixels;
        }
    }

    private static byte[] CompositeHalo(
        byte[] pixels,
        int width,
        int height,
        int stride,
        double blurPoints,
        double offsetXPoints,
        double offsetYPoints,
        double opacity,
        Color color,
        InlineImage image)
    {
        var blurRadius = EffectPixels(blurPoints, image, width, height);
        var blurred = BlurAlpha(pixels, width, height, stride, blurRadius);
        var dx = EffectOffsetPixels(offsetXPoints, image, width, height);
        var dy = EffectOffsetPixels(offsetYPoints, image, width, height);
        var result = new byte[pixels.Length];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            var sx = Math.Clamp(x - dx, 0, width - 1);
            var sy = Math.Clamp(y - dy, 0, height - 1);
            var haloA = blurred[sy * width + sx] / 255.0 * opacity;
            var srcA = pixels[offset + 3] / 255.0;
            var outA = srcA + haloA * (1 - srcA);
            var haloPremul = haloA * (1 - srcA);
            result[offset] = ToByte((pixels[offset] / 255.0) + haloPremul * color.B / 255.0);
            result[offset + 1] = ToByte((pixels[offset + 1] / 255.0) + haloPremul * color.G / 255.0);
            result[offset + 2] = ToByte((pixels[offset + 2] / 255.0) + haloPremul * color.R / 255.0);
            result[offset + 3] = ToByte(outA);
        }

        return result;
    }

    private static AvaloniaPictureEffectRaster CompositeHaloExpanded(
        byte[] pixels,
        int width,
        int height,
        int stride,
        double blurPoints,
        double offsetXPoints,
        double offsetYPoints,
        double opacity,
        Color color,
        InlineImage image)
    {
        var blurRadius = EffectPixels(blurPoints, image, width, height);
        var offsetX = EffectOffsetPixels(offsetXPoints, image, width, height);
        var offsetY = EffectOffsetPixels(offsetYPoints, image, width, height);
        var sourceX = blurRadius + Math.Max(0, -offsetX) + 1;
        var sourceY = blurRadius + Math.Max(0, -offsetY) + 1;
        var rightInset = blurRadius + Math.Max(0, offsetX) + 1;
        var bottomInset = blurRadius + Math.Max(0, offsetY) + 1;
        var expandedWidth = width + sourceX + rightInset;
        var expandedHeight = height + sourceY + bottomInset;
        var expandedStride = checked(expandedWidth * 4);
        var expanded = new byte[checked(expandedStride * expandedHeight)];

        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(pixels, y * stride, expanded, (y + sourceY) * expandedStride + sourceX * 4, stride);

        var blurred = BlurAlpha(expanded, expandedWidth, expandedHeight, expandedStride, blurRadius);
        var result = new byte[expanded.Length];
        for (var y = 0; y < expandedHeight; y++)
        for (var x = 0; x < expandedWidth; x++)
        {
            var offset = y * expandedStride + x * 4;
            var sourceOffsetX = x - sourceX;
            var sourceOffsetY = y - sourceY;
            var hasSource = sourceOffsetX >= 0 && sourceOffsetX < width &&
                            sourceOffsetY >= 0 && sourceOffsetY < height;
            var sourceAlpha = hasSource
                ? pixels[sourceOffsetY * stride + sourceOffsetX * 4 + 3] / 255.0
                : 0;
            var haloX = Math.Clamp(x - offsetX, 0, expandedWidth - 1);
            var haloY = Math.Clamp(y - offsetY, 0, expandedHeight - 1);
            var haloAlpha = blurred[haloY * expandedWidth + haloX] / 255.0 * opacity;
            var haloPremul = haloAlpha * (1 - sourceAlpha);

            if (hasSource)
            {
                var sourceOffset = sourceOffsetY * stride + sourceOffsetX * 4;
                result[offset] = ToByte(pixels[sourceOffset] / 255.0 + haloPremul * color.B / 255.0);
                result[offset + 1] = ToByte(pixels[sourceOffset + 1] / 255.0 + haloPremul * color.G / 255.0);
                result[offset + 2] = ToByte(pixels[sourceOffset + 2] / 255.0 + haloPremul * color.R / 255.0);
            }
            else
            {
                result[offset] = ToByte(haloPremul * color.B / 255.0);
                result[offset + 1] = ToByte(haloPremul * color.G / 255.0);
                result[offset + 2] = ToByte(haloPremul * color.R / 255.0);
            }

            result[offset + 3] = ToByte(sourceAlpha + haloPremul);
        }

        return new(
            result,
            expandedWidth,
            expandedHeight,
            expandedStride,
            new PixelRect(sourceX, sourceY, width, height));
    }

    private static byte[] ApplyBevel(byte[] pixels, int width, int height, int stride, int preset)
    {
        var result = (byte[])pixels.Clone();
        var edgeWidth = preset switch { 1 => 1, 2 => 2, 3 => 1, _ => 3 };
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            var alpha = pixels[offset + 3] / 255.0;
            if (alpha <= 0)
                continue;
            var top = AlphaAt(pixels, width, height, stride, x, y - 1);
            var left = AlphaAt(pixels, width, height, stride, x - 1, y);
            var bottom = AlphaAt(pixels, width, height, stride, x, y + 1);
            var right = AlphaAt(pixels, width, height, stride, x + 1, y);
            var highlight = Math.Max(0, alpha - Math.Min(top, left));
            var shade = Math.Max(0, alpha - Math.Min(bottom, right));
            var amount = Math.Min(1, Math.Max(highlight, shade) * edgeWidth * 0.40);
            if (amount <= 0)
                continue;

            var lightAmount = Math.Min(1, highlight * edgeWidth * 0.40);
            var darkAmount = Math.Min(1, shade * edgeWidth * 0.30);
            result[offset] = ToByte(pixels[offset] / 255.0 * (1 - darkAmount) + lightAmount);
            result[offset + 1] = ToByte(pixels[offset + 1] / 255.0 * (1 - darkAmount) + lightAmount);
            result[offset + 2] = ToByte(pixels[offset + 2] / 255.0 * (1 - darkAmount) + lightAmount);
        }
        return result;
    }

    private static double AlphaAt(byte[] pixels, int width, int height, int stride, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return 0;
        return pixels[y * stride + x * 4 + 3] / 255.0;
    }

    private static int EffectPixels(double points, InlineImage image, int width, int height)
    {
        var pointScale = Math.Max(
            width / Math.Max(1.0, image.WidthPt),
            height / Math.Max(1.0, image.HeightPt));
        return points > 0
            ? Math.Clamp(Math.Max(1, (int)Math.Round(points * pointScale)), 1, 32)
            : 0;
    }

    private static int EffectOffsetPixels(double points, InlineImage image, int width, int height) =>
        points == 0 ? 0 : Math.Sign(points) * EffectPixels(Math.Abs(points), image, width, height);

    private static Color ParseColor(string? hex, Color fallback)
    {
        return DrawingMlRgbColor.TryParseHexRgb(hex, out var color)
            ? Color.FromRgb(color.R, color.G, color.B)
            : fallback;
    }

    private static void ReadPixels(Bitmap source, out byte[] pixels, out int width, out int height, out int stride)
    {
        width = source.PixelSize.Width;
        height = source.PixelSize.Height;
        stride = checked(width * 4);
        pixels = new byte[checked(stride * height)];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally { handle.Free(); }
    }

    private static Bitmap CreateBitmap(PixelSize size, Vector dpi, byte[] pixels, int stride)
    {
        var bitmap = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var framebuffer = bitmap.Lock();
        for (var y = 0; y < size.Height; y++)
            Marshal.Copy(pixels, y * stride, IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), stride);
        return bitmap;
    }

    private static byte[] BlurAlpha(byte[] pixels, int width, int height, int stride, int radius)
    {
        var alpha = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            alpha[y * width + x] = pixels[y * stride + x * 4 + 3];
        return BoxBlurSingle(alpha, width, height, radius);
    }

    private static byte[] BoxBlurSingle(byte[] pixels, int width, int height, int radius)
    {
        if (radius <= 0)
            return (byte[])pixels.Clone();
        var temp = new byte[pixels.Length];
        var result = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            var count = 0;
            for (var dx = -radius; dx <= radius; dx++)
            {
                var sx = Math.Clamp(x + dx, 0, width - 1);
                sum += pixels[y * width + sx];
                count++;
            }
            temp[y * width + x] = (byte)(sum / count);
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            var count = 0;
            for (var dy = -radius; dy <= radius; dy++)
            {
                var sy = Math.Clamp(y + dy, 0, height - 1);
                sum += temp[sy * width + x];
                count++;
            }
            result[y * width + x] = (byte)(sum / count);
        }
        return result;
    }

    private static byte[] EdgeColor(byte[] pixels, int width, int height, int stride, bool invert, int threshold)
    {
        var edges = PremultipliedBgraRasterEffects.Sobel(pixels, width, height, stride);
        var result = new byte[pixels.Length];
        for (var i = 0; i < result.Length; i += 4)
        {
            var e = edges[i / 4];
            if (threshold > 0) e = e > threshold ? (byte)0 : (byte)255;
            var factor = invert ? 1 - e / 255.0 : e / 255.0;
            result[i] = ToByte(pixels[i] / 255.0 * factor);
            result[i + 1] = ToByte(pixels[i + 1] / 255.0 * factor);
            result[i + 2] = ToByte(pixels[i + 2] / 255.0 * factor);
            result[i + 3] = pixels[i + 3];
        }
        return result;
    }

    private static int Luminance(byte[] pixels, int offset) =>
        (int)(0.2126 * pixels[offset + 2] + 0.7152 * pixels[offset + 1] + 0.0722 * pixels[offset] + 0.5);

    private static byte ToByte(double value) =>
        PremultipliedBgraRasterEffects.ToByte(value);

    private static byte Clamp255(int value) =>
        PremultipliedBgraRasterEffects.ClampByte(value);
}
