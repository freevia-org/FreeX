using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    /// <see cref="Free.Shared.Shell.ExportAtomicWriter"/>).
    /// </summary>
    /// <param name="paginator">A laid-out paginator, e.g. from <see cref="PrintLayout.BuildPaginator"/>.</param>
    /// <param name="title">Optional document title written into the PDF metadata.</param>
    public static byte[] RenderToBytes(DocumentPaginator paginator, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(paginator);
        return WpfRasterPdfWriter.WriteToBytes(BuildDocument(paginator, title));
    }

    private static PdfRasterDocument BuildDocument(DocumentPaginator paginator, string? title)
    {
        // Force a valid page count so GetPage covers the whole document.
        if (!paginator.IsPageCountValid)
            paginator.ComputePageCount();

        var pageCount = paginator.PageCount;
        if (pageCount <= 0)
            throw new InvalidOperationException("The document produced no printable pages.");

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
            pages.Add(new PdfRasterPage(widthDip * DipToPoint, heightDip * DipToPoint, imageBytes));
        }

        var properties = new PdfDocumentProperties(
            Title: string.IsNullOrWhiteSpace(title) ? null : title,
            Creator: "FreeW");
        return new PdfRasterDocument(pages, properties);
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
