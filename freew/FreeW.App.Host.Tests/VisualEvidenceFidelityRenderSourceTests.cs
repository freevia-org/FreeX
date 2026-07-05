using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class VisualEvidenceFidelityRenderSourceTests
{
    [Fact]
    public void FidelityRender_EmitsSharedVisualEvidenceManifestAndTrustChecks()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "Program.cs"));
        var project = File.ReadAllText(RepositoryFile("freew", "tools", "FreeW.FidelityRender", "FreeW.FidelityRender.csproj"));

        source.Should().Contain("FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence)");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildEvidenceRow(");
        source.Should().Contain("FreeWVisualEvidencePlanner.EnsureTrusted(row)");
        source.Should().Contain("ComputeWpfPixelStats(");
        source.Should().Contain("FreeWVisualEvidencePlanner.ResolveSectionOrdinal");
        source.Should().Contain("sectionRelativePageNumber");
        source.Should().Contain("f2-footnotes.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument");
        source.Should().Contain("f2-endnotes.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument");
        source.Should().Contain("f2-columns.docx");
        source.Should().Contain("f2-border-watermark.docx");
        source.Should().Contain("f2-section-landscape.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument");
        source.Should().Contain("f2-tracked-changes.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument");
        source.Should().Contain("f2-comments.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument");
        source.Should().Contain("review-proofing-visual-depth.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument");
        source.Should().Contain("field-page-number-variants.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument");
        source.Should().Contain("references-heavy-fields.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument");
        source.Should().Contain("table-layout-complex.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument");
        source.Should().Contain("table-pagination-repeat-header.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument");
        source.Should().Contain("drawing-objects-complex.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument");
        source.Should().Contain("object-format-position-size-style.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument");
        source.Should().Contain("chart-smartart-complex.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument");
        source.Should().Contain("wordart-watermark-stress.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument");
        source.Should().Contain("wordart-picture-watermark-layout.docx");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument");
        source.Should().Contain("RenderPictureWatermark(");
        source.Should().Contain("WatermarkVisualPlanner.BuildPictureLayout(");
        source.Should().Contain("backstage-print-preview-fidelity.docx");
        source.Should().Contain("backstage-pdf-export-fidelity.docx");
        source.Should().Contain("BuildVisualEvidenceOutputPath(outDir, name, i + 1)");
        source.Should().Contain("FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, pageNumber)");
        source.Should().Contain("hostId: \"wpf-fidelity-render\"");
        source.Should().Contain("[\"captureSource\"] = \"wpf-composite-renderer\"");
        source.Should().Contain("\"--software-fallback\"");
        source.Should().Contain("RenderDocumentSoftwareFallback(");
        source.Should().Contain("Software evidence renderer requested by --software-fallback");
        source.Should().Contain("renderPath: \"software-fallback\"");
        source.Should().Contain("captureSource: \"software-renderer\"");
        source.Should().Contain("[\"wpfRenderTargetBitmapReason\"] = wpfRenderTargetFailure");
        project.Should().Contain("FreeW.App.Presentation");
        project.Should().Contain("PackageReference Include=\"SkiaSharp\"");
    }

    [Fact]
    public void WpfDocumentView_RendersPictureWatermarkThroughSharedPlanner()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        source.Should().Contain("BuildPictureWatermarkBrush(");
        source.Should().Contain("WatermarkVisualPlanner.BuildPictureLayout(");
        source.Should().Contain("if (options.IsPicture)");
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
