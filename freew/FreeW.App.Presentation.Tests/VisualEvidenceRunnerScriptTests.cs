using System.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidenceRunnerScriptTests
{
    [Fact]
    public void WordBaselineEvidenceRunner_UsesTrustedWpfCompositeUnlessFallbackIsRequested()
    {
        var source = File.ReadAllText(RepositoryFile(
            "tools",
            "Run-FreeWWordBaselineEvidence.ps1"));

        source.Should().Contain("FreeW.FidelityRender");
        source.Should().Contain("[int]$MaxPagesPerDocument = 4");
        source.Should().Contain("[switch]$UseSoftwareFallback");
        source.Should().Contain("$wpfRenderArgs = @(");
        source.Should().Contain("if ($UseSoftwareFallback)");
        source.Should().Contain("$wpfRenderArgs += \"--software-fallback\"");
        source.Should().Contain("Invoke-DotNetRunNoBuild $fidelityRenderProject $wpfRenderArgs");
        source.Should().Contain("$avaloniaRenderArgs = @($avaloniaDir, \"--fixtures-dir\", $fixtureDir)");
        source.Should().Contain("Invoke-DotNetRun $pageLayoutShotProject $avaloniaRenderArgs");
        source.Should().Contain("-AllowMissingWord");
        source.Should().Contain("[switch]$UseVisibleWordPublish");
        source.Should().Contain("Export-WordPdfsVisible.ps1");
        source.Should().Contain("--word-baseline-unavailable-reason");
        source.Should().Contain("--allow-no-word-fallback-evidence");
        source.Should().Contain("evidenceMode = \"no-word-fallback\"");
        source.Should().Contain("baselineEvidenceClass = \"word-baseline-unavailable\"");
        source.Should().Contain("authoritativeWordPngParity = $false");
        source.Should().Contain("_word_baseline_readiness_manifest.json");
        source.Should().Contain("schema = \"freew.word-baseline-readiness.v1\"");
        source.Should().Contain("candidateBaselinePaths = $candidateBaselinePaths");
        source.Should().Contain("remainingWordBaselineBlockerIds = $blockerIds");
        source.Should().Contain("no-Word run must not claim authoritative Word PNG parity");
        source.Should().Contain("no-Word run must record candidate Word baseline PNG paths");
        source.Should().Contain("Word baseline readiness manifest:");
        source.Should().Contain("FreeW.VisualEvidenceSummary");
    }

    [Fact]
    public void VisibleWordPdfExporter_DrivesPublishDialogWithoutOwningExistingWordSession()
    {
        var source = File.ReadAllText(RepositoryFile(
            "tools",
            "FreeW.RenderCompare",
            "Export-WordPdfsVisible.ps1"));

        source.Should().Contain("[Runtime.InteropServices.Marshal]::GetActiveObject($ProgId)");
        source.Should().Contain("New-Object -ComObject $ProgId");
        source.Should().Contain("$createdWord");
        source.Should().Contain("if ($createdWord -and $word)");
        source.Should().Contain("FileSaveAsPdfOrXps");
        source.Should().Contain("Publish as PDF or XPS");
        source.Should().Contain("word-export-visible-ui.csv");
        source.Should().Contain("Visible Word PDF exports complete");
    }

    [Fact]
    public void PdfRasterizer_PreservesNativeWordPagePixelGeometry()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew",
            "tools",
            "FreeW.PdfRasterize",
            "Program.cs"));

        source.Should().Contain("Windows.Data.Pdf reports each page in its native 96-DPI geometry");
        source.Should().Contain("MaximumEvidenceWidth = 816.0");
        source.Should().Contain("MaximumEvidenceHeight = 1056.0");
        source.Should().Contain("DestinationWidth = outputWidth");
        source.Should().Contain("DestinationHeight = outputHeight");
    }

    [Fact]
    public void WordBaselineEvidenceRunner_RasterizesEveryPdfPageAtItsOwnGeometry()
    {
        var source = File.ReadAllText(RepositoryFile(
            "tools",
            "Run-FreeWWordBaselineEvidence.ps1"));

        source.Should().Contain("$rasterArgs = @($pdf.FullName, $wordBaselineDir)");
        source.Should().NotContain("Find-EvidencePage");
        source.Should().NotContain("Get-PngDimensions");
    }

    [Fact]
    public void CombinedVisualEvidenceRunner_ForwardsOptionalWordBaselineComparison()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew-fidelity-corpus",
            "tools",
            "Run-FreeWVisualEvidence.ps1"));

        source.Should().Contain("[string]$WordBaselineDir");
        source.Should().Contain("[switch]$IncludeWordBaseline");
        source.Should().Contain("[string]$BaselineTolerance = 'word-png-default'");
        source.Should().Contain("[string]$WordBaselineUnavailableReason");
        source.Should().Contain("[string]$ScenarioSet");
        source.Should().Contain("[string[]]$ScenarioId");
        source.Should().Contain("BackstagePrintExport = @(");
        source.Should().Contain("CoreLayoutProof = @(");
        source.Should().Contain("ReferencesHeavyWordBaselineProof = @(");
        source.Should().Contain("LegalReferenceSectionPageProof = @(");
        source.Should().Contain("PageCompositionProof = @(");
        source.Should().Contain("FloatingWrappingVisualProof = @(");
        source.Should().Contain("HeaderFooterImageVisualProof = @(");
        source.Should().Contain("TableLayoutProof = @(");
        source.Should().Contain("TablePaginationPageCompositionProof = @(");
        source.Should().Contain("DrawingObjectVisualProof = @(");
        source.Should().Contain("ShapeObjectVisualProof = @(");
        source.Should().Contain("ObjectFormatVisualProof = @(");
        source.Should().Contain("SmartArtPolygonVisualProof = @(");
        source.Should().Contain("ChartVisualProof = @(");
        source.Should().Contain("WordArtWatermarkVisualProof = @(");
        source.Should().Contain("ReviewMarkupVisualProof = @(");
        source.Should().Contain("ReviewCompareCombineVisualProof = @(");
        source.Should().Contain("ReviewProofingVisualProof = @(");
        source.Should().Contain("EquationStructureVisualProof = @(");
        source.Should().Contain("NotePlacementVisualProof = @(");
        source.Should().Contain("SectionGeometryVisualProof = @(");
        source.Should().Contain("'field-page-number-variants'");
        source.Should().Contain("'references-heavy-fields'");
        source.Should().Contain("'legal-reference-section-page-numbers'");
        source.Should().Contain("'equation-structures'");
        source.Should().Contain("'review-protection-proofing-comments-only'");
        source.Should().Contain("'page-composition-columns'");
        source.Should().Contain("'page-composition-border-watermark'");
        source.Should().Contain("'f2-01-float-wrap'");
        source.Should().Contain("'page-composition-floating-image'");
        source.Should().Contain("freew-fidelity-corpus/runs/floating-wrapping-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/header-footer-image-proof");
        source.Should().Contain("'f2-hf-images'");
        source.Should().Contain("$f2ObjectsProject = Join-Path $repoRoot 'freew\\tools\\_corpus_f2_objects\\_corpus_f2_objects.csproj'");
        source.Should().Contain("Generate floating/wrapping DOCX fixtures");
        source.Should().Contain("$effectiveScenarioIds -contains 'f2-01-float-wrap'");
        source.Should().Contain("'table-layout-complex'");
        source.Should().Contain("'table-pagination-repeat-header'");
        source.Should().Contain("'table-page-composition-stress'");
        source.Should().Contain("freew-fidelity-corpus/runs/table-pagination-page-composition-proof");
        source.Should().Contain("'drawing-objects-complex'");
        source.Should().Contain("'object-format-position-size-style'");
        source.Should().Contain("'chart-smartart-complex'");
        source.Should().Contain("'wordart-watermark-stress'");
        source.Should().Contain("'wordart-picture-watermark-layout'");
        source.Should().Contain("freew-fidelity-corpus/runs/shape-object-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/object-format-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/smartart-polygon-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/chart-visual-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/wordart-watermark-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/review-markup-proof");
        source.Should().Contain("'f2-tracked-changes'");
        source.Should().Contain("'f2-comments'");
        source.Should().Contain("'review-compare-visual-proof'");
        source.Should().Contain("'review-combine-visual-proof'");
        source.Should().Contain("'review-proofing-visual-depth'");
        source.Should().Contain("Unknown ScenarioSet '$ScenarioSet'");
        source.Should().Contain("$effectiveScenarioIds = @($effectiveScenarioIds | Select-Object -Unique)");
        source.Should().Contain("Render-WordBaseline.ps1");
        source.Should().Contain("Resolve-ToolRepoPath -Path $WordBaselineDir -RepoRoot $repoRoot");
        source.Should().Contain("Join-Path $wordBaselineRenderRoot 'word'");
        source.Should().Contain("Test-Path -LiteralPath $wordBaselineRoot -PathType Container");
        source.Should().Contain("-WordBaselineUnavailableReason cannot be combined with -WordBaselineDir or -IncludeWordBaseline.");
        source.Should().Contain("Invoke-PowerShellStep 'Render MS Word baseline PNGs'");
        source.Should().Contain("$wordBaselineArgs = @(");
        source.Should().Contain("if ($effectiveScenarioIds.Count -gt 0)");
        source.Should().Contain("$wordBaselineDocs = @($effectiveScenarioIds | ForEach-Object { \"$_.docx\" })");
        source.Should().Contain("$wordBaselineArgs += '-Docs'");
        source.Should().Contain("$wordBaselineArgs += ($wordBaselineDocs -join ',')");
        source.Should().Contain("Selected Word baseline fixture(s) are missing");
        source.Should().Contain("$selectedWpfFixtureDir = Join-Path $runRoot 'wpf-fixtures'");
        source.Should().Contain("Copy-Item -LiteralPath (Join-Path $fixtureDir \"$scenarioId.docx\") -Destination $selectedWpfFixtureDir -Force");
        source.Should().Contain("$avaloniaRenderArgs += @('--scenario', $scenarioId)");
        source.Should().Contain("'--fixtures-dir', $fixtureDir");
        source.Should().Contain("'--manifest', $wpfManifest");
        source.Should().Contain("'--manifest', $avaloniaManifest");
        source.Should().Contain("'--word-baseline-dir', $wordBaselineRoot");
        source.Should().Contain("'--word-baseline-unavailable-reason', $WordBaselineUnavailableReason");
        source.Should().Contain("'--include-scenario', $scenario");
        source.Should().Contain("'--baseline-tolerance', $BaselineTolerance");
        source.Should().Contain("$allowNoWordFallbackEvidence = -not $wordBaselineRoot");
        source.Should().Contain("$summaryArgs += '--allow-no-word-fallback-evidence'");
        source.Should().Contain("Invoke-DotNetStep 'Validate and normalize combined visual evidence' $summaryArgs");
        source.Should().Contain("if (Test-ScenarioFilterIncludesBackstage $effectiveScenarioIds)");
        source.Should().Contain("Assert-BackstageEvidenceReadiness $summaryJson $allowNoWordFallbackEvidence");
        source.Should().Contain("Backstage evidence readiness: skipped by scenario filter");
        source.Should().Contain("Assert-CoreLayoutProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("backstage-print-preview-fidelity");
        source.Should().Contain("backstage-pdf-export-fidelity");
        source.Should().Contain("wpf-fidelity-render");
        source.Should().Contain("avalonia-page-layout-shot");
        source.Should().Contain("[int]$summary.schemaVersion -lt 24");
        source.Should().Contain("Backstage evidence readiness requires FreeW visual evidence summary schema v24 or newer");
        source.Should().Contain("$evidenceRows = @($summary.evidence)");
        source.Should().Contain("$requiredWorkflowByScenario");
        source.Should().Contain("$requiredArtifactKindByScenario");
        source.Should().Contain("$requiredPipelineByScenario");
        source.Should().Contain("$requiredRouteByScenario");
        source.Should().Contain("'backstage-print-preview-fidelity' = 'print-preview'");
        source.Should().Contain("'backstage-pdf-export-fidelity' = 'pdf-export'");
        source.Should().Contain("'backstage-print-preview-fidelity' = 'print-preview-fixed-layout'");
        source.Should().Contain("'backstage-pdf-export-fidelity' = 'pdf-export-rasterized'");
        source.Should().Contain("'backstage-print-preview-fidelity' = 'print-preview-fixed-layout-artifact'");
        source.Should().Contain("'backstage-pdf-export-fidelity' = 'pdf-export-rasterized-artifact'");
        source.Should().Contain("'backstage-print-preview-fidelity' = 'backstage-print-preview-fixed-layout-capture'");
        source.Should().Contain("'backstage-pdf-export-fidelity' = 'backstage-pdf-export-raster-capture'");
        source.Should().Contain("'wpf-fidelity-render' = 'wpf-composite-renderer'");
        source.Should().Contain("'avalonia-page-layout-shot' = 'avalonia-render-target'");
        source.Should().Contain("fallback hygiene expected wpfRenderTargetBitmap 'unavailable'");
        source.Should().Contain("missing trusted normalized evidence row for backstage artifact metadata");
        source.Should().Contain("captureSource '$captureSource' expected '$expectedCaptureSource'");
        source.Should().Contain("backstageWorkflow '$workflow' expected '$expectedWorkflow'");
        source.Should().Contain("backstageArtifactKind '$artifactKind' expected '$expectedArtifactKind'");
        source.Should().Contain("backstagePipeline '$pipeline' expected '$expectedPipeline'");
        source.Should().Contain("backstageCaptureRoute '$route' expected '$expectedRoute'");
        source.Should().Contain("Backstage evidence readiness failed");
        source.Should().Contain("Backstage evidence readiness: trusted real rows=");
        source.Should().Contain("Backstage runner evidence hygiene: WPF software fallback rows=");
        source.Should().Contain("Backstage artifact metadata: verified rows=");
        source.Should().Contain("Backstage capture routes: verified rows=");
        source.Should().Contain("Core layout proof readiness: trusted scenario rows=");
        source.Should().Contain("Assert-ReferencesHeavyWordBaselineProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("References-heavy Word baseline proof readiness requires FreeW visual evidence summary schema v46 or newer");
        source.Should().Contain("$readinessRows = @($summary.referencesHeavyProofReadiness)");
        source.Should().Contain("missing references-heavy field/TOA proof readiness row");
        source.Should().Contain("missing references-heavy semantic readiness summary");
        source.Should().Contain("missing cached bibliography result signature");
        source.Should().Contain("missing cached TOA page-reference sentinel");
        source.Should().Contain("missing generated TOA page-number evidence");
        source.Should().Contain("missing honest word-baseline-unavailable TOA page-number blocker");
        source.Should().Contain("References-heavy field/TOA proof readiness rows: verified rows=");
        source.Should().Contain("References-heavy Word baseline proof readiness: trusted scenario rows=");
        source.Should().Contain("References-heavy semantic field/TOA rows: verified rows=");
        source.Should().Contain("References-heavy Word-baseline policy rows: verified rows=");
        source.Should().Contain("References-heavy Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-LegalReferenceSectionPageProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Legal-reference section page-number proof readiness requires FreeW visual evidence summary schema v39 or newer");
        source.Should().Contain("$readinessRows = @($summary.legalReferenceProofReadiness)");
        source.Should().Contain("missing legal-reference section page-number proof readiness row");
        source.Should().Contain("missing generated TOA page-reference metadata");
        source.Should().Contain("missing honest word-baseline-unavailable legal-reference blocker");
        source.Should().Contain("legal-reference-section-page-number-fidelity");
        source.Should().Contain("Legal-reference section page-number proof readiness: trusted scenario rows=");
        source.Should().Contain("Legal-reference section page-number semantic rows: verified rows=");
        source.Should().Contain("Legal-reference section page-number Word-baseline policy rows: verified rows=");
        source.Should().Contain("Legal-reference section page-number Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-PageCompositionProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Page composition proof readiness requires FreeW visual evidence summary schema v25 or newer");
        source.Should().Contain("'avalonia-page-layout-shot'");
        source.Should().Contain("BaselineScenarioId = 'f2-columns'");
        source.Should().Contain("BaselineScenarioId = 'f2-border-watermark'");
        source.Should().Contain("missing Word-baseline policy row");
        source.Should().Contain("Page composition proof readiness: trusted scenario rows=");
        source.Should().Contain("Page composition Word-baseline policy rows: verified rows=");
        source.Should().Contain("Assert-FloatingWrappingProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Floating/wrapping proof readiness requires FreeW visual evidence summary schema v29 or newer");
        source.Should().Contain("$readinessRows = @($summary.floatingWrappingProofReadiness)");
        source.Should().Contain("missing WPF/Avalonia floating wrap semantic evidence");
        source.Should().Contain("Floating/wrapping proof readiness: trusted paired row=1");
        source.Should().Contain("Floating/wrapping Word-baseline policy rows: verified rows=");
        source.Should().Contain("Assert-HeaderFooterImageVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Header/footer image visual proof readiness requires FreeW visual evidence summary schema v43 or newer");
        source.Should().Contain("$readinessRows = @($summary.headerFooterImageProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })");
        source.Should().Contain("missing header/footer image semantic readiness summary");
        source.Should().Contain("missing honest word-baseline-unavailable header/footer image blocker");
        source.Should().Contain("f2-hf-images-word-baseline-fidelity");
        source.Should().Contain("Header/footer image visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Header/footer image Word-baseline policy rows: verified rows=");
        source.Should().Contain("Assert-TableLayoutProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Table layout proof readiness requires FreeW visual evidence summary schema v25 or newer");
        source.Should().Contain("'table-layout-complex' = 1");
        source.Should().Contain("'table-pagination-repeat-header' = 2");
        source.Should().Contain("'table-page-composition-stress' = 3");
        source.Should().Contain("Table layout proof readiness: trusted scenario rows=");
        source.Should().Contain("Table layout Word-baseline policy rows: verified rows=");
        source.Should().Contain("Assert-TablePaginationPageCompositionProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Table pagination/page composition proof readiness requires FreeW visual evidence summary schema v49 or newer");
        source.Should().Contain("$readinessRows = @($summary.tablePaginationProofReadiness)");
        source.Should().Contain("missing repeated-header pagination semantic evidence");
        source.Should().Contain("missing deterministic table semantic evidence fingerprints");
        source.Should().Contain("missing honest word-baseline-unavailable table pagination blocker");
        source.Should().Contain("Table pagination/page composition proof readiness: trusted scenario rows=");
        source.Should().Contain("Table pagination/page composition semantic rows: verified rows=");
        source.Should().Contain("Table pagination/page composition Word-baseline policy rows: verified rows=");
        source.Should().Contain("Assert-DrawingObjectVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Drawing object visual proof readiness requires FreeW visual evidence summary schema v28 or newer");
        source.Should().Contain("$readinessRows = @($summary.drawingObjectProofReadiness)");
        source.Should().Contain("missing drawing/object proof readiness row");
        source.Should().Contain("missing drawing/object semantic readiness summary");
        source.Should().Contain("drawing-object renderer pair '$scenarioId'");
        source.Should().Contain("chart/SmartArt renderer pair '$scenarioId'");
        source.Should().Contain("WordArt watermark renderer pair '$scenarioId'");
        source.Should().Contain("missing grouped drawing/chart/SmartArt/WordArt semantic evidence");
        source.Should().Contain("missing object-format alt text, effects, or z-order semantic evidence");
        source.Should().Contain("missing object-format position/size/style object semantic evidence");
        source.Should().Contain("missing object-format shared effect semantic evidence");
        source.Should().Contain("missing object-format effect summary '$requiredEffect'");
        source.Should().Contain("missing chart/SmartArt semantic visual signatures");
        source.Should().Contain("missing chart data signatures");
        source.Should().Contain("missing WordArt, watermark, page-border, or effect semantic evidence");
        source.Should().Contain("missing WordArt picture-watermark layout semantic evidence");
        source.Should().Contain("Drawing object visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Drawing object visual semantic rows: verified rows=");
        source.Should().Contain("Drawing object Word-baseline policy rows: verified rows=");
        source.Should().Contain("Drawing object Word-baseline policy rows: no Word baseline mode requested");
        source.Should().Contain("Assert-WordArtWatermarkVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("WordArt/watermark visual proof readiness requires FreeW visual evidence summary schema v47 or newer");
        source.Should().Contain("$readinessRows = @($summary.wordArtWatermarkProofReadiness)");
        source.Should().Contain("missing WordArt/watermark proof readiness row");
        source.Should().Contain("missing WordArt/watermark semantic readiness summary");
        source.Should().Contain("semantic readiness does not report picture watermark and WordArt evidence");
        source.Should().Contain("missing honest word-baseline-unavailable WordArt/watermark blocker");
        source.Should().Contain("WordArt/watermark visual proof readiness: trusted scenario rows=");
        source.Should().Contain("WordArt/watermark visual semantic rows: verified rows=");
        source.Should().Contain("WordArt/watermark Word-baseline unavailable blockers: verified rows=");
        source.Should().Contain("Assert-SmartArtPolygonVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("SmartArt polygon visual proof readiness requires FreeW visual evidence summary schema v38 or newer");
        source.Should().Contain("missing Basic Pyramid polygon geometry signature");
        source.Should().Contain("missing honest word-baseline-unavailable SmartArt polygon blocker");
        source.Should().Contain("chart-smartart-complex-word-baseline-fidelity");
        source.Should().Contain("SmartArt polygon visual proof readiness: trusted semantic rows=");
        source.Should().Contain("SmartArt polygon Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-ChartVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Chart visual proof readiness requires FreeW visual evidence summary schema v48 or newer");
        source.Should().Contain("missing styled column chart signature");
        source.Should().Contain("missing marker scatter chart signature");
        source.Should().Contain("missing column chart data signature");
        source.Should().Contain("missing scatter chart data signature");
        source.Should().Contain("missing honest word-baseline-unavailable chart visual blocker");
        source.Should().Contain("Chart visual proof readiness: trusted semantic rows=");
        source.Should().Contain("Chart visual Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-ReviewMarkupVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Review markup visual proof readiness requires FreeW visual evidence summary schema v42 or newer");
        source.Should().Contain("$readinessRows = @($summary.reviewMarkupProofReadiness)");
        source.Should().Contain("missing review markup semantic readiness summary");
        source.Should().Contain("missing tracked-change authorship semantic evidence");
        source.Should().Contain("missing comment anchor/reference semantic evidence");
        source.Should().Contain("missing honest word-baseline-unavailable review markup blocker");
        source.Should().Contain("Review markup visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Review markup visual semantic rows: verified rows=");
        source.Should().Contain("Review markup Word-baseline policy rows: verified rows=");
        source.Should().Contain("Review markup Word-baseline unavailable blockers: verified rows=");
        source.Should().Contain("Assert-ReviewCompareCombineVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Review compare/combine visual proof readiness requires FreeW visual evidence summary schema v41 or newer");
        source.Should().Contain("$remainingBlockers = @($summary.remainingEvidenceBlockers)");
        source.Should().Contain("$readinessRows = @($summary.reviewCompareCombineProofReadiness)");
        source.Should().Contain("missing review compare/combine proof readiness row");
        source.Should().Contain("missing compare/combine semantic readiness summary");
        source.Should().Contain("missing compare revision/authorship semantic evidence");
        source.Should().Contain("missing combine multi-author semantic evidence");
        source.Should().Contain("compare/combine stable signatures do not cover every revision");
        source.Should().Contain("missing honest word-baseline-unavailable review compare/combine blocker");
        source.Should().Contain("Review compare/combine visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Review compare/combine visual semantic rows: verified rows=");
        source.Should().Contain("Review compare/combine Word-baseline policy rows: verified rows=");
        source.Should().Contain("Review compare/combine Word-baseline policy rows: no Word baseline mode requested");
        source.Should().Contain("Review compare/combine Word-baseline unavailable blockers: verified rows=");
        source.Should().Contain("Assert-ReviewProofingVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Review proofing visual proof readiness requires FreeW visual evidence summary schema v34 or newer");
        source.Should().Contain("$readinessRows = @($summary.reviewProofingProofReadiness)");
        source.Should().Contain("missing proofing visual adornment metadata");
        source.Should().Contain("missing stable spelling-squiggle signature");
        source.Should().Contain("missing stable grammar-squiggle signature");
        source.Should().Contain("missing honest word-baseline-unavailable proofing visual blocker");
        source.Should().Contain("Review proofing visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Review proofing visual semantic rows: verified rows=");
        source.Should().Contain("Review proofing Word-baseline policy rows: verified rows=");
        source.Should().Contain("Review proofing Word-baseline unavailable blockers: verified rows=");
        source.Should().Contain("Assert-EquationStructureVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Equation structure visual proof readiness requires FreeW visual evidence summary schema v37 or newer");
        source.Should().Contain("missing modeled equation geometry depth");
        source.Should().Contain("missing equation spacing geometry signature token");
        source.Should().Contain("'geometry=function-apply'");
        source.Should().Contain("'spacing=equationarray'");
        source.Should().Contain("FunctionArgument = 2");
        source.Should().Contain("structureFamilies=");
        source.Should().Contain("roleFamilies=");
        source.Should().Contain("missing honest word-baseline-unavailable equation visual blocker");
        source.Should().Contain("equation-structures-word-baseline-fidelity");
        source.Should().Contain("Equation structure visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Equation structure visual semantic rows: verified rows=");
        source.Should().Contain("Equation structure Word-baseline policy rows: verified rows=");
        source.Should().Contain("Equation structure Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-NotePlacementVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Note placement visual proof readiness requires FreeW visual evidence summary schema v44 or newer");
        source.Should().Contain("$readinessRows = @($summary.notePlacementProofReadiness)");
        source.Should().Contain("missing final body-page endnote semantic evidence");
        source.Should().Contain("missing honest word-baseline-unavailable note placement blocker");
        source.Should().Contain("Note placement visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Note placement semantic rows: verified rows=");
        source.Should().Contain("Note placement Word-baseline policy rows: verified rows=");
        source.Should().Contain("Note placement Word-baseline unavailable blockers: verified rows=");
        source.Should().Contain("freew-fidelity-corpus/runs/section-geometry-proof");
        source.Should().Contain("'f2-section-landscape'");
        source.Should().Contain("Assert-SectionGeometryVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Section geometry visual proof readiness requires FreeW visual evidence summary schema v45 or newer");
        source.Should().Contain("$readinessRows = @($summary.sectionGeometryProofReadiness | Where-Object { $_.scenarioId -eq $scenarioId })");
        source.Should().Contain("missing section geometry semantic readiness summary");
        source.Should().Contain("missing honest word-baseline-unavailable section geometry blocker");
        source.Should().Contain("f2-section-landscape-word-baseline-fidelity");
        source.Should().Contain("Section geometry visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Section geometry semantic rows: verified rows=");
        source.Should().Contain("Section geometry Word-baseline policy rows: verified rows=");
        source.Should().Contain("Section geometry Word-baseline unavailable blocker: verified");
        source.Should().Contain("Word baseline mode: word-png-comparison");
        source.Should().Contain("Word baseline mode: word-baseline-unavailable");
        source.Should().Contain("Word baseline mode: visual-evidence-only");
        source.Should().Contain("Scenario set:");
        source.Should().Contain("Scenario filter:");
    }

    [Fact]
    public void LegacyFloatingObjectCorpus_DoesNotOverwriteTheCanonicalWordBaselineFixture()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew",
            "tools",
            "_corpus_f2_objects",
            "Program.cs"));

        source.Should().Contain("f2-objects-01-float-wrap.docx");
        source.Should().NotContain("Path.Combine(outDir, \"f2-01-float-wrap.docx\")");
    }

    [Fact]
    public void VisualEvidenceSummaryTool_SupportsScenarioFilter()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew",
            "tools",
            "FreeW.VisualEvidenceSummary",
            "Program.cs"));

        source.Should().Contain("--include-scenario");
        source.Should().Contain("--scenario");
        source.Should().Contain("AddScenarioIds(options, ReadValue(args, ref i, arg));");
        source.Should().Contain("--allow-no-word-fallback-evidence");
        source.Should().Contain("allowNoWordFallbackEvidence: options.AllowNoWordFallbackEvidence");
        source.Should().Contain("value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)");
        source.Should().Contain("includedScenarioIds: options.IncludeScenarioIds");
        source.Should().Contain("public bool AllowNoWordFallbackEvidence { get; set; }");
        source.Should().Contain("public List<string> IncludeScenarioIds { get; } = [];");
    }

    [Fact]
    public void WordBaselineRenderer_ReportsMissingWordComBeforeAutomation()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew-fidelity-corpus",
            "tools",
            "Render-WordBaseline.ps1"));

        source.Should().Contain("[type]::GetTypeFromProgID('Word.Application', $false)");
        source.Should().Contain("Word COM is not available: COM ProgID 'Word.Application' is not registered");
        source.Should().Contain("Word baseline mode: real-word-png-render");
        source.Should().Contain("-Width and -Height must be supplied together when requesting fixed raster dimensions.");
        source.Should().Contain("ForEach-Object { $_ -split ',' }");
    }

    [Fact]
    public void WordPdfExporter_ProbesWordReadinessAndEmitsLifecycleStages()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew-fidelity-corpus",
            "tools",
            "Export-WordPdf.ps1"));

        source.Should().Contain("[ValidateRange(1, 120)][int]$ReadyTimeoutSeconds = 30");
        source.Should().Contain("function Wait-WordReady");
        source.Should().Contain("$Application.Documents.Count");
        source.Should().Contain("$Application.BackgroundSavingStatus");
        source.Should().Contain("$Application.BackgroundPrintingStatus");
        source.Should().Contain("Wait-WordReady $word $ReadyTimeoutSeconds");
        source.Should().Contain("function Write-WordPdfTrace");
        source.Should().Contain("Write-WordPdfTrace \"opening:");
        source.Should().Contain("Write-WordPdfTrace \"opened read-only; exporting:");
        source.Should().Contain("inputLength=$($InputPath.Length); outputLength=$($OutputPath.Length)");
        source.Should().Contain("Word returned from ExportAsFixedFormat without creating");
        source.Should().Contain("Word did not become ready within $TimeoutSeconds seconds.");

        var baselineSource = File.ReadAllText(RepositoryFile(
            "freew-fidelity-corpus",
            "tools",
            "Render-WordBaseline.ps1"));
        baselineSource.Should().Contain("@('-TracePath', $TracePath)");
    }

    [Fact]
    public void PageBorderArtProbeGenerator_AuthorsCanonicalIsolatedPackage()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew-fidelity-corpus",
            "tools",
            "New-PageBorderArtProbe.ps1"));

        source.Should().Contain("[ValidatePattern('^[A-Za-z][A-Za-z0-9]*$')][string]$Token");
        source.Should().Contain("<w:pgBorders w:offsetFrom=\"page\">");
        source.Should().Contain("w:val=\"$escapedToken\" w:sz=\"$size\" w:space=\"$space\"");
        source.Should().Contain("[IO.Compression.ZipArchiveMode]::Create");
        source.Should().Contain("Get-FileHash -Algorithm SHA256");
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
