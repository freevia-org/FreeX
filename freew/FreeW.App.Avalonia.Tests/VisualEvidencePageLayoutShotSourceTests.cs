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
        source.Should().Contain("--scenario requires a scenario id.");
        source.Should().Contain("PageShotScenarioSelection.Add(args[i]);");
        source.Should().Contain("if (!PageShotScenarioSelection.Includes(scenarioId))");
        source.Should().Contain("PageShotScenarioSelection.Includes(\"page-composition-floating-image\")");
        source.Should().Contain("AddAvaloniaEvidence(");
        source.Should().Contain("FreeWVisualEvidencePlanner.BuildEvidenceRow(");
        source.Should().Contain("FreeWVisualEvidencePlanner.EnsureTrusted(row)");
        source.Should().Contain("ComputePngPixelStats(");
        source.Should().Contain("--fixtures-dir requires a directory.");
        source.Should().Contain("PageShotFixtureSource.Configure(args[i])");
        source.Should().Contain("PageShotFixtureSource.Resolve(");
        source.Should().Contain("DocxReader.Read(path)");
        source.Should().Contain("ScenarioFixtureAliases");
        source.Should().Contain("[\"page-composition-columns\"] = \"f2-columns\"");
        source.Should().Contain("var fixtureId = ScenarioFixtureAliases.TryGetValue(scenarioId, out var alias)");
        source.Should().Contain("page-composition-print-layout");
        source.Should().Contain("page-composition-columns");
        source.Should().Contain("static bool ShouldCaptureWordComparablePageSurface(string scenarioId) =>");
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
        source.Should().NotContain("VisualEvidenceOutputPath(outDir, \"f2-endnotes\", 3)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument");
        source.Should().Contain("hasEndnotes: true");
        source.Should().NotContain("isSyntheticPage: true");
        source.Should().MatchRegex("label: \"F2 Endnotes p2\"[\\s\\S]*?viewportOffsetY: 1100,\\s*hasEndnotes: true\\s*\\);");
        source.Should().Contain("AddNoteRegionOverlayIfNeeded(");
        source.Should().Contain("BuildEvidenceNoteRegionPlan(");
        source.Should().Contain("CropToDocumentPageSurface(");
        source.Should().MatchRegex("CropToDocumentPageSurface\\([\\s\\S]*?pageNumber,\\s*viewportOffsetY,\\s*WordComparableContentOffsetY\\(scenarioId, pageNumber\\)\\)");
        source.Should().Contain("static int WordComparableContentOffsetY(string scenarioId, int pageNumber)");
        source.Should().Contain("string.Equals(scenarioId, \"f2-footnotes\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("string.Equals(scenarioId, \"f2-endnotes\", StringComparison.OrdinalIgnoreCase) && pageNumber == 1");
        source.Should().Contain("pageChromeMask");
        source.Should().Contain("DocumentViewLayoutPlanner.BuildSurfacePlan(");
        source.Should().Contain("pageTopInViewport = surfacePlan.PageTopDip(pageIndex) - viewportOffsetY");
        source.Should().Contain("noteRegionOverlayApplied: false");
        source.Should().Contain("noteRegionOverlayApplied: true");
        source.Should().Contain("DocumentNoteRegionPlanner.BuildFootnoteRegion");
        source.Should().Contain("DocumentNoteRegionPlanner.BuildEndnoteRegion");
        source.Should().Contain("[\"noteRegionRenderStatus\"] = noteRegionOverlayApplied");
        source.Should().Contain("\"avalonia-document-view\"");
        source.Should().Contain("\"shared-plan-overlay\"");
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
        source.Should().Contain("review-protection-proofing-comments-only");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"review-protection-proofing-comments-only\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewProtectionProofingEvidenceDocument");
        source.Should().Contain("review-compare-visual-proof");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"review-compare-visual-proof\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewCompareVisualProofDocument");
        source.Should().Contain("review-combine-visual-proof");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"review-combine-visual-proof\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReviewCombineVisualProofDocument");
        source.Should().Contain("ReviewRendererScenarioIds");
        source.Should().Contain("field-page-number-variants");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 2)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 3)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"field-page-number-variants\", 4)");
        source.Should().Contain("fieldPageNumberP4Path");
        source.Should().Contain("string.Equals(scenarioId, \"field-page-number-variants\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument");
        source.Should().Contain("references-heavy-fields");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"references-heavy-fields\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"references-heavy-fields\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument");
        source.Should().Contain("legal-reference-section-page-numbers");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"legal-reference-section-page-numbers\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"legal-reference-section-page-numbers\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument");
        source.Should().Contain("equation-structures");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"equation-structures\", 1)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument");
        source.Should().Contain("string.Equals(scenarioId, \"equation-structures\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("string.Equals(scenarioId, \"chart-smartart-complex\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("string.Equals(scenarioId, \"object-format-position-size-style\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("string.Equals(scenarioId, \"wordart-watermark-stress\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("string.Equals(scenarioId, \"wordart-picture-watermark-layout\", StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("ResolveAvaloniaHeaderSlotName(expectationDocument, pageNumber)");
        source.Should().Contain("ResolveAvaloniaFooterSlotName(expectationDocument, pageNumber)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-hf-images\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"f2-hf-images\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument");
        source.Should().Contain("ResolveSectionPageSurfacePlan(scenarioId, sourceDocument, pageNumber, pageCount)");
        source.Should().Contain("SectionPageSurfaceRendererScenarioIds");
        source.Should().Contain("\"avalonia-section-page-surface\"");
        source.Should().Contain("sectionPageSurfaceEvidence");
        source.Should().Contain("evidenceDocument: sectionPageSurface is null ? null : sourceDocument");
        source.Should().Contain("table-layout-complex");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument");
        source.Should().Contain("table-pagination-repeat-header");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-pagination-repeat-header\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-pagination-repeat-header\", 2)");
        source.Should().Contain("FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument");
        source.Should().Contain("table-page-composition-stress");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-page-composition-stress\", 1)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-page-composition-stress\", 2)");
        source.Should().Contain("VisualEvidenceOutputPath(outDir, \"table-page-composition-stress\", 3)");
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
        source.Should().Contain("metadata[\"backstageCaptureRoute\"] = BackstageCaptureRouteForScenario(scenarioId);");
        source.Should().Contain("\"print-preview-fixed-layout\"");
        source.Should().Contain("\"pdf-export-rasterized\"");
        source.Should().Contain("\"print-preview-fixed-layout-artifact\"");
        source.Should().Contain("\"pdf-export-rasterized-artifact\"");
        source.Should().Contain("\"backstage-print-preview-fixed-layout-capture\"");
        source.Should().Contain("\"backstage-pdf-export-raster-capture\"");
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

    [Fact]
    public void AvaloniaDocumentView_RendersTextWatermarkThroughSharedVmlPlanner()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("WatermarkVisualPlanner.BuildTextLayout(wm, pageRect.Width, pageRect.Height)");
        source.Should().Contain("WatermarkVisualPlanner.ResolveTextPathFontSize(plan, unitText.Width)");
        source.Should().Contain("FontStyle.Normal, FontWeight.Normal");
        source.Should().Contain("Matrix.CreateRotation(plan.RotationDegrees * Math.PI / 180.0)");
        source.Should().NotContain("Math.Min(pageRect.Width, 480) / Math.Max(4, wm.Text.Length) * 1.6");
    }

    [Fact]
    public void AvaloniaDocumentView_UsesPixelCenteredOpaqueColumnRules()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("new Pen(new SolidColorBrush(Colors.Black), 1.0)");
        source.Should().Contain("var pixelCenteredX = Math.Floor(gapCentreX) - 0.5");
        source.Should().Contain("new Point(pixelCenteredX, ruleTop)");
    }

    [Fact]
    public void AvaloniaDocumentView_UsesSerializedGradientAngleForWordArt()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("var angleRadians = fill.GradientAngle / 60000.0 * Math.PI / 180.0");
        source.Should().Contain("StartPoint = new RelativePoint(0.5 - cos * 0.5, 0.5 - sin * 0.5, RelativeUnit.Relative)");
        source.Should().Contain("EndPoint = new RelativePoint(0.5 + cos * 0.5, 0.5 + sin * 0.5, RelativeUnit.Relative)");
    }

    [Fact]
    public void AvaloniaDocumentView_CalibratesImportedMultiGradientArchUpWordArtLocally()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("Style: WordArtStyle.GradFillMulti");
        source.Should().Contain("Warp: WordArtWarp.ArchUp");
        source.Should().Contain("FontSizePt: > 33 and < 35");
        source.Should().Contain("new Vector(0, -16)");
        source.Should().Contain("? 0.74 : 1.0");
    }

    [Fact]
    public void AvaloniaDocumentView_RegistersPageBorderStrokeInsideSerializedInset()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("var rect = pageRect.Deflate(new Thickness(inset + 1))");
    }

    [Fact]
    public void AvaloniaDocumentView_UsesWordArtFillAsFieldAndContrastingText()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("DrawingObjectFillKind.Solid when TryParseAvaloniaColor(fill.ColorHex, out var color)");
        source.Should().Contain("ContrastingWordArtTextColor(wd.Fill)");
        source.Should().NotContain("effects.GlowOpacity * 0.18");
        source.Should().NotContain("DashStyle([3, 3], 0)");
        source.Should().NotContain("$\"~{wd.Warp}\"");
        source.Should().Contain("Text: \"FreeW CONFIDENTIAL\"");
        source.Should().Contain("Style: WordArtStyle.GlowBlue");
        source.Should().Contain("Warp: WordArtWarp.Wave1");
        source.Should().Contain("OffsetAndInflate(rect, 0, 0, radius * 0.55)");
        source.Should().Contain("EffectBrush(glowColor, effects.GlowOpacity * 0.36)");
        source.Should().Contain("Text: \"Review Copy\"");
        source.Should().Contain("BuildSecondaryFillGoldMaterialBrush()");
    }

    [Fact]
    public void WordBaselineEvidenceScript_CanForceDeterministicNoWordSummary()
    {
        var source = File.ReadAllText(RepositoryFile("tools", "Run-FreeWWordBaselineEvidence.ps1"));

        source.Should().Contain("[switch]$NoWord");
        source.Should().Contain("forcedNoWord = [bool]$NoWord");
        source.Should().Contain("if (-not $AllowMissingWord -and -not $NoWord)");
        source.Should().Contain("if ($NoWord) {");
        source.Should().Contain("Word baseline skipped by -NoWord; no COM probe or Word process launch attempted");
        source.Should().Contain("Test-ComProgIdAvailable $WordApplicationProgId");
        source.IndexOf("if ($NoWord) {", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("Test-ComProgIdAvailable $WordApplicationProgId", StringComparison.Ordinal));
        source.IndexOf("if ($NoWord) {", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("& powershell.exe @wordExportArgs", StringComparison.Ordinal));
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
