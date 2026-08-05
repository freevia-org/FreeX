using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// WPF-backed implementation of <see cref="IShapeRenderer"/> for the OS-clipboard service.
///
/// Renders the given shapes onto a temporary <see cref="SlideCanvas"/> using an STA thread
/// (WPF requires it), bounding the render to the union rect of the selected shapes,
/// then encodes to PNG.
///
/// Fallback: if rendering fails (any exception) the method returns an empty byte array
/// so the OS-clipboard set degrades gracefully (text-only or nothing).
/// </summary>
public sealed class WpfShapeRenderer : IShapeRenderer
{
    /// <inheritdoc/>
    public byte[] RenderShapesToPng(
        Presentation              presentation,
        Slide                     slide,
        IReadOnlyList<SlideShape> shapes,
        int                       widthPx,
        int                       heightPx)
    {
        if (shapes.Count == 0) return Array.Empty<byte>();

        byte[]? result    = null;
        Exception? error  = null;

        // WPF rendering requires an STA thread.
        var thread = new Thread(() =>
        {
            try
            {
                result = RenderOnSta(presentation, slide, shapes, widthPx, heightPx);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null || result is null)
            return Array.Empty<byte>();

        return result;
    }

    private static byte[] RenderOnSta(
        Presentation              presentation,
        Slide                     slide,
        IReadOnlyList<SlideShape> shapes,
        int                       widthPx,
        int                       heightPx)
    {
        // Render the full slide at the requested pixel size.
        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide        = slide
        };

        canvas.Measure(new Size(widthPx, heightPx));
        canvas.Arrange(new Rect(0, 0, widthPx, heightPx));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        rtb.Freeze();

        var crop = PresentationClipboardShapeCropPlanner.Plan(
            presentation,
            shapes,
            widthPx,
            heightPx);
        if (crop.IsFullFrame(widthPx, heightPx))
            return EncodePng(rtb);

        // Crop via CroppedBitmap.
        var cropped = new CroppedBitmap(rtb,
            new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        cropped.Freeze();

        return EncodePng(cropped);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
