using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class VisualEvidencePageLayoutShotSourceTests
{
    [Fact]
    public void PageLayoutShot_EmitsSharedVisualEvidenceManifestAndTrustChecks()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.PageLayoutShot", "Program.cs"));
        var project = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.PageLayoutShot", "FreeW.PageLayoutShot.csproj"));

        source.Should().Contain("FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence)");
        source.Should().Contain("AddAvaloniaEvidence(");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildEvidenceRow(");
        source.Should().Contain("FreeWVisualEvidencePlanner.EnsureTrusted(row)");
        source.Should().Contain("ComputePngPixelStats(");
        source.Should().Contain("page-composition-print-layout");
        source.Should().Contain("page-composition-columns");
        source.Should().Contain("page-composition-border-watermark");
        source.Should().Contain("page-composition-floating-image");
        source.Should().Contain("f2-footnotes");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-footnotes\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-footnotes\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument");
        source.Should().Contain("hasFootnotes: true");
        source.Should().Contain("f2-endnotes");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-endnotes\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-endnotes\", 2)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-endnotes\", 3)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument");
        source.Should().Contain("hasEndnotes: true");
        source.Should().Contain("isSyntheticPage: true");
        source.Should().Contain("AddNoteRegionOverlayIfNeeded(");
        source.Should().Contain("BuildEvidenceNoteRegionPlan(");
        source.Should().Contain("DocumentNoteRegionPlanner.BuildFootnoteRegion");
        source.Should().Contain("DocumentNoteRegionPlanner.BuildEndnoteRegion");
        source.Should().Contain("[\"noteRegionRenderStatus\"] = \"shared-plan-overlay\"");
        source.Should().Contain("f2-section-landscape");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-section-landscape\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-section-landscape\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument");
        source.Should().Contain("sectionGeometryRenderStatus");
        source.Should().Contain("BuildSectionGeometrySurfacePlans");
        source.Should().Contain("avalonia-section-page-surface");
        source.Should().Contain("sectionSurfaceCaptureWidthDip");
        source.Should().Contain("sectionSurfaceCaptureHeightDip");
        source.Should().NotContain("avalonia-global-page-surface-no-section-page-break");
        source.Should().Contain("SectionGeometryRendererScenarioIds");
        source.Should().Contain("f2-tracked-changes");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-tracked-changes\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument");
        source.Should().Contain("f2-comments");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-comments\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument");
        source.Should().Contain("review-proofing-visual-depth");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"review-proofing-visual-depth\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument");
        source.Should().Contain("ReviewRendererScenarioIds");
        source.Should().Contain("field-page-number-variants");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 2)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 3)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument");
        source.Should().Contain("references-heavy-fields");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"references-heavy-fields\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"references-heavy-fields\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument");
        source.Should().Contain("ResolveAvaloniaHeaderSlotName(document, pageNumber)");
        source.Should().Contain("ResolveAvaloniaFooterSlotName(document, pageNumber)");
        source.Should().Contain("table-layout-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument");
        source.Should().Contain("table-pagination-repeat-header");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-pagination-repeat-header\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-pagination-repeat-header\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument");
        source.Should().Contain("table-page-composition-stress");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-page-composition-stress\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-page-composition-stress\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument");
        source.Should().Contain("drawing-objects-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument");
        source.Should().Contain("object-format-position-size-style");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"object-format-position-size-style\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument");
        source.Should().Contain("chart-smartart-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument");
        source.Should().Contain("wordart-watermark-stress");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument");
        source.Should().Contain("wordart-picture-watermark-layout");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument");
        source.Should().Contain("backstage-print-preview-fidelity");
        source.Should().Contain("backstage-pdf-export-fidelity");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"backstage-print-preview-fidelity\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"backstage-pdf-export-fidelity\", 2)");
        source.Should().Contain("viewportOffsetY: 1100");
        source.Should().Contain("pageNumber: pageNumber");
        source.Should().Contain("pageCount: pageCount");
        source.Should().Contain("refusing placeholder fallback for backstage renderer evidence");
        source.Should().Contain("metadata[\"backstageArtifactKind\"] = BackstageArtifactKindForScenario(scenarioId);");
        source.Should().Contain("metadata[\"backstagePipeline\"] = BackstagePipelineForScenario(scenarioId);");
        source.Should().Contain("\"print-preview-fixed-layout\"");
        source.Should().Contain("\"pdf-export-rasterized\"");
        source.Should().Contain("\"print-preview-fixed-layout-artifact\"");
        source.Should().Contain("\"pdf-export-rasterized-artifact\"");
        source.Should().Contain("refusing placeholder fallback for review renderer evidence");
        source.Should().Contain("freew_columns_layout.png");
        source.Should().Contain("freew_border_watermark.png");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildSectionOwnerId");
        source.Should().Contain("hostId: \"avalonia-page-layout-shot\"");
        project.Should().Contain("FreeW.App.Presentation");
    }

    [Fact]
    public void AvaloniaDocumentView_RendersPictureWatermarkThroughSharedPlanner()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("DrawPictureWatermark(context, pageRect, wm)");
        source.Should().Contain("WatermarkVisualPlanner.BuildPictureLayout(");
        source.Should().Contain("context.PushOpacity(plan.Opacity)");
        source.Should().NotContain("wm.IsPicture || string.IsNullOrWhiteSpace(wm.Text)");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
