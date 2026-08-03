using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>Renders the shared SVG drawing model to real cross-platform PNG bytes.</summary>
public static class SvgIconRasterizer
{
    /// <summary>Loads an SVG as a native Avalonia drawing for vector UI presentation.</summary>
    public static DrawingImage LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return SvgIconParser.TryParseFile(path)
            ?? throw new InvalidDataException($"The selected SVG cannot be rendered: {path}");
    }

    /// <summary>
    /// Loads an SVG using only its painted bounds. WPF's thumbnail rasterizer draws a
    /// <c>DrawingImage</c> into the requested bitmap rectangle, so narrow artwork is
    /// expanded to that rectangle instead of retaining transparent viewBox margins.
    /// </summary>
    public static DrawingImage LoadFileToPaintedBounds(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return SvgIconParser.TryParseFile(path, monochromeBrush: null, includeViewBoxBounds: false)
            ?? throw new InvalidDataException($"The selected SVG cannot be rendered: {path}");
    }

    public static byte[] RasterizeFileToPng(string path, int pixelSize = 128)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (pixelSize is < 16 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));

        var drawing = LoadFile(path);
        var image = new Image
        {
            Source = drawing,
            Width = pixelSize,
            Height = pixelSize,
            Stretch = Stretch.Uniform,
        };
        var size = new Size(pixelSize, pixelSize);
        image.Measure(size);
        image.Arrange(new Rect(size));

        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelSize, pixelSize), new Vector(96, 96));
        bitmap.Render(image);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        var bytes = stream.ToArray();
        if (bytes.Length == 0)
            throw new InvalidDataException($"The selected SVG produced an empty PNG: {path}");
        return bytes;
    }
}
