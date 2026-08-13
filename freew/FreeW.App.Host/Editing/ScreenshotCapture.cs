using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Screen-capture helpers backing Insert &gt; Illustrations &gt; Screenshot (Screen Clipping). The
/// region capture uses GDI+ <see cref="Graphics.CopyFromScreen(int, int, int, int, Size)"/> against the
/// virtual screen, encodes the result to PNG, and the bytes are inserted through the exact same
/// <see cref="DocumentView.InsertImage"/> path as Insert Picture. PNG decoding remains native while
/// portable image-model construction is delegated to the shared presentation factory.
/// </summary>
internal static class ScreenshotCapture
{
    /// <summary>
    /// Converts PNG bytes (e.g. a screen clip) to an <see cref="InlineImage"/>, deriving the point
    /// width/height from the PNG's pixel dimensions (96 DPI device-independent pixels → points) and
    /// applying the shared screen-clip insertion plan. The bytes are stored verbatim as
    /// <see cref="ImageFormat.Png"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The bytes are empty or not a decodable image.</exception>
    public static InlineImage PngToInlineImage(byte[] pngBytes)
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

        return ScreenClipImageFactory.Create(pngBytes, pixelWidth, pixelHeight);
    }

    /// <summary>
    /// Captures a rectangular screen region (in virtual-screen pixel coordinates) and encodes it as PNG.
    /// Returns <see langword="null"/> when the region is degenerate (zero/negative width or height) so a
    /// cancelled or empty drag inserts nothing.
    /// </summary>
    public static byte[]? CaptureRegionPng(System.Drawing.Rectangle region)
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
