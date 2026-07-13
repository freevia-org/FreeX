using System.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidenceRunnerScriptTests
{
    [Fact]
    public void WordBaselineEvidenceRunner_UsesSoftwareFallbackForWpfEvidenceRender()
    {
        var source = File.ReadAllText(RepositoryFile(
            "tools",
            "Run-FreeWWordBaselineEvidence.ps1"));

        source.Should().Contain("FreeW.FidelityRender");
        source.Should().Contain("\"--composite\", \"--software-fallback\"");
        source.Should().Contain("-AllowMissingWord");
        source.Should().Contain("--word-baseline-unavailable-reason");
        source.Should().Contain("--allow-no-word-fallback-evidence");
        source.Should().Contain("evidenceMode = \"no-word-fallback\"");
        source.Should().Contain("baselineEvidenceClass = \"word-baseline-unavailable\"");
        source.Should().Contain("authoritativeWordPngParity = $false");
        source.Should().Contain("FreeW.VisualEvidenceSummary");
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
        source.Should().Contain("TableLayoutProof = @(");
        source.Should().Contain("TablePaginationPageCompositionProof = @(");
        source.Should().Contain("DrawingObjectVisualProof = @(");
        source.Should().Contain("ShapeObjectVisualProof = @(");
        source.Should().Contain("SmartArtPolygonVisualProof = @(");
        source.Should().Contain("ChartVisualProof = @(");
        source.Should().Contain("WordArtWatermarkVisualProof = @(");
        source.Should().Contain("ReviewCompareCombineVisualProof = @(");
        source.Should().Contain("ReviewProofingVisualProof = @(");
        source.Should().Contain("EquationStructureVisualProof = @(");
        source.Should().Contain("'field-page-number-variants'");
        source.Should().Contain("'references-heavy-fields'");
        source.Should().Contain("'legal-reference-section-page-numbers'");
        source.Should().Contain("'equation-structures'");
        source.Should().Contain("'review-protection-proofing-comments-only'");
        source.Should().Contain("'page-composition-columns'");
        source.Should().Contain("'page-composition-border-watermark'");
        source.Should().Contain("'f2-01-float-wrap'");
        source.Should().Contain("'page-composition-floating-image'");
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
        source.Should().Contain("freew-fidelity-corpus/runs/smartart-polygon-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/chart-visual-proof");
        source.Should().Contain("freew-fidelity-corpus/runs/wordart-watermark-proof");
        source.Should().Contain("'review-compare-visual-proof'");
        source.Should().Contain("'review-combine-visual-proof'");
        source.Should().Contain("'review-proofing-visual-depth'");
        source.Should().Contain("Unknown ScenarioSet '$ScenarioSet'");
        source.Should().Contain("$effectiveScenarioIds = @($effectiveScenarioIds | Select-Object -Unique)");
        source.Should().Contain("Render-WordBaseline.ps1");
        source.Should().Contain("Resolve-RepositoryPath $WordBaselineDir");
        source.Should().Contain("Join-Path $wordBaselineRenderRoot 'word'");
        source.Should().Contain("Test-Path -LiteralPath $wordBaselineRoot -PathType Container");
        source.Should().Contain("-WordBaselineUnavailableReason cannot be combined with -WordBaselineDir or -IncludeWordBaseline.");
        source.Should().Contain("Invoke-PowerShellStep 'Render MS Word baseline PNGs'");
        source.Should().Contain("'--manifest', $wpfManifest");
        source.Should().Contain("'--manifest', $avaloniaManifest");
        source.Should().Contain("'--word-baseline-dir', $wordBaselineRoot");
        source.Should().Contain("'--word-baseline-unavailable-reason', $WordBaselineUnavailableReason");
        source.Should().Contain("'--include-scenario', $scenario");
        source.Should().Contain("'--baseline-tolerance', $BaselineTolerance");
        source.Should().Contain("Invoke-DotNetStep 'Validate and normalize combined visual evidence' $summaryArgs");
        source.Should().Contain("if (Test-ScenarioFilterIncludesBackstage $effectiveScenarioIds)");
        source.Should().Contain("Assert-BackstageEvidenceReadiness $summaryJson");
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
        source.Should().Contain("missing trusted normalized evidence row for backstage artifact metadata");
        source.Should().Contain("backstageWorkflow '$workflow' expected '$expectedWorkflow'");
        source.Should().Contain("backstageArtifactKind '$artifactKind' expected '$expectedArtifactKind'");
        source.Should().Contain("backstagePipeline '$pipeline' expected '$expectedPipeline'");
        source.Should().Contain("backstageCaptureRoute '$route' expected '$expectedRoute'");
        source.Should().Contain("Backstage evidence readiness failed");
        source.Should().Contain("Backstage evidence readiness: trusted required rows=");
        source.Should().Contain("Backstage artifact metadata: verified rows=");
        source.Should().Contain("Backstage capture routes: verified rows=");
        source.Should().Contain("Core layout proof readiness: trusted scenario rows=");
        source.Should().Contain("Assert-ReferencesHeavyWordBaselineProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("References-heavy Word baseline proof readiness requires FreeW visual evidence summary schema v33 or newer");
        source.Should().Contain("missing cached bibliography result signature");
        source.Should().Contain("missing cached TOA page-reference sentinel");
        source.Should().Contain("missing generated TOA page-number evidence");
        source.Should().Contain("missing honest word-baseline-unavailable TOA page-number blocker");
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
        source.Should().Contain("Assert-TableLayoutProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Table layout proof readiness requires FreeW visual evidence summary schema v25 or newer");
        source.Should().Contain("'table-layout-complex' = 1");
        source.Should().Contain("'table-pagination-repeat-header' = 2");
        source.Should().Contain("'table-page-composition-stress' = 2");
        source.Should().Contain("Table layout proof readiness: trusted scenario rows=");
        source.Should().Contain("Table layout Word-baseline policy rows: verified rows=");
        source.Should().Contain("Assert-TablePaginationPageCompositionProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Table pagination/page composition proof readiness requires FreeW visual evidence summary schema v40 or newer");
        source.Should().Contain("$readinessRows = @($summary.tablePaginationProofReadiness)");
        source.Should().Contain("missing repeated-header pagination semantic evidence");
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
        source.Should().Contain("missing chart/SmartArt semantic visual signatures");
        source.Should().Contain("missing WordArt, watermark, page-border, or effect semantic evidence");
        source.Should().Contain("missing WordArt picture-watermark layout semantic evidence");
        source.Should().Contain("Drawing object visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Drawing object visual semantic rows: verified rows=");
        source.Should().Contain("Drawing object Word-baseline policy rows: verified rows=");
        source.Should().Contain("Drawing object Word-baseline policy rows: no Word baseline mode requested");
        source.Should().Contain("Assert-SmartArtPolygonVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("SmartArt polygon visual proof readiness requires FreeW visual evidence summary schema v38 or newer");
        source.Should().Contain("missing Basic Pyramid polygon geometry signature");
        source.Should().Contain("missing honest word-baseline-unavailable SmartArt polygon blocker");
        source.Should().Contain("chart-smartart-complex-word-baseline-fidelity");
        source.Should().Contain("SmartArt polygon visual proof readiness: trusted semantic rows=");
        source.Should().Contain("SmartArt polygon Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-ChartVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Chart visual proof readiness requires FreeW visual evidence summary schema v38 or newer");
        source.Should().Contain("missing styled column chart signature");
        source.Should().Contain("missing marker scatter chart signature");
        source.Should().Contain("missing honest word-baseline-unavailable chart visual blocker");
        source.Should().Contain("Chart visual proof readiness: trusted semantic rows=");
        source.Should().Contain("Chart visual Word-baseline unavailable blocker: verified");
        source.Should().Contain("Assert-ReviewCompareCombineVisualProofReadiness $summaryJson $effectiveScenarioIds");
        source.Should().Contain("Review compare/combine visual proof readiness requires FreeW visual evidence summary schema v30 or newer");
        source.Should().Contain("$readinessRows = @($summary.reviewCompareCombineProofReadiness)");
        source.Should().Contain("missing review compare/combine proof readiness row");
        source.Should().Contain("missing compare/combine semantic readiness summary");
        source.Should().Contain("missing compare revision/authorship semantic evidence");
        source.Should().Contain("missing combine multi-author semantic evidence");
        source.Should().Contain("compare/combine stable signatures do not cover every revision");
        source.Should().Contain("Review compare/combine visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Review compare/combine visual semantic rows: verified rows=");
        source.Should().Contain("Review compare/combine Word-baseline policy rows: verified rows=");
        source.Should().Contain("Review compare/combine Word-baseline policy rows: no Word baseline mode requested");
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
        source.Should().Contain("missing equation spacing geometry signatures");
        source.Should().Contain("missing honest word-baseline-unavailable equation visual blocker");
        source.Should().Contain("equation-structures-word-baseline-fidelity");
        source.Should().Contain("Equation structure visual proof readiness: trusted scenario rows=");
        source.Should().Contain("Equation structure visual semantic rows: verified rows=");
        source.Should().Contain("Equation structure Word-baseline policy rows: verified rows=");
        source.Should().Contain("Equation structure Word-baseline unavailable blocker: verified");
        source.Should().Contain("Word baseline mode: word-png-comparison");
        source.Should().Contain("Word baseline mode: word-baseline-unavailable");
        source.Should().Contain("Word baseline mode: visual-evidence-only");
        source.Should().Contain("Scenario set:");
        source.Should().Contain("Scenario filter:");
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
