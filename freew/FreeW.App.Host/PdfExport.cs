using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Wpf;

namespace FreeW.App.Host;

/// <summary>
/// Renders a paginated FreeW document to a real PDF through the shared PDF tier.
///
/// <para>
/// FreeW's app-specific half is the Document → page adapter: it reuses the existing print pipeline
/// (<see cref="PrintLayout.BuildPaginator"/>) — which already breaks the editor's content into pages
/// at the model's <see cref="FreeW.Core.Model.PageSettings"/> geometry and composites the
/// header/footer (with live page numbers), watermark, page border and footnotes — and rasterizes each
/// <see cref="DocumentPage"/> to a bitmap. The bitmaps are handed to the shared
/// <see cref="WpfRasterPdfWriter"/> (PDFsharp) as a <see cref="PdfRasterDocument"/>, so FreeW and FreeX
/// share one rasterized-page → PDF emitter rather than each carrying its own PDFsharp plumbing.
/// </para>
///
/// <para>
/// FreeW's pages are built from <c>FlowDocument</c> content (via <c>VisualBrush</c>-tiled
/// <c>DrawingVisual</c>s), not the simple text controls FreeX's print pipeline uses, so there is no
/// live control tree to walk for a selectable-text overlay. Instead, the same paginator is round-tripped
/// once through WPF's own XPS serializer (exactly what <see cref="XpsExport"/> does for the standalone
/// XPS export) — that conversion is WPF's in-box mechanism for turning arbitrary page content into real
/// vector glyph runs — and the resulting <see cref="Glyphs"/> runs are read back via the shared
/// <see cref="WpfXpsTextOverlayExtractor"/> and passed to <see cref="WpfRasterPdfWriter"/> as each raster
/// page's <see cref="PdfRasterPage.TextOverlays"/>, so the exported PDF is searchable/selectable
/// (matching FreeX's WPF export and FreeW's own Avalonia PDF export) instead of a raster-only image.
/// </para>
///
/// <para>
/// No print-to-file driver or external tool is required, so export is fully deterministic and works
/// headless (e.g. in tests).
/// </para>
/// </summary>
internal static class PdfExport
{
    // WPF lays visuals out in device-independent pixels (1/96 inch); PDF user space is points (1/72 inch).
    private const double DipPerInch = 96.0;
    private const double PointsPerInch = 72.0;
    private const double DipToPoint = PointsPerInch / DipPerInch;

    /// <summary>
    /// Renders the supplied paginator to PDF bytes in memory. Must be called on the UI / STA thread
    /// because it walks the WPF visual tree (the caller can then flush the bytes to disk off-thread via
    /// <see cref="Free.Shared.AppServices.AtomicFileWriter"/>).
    /// </summary>
    /// <param name="paginator">A laid-out paginator, e.g. from <see cref="PrintLayout.BuildPaginator"/>.</param>
    /// <param name="title">Optional document title written into the PDF metadata.</param>
    /// <param name="imageDiagnostics">
    /// Optional sink for non-fatal image warnings: populated by <see cref="WpfRasterPdfWriter"/> if a
    /// rendered page's bytes cannot be decoded when the PDF is written, so the caller can surface the
    /// loss instead of the export silently dropping a page's content.
    /// </param>
    public static byte[] RenderToBytes(DocumentPaginator paginator, string? title = null, ICollection<string>? imageDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(paginator);
        return WpfRasterPdfWriter.WriteToBytes(BuildDocument(paginator, title), imageDiagnostics);
    }

    private static PdfRasterDocument BuildDocument(DocumentPaginator paginator, string? title)
    {
        // Force a valid page count so GetPage covers the whole document.
        if (!paginator.IsPageCountValid)
            paginator.ComputePageCount();

        var pageCount = paginator.PageCount;
        if (pageCount <= 0)
            throw new InvalidOperationException("The document produced no printable pages.");

        var textOverlaysPerPage = BuildTextOverlaysPerPage(paginator);

        var pages = new List<PdfRasterPage>(pageCount);
        for (var i = 0; i < pageCount; i++)
        {
            using var docPage = paginator.GetPage(i);
            // Prefer the paginator's nominal page size; fall back to the page's own size if unset.
            var sizeDip = docPage.Size;
            if (double.IsNaN(sizeDip.Width) || sizeDip.Width <= 0 || double.IsNaN(sizeDip.Height) || sizeDip.Height <= 0)
                sizeDip = paginator.PageSize;

            var widthDip = Math.Max(1.0, sizeDip.Width);
            var heightDip = Math.Max(1.0, sizeDip.Height);

            var imageBytes = RenderPagePng(docPage.Visual, widthDip, heightDip);
            var textOverlays = i < textOverlaysPerPage.Count && textOverlaysPerPage[i].Count > 0
                ? textOverlaysPerPage[i]
                : null;
            pages.Add(new PdfRasterPage(widthDip * DipToPoint, heightDip * DipToPoint, imageBytes, textOverlays));
        }

        var properties = new PdfDocumentProperties(
            Title: string.IsNullOrWhiteSpace(title) ? null : title,
            Creator: "FreeW");
        return new PdfRasterDocument(pages, properties);
    }

    // Round-trips the paginator through WPF's own XPS serializer purely to recover a text layer: XPS
    // serialization is WPF's in-box mechanism for turning arbitrary page content (FreeW's FlowDocument
    // pages are painted via VisualBrush-tiled DrawingVisuals, not simple text controls) into real vector
    // glyph runs with an absolute page-space origin. Reading those pages back and walking their Glyphs
    // runs (via the shared WpfXpsTextOverlayExtractor) gives per-page overlays for the raster PDF path
    // without duplicating FreeX's control-tree overlay extractor, which does not apply to this host's
    // page content. Returns one (possibly empty) overlay list per paginator page, in page order.
    private static IReadOnlyList<IReadOnlyList<PdfTextOverlay>> BuildTextOverlaysPerPage(DocumentPaginator paginator)
    {
        // Read the glyph runs each rendered page already carries, rather than round-tripping the
        // paginator through WPF's XPS serializer to get them back.
        //
        // The XPS route cannot be relied on: WPF subsets every font it serializes, and
        // MS.Internal.TrueTypeSubsetter throws FileFormatException on a font it cannot parse. A stock
        // Windows Calibri (6.27, 7048 glyphs -- GlyphTypeface loads it without complaint) fails it,
        // which cost the entire selectable-text layer on that machine. Walking the page visual needs
        // no serializer, package or subset, so it does not care what the fonts are.
        //
        // It is also better evidence: an XPS font resource URI carries no family name, so the XPS
        // extractor had to substitute "Segoe UI", while a GlyphRun still knows its typeface.
        var overlaysPerPage = new List<IReadOnlyList<PdfTextOverlay>>();
        for (var pageIndex = 0; pageIndex < paginator.PageCount; pageIndex++)
        {
            using var page = paginator.GetPage(pageIndex);
            overlaysPerPage.Add(page?.Visual is { } visual
                ? WpfVisualTextOverlayExtractor.Extract(visual, DipToPoint)
                : []);
        }

        return overlaysPerPage;
    }

    private static byte[] RenderPagePng(Visual pageVisual, double widthDip, double heightDip)
    {
        var target = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(widthDip)),
            Math.Max(1, (int)Math.Ceiling(heightDip)),
            DipPerInch,
            DipPerInch,
            PixelFormats.Pbgra32);

        // Paint a white sheet first so transparent page regions export as white, not black.
        var backing = new System.Windows.Controls.Border
        {
            Background = Brushes.White,
            Width = widthDip,
            Height = heightDip
        };
        backing.Measure(new Size(widthDip, heightDip));
        backing.Arrange(new Rect(new Size(widthDip, heightDip)));
        backing.UpdateLayout();
        target.Render(backing);

        target.Render(pageVisual);
        target.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
