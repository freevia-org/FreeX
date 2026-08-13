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
    // Default raster resolution for SVGs that carry no explicit pixel dimensions.
    private const int DefaultPx = 400;

    /// <summary>
    /// Rasterize SVG content from <paramref name="stream"/> into a PNG-encoded <see cref="InlineImage"/>.
    /// The stream is read from its current position. Same sizing rules as the file-path overload.
    /// Throws <see cref="InvalidOperationException"/> if SharpVectors cannot parse the content.
    /// </summary>
    public static InlineImage RasterizeToInlineImage(Stream stream, int maximumPixelEdge = DefaultPx)
    {
        // SharpVectors 1.8.5 has no stream reader — write to a temp file and use FileSvgReader.
        using var temporaryFile = TemporaryFileLease.Create("freew_icon_", ".svg");
        using (var output = temporaryFile.OpenWrite())
            stream.CopyTo(output);
        return RasterizeToInlineImage(temporaryFile.Path, maximumPixelEdge);
    }

    /// <summary>
    /// Rasterize the SVG file at <paramref name="path"/> into a PNG-encoded <see cref="InlineImage"/>.
    /// Shared presentation policy owns the raster extent and document display size.
    /// Throws <see cref="InvalidOperationException"/> if SharpVectors cannot parse the file.
    /// </summary>
    public static InlineImage RasterizeToInlineImage(string path, int maximumPixelEdge = DefaultPx)
    {
        if (maximumPixelEdge <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPixelEdge));

        var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
        {
            IncludeRuntime = false,
            OptimizePath = true,
            TextAsGeometry = true
        };
        using var reader = new SharpVectors.Converters.FileSvgReader(settings);
        var drawing = reader.Read(path)
            ?? throw new InvalidOperationException("Could not parse the SVG file.");
        return RasterizeDrawing(drawing, maximumPixelEdge);
    }

    // ── Shared rasterization kernel ──────────────────────────────────────────────────────────────
    private static InlineImage RasterizeDrawing(
        System.Windows.Media.Drawing drawing,
        int maximumPixelEdge)
    {
        // Determine natural SVG size from the drawing bounds or fall back to DefaultPx × DefaultPx.
        var bounds = drawing.Bounds;
        var surface = PictureInsertionPlanner.BuildVectorRasterSurface(
            bounds.Width,
            bounds.Height,
            maximumPixelEdge);

        // Render WPF drawing into an off-screen bitmap.
        var drawingImage = new DrawingImage(drawing);
        drawingImage.Freeze();

        var rtb = new RenderTargetBitmap(
            surface.PixelWidth,
            surface.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawImage(
                drawingImage,
                new System.Windows.Rect(0, 0, surface.PixelWidth, surface.PixelHeight));
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
