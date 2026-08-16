using Free.Shared.Pdf;
using SkiaSharp;

namespace Free.Shared.Pdf.Skia;

/// <summary>SkiaSharp PDF backend for the shared raster page model.</summary>
public static class SkiaRasterPdfWriter
{
    /// <param name="imageDiagnostics">
    /// Optional sink for non-fatal image warnings. A page whose raster image bytes this backend
    /// cannot decode (e.g. corrupt or truncated bytes) is rendered as a blank page rather than
    /// failing the whole export; when this collection is supplied, one message per blanked page is
    /// appended to it so the loss is discoverable instead of silent. Mirrors
    /// <see cref="SkiaPdfWriter.Write"/>'s diagnostic policy so the shared PDF writers agree.
    /// </param>
    public static int Write(PdfRasterDocument document, Stream stream, ICollection<string>? imageDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("PDF export requires at least one rendered page.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        var metadata = BuildMetadata(document.Properties);
        var pageCount = 0;
        using (var pdf = SKDocument.CreatePdf(stream, metadata))
        using (var typefaces = SkiaPdfWriter.PdfTypefaceSet.Create())
        using (var textRenderer = new SkiaPdfWriter.FallbackTextRenderer())
        using (var overlayPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            foreach (var page in document.Pages)
            {
                var image = TryDecodeImage(page.ImageBytes, pageCount + 1, imageDiagnostics);
                var canvas = pdf.BeginPage((float)page.WidthPoints, (float)page.HeightPoints);
                canvas.Clear(SKColors.White);
                if (image is not null)
                {
                    using (image)
                    {
                        canvas.DrawImage(
                            image,
                            new SKRect(0, 0, (float)page.WidthPoints, (float)page.HeightPoints));
                    }
                }
                if (page.TextOverlays is { Count: > 0 } textOverlays)
                    DrawTextOverlays(canvas, textOverlays, typefaces, textRenderer, overlayPaint);
                if (page.LinkOverlays is { Count: > 0 })
                    AddLinkAnnotations(canvas, page);
                pdf.EndPage();
                pageCount++;
            }

            pdf.Close();
        }

        return pageCount;
    }

    // Draws each overlay's real, embedded-font text (reusing SkiaPdfWriter's vector-text machinery,
    // the same one already trusted for FreeP's notes-page/handout PDF text) at a near-zero alpha: Skia's
    // canvas API has no PDF text-render-mode-3 ("invisible but selectable") setter the way PDFsharp's
    // raw content-stream injection does, so this is the closest equivalent -- a real Tj-emitting text
    // draw that is imperceptible to a reader but still present, embedded, and ToUnicode-mapped for
    // search/select/screen-reader tools. See PdfTextOverlay's doc comment for the coordinate space
    // (top-left, y-down points) -- Skia's PDF canvas already uses that same space, so X/Y need no flip.
    private const byte OverlayAlpha = 1;

    private static void DrawTextOverlays(
        SKCanvas canvas,
        IReadOnlyList<PdfTextOverlay> overlays,
        SkiaPdfWriter.PdfTypefaceSet typefaces,
        SkiaPdfWriter.FallbackTextRenderer textRenderer,
        SKPaint overlayPaint)
    {
        foreach (var overlay in overlays)
        {
            if (string.IsNullOrEmpty(overlay.Text))
                continue;

            var face = (overlay.Bold, overlay.Italic) switch
            {
                (true, true) => PdfFontFace.BoldItalic,
                (true, false) => PdfFontFace.Bold,
                (false, true) => PdfFontFace.Italic,
                _ => PdfFontFace.Regular,
            };
            var typeface = typefaces.For(overlay.FontFamily, face);
            var baseline = (float)(overlay.Y + overlay.FontSize);
            overlayPaint.Color = new SKColor(overlay.Color.R, overlay.Color.G, overlay.Color.B, OverlayAlpha);

            if (Math.Abs(overlay.RotationDegrees) < 0.0001)
            {
                textRenderer.DrawText(canvas, overlay.Text, (float)overlay.X, baseline, typeface, (float)overlay.FontSize, overlayPaint);
                continue;
            }

            var saveCount = canvas.Save();
            canvas.RotateDegrees((float)overlay.RotationDegrees, (float)overlay.X, baseline);
            textRenderer.DrawText(canvas, overlay.Text, (float)overlay.X, baseline, typeface, (float)overlay.FontSize, overlayPaint);
            canvas.RestoreToCount(saveCount);
        }
    }

    // Deliberately kept single-parameter (no imageDiagnostics pass-through, unlike the overload
    // below): FreeP's Avalonia (Skia) shell binds this method as a bare method-group to the shared
    // single-parameter PresentationRasterPdfWriter delegate (see FreeP.App.Avalonia's MainWindow.cs
    // and PresentationPrintOutputPackageExecutor.BuildPackage's writeRasterPdf parameter), and C#
    // method-group-to-delegate conversion does not permit dropping a trailing optional parameter --
    // adding one directly to this overload would break that binding at compile time. Mirrors
    // SkiaPdfWriter.WriteToBytesWithPortableFallback's documented rationale for the same constraint.
    public static byte[] WriteToBytes(PdfRasterDocument document)
    {
        using var stream = new MemoryStream();
        Write(document, stream);
        return stream.ToArray();
    }

    /// <param name="imageDiagnostics">See <see cref="Write"/>.</param>
    public static byte[] WriteToBytes(PdfRasterDocument document, ICollection<string>? imageDiagnostics)
    {
        using var stream = new MemoryStream();
        Write(document, stream, imageDiagnostics);
        return stream.ToArray();
    }

    // R137: FreeP's raster PDF export (its default File > Export as PDF / Print > Full Page Slides
    // path) carries hyperlinks as PdfRasterPage.LinkOverlays, but this writer -- unlike
    // WpfRasterPdfWriter, which already implemented external-URI annotations -- silently dropped them
    // entirely, so Avalonia-exported PDFs had zero clickable links where WPF-exported ones at least
    // had external ones. Mirrors WpfRasterPdfWriter.AddLinkAnnotations' scope: external URI targets
    // only. Internal (slide-to-slide) targets are intentionally skipped here -- PdfRasterDocument has
    // no cross-page named-destination table the way PdfContentDocument does (see
    // SkiaPdfWriter.AddNamedDestinations/AddLinkAnnotations), so a DestinationName-only overlay has no
    // page to resolve to on this backend yet.
    private static void AddLinkAnnotations(SKCanvas canvas, PdfRasterPage page)
    {
        foreach (var link in PdfAnnotationPlanner.BuildLinkAnnotations(page.WidthPoints, page.HeightPoints, page.LinkOverlays))
        {
            if (string.IsNullOrEmpty(link.Uri))
                continue;

            var rect = new SKRect((float)link.Left, (float)link.Top, (float)link.Right, (float)link.Bottom);
            canvas.DrawUrlAnnotation(rect, link.Uri);
        }
    }

    private static SKImage? TryDecodeImage(byte[] imageBytes, int pageNumber, ICollection<string>? imageDiagnostics)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            imageDiagnostics?.Add(
                $"Page {pageNumber} carried no image bytes and was rendered blank in the exported PDF.");
            return null;
        }

        using var data = SKData.CreateCopy(imageBytes);
        var image = SKImage.FromEncodedData(data);
        if (image is null)
        {
            // Bytes Skia's native decoder rejects (corrupt, truncated, or an unrecognized format).
            // One bad page image must not abort the whole export -- render that page blank instead
            // and surface the loss, matching PortablePdfWriter's/SkiaPdfWriter's image-decode policy.
            imageDiagnostics?.Add(
                $"Page {pageNumber} image could not be decoded and was rendered blank in the exported PDF.");
        }

        return image;
    }

    private static SKDocumentPdfMetadata BuildMetadata(PdfDocumentProperties? properties)
    {
        var metadata = new SKDocumentPdfMetadata();
        if (properties is null)
            return metadata;

        if (!string.IsNullOrWhiteSpace(properties.Title))
            metadata.Title = properties.Title;
        if (!string.IsNullOrWhiteSpace(properties.Author))
            metadata.Author = properties.Author;
        if (!string.IsNullOrWhiteSpace(properties.Subject))
            metadata.Subject = properties.Subject;
        if (!string.IsNullOrWhiteSpace(properties.Keywords))
            metadata.Keywords = properties.Keywords;
        if (!string.IsNullOrWhiteSpace(properties.Creator))
            metadata.Creator = properties.Creator;
        return metadata;
    }
}
