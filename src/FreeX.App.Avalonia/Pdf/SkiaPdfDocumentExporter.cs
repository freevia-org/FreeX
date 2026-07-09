using System.IO;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Pdf.Skia;

namespace FreeX.App.Avalonia.Pdf;

/// <summary>
/// PDF exporter for the Avalonia shell that renders workbook text with SkiaSharp's PDF backend.
/// Unlike the dependency-free portable WinAnsi exporter, Skia shapes text (HarfBuzz) and
/// <b>automatically embeds/subsets</b> the fonts it draws, so non-WinAnsi text (Cyrillic, Greek,
/// accented Latin, CJK) renders without us bundling a font.
/// <para>
/// It is now a thin shim over the shared tier: it builds the same app-agnostic
/// <see cref="Free.Shared.Pdf.PdfContentDocument"/> as the portable exporter (via
/// <see cref="WorkbookPdfContentBuilder"/>, so geometry is identical) and hands it to the shared
/// <see cref="SkiaPdfWriter"/>.
/// </para>
/// </summary>
public static class SkiaPdfDocumentExporter
{
    public static PortablePdfDocumentExportResult Save(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        Stream stream,
        PortablePdfDocumentOptions? options = null,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));
        if (!exportPlan.IsReady)
            throw new InvalidOperationException(exportPlan.StatusText);

        // Use the page-setup-aware builder when the export plan was produced by
        // CreatePlanFromPageSetup (page dimensions/gridlines/header-footer honored per sheet).
        // Fall back to the legacy options-driven builder when an explicit options object is passed.
        var document = options is null
            ? WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan, workbookDirectory)
            : WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);

        var pageCount = SkiaPdfWriter.Write(document, stream);

        return new PortablePdfDocumentExportResult(
            pageCount,
            $"Exported PDF (Skia, embedded fonts): {pageCount} {(pageCount == 1 ? "page" : "pages")}.");
    }
}
