using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Headless off-screen renderer: renders a <see cref="Slide"/> to a PNG byte array
/// using Avalonia's <see cref="RenderTargetBitmap"/> — no on-screen window
/// or WPF STA thread required.
///
/// Requires that the Avalonia application has been initialised with
/// <c>AppBuilder.Configure&lt;…&gt;().UseHeadless(…)</c> before calling these methods.
/// For production use from FreeP.App.Avalonia host (14B), call from the Avalonia
/// UI thread / dispatcher.
/// </summary>
public static class SlideRenderer
{
    /// <summary>
    /// Renders slide <paramref name="slideIndex"/> to an in-memory PNG byte array.
    /// </summary>
    public static byte[] RenderToBytes(
        Presentation presentation,
        int slideIndex,
        int widthPx,
        int heightPx)
        => RenderToBytesCore(presentation, slideIndex, widthPx, heightPx, includeCommentsAndInkMarkup: false);

    public static byte[] RenderToBytesWithPrintMarkup(
        Presentation presentation,
        int slideIndex,
        int widthPx,
        int heightPx,
        bool includeCommentsAndInkMarkup)
        => RenderToBytesCore(presentation, slideIndex, widthPx, heightPx, includeCommentsAndInkMarkup);

    private static byte[] RenderToBytesCore(
        Presentation presentation,
        int slideIndex,
        int widthPx,
        int heightPx,
        bool includeCommentsAndInkMarkup)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (slideIndex < 0 || slideIndex >= presentation.Slides.Count)
            throw new ArgumentOutOfRangeException(nameof(slideIndex));

        var slide = presentation.Slides[slideIndex];

        // Build the canvas, measure + arrange so it knows its render size.
        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide        = slide,
            SlideIndex   = slideIndex,
            RenderPrintMarkup = includeCommentsAndInkMarkup,
        };

        var size = new Size(widthPx, heightPx);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        // Off-screen rasterisation via Avalonia RenderTargetBitmap.
        var rtb = new RenderTargetBitmap(new PixelSize(widthPx, heightPx));
        rtb.Render(canvas);

        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}
