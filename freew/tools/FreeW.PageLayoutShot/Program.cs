// FreeW.PageLayoutShot — renders the FreeW Avalonia DocumentView to PNGs for visual verification.
// Uses the real Avalonia Skia backend (not the headless stub) so the output contains actual pixels.
//
// Usage:
//   FreeW.PageLayoutShot [<output-dir>] [--scenario <scenario-id> ...]
//
// If <output-dir> is omitted PNGs are written next to the executable:
//   freew_print_layout.png  — Print Layout (grey desk + discrete white pages + drop-shadow)
//   freew_web_layout.png    — Web Layout (plain white, continuous column, no page chrome)
//   freew_draft_layout.png  — Draft (plain white, minimal left margin, continuous)
//
// The program exits after writing the PNGs (no interactive window appears).

using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;
using FreeW.Core.Model;
using SkiaSharp;

var positionalArgs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--scenario", StringComparison.OrdinalIgnoreCase))
    {
        if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
            throw new ArgumentException("--scenario requires a scenario id.");

        PageShotScenarioSelection.Add(args[i]);
        continue;
    }

    if (string.Equals(args[i], "--fixtures-dir", StringComparison.OrdinalIgnoreCase))
    {
        if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
            throw new ArgumentException("--fixtures-dir requires a directory.");

        PageShotFixtureSource.Configure(args[i]);
        continue;
    }

    positionalArgs.Add(args[i]);
}

if (positionalArgs.Count > 1)
    throw new ArgumentException("usage: FreeW.PageLayoutShot [<output-dir>] [--scenario <scenario-id> ...]");

var outDir = positionalArgs.Count > 0 ? positionalArgs[0] : AppContext.BaseDirectory;

int exitCode = 0;
var done = new ManualResetEventSlim(false);

// Run in an Avalonia event loop (required for layout + glyph shaping).
AppBuilder.Configure<PageShotApp>()
    .UsePlatformDetect()
    .SetupWithoutStarting();

Dispatcher.UIThread.Post(() =>
{
    try
    {
        exitCode = RenderAll(outDir);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[PageLayoutShot] Error: {ex.Message}");
        exitCode = 1;
    }
    finally
    {
        done.Set();
    }
});

Dispatcher.UIThread.RunJobs();
done.Wait();
return exitCode;

static int RenderAll(string outDir)
{
    Directory.CreateDirectory(outDir);

    var printPath = Path.GetFullPath(Path.Combine(outDir, "freew_print_layout.png"));
    var webPath   = Path.GetFullPath(Path.Combine(outDir, "freew_web_layout.png"));
    var draftPath = Path.GetFullPath(Path.Combine(outDir, "freew_draft_layout.png"));
    var floatPath = Path.GetFullPath(Path.Combine(outDir, "freew_floating_image.png"));
    var columnsPath = Path.GetFullPath(Path.Combine(outDir, "freew_columns_layout.png"));
    var borderWatermarkPath = Path.GetFullPath(Path.Combine(outDir, "freew_border_watermark.png"));
    var footnotesP1Path = VisualEvidenceOutputPath(outDir, "f2-footnotes", 1);
    var footnotesP2Path = VisualEvidenceOutputPath(outDir, "f2-footnotes", 2);
    var endnotesP1Path = VisualEvidenceOutputPath(outDir, "f2-endnotes", 1);
    var endnotesP2Path = VisualEvidenceOutputPath(outDir, "f2-endnotes", 2);
    var fieldPageNumberP1Path = VisualEvidenceOutputPath(outDir, "field-page-number-variants", 1);
    var fieldPageNumberP2Path = VisualEvidenceOutputPath(outDir, "field-page-number-variants", 2);
    var fieldPageNumberP3Path = VisualEvidenceOutputPath(outDir, "field-page-number-variants", 3);
    var fieldPageNumberP4Path = VisualEvidenceOutputPath(outDir, "field-page-number-variants", 4);
    var referencesHeavyP1Path = VisualEvidenceOutputPath(outDir, "references-heavy-fields", 1);
    var referencesHeavyP2Path = VisualEvidenceOutputPath(outDir, "references-heavy-fields", 2);
    var legalReferenceP1Path = VisualEvidenceOutputPath(outDir, "legal-reference-section-page-numbers", 1);
    var legalReferenceP2Path = VisualEvidenceOutputPath(outDir, "legal-reference-section-page-numbers", 2);
    var equationStructuresPath = VisualEvidenceOutputPath(outDir, "equation-structures", 1);
    var headerFooterImagesP1Path = VisualEvidenceOutputPath(outDir, "f2-hf-images", 1);
    var headerFooterImagesP2Path = VisualEvidenceOutputPath(outDir, "f2-hf-images", 2);
    var oddEvenHeadersP1Path = VisualEvidenceOutputPath(outDir, "f2-hf-oddeven", 1);
    var oddEvenHeadersP2Path = VisualEvidenceOutputPath(outDir, "f2-hf-oddeven", 2);
    var sectionLandscapeP1Path = VisualEvidenceOutputPath(outDir, "f2-section-landscape", 1);
    var sectionLandscapeP2Path = VisualEvidenceOutputPath(outDir, "f2-section-landscape", 2);
    var trackedChangesPath = VisualEvidenceOutputPath(outDir, "f2-tracked-changes", 1);
    var commentsPath = VisualEvidenceOutputPath(outDir, "f2-comments", 1);
    var reviewProofingDepthPath = VisualEvidenceOutputPath(outDir, "review-proofing-visual-depth", 1);
    var reviewProtectionProofingPath = VisualEvidenceOutputPath(outDir, "review-protection-proofing-comments-only", 1);
    var reviewCompareProofPath = VisualEvidenceOutputPath(outDir, "review-compare-visual-proof", 1);
    var reviewCombineProofPath = VisualEvidenceOutputPath(outDir, "review-combine-visual-proof", 1);
    var tableLayoutPath = VisualEvidenceOutputPath(outDir, "table-layout-complex", 1);
    var tablePaginationP1Path = VisualEvidenceOutputPath(outDir, "table-pagination-repeat-header", 1);
    var tablePaginationP2Path = VisualEvidenceOutputPath(outDir, "table-pagination-repeat-header", 2);
    var tablePageCompositionP1Path = VisualEvidenceOutputPath(outDir, "table-page-composition-stress", 1);
    var tablePageCompositionP2Path = VisualEvidenceOutputPath(outDir, "table-page-composition-stress", 2);
    var tablePageCompositionP3Path = VisualEvidenceOutputPath(outDir, "table-page-composition-stress", 3);
    var drawingObjectsPath = VisualEvidenceOutputPath(outDir, "drawing-objects-complex", 1);
    var objectFormatPath = VisualEvidenceOutputPath(outDir, "object-format-position-size-style", 1);
    var chartSmartArtPath = VisualEvidenceOutputPath(outDir, "chart-smartart-complex", 1);
    var chartSmartArtP2Path = VisualEvidenceOutputPath(outDir, "chart-smartart-complex", 2);
    var wordArtWatermarkPath = VisualEvidenceOutputPath(outDir, "wordart-watermark-stress", 1);
    var wordArtPictureWatermarkPath = VisualEvidenceOutputPath(outDir, "wordart-picture-watermark-layout", 1);
    var printPreviewP1Path = VisualEvidenceOutputPath(outDir, "backstage-print-preview-fidelity", 1);
    var printPreviewP2Path = VisualEvidenceOutputPath(outDir, "backstage-print-preview-fidelity", 2);
    var pdfExportP1Path = VisualEvidenceOutputPath(outDir, "backstage-pdf-export-fidelity", 1);
    var pdfExportP2Path = VisualEvidenceOutputPath(outDir, "backstage-pdf-export-fidelity", 2);
    var evidence = new List<FreeWVisualEvidenceRow>();

    var rc = RenderMode(DocumentViewMode.PrintLayout, printPath,
        width: 960, height: 3300,
        label: "Print Layout",
        scenarioId: "page-composition-print-layout",
        evidence: evidence);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.WebLayout, webPath,
        width: 960, height: 2400,
        label: "Web Layout",
        scenarioId: "page-composition-web-layout",
        evidence: evidence);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.Draft, draftPath,
        width: 960, height: 2400,
        label: "Draft",
        scenarioId: "page-composition-draft",
        evidence: evidence);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, columnsPath,
        width: 960, height: 1800,
        label: "Columns",
        scenarioId: "page-composition-columns",
        evidence: evidence,
        documentFactory: BuildColumnsDocument);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, borderWatermarkPath,
        width: 960, height: 1800,
        label: "Border + Watermark",
        scenarioId: "page-composition-border-watermark",
        evidence: evidence,
        documentFactory: BuildBorderWatermarkDocument);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, footnotesP1Path,
        width: 960, height: 1200,
        label: "F2 Footnotes p1",
        scenarioId: "f2-footnotes",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument,
        pageNumber: 1,
        pageCount: 2,
        hasFootnotes: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, footnotesP2Path,
        width: 960, height: 1200,
        label: "F2 Footnotes p2",
        scenarioId: "f2-footnotes",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100,
        hasFootnotes: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, endnotesP1Path,
        width: 960, height: 1200,
        label: "F2 Endnotes p1",
        scenarioId: "f2-endnotes",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, endnotesP2Path,
        width: 960, height: 1200,
        label: "F2 Endnotes p2",
        scenarioId: "f2-endnotes",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100,
        hasEndnotes: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, fieldPageNumberP1Path,
        width: 960, height: 1200,
        label: "Field Page Number p1",
        scenarioId: "field-page-number-variants",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument,
        pageNumber: 1,
        pageCount: 4);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, fieldPageNumberP2Path,
        width: 960, height: 1200,
        label: "Field Page Number p2",
        scenarioId: "field-page-number-variants",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument,
        pageNumber: 2,
        pageCount: 4,
        alignViewportToRequestedPage: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, fieldPageNumberP3Path,
        width: 960, height: 1200,
        label: "Field Page Number p3",
        scenarioId: "field-page-number-variants",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument,
        pageNumber: 3,
        pageCount: 4,
        alignViewportToRequestedPage: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, fieldPageNumberP4Path,
        width: 960, height: 1200,
        label: "Field Page Number p4",
        scenarioId: "field-page-number-variants",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument,
        pageNumber: 4,
        pageCount: 4,
        alignViewportToRequestedPage: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, headerFooterImagesP1Path,
        width: 960, height: 1200,
        label: "Header/Footer Images p1",
        scenarioId: "f2-hf-images",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, headerFooterImagesP2Path,
        width: 960, height: 1200,
        label: "Header/Footer Images p2",
        scenarioId: "f2-hf-images",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, oddEvenHeadersP1Path,
        width: 960, height: 1200,
        label: "Odd/Even Headers p1",
        scenarioId: "f2-hf-oddeven",
        evidence: evidence,
        documentFactory: BuildOddEvenHeadersDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, oddEvenHeadersP2Path,
        width: 960, height: 1200,
        label: "Odd/Even Headers p2",
        scenarioId: "f2-hf-oddeven",
        evidence: evidence,
        documentFactory: BuildOddEvenHeadersDocument,
        pageNumber: 2,
        pageCount: 2,
        alignViewportToRequestedPage: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, referencesHeavyP1Path,
        width: 960, height: 1200,
        label: "References Heavy p1",
        scenarioId: "references-heavy-fields",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, referencesHeavyP2Path,
        width: 960, height: 1200,
        label: "References Heavy p2",
        scenarioId: "references-heavy-fields",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, legalReferenceP1Path,
        width: 960, height: 1200,
        label: "Legal Reference Section Page Numbers p1",
        scenarioId: "legal-reference-section-page-numbers",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, legalReferenceP2Path,
        width: 960, height: 1200,
        label: "Legal Reference Section Page Numbers p2",
        scenarioId: "legal-reference-section-page-numbers",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, equationStructuresPath,
        width: 960, height: 1200,
        label: "Equation Structures",
        scenarioId: "equation-structures",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, sectionLandscapeP1Path,
        width: 1160, height: 1200,
        label: "F2 Section Landscape p1",
        scenarioId: "f2-section-landscape",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, sectionLandscapeP2Path,
        width: 1160, height: 1000,
        label: "F2 Section Landscape p2",
        scenarioId: "f2-section-landscape",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 300);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, trackedChangesPath,
        width: 960, height: 1200,
        label: "F2 Tracked Changes",
        scenarioId: "f2-tracked-changes",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, commentsPath,
        width: 960, height: 1200,
        label: "F2 Comments",
        scenarioId: "f2-comments",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, reviewProofingDepthPath,
        width: 960, height: 1400,
        label: "Review Proofing Visual Depth",
        scenarioId: "review-proofing-visual-depth",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, reviewProtectionProofingPath,
        width: 960, height: 1400,
        label: "Review Protection Proofing Comments Only",
        scenarioId: "review-protection-proofing-comments-only",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildReviewProtectionProofingEvidenceDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, reviewCompareProofPath,
        width: 960, height: 1400,
        label: "Review Compare Visual Proof",
        scenarioId: "review-compare-visual-proof",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildReviewCompareVisualProofDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, reviewCombineProofPath,
        width: 960, height: 1400,
        label: "Review Combine Visual Proof",
        scenarioId: "review-combine-visual-proof",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildReviewCombineVisualProofDocument,
        pageNumber: 1,
        pageCount: 1);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, tableLayoutPath,
        width: 960, height: 1600,
        label: "Table Layout",
        scenarioId: "table-layout-complex",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, tablePaginationP1Path,
        width: 960, height: 900,
        label: "Table Pagination p1",
        scenarioId: "table-pagination-repeat-header",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, tablePaginationP2Path,
        width: 960, height: 900,
        label: "Table Pagination p2",
        scenarioId: "table-pagination-repeat-header",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 550);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, tablePageCompositionP1Path,
        width: 960, height: 900,
        label: "Table Page Composition p1",
        scenarioId: "table-page-composition-stress",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument,
        pageNumber: 1,
        pageCount: 3,
        hasFootnotes: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, tablePageCompositionP2Path,
        width: 960, height: 900,
        label: "Table Page Composition p2",
        scenarioId: "table-page-composition-stress",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument,
        pageNumber: 2,
        pageCount: 3,
        viewportOffsetY: 550,
        hasFootnotes: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, tablePageCompositionP3Path,
        width: 960, height: 900,
        label: "Table Page Composition p3",
        scenarioId: "table-page-composition-stress",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument,
        pageNumber: 3,
        pageCount: 3,
        viewportOffsetY: 1100,
        hasFootnotes: true);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, drawingObjectsPath,
        width: 960, height: 1700,
        label: "Drawing Objects",
        scenarioId: "drawing-objects-complex",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, objectFormatPath,
        width: 960, height: 1700,
        label: "Object Format",
        scenarioId: "object-format-position-size-style",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, chartSmartArtPath,
        width: 960, height: 1700,
        label: "Chart + SmartArt",
        scenarioId: "chart-smartart-complex",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument,
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, chartSmartArtP2Path,
        width: 960, height: 1200,
        label: "Chart + SmartArt p2",
        scenarioId: "chart-smartart-complex",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument,
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, wordArtWatermarkPath,
        width: 960, height: 1700,
        label: "WordArt + Watermark",
        scenarioId: "wordart-watermark-stress",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, wordArtPictureWatermarkPath,
        width: 960, height: 1700,
        label: "WordArt + Picture Watermark",
        scenarioId: "wordart-picture-watermark-layout",
        evidence: evidence,
        documentFactory: FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument);
    if (rc != 0) return rc;

    // ── FO1: Floating-image render capture ──────────────────────────────────────────────────────────
    rc = RenderFloatingImageScene(floatPath, evidence);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, printPreviewP1Path,
        width: 960, height: 1200,
        label: "Backstage Print Preview p1",
        scenarioId: "backstage-print-preview-fidelity",
        evidence: evidence,
        documentFactory: () => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage Print Preview Fidelity",
            "Avalonia print preview renderer capture"),
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, printPreviewP2Path,
        width: 960, height: 1200,
        label: "Backstage Print Preview p2",
        scenarioId: "backstage-print-preview-fidelity",
        evidence: evidence,
        documentFactory: () => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage Print Preview Fidelity",
            "Avalonia print preview renderer capture"),
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, pdfExportP1Path,
        width: 960, height: 1200,
        label: "Backstage PDF Export p1",
        scenarioId: "backstage-pdf-export-fidelity",
        evidence: evidence,
        documentFactory: () => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage PDF Export Fidelity",
            "Avalonia PDF export raster renderer capture"),
        pageNumber: 1,
        pageCount: 2);
    if (rc != 0) return rc;

    rc = RenderMode(DocumentViewMode.PrintLayout, pdfExportP2Path,
        width: 960, height: 1200,
        label: "Backstage PDF Export p2",
        scenarioId: "backstage-pdf-export-fidelity",
        evidence: evidence,
        documentFactory: () => FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage PDF Export Fidelity",
            "Avalonia PDF export raster renderer capture"),
        pageNumber: 2,
        pageCount: 2,
        viewportOffsetY: 1100);
    if (rc != 0) return rc;

    FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence);
    return 0;
}

static TextDocument BuildColumnsDocument()
{
    var doc = TextDocument.CreateEmpty();
    doc.Page.ColumnCount = 2;
    doc.Page.ColumnSpacingPt = 36;
    doc.Page.ColumnsLineBetween = true;
    doc.Blocks.Clear();

    var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };
    void AddPara(string text)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text, bodyFmt));
        doc.Blocks.Add(paragraph);
    }

    AddPara("Two Column Page Composition");
    AddPara("This capture verifies that the shared visual evidence manifest records multi-column page composition.");
    for (var i = 1; i <= 24; i++)
        AddPara($"Column paragraph {i}: the page should flow into two Word-style columns with a visible gutter.");

    return doc;
}

static TextDocument BuildBorderWatermarkDocument()
{
    var doc = TextDocument.CreateEmpty();
    doc.Page.PageBorder = new PageBorder("#000080", 3.0);
    doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
    {
        FontColorHex = "#808080",
        Opacity = 0.4,
        Layout = WatermarkLayout.Diagonal,
    };
    doc.Blocks.Clear();

    var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };
    void AddPara(string text)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text, bodyFmt));
        doc.Blocks.Add(paragraph);
    }

    AddPara("Page Border And Watermark");
    AddPara("This capture verifies page background composition, a visible page border, and a diagonal text watermark.");
    for (var i = 1; i <= 12; i++)
        AddPara($"Watermark paragraph {i}: body text should remain visible above the watermark and inside the border.");

    return doc;
}

static TextDocument BuildOddEvenHeadersDocument()
{
    var doc = TextDocument.CreateEmpty();
    doc.Page.DifferentOddEvenPages = true;
    doc.FinalSectionHeadersFooters.Header = new HeaderFooter("=== ODD PAGE HEADER (pages 1, 3, ...) ===");
    doc.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("=== EVEN PAGE HEADER (pages 2, 4, ...) ===");
    doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("=== ODD PAGE FOOTER ===");
    doc.FinalSectionHeadersFooters.EvenFooter = new HeaderFooter("=== EVEN PAGE FOOTER ===");
    doc.Blocks.Clear();
    doc.Blocks.Add(new Paragraph("Odd/Even Headers Demo") { StyleId = "Heading1" });
    doc.Blocks.Add(new Paragraph("Page 1 (odd) → ODD PAGE HEADER. Page 2 (even) → EVEN PAGE HEADER. Page 3 (odd) → ODD PAGE HEADER."));
    for (var i = 1; i <= 50; i++)
        doc.Blocks.Add(new Paragraph($"Paragraph {i}: Mirror-margin headers alternate on odd/even pages."));

    return doc;
}

/// <summary>
/// Renders a document containing three floating images (behind-text, in-front, and square-wrap)
/// to verify the FO1 floating-image render path: correct placement, z-order, and image pixel output.
/// </summary>
static int RenderFloatingImageScene(string outPath, List<FreeWVisualEvidenceRow> evidence)
{
    if (!PageShotScenarioSelection.Includes("page-composition-floating-image"))
        return 0;

    var doc = BuildFloatingImageDocument();
    var view = new DocumentView();
    view.LoadDocument(doc);
    view.ViewMode = DocumentViewMode.PrintLayout;
    view.Measure(new Size(816, 1400));
    view.Arrange(new Rect(0, 0, 816, 1400));
    view.UpdateLayout();
    Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

    var bitmap = new RenderTargetBitmap(new PixelSize(816, 1400), new Vector(96, 96));
    bitmap.Render(view);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    var bytes = stream.ToArray();

    if (bytes.Length > 0)
    {
        File.WriteAllBytes(outPath, bytes);
        AddAvaloniaEvidence(
            evidence,
            scenarioId: "page-composition-floating-image",
            outputPath: outPath,
            pngBytes: bytes,
            pixelWidth: 816,
            pixelHeight: 1400,
            page: doc.Page,
            layoutKind: DocumentViewLayoutKind.PrintLayout,
            captureSource: "avalonia-render-target",
            viewMode: "PrintLayout",
            document: doc);
        Console.WriteLine($"[PageLayoutShot] Floating Image: {bytes.Length:N0} bytes → {outPath}");
        return 0;
    }

    Console.Error.WriteLine("[PageLayoutShot] Floating Image: encoding produced 0 bytes.");
    return 2;
}

/// <summary>
/// Builds a document with body text + three floating images to exercise the full FO1 path:
/// • InFront image  (Square wrap, zOrder=10) — rendered after text, visible on top
/// • Behind  image  (Behind wrap, zOrder=1)  — rendered before text, behind body
/// • TopAndBottom   (Square wrap, zOrder=5)  — in-front bucket, medium z-order
/// </summary>
static TextDocument BuildFloatingImageDocument()
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Clear();

    var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };

    // Tiny 4x4 orange PNG (validates that a real bitmap is drawn, not just placeholder).
    static byte[] TinyPng()
    {
        using var bmp = new SkiaSharp.SKBitmap(40, 30, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        bmp.Erase(new SkiaSharp.SKColor(255, 128, 0)); // orange fill
        using var img  = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // Anchor paragraph: has body text AND a floating image (InFront, Square wrap).
    var anchorPara = new Paragraph();
    anchorPara.Runs.Add(new Run(
        "This paragraph has a floating image anchored to it (Square wrap, in-front). " +
        "The orange rectangle should appear on top of this text.", bodyFmt));
    var imgInFront = new InlineImage(TinyPng(), 144, 72)
    {
        Wrapping           = ImageWrapping.InFront,
        HorizontalOffsetPt = 72,   // 1 in from column left
        VerticalOffsetPt   = 24,   // 1/3 in below paragraph top
        HorizontalAnchor   = HorizontalAnchor.Column,
        VerticalAnchor     = VerticalAnchor.Paragraph,
        ZOrderIndex        = 10,
    };
    anchorPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = imgInFront });
    doc.Blocks.Add(anchorPara);

    // Second paragraph: behind-text image (should render below text).
    var behindPara = new Paragraph();
    behindPara.Runs.Add(new Run(
        "This paragraph has a behind-text floating image. The orange rectangle should " +
        "appear BEHIND this text (text drawn on top of the image).", bodyFmt));
    var imgBehind = new InlineImage(TinyPng(), 180, 80)
    {
        Wrapping           = ImageWrapping.Behind,
        HorizontalOffsetPt = 36,
        VerticalOffsetPt   = 0,
        HorizontalAnchor   = HorizontalAnchor.Column,
        VerticalAnchor     = VerticalAnchor.Paragraph,
        ZOrderIndex        = 1,
    };
    behindPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = imgBehind });
    doc.Blocks.Add(behindPara);

    // Third paragraph: page-anchor image (VerticalAnchor.Page).
    var pagePara = new Paragraph();
    pagePara.Runs.Add(new Run(
        "This paragraph has a page-anchored floating image (absolute position on the page). " +
        "The orange rectangle should appear at a fixed position from the page top.", bodyFmt));
    var imgPage = new InlineImage(TinyPng(), 100, 60)
    {
        Wrapping           = ImageWrapping.TopAndBottom,
        HorizontalOffsetPt = 400,
        VerticalOffsetPt   = 200,
        HorizontalAnchor   = HorizontalAnchor.Page,
        VerticalAnchor     = VerticalAnchor.Page,
        ZOrderIndex        = 5,
    };
    pagePara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = imgPage });
    doc.Blocks.Add(pagePara);

    // More body text so the page has content around the floats.
    for (var i = 1; i <= 8; i++)
    {
        var p = new Paragraph();
        p.Runs.Add(new Run(
            $"Body paragraph {i}: lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
            "The quick brown fox jumps over the lazy dog.", bodyFmt));
        doc.Blocks.Add(p);
    }

    return doc;
}

static int RenderMode(
    DocumentViewMode mode,
    string outPath,
    int width,
    int height,
    string label,
    string scenarioId,
    List<FreeWVisualEvidenceRow> evidence,
    Func<TextDocument>? documentFactory = null,
    int pageNumber = 1,
    int pageCount = 1,
    double viewportOffsetY = 0,
    bool alignViewportToRequestedPage = false,
    bool hasFootnotes = false,
    bool hasEndnotes = false,
    bool isSyntheticPage = false)
{
    if (!PageShotScenarioSelection.Includes(scenarioId))
        return 0;

    var sourceDocument = PageShotFixtureSource.Resolve(
        scenarioId,
        documentFactory ?? BuildMultiPageDocument);
    if (alignViewportToRequestedPage && pageNumber > 1)
    {
        var surfacePlan = DocumentViewLayoutPlanner.BuildSurfacePlan(
            sourceDocument.Page,
            DocumentViewLayoutKind.PrintLayout,
            width);
        viewportOffsetY = surfacePlan.PageTopDip(pageNumber - 1);
    }
    var sectionPageSurface = ResolveSectionPageSurfacePlan(scenarioId, sourceDocument, pageNumber, pageCount);
    var sectionGeometryPage = sectionPageSurface?.PagePlan
        ?? ResolveSectionGeometryPage(scenarioId, sourceDocument, pageNumber, pageCount);
    var doc = sectionPageSurface?.Document ?? sourceDocument;
    var evidencePage = sectionGeometryPage?.Page ?? doc.Page;
    if (sectionPageSurface is not null)
    {
        width = (int)Math.Max(1, Math.Ceiling(sectionPageSurface.CaptureWidthDip));
        height = (int)Math.Max(1, Math.Ceiling(sectionPageSurface.CaptureHeightDip));
        viewportOffsetY = 0;
    }

    var view = new DocumentView();
    view.LoadDocument(doc);
    view.ViewMode = mode;
    Control renderTarget = view;
    if (viewportOffsetY > 0)
    {
        var contentHeight = height + viewportOffsetY;
        view.Width = width;
        view.Height = contentHeight;
        var frame = new Canvas
        {
            Width = width,
            Height = height,
            ClipToBounds = true,
            Background = Brushes.Transparent
        };
        frame.Children.Add(view);
        Canvas.SetTop(view, -viewportOffsetY);
        frame.Measure(new Size(width, height));
        frame.Arrange(new Rect(0, 0, width, height));
        frame.UpdateLayout();
        renderTarget = frame;
    }
    else
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
    }
    Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

    var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
    bitmap.Render(renderTarget);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    var bytes = stream.ToArray();
    var capturesWordComparablePageSurface = ShouldCaptureWordComparablePageSurface(scenarioId);
    // An isolated section surface always renders its selected source page as page one.
    var cropPageNumber = sectionPageSurface is null ? pageNumber : 1;
    var captureWidth = width;
    var captureHeight = height;
    if (capturesWordComparablePageSurface)
    {
        (bytes, captureWidth, captureHeight) = CropToDocumentPageSurface(
            bytes,
            evidencePage,
            width,
            height,
            cropPageNumber,
            viewportOffsetY,
            WordComparableContentOffsetY(scenarioId, pageNumber));
    }
    if (ShouldNormalizeSectionPageSurfaceToWordBaseline(scenarioId))
    {
        (bytes, captureWidth, captureHeight) = NormalizeToWordBaselineRasterSurface(
            bytes,
            captureWidth,
            captureHeight);
    }
    if (bytes.Length > 0)
    {
        File.WriteAllBytes(outPath, bytes);
        AddAvaloniaEvidence(
            evidence,
            scenarioId,
            outPath,
            bytes,
            captureWidth,
            captureHeight,
            evidencePage,
            LayoutKindFor(mode),
            captureSource: capturesWordComparablePageSurface
                ? "avalonia-word-page-surface"
                : sectionPageSurface is null
                ? "avalonia-render-target"
                : "avalonia-section-page-surface",
            viewMode: mode.ToString(),
            pageNumber: pageNumber,
            pageCount: pageCount,
            hasFootnotes: hasFootnotes,
            hasEndnotes: hasEndnotes,
            isSyntheticPage: isSyntheticPage,
            sectionGeometryPage: sectionGeometryPage,
            sectionGeometrySurfacePlan: sectionPageSurface,
            document: doc,
            evidenceDocument: sectionPageSurface is null ? null : sourceDocument,
            noteRegionOverlayApplied: false);
        Console.WriteLine($"[PageLayoutShot] {label}: {bytes.Length:N0} bytes → {outPath}");
        return 0;
    }

    if (IsBackstageRendererScenario(scenarioId))
    {
        Console.Error.WriteLine($"[PageLayoutShot] {label}: Avalonia RenderTargetBitmap produced 0 bytes; refusing placeholder fallback for backstage renderer evidence.");
        return 2;
    }

    if (IsReviewRendererScenario(scenarioId))
    {
        Console.Error.WriteLine($"[PageLayoutShot] {label}: Avalonia RenderTargetBitmap produced 0 bytes; refusing placeholder fallback for review renderer evidence.");
        return 2;
    }

    // Fallback: encode via SkiaSharp if the Avalonia encoder produced nothing.
    var pngBytes = TryEncodeViaSkia(renderTarget, width, height, label);
    if (pngBytes is { Length: > 0 })
    {
        var fallbackWidth = width;
        var fallbackHeight = height;
        if (ShouldCaptureWordComparablePageSurface(scenarioId))
        {
            (pngBytes, fallbackWidth, fallbackHeight) = CropToDocumentPageSurface(
                pngBytes,
                evidencePage,
                width,
                height,
                cropPageNumber,
                viewportOffsetY,
                WordComparableContentOffsetY(scenarioId, pageNumber));
        }
        if (ShouldNormalizeSectionPageSurfaceToWordBaseline(scenarioId))
        {
            (pngBytes, fallbackWidth, fallbackHeight) = NormalizeToWordBaselineRasterSurface(
                pngBytes,
                fallbackWidth,
                fallbackHeight);
        }
        pngBytes = AddNoteRegionOverlayIfNeeded(
            pngBytes,
            fallbackWidth,
            fallbackHeight,
            evidencePage,
            doc,
            pageNumber,
            hasFootnotes,
            hasEndnotes,
            isSyntheticPage);
        File.WriteAllBytes(outPath, pngBytes);
        AddAvaloniaEvidence(
            evidence,
            scenarioId,
            outPath,
            pngBytes,
            fallbackWidth,
            fallbackHeight,
            evidencePage,
            LayoutKindFor(mode),
            captureSource: ShouldCaptureWordComparablePageSurface(scenarioId)
                ? "skia-fallback-word-page-surface"
                : sectionPageSurface is null
                ? "skia-fallback-placeholder"
                : "skia-fallback-section-page-surface",
            viewMode: mode.ToString(),
            pageNumber: pageNumber,
            pageCount: pageCount,
            hasFootnotes: hasFootnotes,
            hasEndnotes: hasEndnotes,
            isSyntheticPage: isSyntheticPage,
            sectionGeometryPage: sectionGeometryPage,
            sectionGeometrySurfacePlan: sectionPageSurface,
            document: doc,
            evidenceDocument: sectionPageSurface is null ? null : sourceDocument,
            noteRegionOverlayApplied: true);
        Console.WriteLine($"[PageLayoutShot] {label} (Skia fallback): {pngBytes.Length:N0} bytes → {outPath}");
        return 0;
    }

    Console.Error.WriteLine($"[PageLayoutShot] {label}: both encoding paths produced 0 bytes.");
    return 2;
}

static DocumentViewLayoutKind LayoutKindFor(DocumentViewMode mode) =>
    mode switch
    {
        DocumentViewMode.WebLayout => DocumentViewLayoutKind.WebLayout,
        DocumentViewMode.Draft => DocumentViewLayoutKind.Draft,
        _ => DocumentViewLayoutKind.PrintLayout
    };

static string VisualEvidenceOutputPath(string outDir, string scenarioId, int pageNumber) =>
    Path.GetFullPath(Path.Combine(
        outDir,
        FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, pageNumber)));

static bool IsBackstageRendererScenario(string scenarioId) =>
    FreeWVisualEvidenceManifestNormalizer.BackstageRendererScenarioIds.Contains(
        scenarioId,
        StringComparer.OrdinalIgnoreCase);

static bool IsReviewRendererScenario(string scenarioId) =>
    FreeWVisualEvidenceManifestNormalizer.ReviewRendererScenarioIds.Contains(
        scenarioId,
        StringComparer.OrdinalIgnoreCase);

static string BackstageWorkflowForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "print-preview",
        "backstage-pdf-export-fidelity" => "pdf-export",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static string BackstageArtifactKindForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "print-preview-fixed-layout",
        "backstage-pdf-export-fidelity" => "pdf-export-rasterized",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static string BackstagePipelineForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "print-preview-fixed-layout-artifact",
        "backstage-pdf-export-fidelity" => "pdf-export-rasterized-artifact",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static string BackstageCaptureRouteForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "backstage-print-preview-fixed-layout-capture",
        "backstage-pdf-export-fidelity" => "backstage-pdf-export-raster-capture",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static void AddAvaloniaEvidence(
    List<FreeWVisualEvidenceRow> evidence,
    string scenarioId,
    string outputPath,
    byte[] pngBytes,
    int pixelWidth,
    int pixelHeight,
    PageSettings page,
    DocumentViewLayoutKind layoutKind,
    string captureSource,
    string viewMode,
    int pageNumber = 1,
    int pageCount = 1,
    bool hasFootnotes = false,
    bool hasEndnotes = false,
    bool isSyntheticPage = false,
    FreeWVisualSectionGeometryPagePlan? sectionGeometryPage = null,
    FreeWVisualSectionGeometrySurfacePlan? sectionGeometrySurfacePlan = null,
    TextDocument? document = null,
    TextDocument? evidenceDocument = null,
    bool noteRegionOverlayApplied = false)
{
    var expectationDocument = evidenceDocument ?? document;
    var stats = ComputePngPixelStats(pngBytes, pixelWidth, pixelHeight);
    var sectionOrdinal = sectionGeometryPage?.SectionOrdinal
        ?? (expectationDocument is null ? 1 : FreeWVisualEvidencePlanner.ResolveSectionOrdinal(expectationDocument, page));
    var sectionRelativePageNumber = sectionGeometryPage?.SectionRelativePageNumber ?? 1;
    var sectionOwnerId = sectionGeometryPage?.SectionOwnerId
        ?? FreeWVisualEvidencePlanner.BuildSectionOwnerId(sectionOrdinal);
    var metadata = new Dictionary<string, string>
    {
        ["renderer"] = "FreeW.PageLayoutShot",
        ["captureSource"] = captureSource,
        ["viewMode"] = viewMode,
        ["pageNumber"] = pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["pageCount"] = pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    if (sectionGeometryPage is not null)
    {
        metadata["sectionGeometryEvidence"] = sectionGeometrySurfacePlan is null
            ? "shared-expectation"
            : "shared-page-surface";
        metadata["sectionGeometryRenderStatus"] = sectionGeometrySurfacePlan is null
            ? "shared-expectation"
            : "avalonia-" + sectionGeometrySurfacePlan.RenderStatus;
        metadata["expectedOrientation"] = sectionGeometryPage.Orientation;
    }

    if (IsBackstageRendererScenario(scenarioId))
    {
        metadata["backstageWorkflow"] = BackstageWorkflowForScenario(scenarioId);
        metadata["backstageArtifactKind"] = BackstageArtifactKindForScenario(scenarioId);
        metadata["backstagePipeline"] = BackstagePipelineForScenario(scenarioId);
        metadata["backstageCaptureRoute"] = BackstageCaptureRouteForScenario(scenarioId);
    }

    if (sectionGeometrySurfacePlan is not null)
    {
        metadata["sectionPageSurfaceEvidence"] = "shared-page-surface";
        metadata["sectionPageSurfaceRenderStatus"] = "avalonia-" + sectionGeometrySurfacePlan.RenderStatus;
        metadata["sectionSurfaceSourceBlocks"] = string.Join(",", sectionGeometrySurfacePlan.SourceBlockIndexes);
        metadata["sectionSurfacePageWidthDip"] = PageLayout.PointsToDip(sectionGeometrySurfacePlan.Page.WidthPt)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["sectionSurfacePageHeightDip"] = PageLayout.PointsToDip(sectionGeometrySurfacePlan.Page.HeightPt)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["sectionSurfaceCaptureWidthDip"] = sectionGeometrySurfacePlan.CaptureWidthDip
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["sectionSurfaceCaptureHeightDip"] = sectionGeometrySurfacePlan.CaptureHeightDip
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["sectionSurfacePageLeftDip"] = sectionGeometrySurfacePlan.PageLeftDip
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["sectionSurfacePageTopDip"] = sectionGeometrySurfacePlan.PageTopDip
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    var noteRegionPlan = BuildEvidenceNoteRegionPlan(document, page, pageNumber, hasFootnotes, hasEndnotes, isSyntheticPage);
    if (noteRegionPlan is { HasContent: true })
    {
        metadata["noteRegionKind"] = noteRegionPlan.Kind.ToString();
        metadata["noteRegionRows"] = noteRegionPlan.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["noteRegionLabels"] = string.Join(",", noteRegionPlan.Rows.Select(r => r.Label));
        metadata["noteRegionRenderStatus"] = noteRegionOverlayApplied
            ? "shared-plan-overlay"
            : "avalonia-document-view";
    }

    var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
        scenarioId: scenarioId,
        hostId: "avalonia-page-layout-shot",
        outputPath: outputPath,
        pixelWidth: stats.Width > 0 ? stats.Width : pixelWidth,
        pixelHeight: stats.Height > 0 ? stats.Height : pixelHeight,
        byteLength: pngBytes.LongLength,
        pixelStats: stats,
        page: page,
        pageNumber: pageNumber,
        pageCount: pageCount,
        layoutKind: layoutKind,
        availableWidthDip: pixelWidth,
        headerSlotName: ResolveAvaloniaHeaderSlotName(expectationDocument, pageNumber),
        footerSlotName: ResolveAvaloniaFooterSlotName(expectationDocument, pageNumber),
        hasFootnotes: hasFootnotes,
        hasEndnotes: hasEndnotes,
        isSyntheticPage: isSyntheticPage,
        sectionOrdinal: sectionOrdinal,
        sectionRelativePageNumber: sectionRelativePageNumber,
        sectionOwnerId: sectionOwnerId,
        hostMetadata: metadata,
        document: expectationDocument);
    FreeWVisualEvidencePlanner.EnsureTrusted(row);
    evidence.Add(row);
}

static DocumentNoteRegionPlan? BuildEvidenceNoteRegionPlan(
    TextDocument? document,
    PageSettings page,
    int pageNumber,
    bool hasFootnotes,
    bool hasEndnotes,
    bool isSyntheticPage)
{
    if (document is null)
        return null;

    var (contentWidth, _) = PageLayout.ContentAreaDip(page);
    if (hasFootnotes)
    {
        var ids = DocumentNoteRegionPlanner.FootnoteIdsForEvidencePage(document, pageNumber);
        return ids.Count == 0
            ? null
            : DocumentNoteRegionPlanner.BuildFootnoteRegion(document, ids, pageNumber, contentWidth);
    }

    if (hasEndnotes)
    {
        var ids = DocumentNoteRegionPlanner.EndnoteIdsForSyntheticPage(document);
        return ids.Count == 0
            ? null
            : DocumentNoteRegionPlanner.BuildEndnoteRegion(document, ids, pageNumber, contentWidth, isSyntheticPage: false);
    }

    return null;
}

static bool ShouldCaptureWordComparablePageSurface(string scenarioId) =>
    string.Equals(scenarioId, "page-composition-columns", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-footnotes", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-endnotes", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "equation-structures", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "chart-smartart-complex", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "object-format-position-size-style", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "field-page-number-variants", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-hf-images", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-hf-oddeven", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "table-layout-complex", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "table-pagination-repeat-header", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "table-page-composition-stress", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "wordart-watermark-stress", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "wordart-picture-watermark-layout", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-tracked-changes", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-comments", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "review-compare-visual-proof", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "review-proofing-visual-depth", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "review-combine-visual-proof", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(scenarioId, "f2-section-landscape", StringComparison.OrdinalIgnoreCase);

static bool ShouldNormalizeSectionPageSurfaceToWordBaseline(string scenarioId) =>
    string.Equals(scenarioId, "f2-section-landscape", StringComparison.OrdinalIgnoreCase);

static int WordComparableContentOffsetY(string scenarioId, int pageNumber)
{
    if (string.Equals(scenarioId, "equation-structures", StringComparison.OrdinalIgnoreCase))
        return 2;

    return string.Equals(scenarioId, "f2-footnotes", StringComparison.OrdinalIgnoreCase) ||
           (string.Equals(scenarioId, "f2-endnotes", StringComparison.OrdinalIgnoreCase) && pageNumber == 1)
        ? 1
        : 0;
}

static (byte[] PngBytes, int PixelWidth, int PixelHeight) CropToDocumentPageSurface(
    byte[] pngBytes,
    PageSettings page,
    int fallbackWidth,
    int fallbackHeight,
    int pageNumber,
    double viewportOffsetY,
    int contentOffsetY)
{
    using var source = SKBitmap.Decode(pngBytes);
    if (source is null)
        return (pngBytes, fallbackWidth, fallbackHeight);

    var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(page);
    var pixelWidth = Math.Max(1, (int)Math.Round(pageWidthDip));
    var pixelHeight = Math.Max(1, (int)Math.Round(pageHeightDip));
    if (source.Width < pixelWidth || source.Height < pixelHeight)
        return (pngBytes, source.Width, source.Height);

    // Reuse the same Print Layout surface geometry that DocumentView uses. The frame may already
    // have scrolled to the requested page, so translate its physical page top into the viewport.
    var sourceX = Math.Clamp((source.Width - pixelWidth) / 2, 0, source.Width - pixelWidth);
    var pageIndex = Math.Max(0, pageNumber - 1);
    var surfacePlan = DocumentViewLayoutPlanner.BuildSurfacePlan(
        page,
        DocumentViewLayoutKind.PrintLayout,
        source.Width);
    var pageTopInViewport = surfacePlan.PageTopDip(pageIndex) - viewportOffsetY;
    var sourceY = Math.Clamp((int)Math.Round(pageTopInViewport), 0, source.Height - pixelHeight);
    using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
    if (surface is null)
        return (pngBytes, source.Width, source.Height);

    surface.Canvas.Clear(SKColors.White);
    surface.Canvas.DrawBitmap(
        source,
        new SKRect(sourceX, sourceY, sourceX + pixelWidth, sourceY + pixelHeight),
        new SKRect(0, contentOffsetY, pixelWidth, pixelHeight + contentOffsetY));
    if (contentOffsetY > 0)
    {
        // The print-layout editor draws a page-chrome outline. Word's exported page PNG does not.
        using var pageChromeMask = new SKPaint { Color = SKColors.White, IsAntialias = false };
        surface.Canvas.DrawRect(0, contentOffsetY, pixelWidth, 1, pageChromeMask);
        surface.Canvas.DrawRect(0, contentOffsetY, 1, pixelHeight - contentOffsetY, pageChromeMask);
        surface.Canvas.DrawRect(pixelWidth - 1, contentOffsetY, 1, pixelHeight - contentOffsetY, pageChromeMask);
    }
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 95);
    return data is null
        ? (pngBytes, source.Width, source.Height)
        : (data.ToArray(), pixelWidth, pixelHeight);
}

static (byte[] PngBytes, int PixelWidth, int PixelHeight) NormalizeToWordBaselineRasterSurface(
    byte[] pngBytes,
    int fallbackWidth,
    int fallbackHeight)
{
    using var source = SKBitmap.Decode(pngBytes);
    if (source is null)
        return (pngBytes, fallbackWidth, fallbackHeight);

    var plan = WordBaselineRasterSurfacePlanner.Build(source.Width, source.Height);
    if (plan.IsIdentity)
        return (pngBytes, source.Width, source.Height);

    using var surface = SKSurface.Create(new SKImageInfo(
        plan.PixelWidth,
        plan.PixelHeight,
        SKColorType.Bgra8888,
        SKAlphaType.Premul));
    if (surface is null)
        return (pngBytes, source.Width, source.Height);

    surface.Canvas.Clear(SKColors.White);
    using (var paint = new SKPaint { IsAntialias = true })
    {
        surface.Canvas.DrawBitmap(
            source,
            new SKRect(0, 0, source.Width, source.Height),
            new SKRect(0, 0, plan.PixelWidth, plan.PixelHeight),
            paint);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 95);
    return data is null
        ? (pngBytes, source.Width, source.Height)
        : (data.ToArray(), plan.PixelWidth, plan.PixelHeight);
}

static byte[] AddNoteRegionOverlayIfNeeded(
    byte[] pngBytes,
    int pixelWidth,
    int pixelHeight,
    PageSettings page,
    TextDocument? document,
    int pageNumber,
    bool hasFootnotes,
    bool hasEndnotes,
    bool isSyntheticPage)
{
    if (pngBytes.Length == 0)
        return pngBytes;

    var plan = BuildEvidenceNoteRegionPlan(document, page, pageNumber, hasFootnotes, hasEndnotes, isSyntheticPage);
    if (plan is not { HasContent: true })
        return pngBytes;

    using var bitmap = SKBitmap.Decode(pngBytes);
    if (bitmap is null)
        return pngBytes;

    using var canvas = new SKCanvas(bitmap);
    using var separatorPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 1, IsAntialias = true };
    using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var labelPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var headingPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var textFont = new SKFont { Size = 12 };
    using var labelFont = new SKFont { Size = 9 };
    using var headingFont = new SKFont { Size = 15, Embolden = true };

    var (pageWidthDip, _) = PageLayout.PageSizeDip(page);
    var (marginLeftDip, marginTopDip, _, marginBottomDip) = PageLayout.MarginsDip(page);
    var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);
    var pageLeft = Math.Max(0, (pixelWidth - pageWidthDip) / 2.0);
    var x = (float)Math.Max(12, pageLeft + marginLeftDip);
    var maxTextWidth = (float)Math.Max(120, Math.Min(contentWidthDip, pixelWidth - x - 24));
    var y = plan.IsSyntheticPage
        ? (float)Math.Max(48, marginTopDip + 24)
        : (float)Math.Max(48, pixelHeight - marginBottomDip - plan.EstimatedHeightDip - 36);
    if (plan.Kind == DocumentNoteRegionKind.Endnotes && !plan.IsSyntheticPage)
        y = Math.Max((float)marginTopDip, FindLastPaintedRow(bitmap) + 16);

    if (plan.Heading is not null)
    {
        canvas.DrawText(plan.Heading, x, y, SKTextAlign.Left, headingFont, headingPaint);
        y += 21;
    }

    canvas.DrawLine(
        x + (float)plan.SeparatorXOffsetDip,
        y,
        x + (float)Math.Min(maxTextWidth, plan.SeparatorWidthDip),
        y,
        separatorPaint);
    y += 16;

    foreach (var row in plan.Rows)
    {
        canvas.DrawText(row.Label, x, y - 3, SKTextAlign.Left, labelFont, labelPaint);
        var lineX = x + 16;
        foreach (var line in WrapNoteText(row.Text, textFont, maxTextWidth - 16))
        {
            canvas.DrawText(line, lineX, y, SKTextAlign.Left, textFont, textPaint);
            y += 15;
        }

        y += 3;
    }

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 95);
    return data?.ToArray() ?? pngBytes;
}

static int FindLastPaintedRow(SKBitmap bitmap)
{
    for (var y = bitmap.Height - 2; y >= 0; y--)
    {
        // The captured Print Layout page has a one-pixel sheet border. Ignore page chrome so the
        // final body line, rather than that border, determines where endnotes begin.
        for (var x = 24; x < bitmap.Width - 24; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.Red < 245 || color.Green < 245 || color.Blue < 245)
                return y;
        }
    }

    return 0;
}

static IEnumerable<string> WrapNoteText(string text, SKFont font, float maxWidth)
{
    var words = text.ReplaceLineEndings(" ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var line = string.Empty;
    foreach (var word in words)
    {
        var candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
        if (font.MeasureText(candidate) <= maxWidth || string.IsNullOrEmpty(line))
        {
            line = candidate;
            continue;
        }

        yield return line;
        line = word;
    }

    if (!string.IsNullOrEmpty(line))
        yield return line;
}

static string? ResolveAvaloniaHeaderSlotName(TextDocument? document, int pageNumber) =>
    document is null ? null : ResolveAvaloniaHeaderFooterSlot(document, pageNumber, header: true)?.Name;

static string? ResolveAvaloniaFooterSlotName(TextDocument? document, int pageNumber) =>
    document is null ? null : ResolveAvaloniaHeaderFooterSlot(document, pageNumber, header: false)?.Name;

static (string Name, HeaderFooter Value)? ResolveAvaloniaHeaderFooterSlot(
    TextDocument document,
    int pageNumber,
    bool header)
{
    var hf = document.FinalSectionHeadersFooters;
    if (pageNumber == 1 && document.Page.DifferentFirstPage)
    {
        var first = header ? hf.FirstHeader : hf.FirstFooter;
        if (first is not null && !first.IsEmpty)
            return (header ? "first-header" : "first-footer", first);
    }

    if (document.Page.DifferentOddEvenPages && pageNumber % 2 == 0)
    {
        var even = header ? hf.EvenHeader : hf.EvenFooter;
        if (even is not null && !even.IsEmpty)
            return (header ? "even-header" : "even-footer", even);
    }

    var normal = header ? hf.Header : hf.Footer;
    if (normal is not null && !normal.IsEmpty)
        return (header ? "header" : "footer", normal);

    return null;
}

static FreeWVisualSectionGeometryPagePlan? ResolveSectionGeometryPage(
    string scenarioId,
    TextDocument document,
    int pageNumber,
    int pageCount)
{
    if (!FreeWVisualEvidenceManifestNormalizer.SectionGeometryRendererScenarioIds.Contains(
            scenarioId,
            StringComparer.OrdinalIgnoreCase))
    {
        return null;
    }

    return FreeWVisualEvidencePlanner
        .BuildSectionGeometryPagePlans(document, pageCount)
        .FirstOrDefault(page => page.PageNumber == pageNumber);
}

static FreeWVisualSectionGeometrySurfacePlan? ResolveSectionPageSurfacePlan(
    string scenarioId,
    TextDocument document,
    int pageNumber,
    int pageCount)
{
    if (!FreeWVisualEvidenceManifestNormalizer.SectionPageSurfaceRendererScenarioIds.Contains(
            scenarioId,
            StringComparer.OrdinalIgnoreCase))
    {
        return null;
    }

    return FreeWVisualEvidencePlanner
        .BuildSectionGeometrySurfacePlans(document, pageCount)
        .FirstOrDefault(page => page.PageNumber == pageNumber);
}

static FreeWVisualPixelStats ComputePngPixelStats(byte[] pngBytes, int fallbackWidth, int fallbackHeight)
{
    using var bitmap = SKBitmap.Decode(pngBytes);
    if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
    {
        return FreeWVisualEvidencePlanner.ComputePixelStats(
            ReadOnlySpan<byte>.Empty,
            fallbackWidth,
            fallbackHeight,
            0,
            FreeWVisualEvidencePixelFormat.Rgba32);
    }

    var width = bitmap.Width;
    var height = bitmap.Height;
    var stride = width * 4;
    var pixels = new byte[stride * height];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var offset = y * stride + x * 4;
            pixels[offset] = color.Red;
            pixels[offset + 1] = color.Green;
            pixels[offset + 2] = color.Blue;
            pixels[offset + 3] = color.Alpha;
        }
    }

    return FreeWVisualEvidencePlanner.ComputePixelStats(
        pixels,
        width,
        height,
        stride,
        FreeWVisualEvidencePixelFormat.Rgba32);
}

static byte[] TryEncodeViaSkia(Control view, int width, int height, string label = "")
{
    try
    {
        // Use a WriteableBitmap and draw into it via a fresh ImmediateDrawingContext.
        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var locked = wb.Lock())
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, locked.Address, locked.RowBytes);
            if (surface is null)
                return [];

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Gray);

            // Re-render via Avalonia ImmediateDrawingContext onto the SK surface is not directly
            // available here without Avalonia internals. Record a best-effort grey placeholder.
            canvas.DrawRect(new SKRect(24, 24, width - 24, height - 24), new SKPaint
            {
                Color = SKColors.White,
                IsStroke = false,
            });
            using var textFont  = new SKFont(SKTypeface.Default, 16);
            using var textPaint = new SKPaint { Color = SKColors.DarkBlue, IsAntialias = true };
            canvas.DrawText($"FreeW — {label} (Skia fallback placeholder)", 50, 70,
                SKTextAlign.Left, textFont, textPaint);
            canvas.DrawText("Run FreeW normally to see the real page chrome.", 50, 100,
                SKTextAlign.Left, textFont, textPaint);
            surface.Flush();
        }

        using var readLocked = wb.Lock();
        var infoOut = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap();
        if (!bmp.InstallPixels(infoOut, readLocked.Address, readLocked.RowBytes))
            return [];

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data?.ToArray() ?? [];
    }
    catch
    {
        return [];
    }
}

/// <summary>
/// Builds a document long enough to span 2–3 pages so the multi-page pagination
/// (discrete white page rects with grey gaps) is visible in the captured PNG.
/// A standard US-Letter page (11 in = 792pt) with 1-inch margins leaves ~9 in of
/// text area. At 12pt body text and ~1.3 leading that's roughly 50 lines per page,
/// so we add enough paragraphs to cross at least two page boundaries.
/// </summary>
static TextDocument BuildMultiPageDocument()
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Clear();

    // Standard US-Letter (default from PageSettings).
    // doc.Page.WidthPt = 612; doc.Page.HeightPt = 792; margins = 72pt each side.

    doc.Styles["Heading1"] = new DocumentStyle
    {
        Id        = "Heading1",
        Name      = "Heading 1",
        Run       = RunFormatting.Default with { Bold = true, FontSizePt = 18, ColorHex = "#2B5797" },
        Paragraph = ParagraphFormatting.Default with { SpaceBeforePt = 12, SpaceAfterPt = 6 },
    };
    doc.Styles["Heading2"] = new DocumentStyle
    {
        Id        = "Heading2",
        Name      = "Heading 2",
        Run       = RunFormatting.Default with { Bold = true, FontSizePt = 14, ColorHex = "#2E6DA4" },
        Paragraph = ParagraphFormatting.Default with { SpaceBeforePt = 10, SpaceAfterPt = 4 },
    };

    var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };

    void AddH1(string text)
    {
        var p = new Paragraph { StyleId = "Heading1" };
        p.Runs.Add(new Run(text));
        doc.Blocks.Add(p);
    }

    void AddH2(string text)
    {
        var p = new Paragraph { StyleId = "Heading2" };
        p.Runs.Add(new Run(text));
        doc.Blocks.Add(p);
    }

    void AddPara(string text)
    {
        var p = new Paragraph();
        p.Runs.Add(new Run(text, bodyFmt));
        doc.Blocks.Add(p);
    }

    // ---- Page 1 ----
    AddH1("FreeW — Discrete Multi-Page Pagination");
    AddPara(
        "This document spans multiple pages to verify the discrete pagination feature. " +
        "Each white rectangle represents one page, separated by grey desk gaps — exactly " +
        "like Microsoft Word's Print Layout view.");
    AddH2("Background");
    AddPara(
        "Earlier builds rendered a single tall white page. The new layout engine computes " +
        "a text-area height per page (page height minus top and bottom margins) and wraps " +
        "content line-granularly: a complete line that would cross the bottom margin is " +
        "pushed to the top margin of the next page.");
    AddPara(
        "The formula for page-space Y is: DeskPadding + pageIndex*(pageHeightPx+PageGap) " +
        "+ marginTopDip + offsetWithinTextArea. All glyph coordinates, caret positions, " +
        "hit-testing, selection rendering, find highlights, and GetBlockTop() use the same " +
        "mapping, preserving editing behaviour across page boundaries.");
    AddH2("Coordinate transform");
    AddPara(
        "Content Y (0 = start of first text area) increases monotonically through the " +
        "document. Page index = floor(contentY / textAreaHeight). Offset within the page = " +
        "contentY mod textAreaHeight. Page-space Y adds that offset to the Y of the top " +
        "margin of the chosen page rectangle.");
    AddPara(
        "The ReserveContentY helper checks whether the next line fits in the remaining " +
        "space on the current page (posInPage + lineHeight <= textAreaHeight). If not, it " +
        "bumps contentY to the start of the next page before placing the line. This ensures " +
        "no line is ever split across a page boundary.");
    AddPara(
        "Tables are treated row-by-row: each row is reserved as a unit on the current page " +
        "or pushed to the next. Images are similarly reserved as a whole block. Paragraph " +
        "space-before and space-after accumulate in content-Y space so they scale correctly " +
        "across page boundaries.");
    AddH2("Status bar");
    AddPara(
        "The status bar now shows 'Page X of Y' where X is the one-based index of the page " +
        "containing the caret, and Y is the total page count. The page count is recomputed " +
        "on every layout pass; the caret page updates whenever the caret moves.");
    // Add filler paragraphs to push into page 2.
    for (int i = 1; i <= 12; i++)
        AddPara($"Paragraph {i} of filler text on page 1 — lorem ipsum dolor sit amet, " +
                "consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore " +
                "et dolore magna aliqua. Ut enim ad minim veniam.");

    // ---- Page 2 ----
    AddH1("Page 2 — Content continues here");
    AddPara(
        "This heading and the body below it are on page 2. The grey gap between pages 1 " +
        "and 2 is clearly visible in the rendered PNG, confirming that the page-break " +
        "logic placed content correctly.");
    for (int i = 1; i <= 12; i++)
        AddPara($"Body paragraph {i} on page 2 — the quick brown fox jumps over the lazy " +
                "dog. Pack my box with five dozen liquor jugs. How vexingly quick daft " +
                "zebras jump!");

    // ---- Page 3 ----
    AddH1("Page 3 — Third page verification");
    AddPara(
        "Reaching page 3 confirms the pagination loop handles more than one page boundary " +
        "correctly. PDF export, undo/redo, find/replace, and navigation-pane scroll all " +
        "continue to work because they all share the same page-space Y transform.");
    for (int i = 1; i <= 6; i++)
        AddPara($"Final filler paragraph {i} on page 3. Sphinx of black quartz, judge my " +
                "vow. The five boxing wizards jump quickly.");

    return doc;
}


static class PageShotScenarioSelection
{
    private static readonly HashSet<string> ScenarioIds = new(StringComparer.OrdinalIgnoreCase);

    public static void Add(string scenarioId) => ScenarioIds.Add(scenarioId);

    public static bool Includes(string scenarioId) =>
        ScenarioIds.Count == 0 || ScenarioIds.Contains(scenarioId);
}

static class PageShotFixtureSource
{
    private static string? FixtureDirectory;

    private static readonly IReadOnlyDictionary<string, string> ScenarioFixtureAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["page-composition-columns"] = "f2-columns",
            ["page-composition-border-watermark"] = "f2-border-watermark",
            ["page-composition-floating-image"] = "f2-01-float-wrap"
        };

    public static void Configure(string directory)
    {
        FixtureDirectory = Path.GetFullPath(directory);
    }

    public static TextDocument Resolve(string scenarioId, Func<TextDocument> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (string.IsNullOrWhiteSpace(FixtureDirectory))
            return fallback();

        var fixtureId = ScenarioFixtureAliases.TryGetValue(scenarioId, out var alias)
            ? alias
            : scenarioId;
        var path = Path.Combine(FixtureDirectory, fixtureId + ".docx");
        return File.Exists(path) ? DocxReader.Read(path) : fallback();
    }
}

/// <summary>Minimal Avalonia app used by the page-layout shot tool (no UI shown).</summary>
public sealed class PageShotApp : Application
{
    public override void Initialize() { }
}
