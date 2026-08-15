using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Native GDI+ capture and PNG decoding for Insert > Illustrations > Screenshot.
/// Portable image construction and insertion sequencing belong to the presentation workflow.
/// </summary>
internal static class ScreenshotCapture
{
    /// <summary>Decodes PNG dimensions into a toolkit-neutral capture payload.</summary>
    /// <exception cref="ArgumentException">The bytes are empty or not a decodable image.</exception>
    public static ScreenClipCapture PngToCapture(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
            throw new ArgumentException("Screenshot bytes are empty.", nameof(pngBytes));

        int pixelWidth;
        int pixelHeight;
        try
        {
            using var stream = new MemoryStream(pngBytes, writable: false);
            using var image = Image.FromStream(stream);
            pixelWidth = image.Width;
            pixelHeight = image.Height;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            throw new ArgumentException("Screenshot bytes are not a valid image.", nameof(pngBytes), ex);
        }

        return new ScreenClipCapture(pngBytes, pixelWidth, pixelHeight);
    }

    public static ScreenClipCapture? CaptureRegion(System.Drawing.Rectangle region)
    {
        var pngBytes = CaptureRegionPng(region);
        return pngBytes is null
            ? null
            : PngToCapture(pngBytes);
    }

    /// <summary>
    /// Captures a virtual-screen pixel rectangle and encodes it as PNG, or returns null for an empty region.
    /// </summary>
    private static byte[]? CaptureRegionPng(System.Drawing.Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            return null;

        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.X, region.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }

        using var buffer = new MemoryStream();
        bitmap.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
        return buffer.ToArray();
    }
}
