using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// SVG → <see cref="InlineImage"/> rasterizer for the Insert Picture command. Uses SharpVectors
/// (already referenced for ribbon-icon rendering) to parse the SVG into a WPF <see cref="Drawing"/>,
/// then renders it off-screen via <see cref="RenderTargetBitmap"/> and encodes the result as PNG bytes.
/// No new model fields are needed — the output is plain PNG bytes in <see cref="InlineImage"/>,
/// indistinguishable from any raster insert.
/// </summary>
internal static class SvgRasterizerHelper
{
    /// <summary>
    /// Rasterize SVG content from <paramref name="stream"/> into a PNG-encoded <see cref="InlineImage"/>.
    /// The stream is read from its current position. Same sizing rules as the file-path overload.
    /// Throws <see cref="InvalidOperationException"/> if SharpVectors cannot parse the content.
    /// </summary>
    public static InlineImage RasterizeToInlineImage(Stream stream)
    {
        // SharpVectors 1.8.5 has no stream reader — write to a temp file and use FileSvgReader.
        var tmp = Path.Combine(Path.GetTempPath(), $"freew_icon_{Guid.NewGuid():N}.svg");
        try
        {
            using (var fs = File.Create(tmp))
                stream.CopyTo(fs);
            return RasterizeToInlineImage(tmp);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    /// <summary>
    /// Rasterize the SVG file at <paramref name="path"/> into a PNG-encoded <see cref="InlineImage"/>.
    /// Aspect ratio, raster extent and document display size are planned by shared presentation code.
    /// Throws <see cref="InvalidOperationException"/> if SharpVectors cannot parse the file.
    /// </summary>
    public static InlineImage RasterizeToInlineImage(string path)
    {
        var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
        {
            IncludeRuntime = false,
            OptimizePath = true,
            TextAsGeometry = true
        };
        using var reader = new SharpVectors.Converters.FileSvgReader(settings);
        var drawing = reader.Read(path)
            ?? throw new InvalidOperationException("Could not parse the SVG file.");
        return RasterizeDrawing(drawing);
    }

    // ── Shared rasterization kernel ──────────────────────────────────────────────────────────────
    private static InlineImage RasterizeDrawing(System.Windows.Media.Drawing drawing)
    {
        var bounds = drawing.Bounds;
        var surface = PictureInsertionPlanner.BuildVectorRasterSurface(bounds.Width, bounds.Height);

        // Render WPF drawing into an off-screen bitmap.
        var drawingImage = new DrawingImage(drawing);
        drawingImage.Freeze();

        var rtb = new RenderTargetBitmap(surface.PixelWidth, surface.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawImage(drawingImage, new System.Windows.Rect(0, 0, surface.PixelWidth, surface.PixelHeight));
        }
        rtb.Render(dv);
        rtb.Freeze();

        using var buffer = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        encoder.Save(buffer);

        return PictureInsertionPlanner.CreatePngImage(
            buffer.ToArray(),
            surface.PixelWidth,
            surface.PixelHeight);
    }
}
