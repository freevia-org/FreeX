using System.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Read-only PDF text import (design §5.8): opens a <c>.pdf</c> and recovers its text into a sparse
/// <see cref="TextDocument"/> via <see cref="PdfTextReader"/>. Import is best-effort and lossy (text only —
/// no tables/images/columns/styles; multi-column/scanned PDFs degrade badly), so this format is
/// <see cref="FileFormatDescriptor.CanOpen"/> but not <see cref="FileFormatDescriptor.CanSave"/>:
/// <see cref="Save"/> throws <see cref="NotSupportedException"/>. Saving back to PDF would require the
/// already-shipped raster export path (host-only, visual-tree bound), so users round-trip out via
/// <em>Save As .docx</em> instead. Catalog-ready (correct read-only descriptor); the integrator owns
/// registration.
/// </summary>
public sealed class PdfFileAdapter : IDocumentFileAdapter
{
    public string Extension => ".pdf";
    public string FormatName => "PDF Document";

    /// <summary>Open-only — PDF cannot be saved through the file-adapter seam (see <see cref="Save"/>).</summary>
    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".pdf", "PDF Document", CanOpen: true, CanSave: false),
    ];

    public TextDocument Load(Stream stream) => PdfTextReader.Read(stream);

    /// <summary>Always throws: PDF is a read-only import format here.</summary>
    public void Save(TextDocument document, Stream stream) =>
        throw new NotSupportedException("PDF is read-only — use Save As .docx.");
}
