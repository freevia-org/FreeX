using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Avalonia bitmap adapter for the shared premultiplied-BGRA picture pipeline.
/// </summary>
internal static partial class AvaloniaImageAdjustHelper
{
    public static Bitmap Apply(Bitmap source, InlineImage image) =>
        ApplyWithBounds(source, image).Bitmap;

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
                owned?.Dispose();
                current = next;
                owned = current;
            }

            if (HasRasterEffects(image))
            {
                var next = ApplyPictureEffects(current, image);
                owned?.Dispose();
                current = next.Bitmap;
                sourcePixelRect = next.SourcePixelRect;
                owned = current;
            }

            // Ownership transfers to DocumentView's bitmap cache.
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
        // The pointer overload preserves source premultiplied channel bytes without backend conversion.
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
            colorTemperature,
            PremultipliedTransparencyChannelSource.SourceChannels);

        var output = new WriteableBitmap(size, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var framebuffer = output.Lock())
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    pixels,
                    y * stride,
                    IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes),
                    stride);
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
        double colorTemperature,
        PremultipliedTransparencyChannelSource transparencyChannelSource =
            PremultipliedTransparencyChannelSource.SourceChannels) =>
        PremultipliedBgraRasterEffects.ApplyAdjustmentsInPlace(
            pixels,
            brightnessPct,
            contrastPct,
            saturationPct,
            transparencyPct,
            recolorMode,
            colorTemperature,
            transparencyChannelSource);
}
