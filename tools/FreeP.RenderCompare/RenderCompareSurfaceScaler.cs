using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.RenderCompare;

internal readonly record struct RenderCompareSurfaceSize(int Width, int Height);

/// <summary>
/// Defines the fixed bitmap surface used by the Office-backed RenderCompare lane.
/// The live editor and app export ports preserve the slide ratio; this adapter exists
/// only because PowerPoint Slide.Export fills its caller-supplied width and height.
/// </summary>
internal static class RenderCompareSurfaceScaler
{
    internal static RenderCompareSurfaceSize ResolveNativeRenderSize(
        Presentation presentation,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var native = PresentationPdfScenePlanner.ResolveRasterSize(
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu,
            targetWidth,
            requestedHeightPx: null);
        return new RenderCompareSurfaceSize(native.WidthPx, native.HeightPx);
    }

    internal static byte[] StretchPngToSurface(
        byte[] sourcePng,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(sourcePng);
        var width = Math.Max(1, targetWidth);
        var height = Math.Max(1, targetHeight);

        using var source = new Bitmap(new MemoryStream(sourcePng, writable: false));
        if (source.PixelSize.Width == width && source.PixelSize.Height == height)
            return sourcePng;

        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            context.DrawImage(
                source,
                new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                new Rect(0, 0, width, height));
        }

        using var output = new MemoryStream();
        target.Save(output);
        return output.ToArray();
    }
}
