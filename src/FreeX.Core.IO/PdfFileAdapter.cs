using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Read-only PDF table import: opens a <c>.pdf</c> and extracts its tabular text into a
/// <see cref="Workbook"/> (one sheet per page) via <see cref="PdfTableReader"/>. Import is best-effort
/// (positioned-glyph heuristics — no true table model, no OCR), so this adapter is
/// <see cref="FileFormatDescriptor.CanOpen"/> but not <see cref="FileFormatDescriptor.CanSave"/>.
/// <see cref="Save"/> always throws <see cref="NotSupportedException"/>. Saving back to PDF requires the
/// host-only raster export path; users round-trip out via <em>Save As .xlsx</em> instead.
/// </summary>
public sealed class PdfFileAdapter : IFileAdapter
{
    public string Extension => ".pdf";
    public string FormatName => "PDF Document";

    /// <summary>Open-only — PDF cannot be saved back through the file-adapter seam.</summary>
    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".pdf", "PDF Document", CanOpen: true, CanSave: false),
    ];

    /// <summary>
    /// Reads the PDF from <paramref name="stream"/> and returns a <see cref="Workbook"/> with one
    /// worksheet per page. Pages with no text layer produce an empty (but present) sheet.
    /// The stream is NOT disposed; stream ownership stays with the caller.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="stream"/> is null.</exception>
    /// <exception cref="InvalidDataException">
    /// When the stream is not a valid PDF (malformed, encrypted without password, etc.).
    /// </exception>
    public Workbook Load(Stream stream) => PdfTableReader.Read(stream);

    /// <summary>Always throws: PDF is a read-only import format in FreeX.</summary>
    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException("PDF import is read-only — use Save As .xlsx.");
}
