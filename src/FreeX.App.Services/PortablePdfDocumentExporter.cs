using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services;

public sealed record PortablePdfDocumentOptions(
    double PageWidthPoints = 612,
    double PageHeightPoints = 792,
    double MarginPoints = 36,
    double HeaderHeightPoints = 64,
    double RowHeightPoints = 22,
    double MinimumColumnWidthPoints = 42,
    double MaximumColumnWidthPoints = 118,
    int MaximumCellTextLength = 64);

public sealed record PortablePdfDocumentExportResult(
    int PageCount,
    string StatusText,
    IReadOnlyList<string> ImageDiagnostics);

/// <summary>
/// FreeX's dependency-free PDF exporter. It now consumes the shared <see cref="Free.Shared.Pdf"/>
/// tier: <see cref="WorkbookPdfContentBuilder"/> renders the workbook + export plan into the
/// app-agnostic <see cref="PdfContentDocument"/> draw-op model, and the shared
/// <see cref="PortablePdfWriter"/> emits the bytes. The FreeX-specific work (Workbook → page
/// geometry, styles, number formatting, header/footer) lives in the builder; the PDF byte format
/// lives in the shared writer. Output is byte-for-byte identical to the pre-extraction exporter.
/// </summary>
public static class PortablePdfDocumentExporter
{
    public static PortablePdfDocumentExportResult Save(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        string path,
        PortablePdfDocumentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var (result, bytes) = CreateDocument(workbook, exportPlan, options);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var execution = new AtomicExportExecutor().ExecuteAsync(
            path,
            async (output, cancellationToken) =>
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                return result;
            }).GetAwaiter().GetResult();

        if (execution.Succeeded)
            return execution.Value!;

        throw execution.Exception ?? new IOException(
            execution.Error?.Detail.Message ??
            execution.Validation?.Detail.ToString() ??
            "Portable PDF export did not complete.");
    }

    public static PortablePdfDocumentExportResult Save(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        Stream stream,
        PortablePdfDocumentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanWrite)
            throw new ArgumentException("Portable PDF export requires a writable stream.", nameof(stream));

        var (result, bytes) = CreateDocument(workbook, exportPlan, options);
        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        stream.Write(bytes);
        return result;
    }

    private static (PortablePdfDocumentExportResult Result, byte[] Bytes) CreateDocument(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfDocumentOptions? options)
    {
        if (!exportPlan.IsReady)
            throw new InvalidOperationException(exportPlan.StatusText);

        options ??= new PortablePdfDocumentOptions();
        var textCapabilityPlan = PortablePdfTextCapabilityPlanner.CreatePlan(workbook, exportPlan, options);
        if (!textCapabilityPlan.IsReady)
            throw new InvalidOperationException(textCapabilityPlan.StatusText);

        var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("Portable PDF export requires at least one rendered page.");

        // Populated by PortablePdfWriter when an embedded picture's bytes cannot be decoded (corrupt
        // or an unrecognized format): that image is silently omitted from the page unless this sink
        // catches the diagnostic, so callers can surface the loss instead of the export looking clean.
        var imageDiagnostics = new List<string>();
        var bytes = PortablePdfWriter.WriteToBytes(document, "FreeX portable PDF", imageDiagnostics);
        var statusText = imageDiagnostics.Count == 0
            ? $"Exported portable PDF: {document.Pages.Count} {Pluralize(document.Pages.Count, "page")}."
            : $"Exported portable PDF: {document.Pages.Count} {Pluralize(document.Pages.Count, "page")} " +
              $"({imageDiagnostics.Count} image warning{(imageDiagnostics.Count == 1 ? "" : "s")}).";
        var result = new PortablePdfDocumentExportResult(document.Pages.Count, statusText, imageDiagnostics);
        return (result, bytes);
    }

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
