using System.IO;
using System.Windows.Media.Imaging;
using Free.Shared.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Free.Shared.Pdf.Wpf;

/// <summary>
/// WPF/PDFsharp backend for the shared raster page model. Each <see cref="PdfRasterPage"/> carries a
/// pre-rendered bitmap (the host rasterizes its laid-out page on the UI thread) plus optional
/// selectable-text and link overlays; this writer places the bitmap and overlays onto PDFsharp
/// pages and stamps the document metadata.
///
/// <para>
/// This is the shared core of FreeX's and FreeW's host PDF export: both apps rasterize page visuals
/// to bitmaps and composite them with PDFsharp. Overlay coordinates are in the page's top-left,
/// y-down point space; PDFsharp's user space is also top-left/points, so they map directly.
/// </para>
/// </summary>
public static class WpfRasterPdfWriter
{
    /// <summary>Writes <paramref name="document"/> to <paramref name="stream"/>; returns the page count.</summary>
    /// <param name="drawPageContent">
    /// Optional per-page hook invoked after the raster image is placed but before the selectable-text
    /// overlays, so a host can paint extra vector content (e.g. FreeX gridline/border/shape overlays)
    /// beneath the text/link layers. Arguments are the page's <see cref="XGraphics"/> (user space in
    /// points), the <see cref="PdfPage"/>, and the zero-based page index.
    /// </param>
    /// <param name="configureDocument">
    /// Optional document-level hook invoked after every page has been drawn and overlays added, but
    /// before the PDF is saved. A host uses it to stamp catalog/viewer extras the neutral model does not
    /// cover (bookmarks/outlines, viewer preferences, <c>/Lang</c>, internal cross-page link
    /// destinations, etc.).
    /// </param>
    /// <param name="uncompressedContent">
    /// When <see langword="true"/>, content-stream compression is disabled even if the document carries
    /// no overlays. Hosts pass this when the output must stay greppable/inspectable (e.g. FreeX's
    /// selectable-text export, whose vector-only pages would otherwise be Flate-compressed).
    /// </param>
    /// <param name="imageDiagnostics">
    /// Optional sink for non-fatal image warnings. A page whose raster image bytes this backend
    /// cannot decode (e.g. corrupt or truncated bytes, or a format <see cref="BitmapDecoder"/> does
    /// not recognize) is rendered as a blank page rather than failing the whole export; when this
    /// collection is supplied, one message per blanked page is appended to it so the loss is
    /// discoverable instead of silent. Mirrors <see cref="Free.Shared.Pdf.PortablePdfWriter"/>'s and
    /// <c>SkiaPdfWriter</c>'s image-decode diagnostic policy so the shared PDF writers agree.
    /// </param>
    public static int Write(
        PdfRasterDocument document,
        Stream stream,
        Action<XGraphics, PdfPage, int>? drawPageContent = null,
        Action<PdfDocument>? configureDocument = null,
        bool uncompressedContent = false,
        ICollection<string>? imageDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("PDF export requires at least one rendered page.");

        using var pdf = new PdfDocument();
        ApplyProperties(pdf, document.Properties);

        var hasOverlays = document.Pages.Any(p =>
            (p.TextOverlays is { Count: > 0 }) || (p.LinkOverlays is { Count: > 0 }));
        if (hasOverlays || uncompressedContent)
            pdf.Options.CompressContentStreams = false;

        for (var i = 0; i < document.Pages.Count; i++)
        {
            var rasterPage = document.Pages[i];
            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(rasterPage.WidthPoints);
            page.Height = XUnit.FromPoint(rasterPage.HeightPoints);

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var bitmap = TryDecodeBitmap(rasterPage.ImageBytes, i + 1, imageDiagnostics);
                if (bitmap is not null)
                {
                    using var image = XImage.FromBitmapSource(bitmap);
                    gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
                }

                drawPageContent?.Invoke(gfx, page, i);

                if (rasterPage.TextOverlays is { Count: > 0 } textOverlays)
                    DrawTextOverlays(gfx, textOverlays);
            }

            if (rasterPage.LinkOverlays is { Count: > 0 } linkOverlays)
                AddLinkAnnotations(page, linkOverlays);
        }

        configureDocument?.Invoke(pdf);

        var pageCount = pdf.PageCount;
        pdf.Save(stream);
        return pageCount;
    }

    /// <summary>Writes <paramref name="document"/> to a file path (creating directories as needed).</summary>
    public static int Save(PdfRasterDocument document, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        return Write(document, stream);
    }

    // Deliberately kept single-parameter (no imageDiagnostics pass-through, unlike the overload
    // below): FreeP's WPF shell binds this method as a bare method-group to the shared
    // single-parameter PresentationRasterPdfWriter delegate (see FreeP.App.Host's FileCommands.cs
    // and PresentationPrintOutputPackageExecutor.BuildPackage's writeRasterPdf parameter), and C#
    // method-group-to-delegate conversion does not permit dropping a trailing optional parameter --
    // adding one directly to this overload would break that binding at compile time. Mirrors
    // SkiaPdfWriter.WriteToBytesWithPortableFallback's and SkiaRasterPdfWriter.WriteToBytes's
    // documented rationale for the same constraint.
    /// <summary>Writes <paramref name="document"/> to an in-memory byte array.</summary>
    public static byte[] WriteToBytes(PdfRasterDocument document)
    {
        using var stream = new MemoryStream();
        Write(document, stream);
        return stream.ToArray();
    }

    /// <summary>Writes <paramref name="document"/> to an in-memory byte array.</summary>
    /// <param name="imageDiagnostics">See <see cref="Write"/>.</param>
    public static byte[] WriteToBytes(PdfRasterDocument document, ICollection<string>? imageDiagnostics)
    {
        using var stream = new MemoryStream();
        Write(document, stream, imageDiagnostics: imageDiagnostics);
        return stream.ToArray();
    }

    private static BitmapSource? TryDecodeBitmap(byte[] imageBytes, int pageNumber, ICollection<string>? imageDiagnostics)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            imageDiagnostics?.Add(
                $"Page {pageNumber} carried no image bytes and was rendered blank in the exported PDF.");
            return null;
        }

        try
        {
            using var ms = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (IsRecoverableImageDecodeException(ex))
        {
            // BitmapDecoder.Create throws (rather than returning null/failing gracefully) for bytes
            // it cannot decode -- corrupt/truncated data for a nominally-supported format, or a
            // format the installed WIC codecs do not recognize at all. One bad page image must not
            // abort the whole export -- render that page blank instead and surface the loss,
            // matching PortablePdfWriter's/SkiaPdfWriter's/SkiaRasterPdfWriter's image-decode policy.
            imageDiagnostics?.Add(
                $"Page {pageNumber} image could not be decoded and was rendered blank in the exported PDF: {ex.Message}");
            return null;
        }
    }

    private static bool IsRecoverableImageDecodeException(Exception ex) =>
        ex is FormatException
            or NotSupportedException
            or ArgumentException
            or IOException
            or System.Runtime.InteropServices.COMException;

    private static void DrawTextOverlays(XGraphics gfx, IReadOnlyList<PdfTextOverlay> overlays)
    {
        // Overlays must be invisible (PDF text render mode 3, "Tr 3") so the searchable/selectable
        // text does not double-draw on top of the already-rendered raster glyphs. PDFsharp's
        // XGraphics API has no render-mode setter, so we inject the raw "Tr" operator directly into
        // the page's content stream around each DrawString call, then restore fill mode (0 Tr)
        // immediately after so nothing else drawn later on the page is accidentally hidden.
        var content = gfx.Internals.ContentStringBuilder
            ?? throw new InvalidOperationException("PDF text overlay rendering requires a content stream.");

        foreach (var overlay in overlays)
        {
            var style = XFontStyleEx.Regular;
            if (overlay.Bold && overlay.Italic)
                style = XFontStyleEx.BoldItalic;
            else if (overlay.Bold)
                style = XFontStyleEx.Bold;
            else if (overlay.Italic)
                style = XFontStyleEx.Italic;

            var font = new XFont(overlay.FontFamily, overlay.FontSize, style);
            var brush = new XSolidBrush(XColor.FromArgb(255, overlay.Color.R, overlay.Color.G, overlay.Color.B));
            var point = new XPoint(overlay.X, overlay.Y + overlay.FontSize);
            if (Math.Abs(overlay.RotationDegrees) < 0.0001)
            {
                content.Append("3 Tr\n");
                gfx.DrawString(overlay.Text, font, brush, point);
                content.Append("0 Tr\n");
                continue;
            }

            var state = gfx.Save();
            gfx.RotateAtTransform(overlay.RotationDegrees, point);
            content.Append("3 Tr\n");
            gfx.DrawString(overlay.Text, font, brush, point);
            content.Append("0 Tr\n");
            gfx.Restore(state);
        }
    }

    private static void AddLinkAnnotations(PdfPage page, IReadOnlyList<PdfLinkOverlay> overlays)
    {
        foreach (var link in PdfAnnotationPlanner.BuildLinkAnnotations(
                     page.Width.Point,
                     page.Height.Point,
                     overlays))
        {
            if (string.IsNullOrEmpty(link.Uri))
                continue;

            var top = page.Height.Point - link.Top;
            var bottom = page.Height.Point - link.Bottom;

            var action = new PdfDictionary(page.Owner);
            action.Elements.SetName("/S", "/URI");
            action.Elements.SetString("/URI", link.Uri);

            var annotation = new PdfDictionary(page.Owner);
            annotation.Elements.SetName("/Type", "/Annot");
            annotation.Elements.SetName("/Subtype", "/Link");
            annotation.Elements.SetRectangle(
                "/Rect",
                new PdfRectangle(new XRect(link.Left, bottom, link.Right - link.Left, top - bottom)));
            annotation.Elements.SetName("/H", "/I");
            annotation.Elements.SetInteger("/F", 4);
            annotation.Elements["/Border"] = CreateInvisibleBorder(page.Owner);
            annotation.Elements.SetString("/Contents", link.Tooltip ?? link.Uri);
            annotation.Elements["/A"] = action;

            var annots = page.Elements.GetArray("/Annots");
            if (annots is null)
            {
                annots = new PdfArray(page.Owner);
                page.Elements["/Annots"] = annots;
            }

            annots.Elements.Add(annotation);
        }
    }

    private static PdfArray CreateInvisibleBorder(PdfDocument owner)
    {
        var border = new PdfArray(owner);
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        return border;
    }

    private static void ApplyProperties(PdfDocument pdf, PdfDocumentProperties? properties)
    {
        if (Normalize(properties?.Creator) is { } creator)
            pdf.Info.Creator = creator;
        if (properties is null)
            return;

        if (Normalize(properties.Title) is { } title)
            pdf.Info.Title = title;
        if (Normalize(properties.Author) is { } author)
            pdf.Info.Author = author;
        if (Normalize(properties.Subject) is { } subject)
            pdf.Info.Subject = subject;
        if (Normalize(properties.Keywords) is { } keywords)
            pdf.Info.Keywords = keywords;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
