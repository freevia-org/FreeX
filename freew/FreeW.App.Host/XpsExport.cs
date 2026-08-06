using System.IO;
using System.IO.Packaging;
using System.Windows.Documents;
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
/// XPS export must run on the STA / UI thread because <see cref="XpsDocumentWriter.Write(DocumentPaginator)"/>
/// walks the WPF visual tree. We render into an in-memory OPC package (a <see cref="MemoryStream"/>) and
/// return the bytes so the caller can flush them atomically off-thread via
/// <see cref="Free.Shared.Shell.ExportAtomicWriter"/>. Rendering into a fresh in-memory package side-steps
/// dotnet/wpf #9418 (the <c>IOException</c> seen when <see cref="XpsDocument"/> reopens an existing file in
/// update mode).
/// </para>
/// </summary>
internal static class XpsExport
{
    /// <summary>
    /// Renders the supplied paginator to XPS bytes in memory. Must be called on the UI / STA thread
    /// because it walks the WPF visual tree (the caller can then flush the bytes to disk off-thread via
    /// <see cref="Free.Shared.Shell.ExportAtomicWriter"/>).
    /// </summary>
    /// <param name="paginator">A laid-out paginator, e.g. from <see cref="PrintLayout.BuildPaginator"/>.</param>
    public static byte[] RenderToBytes(DocumentPaginator paginator)
    {
        ArgumentNullException.ThrowIfNull(paginator);

        EnsurePrintable(paginator);

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

    private static void EnsurePrintable(DocumentPaginator paginator)
    {
        // Force a valid page count so the writer serialises the whole document.
        if (!paginator.IsPageCountValid)
            paginator.ComputePageCount();

        if (paginator.PageCount <= 0)
            throw new InvalidOperationException("The document produced no printable pages.");
    }
}
