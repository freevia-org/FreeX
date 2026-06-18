using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// App-neutral WPF rendering primitives shared by screenshot-tour systems: capture a visual
/// (optionally cropped to a height) or an element to a PNG on disk, and compute the device DPI.
/// The *tour logic* — what to render, in which order, with what foreground/focus guards and
/// manifest schema — stays app-specific; these are just the render/crop/encode/write mechanics
/// so a sister app's tours do not reinvent them.
/// </summary>
public static class ScreenshotCapture
{
    /// <summary>The device-pixel DPI scale of <paramref name="visual"/> (1.0 when not yet sourced).</summary>
    public static (double X, double Y) DeviceDpiScale(Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        return (dpiX, dpiY);
    }

    /// <summary>
    /// Renders <paramref name="visual"/> at its current size (clamped to <paramref name="logicalHeight"/>
    /// when provided) and writes <paramref name="fileName"/>.png into <paramref name="outputDir"/>.
    /// Mirrors the window-capture primitive: render-to-bitmap, crop, PNG-encode.
    /// </summary>
    public static async Task CaptureVisualToPngAsync(
        FrameworkElement visual,
        string outputDir,
        string fileName,
        double? logicalHeight = null)
    {
        var (dpiX, dpiY) = DeviceDpiScale(visual);
        var logicalHeightToUse = logicalHeight is { } height
            ? Math.Min(visual.ActualHeight, height)
            : visual.ActualHeight;
        int pw = Math.Max(1, (int)(visual.ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(logicalHeightToUse * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        rtb.Render(visual);
        BitmapSource bitmap = logicalHeight is null
            ? rtb
            : new CroppedBitmap(rtb, new Int32Rect(0, 0, pw, ph));

        await WritePngAsync(bitmap, outputDir, fileName).ConfigureAwait(true);
    }

    /// <summary>
    /// Renders <paramref name="element"/> through a <see cref="VisualBrush"/> (so it captures even when
    /// it is not the rendered root) and writes <paramref name="fileName"/>.png into <paramref name="outputDir"/>.
    /// Mirrors the element-capture primitive.
    /// </summary>
    public static async Task CaptureElementToPngAsync(
        FrameworkElement element,
        string outputDir,
        string fileName)
    {
        element.UpdateLayout();

        var (dpiX, dpiY) = DeviceDpiScale(element);
        int pw = Math.Max(1, (int)(element.ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(element.ActualHeight * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        var drawingVisual = new DrawingVisual();
        using (var context = drawingVisual.RenderOpen())
        {
            var brush = new VisualBrush(element) { Stretch = Stretch.Fill };
            context.DrawRectangle(brush, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        rtb.Render(drawingVisual);
        await WritePngAsync(rtb, outputDir, fileName).ConfigureAwait(true);
    }

    /// <summary>PNG-encodes <paramref name="bitmap"/> and writes it to <paramref name="outputDir"/>/<paramref name="fileName"/>.png.</summary>
    public static async Task WritePngAsync(BitmapSource bitmap, string outputDir, string fileName)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(outputDir, $"{fileName}.png");
        await using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
