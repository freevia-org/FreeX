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
        source.Should().Contain("'field-page-number-variants'");
        source.Should().Contain("'references-heavy-fields'");
        source.Should().Contain("'equation-structures'");
        source.Should().Contain("'review-protection-proofing-comments-only'");
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
        source.Should().Contain("value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)");
        source.Should().Contain("includedScenarioIds: options.IncludeScenarioIds");
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
