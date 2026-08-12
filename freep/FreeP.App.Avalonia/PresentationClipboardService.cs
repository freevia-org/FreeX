using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal interface IPresentationClipboardShapeRenderer
{
    byte[] RenderSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes);
}

internal sealed class AvaloniaClipboardShapeRenderer : IPresentationClipboardShapeRenderer
{
    private const int WidthPx = 1280;
    private const int HeightPx = 720;

    public byte[] RenderSelection(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes)
    {
        if (shapes.Count == 0)
            return [];

        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide = slide,
            SlideIndex = presentation.Slides.IndexOf(slide),
        };
        var fullSize = new Size(WidthPx, HeightPx);
        canvas.Measure(fullSize);
        canvas.Arrange(new Rect(fullSize));

        using var full = new RenderTargetBitmap(new PixelSize(WidthPx, HeightPx));
        full.Render(canvas);

        var crop = PresentationClipboardShapeCropPlanner.Plan(
            presentation,
            shapes,
            WidthPx,
            HeightPx);
        if (crop.IsFullFrame(WidthPx, HeightPx))
            return Save(full);

        var cropped = new CroppedBitmap(
            full,
            new PixelRect(crop.X, crop.Y, crop.Width, crop.Height));
        var image = new Image
        {
            Source = cropped,
            Width = crop.Width,
            Height = crop.Height,
            Stretch = Stretch.Fill,
        };
        var cropSize = new Size(crop.Width, crop.Height);
        image.Measure(cropSize);
        image.Arrange(new Rect(cropSize));

        using var output = new RenderTargetBitmap(new PixelSize(crop.Width, crop.Height));
        output.Render(image);
        return Save(output);
    }

    private static byte[] Save(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    internal static byte[]? NormalizePng(byte[]? pngBytes)
    {
        if (pngBytes is not { Length: > 0 })
            return pngBytes;

        try
        {
            using var bitmap = new Bitmap(new MemoryStream(pngBytes, writable: false));
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
