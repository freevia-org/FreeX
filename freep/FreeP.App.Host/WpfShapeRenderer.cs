using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Crop to the bounding box of the selected shapes (in slide-pixel coords).
        // Slide dimensions in EMU from presentation.
        double slideW = presentation.SlideSizeCxEmu;
        double slideH = presentation.SlideSizeCyEmu;

        if (slideW <= 0 || slideH <= 0)
            return EncodePng(rtb);

        double scaleX = widthPx  / slideW;
        double scaleY = heightPx / slideH;

        // Union rect of all selected shapes in pixel coordinates.
        double left   = double.MaxValue, top    = double.MaxValue;
        double right  = double.MinValue, bottom = double.MinValue;

        foreach (var s in shapes)
        {
            double sl = s.OffsetXEmu  * scaleX;
            double st = s.OffsetYEmu  * scaleY;
            double sr = sl + s.ExtentCxEmu * scaleX;
            double sb = st + s.ExtentCyEmu * scaleY;
            if (sl < left)   left   = sl;
            if (st < top)    top    = st;
            if (sr > right)  right  = sr;
            if (sb > bottom) bottom = sb;
        }

        // Clamp to canvas bounds.
        left   = Math.Max(0, left);
        top    = Math.Max(0, top);
        right  = Math.Min(widthPx,  right);
        bottom = Math.Min(heightPx, bottom);

        int cropW = (int)Math.Max(1, Math.Ceiling(right  - left));
        int cropH = (int)Math.Max(1, Math.Ceiling(bottom - top));

        if (cropW >= widthPx && cropH >= heightPx)
            return EncodePng(rtb);

        // Crop via CroppedBitmap.
        var cropped = new CroppedBitmap(rtb,
            new Int32Rect((int)left, (int)top, cropW, cropH));
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
