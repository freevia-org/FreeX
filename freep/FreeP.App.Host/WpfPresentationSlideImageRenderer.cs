using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal static class WpfPresentationSlideImageRenderer
{
    public static byte[] RenderSlideToPng(Presentation presentation, int slideIndex, int widthPx, int heightPx)
        => RenderSlideToPngCore(presentation, slideIndex, widthPx, heightPx, includeCommentsAndInkMarkup: false);

    public static byte[] RenderSlideToPngWithPrintMarkup(
        Presentation presentation,
        int slideIndex,
        int widthPx,
        int heightPx,
        bool includeCommentsAndInkMarkup)
        => RenderSlideToPngCore(presentation, slideIndex, widthPx, heightPx, includeCommentsAndInkMarkup);

    private static byte[] RenderSlideToPngCore(
        Presentation presentation,
        int slideIndex,
        int widthPx,
        int heightPx,
        bool includeCommentsAndInkMarkup)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (slideIndex < 0 || slideIndex >= presentation.Slides.Count)
            throw new ArgumentOutOfRangeException(nameof(slideIndex));

        byte[]? result = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = RenderOnSta(presentation, slideIndex, widthPx, heightPx, includeCommentsAndInkMarkup);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();

        return result ?? Array.Empty<byte>();
    }

    private static byte[] RenderOnSta(
        Presentation presentation,
        int slideIndex,
        int widthPx,
        int heightPx,
        bool includeCommentsAndInkMarkup)
    {
        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide = presentation.Slides[slideIndex],
            RenderPrintMarkup = includeCommentsAndInkMarkup,
            // Keep exported images free of editor-only gridline and guide aids.
            RenderViewAidsEnabled = false,
        };

        canvas.Measure(new Size(widthPx, heightPx));
        canvas.Arrange(new Rect(0, 0, widthPx, heightPx));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        rtb.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
