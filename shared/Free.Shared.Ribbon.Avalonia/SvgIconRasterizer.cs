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

        return RasterizeToPngCore(LoadFile(path), pixelSize, pixelSize, Stretch.Uniform);
    }

    /// <summary>
    /// Rasterizes an already parsed SVG into an explicitly sized PNG surface. The caller owns the
    /// aspect-ratio policy; this adapter owns only Avalonia drawing and PNG encoding.
    /// </summary>
    public static byte[] RasterizeToPng(DrawingImage drawing, int pixelWidth, int pixelHeight)
        => RasterizeToPngCore(drawing, pixelWidth, pixelHeight, Stretch.Fill);

    private static byte[] RasterizeToPngCore(
        DrawingImage drawing,
        int pixelWidth,
        int pixelHeight,
        Stretch stretch)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        if (pixelWidth is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var image = new Image
        {
            Source = drawing,
            Width = pixelWidth,
            Height = pixelHeight,
            Stretch = stretch,
        };
        var size = new Size(pixelWidth, pixelHeight);
        image.Measure(size);
        image.Arrange(new Rect(size));

        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        bitmap.Render(image);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        var bytes = stream.ToArray();
        if (bytes.Length == 0)
            throw new InvalidDataException("The selected SVG produced an empty PNG.");
        return bytes;
    }
}
