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
    public static int Write(
        PdfRasterDocument document,
        Stream stream,
        Action<XGraphics, PdfPage, int>? drawPageContent = null,
        Action<PdfDocument>? configureDocument = null,
        bool uncompressedContent = false)
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
                using (var image = XImage.FromBitmapSource(DecodeBitmap(rasterPage.ImageBytes)))
                    gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);

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

    /// <summary>Writes <paramref name="document"/> to an in-memory byte array.</summary>
    public static byte[] WriteToBytes(PdfRasterDocument document)
    {
        using var stream = new MemoryStream();
        Write(document, stream);
        return stream.ToArray();
    }

    private static BitmapSource DecodeBitmap(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
            throw new InvalidOperationException("A raster PDF page must carry encoded image bytes.");

        using var ms = new MemoryStream(imageBytes, writable: false);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static void DrawTextOverlays(XGraphics gfx, IReadOnlyList<PdfTextOverlay> overlays)
    {
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
                gfx.DrawString(overlay.Text, font, brush, point);
                continue;
            }

            var state = gfx.Save();
            gfx.RotateAtTransform(overlay.RotationDegrees, point);
            gfx.DrawString(overlay.Text, font, brush, point);
            gfx.Restore(state);
        }
    }

    private static void AddLinkAnnotations(PdfPage page, IReadOnlyList<PdfLinkOverlay> overlays)
    {
        foreach (var overlay in overlays)
        {
            if (overlay.Width <= 0 || overlay.Height <= 0)
                continue;

            var uri = overlay.Uri?.Trim();
            if (string.IsNullOrEmpty(uri))
                continue;

            var left = Math.Clamp(overlay.X, 0, page.Width.Point);
            var right = Math.Clamp(overlay.X + overlay.Width, 0, page.Width.Point);
            var top = Math.Clamp(page.Height.Point - overlay.Y, 0, page.Height.Point);
            var bottom = Math.Clamp(page.Height.Point - (overlay.Y + overlay.Height), 0, page.Height.Point);
            if (right <= left || top <= bottom)
                continue;

            var action = new PdfDictionary(page.Owner);
            action.Elements.SetName("/S", "/URI");
            action.Elements.SetString("/URI", uri);

            var annotation = new PdfDictionary(page.Owner);
            annotation.Elements.SetName("/Type", "/Annot");
            annotation.Elements.SetName("/Subtype", "/Link");
            annotation.Elements.SetRectangle("/Rect", new PdfRectangle(new XRect(left, bottom, right - left, top - bottom)));
            annotation.Elements.SetName("/H", "/I");
            annotation.Elements.SetInteger("/F", 4);
            annotation.Elements["/Border"] = CreateInvisibleBorder(page.Owner);
            annotation.Elements.SetString("/Contents", overlay.Tooltip ?? uri);
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
