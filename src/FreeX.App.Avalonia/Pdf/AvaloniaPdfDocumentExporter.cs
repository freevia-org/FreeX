using System.IO;
using Free.Shared.Pdf.Skia;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Pdf;

/// <summary>
/// Routes the Avalonia shell's <em>File → Export to PDF</em> through the Unicode-capable
/// <see cref="SkiaPdfDocumentExporter"/> when Skia can run, and falls back to the dependency-free
/// WinAnsi <see cref="PortablePdfDocumentExporter"/> when it cannot (headless/no-Skia environments).
/// Both writers consume the same shared <see cref="PortablePdfExportPlan"/>, so the only difference
/// is text fidelity (Skia embeds/subsets fonts; portable is WinAnsi-only) — geometry is identical.
/// <para>
/// This keeps a single decision point so the menu handler and tests exercise the same routing.
/// </para>
/// </summary>
public static class AvaloniaPdfDocumentExporter
{
    /// <summary>
    /// Renders <paramref name="exportPlan"/> to <paramref name="stream"/>, preferring Skia (Unicode)
    /// and falling back to the portable WinAnsi writer if Skia is unavailable or throws while
    /// initializing. Returns the result plus which backend produced the bytes.
    /// </summary>
    public static AvaloniaPdfDocumentExportOutcome Save(
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

        // Skia shapes (HarfBuzz) and automatically embeds/subsets the fonts it draws, so non-WinAnsi
        // text (Cyrillic, Greek, CJK, accented Latin) exports correctly without bundling a font. When
        // the Skia native asset is missing (headless/no-Skia), it throws on first use; we then fall
        // back to the dependency-free WinAnsi writer so export still works for ASCII/WinAnsi content.
        // When no explicit options are supplied the page-setup-aware path is used (page dimensions,
        // gridlines, and header/footer derived from each sheet's OOXML page setup). Passing non-null
        // options bypasses page-setup awareness and uses the fixed geometry supplied by the caller.
        var outcome = PdfBackendFallbackExecutor.Execute(
            stream,
            target => SkiaPdfDocumentExporter.Save(
                workbook,
                exportPlan,
                target,
                options,
                workbookDirectory),
            target => PortablePdfDocumentExporter.Save(workbook, exportPlan, target, options));

        return new AvaloniaPdfDocumentExportOutcome(outcome.Result, outcome.Backend);
    }
}

/// <summary>Result of <see cref="AvaloniaPdfDocumentExporter.Save"/>: the export result plus the backend used.</summary>
public sealed record AvaloniaPdfDocumentExportOutcome(
    PortablePdfDocumentExportResult Result,
    PdfExportBackend Backend);
