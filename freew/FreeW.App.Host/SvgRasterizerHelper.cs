using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private const double PxPerPoint = 96.0 / 72.0;
    private const double MaxWidthPt = 400;
    // Default raster resolution for SVGs that carry no explicit pixel dimensions.
    private const int DefaultPx = 400;

    /// <summary>
    /// Rasterize SVG content from <paramref name="stream"/> into a PNG-encoded <see cref="InlineImage"/>.
    /// The stream is read from its current position. Same sizing rules as the file-path overload.
    /// Throws <see cref="InvalidOperationException"/> if SharpVectors cannot parse the content.
    /// </summary>
    public static InlineImage RasterizeToInlineImage(Stream stream)
    {
        // SharpVectors 1.8.5 has no stream reader — write to a temp file and use FileSvgReader.
        using var temporaryFile = TemporaryFileLease.Create("freew_icon_", ".svg");
        using (var output = temporaryFile.OpenWrite())
            stream.CopyTo(output);
        return RasterizeToInlineImage(temporaryFile.Path);
    }

    /// <summary>
    /// Rasterize the SVG file at <paramref name="path"/> into a PNG-encoded <see cref="InlineImage"/>.
    /// Aspect ratio is preserved; width is capped at <c>MaxWidthPt</c> (400 pt = ~5.6 in).
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
        // Determine natural SVG size from the drawing bounds or fall back to DefaultPx × DefaultPx.
        var bounds = drawing.Bounds;
        double srcW = bounds.IsEmpty || bounds.Width <= 0 ? DefaultPx : bounds.Width;
        double srcH = bounds.IsEmpty || bounds.Height <= 0 ? DefaultPx : bounds.Height;

        // Scale so the wider dimension is DefaultPx, preserving aspect ratio.
        double scale = DefaultPx / Math.Max(srcW, srcH);
        int pxW = Math.Max(1, (int)Math.Round(srcW * scale));
        int pxH = Math.Max(1, (int)Math.Round(srcH * scale));

        // Render WPF drawing into an off-screen bitmap.
        var drawingImage = new DrawingImage(drawing);
        drawingImage.Freeze();

        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var ctx = dv.RenderOpen())
        {
            ctx.DrawImage(drawingImage, new System.Windows.Rect(0, 0, pxW, pxH));
        }
        rtb.Render(dv);
        rtb.Freeze();

        using var buffer = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        encoder.Save(buffer);

        // Cap width at MaxWidthPt, preserving the rasterized aspect ratio.
        var widthPt = pxW / PxPerPoint;
        var heightPt = pxH / PxPerPoint;
        if (widthPt > MaxWidthPt && widthPt > 0)
        {
            heightPt *= MaxWidthPt / widthPt;
            widthPt = MaxWidthPt;
        }
        return new InlineImage(buffer.ToArray(), widthPt, heightPt)
        {
            OriginalPixelWidth  = pxW,
            OriginalPixelHeight = pxH,
        };
    }
}
