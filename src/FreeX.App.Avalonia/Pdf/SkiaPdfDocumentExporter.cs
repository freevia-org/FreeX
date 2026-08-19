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
        //
        // font-text-measurement-F1: SkiaPdfWriter below draws every PdfText op with real glyph
        // advances (SKFont.MeasureText, SkiaPdfWriter.cs's FallbackTextRenderer) -- not the flat
        // character-count guess WorkbookPdfContentBuilder falls back to by default -- so the
        // Center/Right/Justify/Distributed text positions this builder precomputes must be measured
        // the same way, or they disagree with what actually gets drawn. SkiaPdfTextMeasurer measures
        // with the identical SkiaSharp API (SKFont.MeasureText) and typeface-resolution chain
        // SkiaPdfWriter itself draws with, so the exported PDF's text positions agree with what
        // actually gets drawn -- without adding a dependency on Avalonia's platform/font-manager
        // services (unlike AvaloniaTextMeasurer/FormattedText), which this export path does not
        // otherwise require and which is not initialized in every host that calls this method.
        using var skiaTextMeasurer = new SkiaPdfTextMeasurer();
        var document = options is null
            ? WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan, workbookDirectory, skiaTextMeasurer)
            : WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);

        // Populated by SkiaPdfWriter when an embedded picture's bytes cannot be decoded (corrupt or
        // an unrecognized format): that image is silently omitted from the page unless this sink
        // catches the diagnostic, so callers can surface the loss instead of the export looking clean.
        var imageDiagnostics = new List<string>();
        var pageCount = SkiaPdfWriter.Write(document, stream, imageDiagnostics);

        var statusText = imageDiagnostics.Count == 0
            ? $"Exported PDF (Skia, embedded fonts): {pageCount} {(pageCount == 1 ? "page" : "pages")}."
            : $"Exported PDF (Skia, embedded fonts): {pageCount} {(pageCount == 1 ? "page" : "pages")} " +
              $"({imageDiagnostics.Count} image warning{(imageDiagnostics.Count == 1 ? "" : "s")}).";

        return new PortablePdfDocumentExportResult(pageCount, statusText, imageDiagnostics);
    }
}
