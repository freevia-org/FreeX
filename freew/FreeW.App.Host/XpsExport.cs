using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using System.Windows.Xps;

namespace FreeW.App.Host;

/// <summary>
/// Exports a paginated FreeW document to a real XPS package using the in-box WPF XPS stack
/// (<see cref="System.Windows.Xps.Packaging.XpsDocument"/> + <see cref="XpsDocumentWriter"/>).
///
/// <para>
/// This mirrors <see cref="PdfExport"/> but is export-only and is NOT a catalog Load/Save adapter — XPS
/// has no logical structure to import back (reconstructing it from positioned glyphs is OCR-grade, and
/// Word cannot open .xps either). It consumes the exact same paginator
/// (<see cref="PrintLayout.BuildPaginator"/>) the Print / PDF paths use, so the page geometry,
/// header/footer (with live page numbers), watermark, page border and footnotes are identical.
/// </para>
///
/// <para>
/// Unlike the PDF raster path, <see cref="XpsDocumentWriter"/> serializes real vector glyph runs, so the
/// resulting .xps carries selectable/searchable text and scales crisply.
/// </para>
///
/// <para>
/// WPF's internal TrueType subsetter can reject an otherwise renderable installed font (notably some
/// Windows Calibri files). If that happens, export retries from the already-laid-out page visuals as
/// image-only XPS pages. This compatibility path preserves the page geometry and visible content while
/// avoiding the bad font resource; healthy exports retain the vector text path above.
/// </para>
///
/// <para>
/// XPS export must run on the STA / UI thread because <see cref="XpsDocumentWriter.Write(DocumentPaginator)"/>
/// walks the WPF visual tree. We render into an in-memory OPC package (a <see cref="MemoryStream"/>) and
/// return the bytes so the caller can flush them atomically off-thread via
/// <see cref="Free.Shared.AppServices.AtomicFileWriter"/>. Rendering into a fresh in-memory package side-steps
/// dotnet/wpf #9418 (the <c>IOException</c> seen when <see cref="XpsDocument"/> reopens an existing file in
/// update mode).
/// </para>
/// </summary>
internal static partial class XpsExport
{
    /// <summary>
    /// Renders the supplied paginator to XPS bytes in memory. Must be called on the UI / STA thread
    /// because it walks the WPF visual tree (the caller can then flush the bytes to disk off-thread via
    /// <see cref="Free.Shared.AppServices.AtomicFileWriter"/>).
    /// </summary>
    /// <param name="paginator">A laid-out paginator, e.g. from <see cref="PrintLayout.BuildPaginator"/>.</param>
    public static byte[] RenderToBytes(DocumentPaginator paginator)
    {
        return RenderToBytesCore(paginator, RenderVectorPages);
    }

    private static byte[] RenderToBytesCore(
        DocumentPaginator paginator,
        Func<DocumentPaginator, byte[]> vectorRenderer)
    {
        ArgumentNullException.ThrowIfNull(paginator);
        ArgumentNullException.ThrowIfNull(vectorRenderer);

        EnsurePrintable(paginator);

        try
        {
            return vectorRenderer(paginator);
        }
        catch (FileFormatException)
        {
            // WPF's internal TrueType subsetter can reject a valid installed font after the paginator
            // has already laid out the document (stock Calibri is one observed example). Re-rendering
            // with a different FontFamily here is unsafe: the paginator contains realized glyph visuals,
            // and headers/notes can carry independently-created Calibri runs. Rasterizing the existing
            // page visuals preserves their exact geometry/content and removes the bad font from the XPS
            // serialization boundary without mutating the source FlowDocument.
            return RenderRasterizedPages(paginator);
        }
    }

    private static byte[] RenderVectorPages(DocumentPaginator paginator)
    {
        // Build the OPC package entirely in memory so we never touch the destination path until the
        // caller hands the finished bytes to the atomic writer.
        using var ms = new MemoryStream();

        // The package and XpsDocument must be disposed (which flushes/finalises the OPC parts) BEFORE we
        // read the MemoryStream — hence the nested using blocks closing first.
        using (var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite))
        using (var xpsDocument = new XpsDocument(package))
        {
            var writer = XpsDocument.CreateXpsDocumentWriter(xpsDocument);
            writer.Write(paginator);
        }

        return ms.ToArray();
    }

    private static byte[] RenderRasterizedPages(DocumentPaginator paginator)
    {
        var pages = new List<DocumentPage>(paginator.PageCount);
        for (var pageNumber = 0; pageNumber < paginator.PageCount; pageNumber++)
        {
            using var sourcePage = paginator.GetPage(pageNumber);
            var size = UsablePageSize(sourcePage.Size) ? sourcePage.Size : paginator.PageSize;
            if (!UsablePageSize(size))
                throw new InvalidOperationException("The document produced a page with no usable size.");

            var bitmap = RasterizePage(sourcePage.Visual, size);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawImage(bitmap, new Rect(new Point(), size));
            }

            pages.Add(new DocumentPage(
                visual,
                size,
                new Rect(new Point(), size),
                new Rect(new Point(), size)));
        }

        return RenderVectorPages(new RasterizedDocumentPaginator(pages));
    }

    private static BitmapSource RasterizePage(Visual? visual, Size size)
    {
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width)),
            Math.Max(1, (int)Math.Ceiling(size.Height)),
            96,
            96,
            PixelFormats.Pbgra32);

        // Render onto an opaque white sheet so transparent page regions remain paper-white in readers.
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(Brushes.White, null, new Rect(new Point(), size));
        bitmap.Render(background);
        if (visual is not null)
            bitmap.Render(visual);

        bitmap.Freeze();
        return bitmap;
    }

    private static bool UsablePageSize(Size size) =>
        double.IsFinite(size.Width)
        && double.IsFinite(size.Height)
        && size.Width > 0
        && size.Height > 0;

    private sealed class RasterizedDocumentPaginator(IReadOnlyList<DocumentPage> pages) : DocumentPaginator, IDocumentPaginatorSource
    {
        private readonly IReadOnlyList<DocumentPage> _pages = pages;
        private readonly Size _pageSize = pages.Count > 0 ? pages[0].Size : Size.Empty;

        public override bool IsPageCountValid => true;
        public override int PageCount => _pages.Count;
        public override Size PageSize
        {
            get => _pageSize;
            set => throw new NotSupportedException();
        }

        public override IDocumentPaginatorSource Source => this;
        public DocumentPaginator DocumentPaginator => this;

        public override DocumentPage GetPage(int pageNumber) =>
            pageNumber >= 0 && pageNumber < _pages.Count
                ? _pages[pageNumber]
                : DocumentPage.Missing;
    }

    private static void EnsurePrintable(DocumentPaginator paginator)
    {
        // Force a valid page count so the writer serialises the whole document.
        if (!paginator.IsPageCountValid)
            paginator.ComputePageCount();

        if (paginator.PageCount <= 0)
            throw new InvalidOperationException("The document produced no printable pages.");
    }
}
