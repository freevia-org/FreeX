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
        source.Should().Contain("Render-WordBaseline.ps1");
        source.Should().Contain("Resolve-RepositoryPath $WordBaselineDir");
        source.Should().Contain("Join-Path $wordBaselineRenderRoot 'word'");
        source.Should().Contain("Test-Path -LiteralPath $wordBaselineRoot -PathType Container");
        source.Should().Contain("Invoke-PowerShellStep 'Render MS Word baseline PNGs'");
        source.Should().Contain("'--manifest', $wpfManifest");
        source.Should().Contain("'--manifest', $avaloniaManifest");
        source.Should().Contain("'--word-baseline-dir', $wordBaselineRoot");
        source.Should().Contain("'--baseline-tolerance', $BaselineTolerance");
        source.Should().Contain("Invoke-DotNetStep 'Validate and normalize combined visual evidence' $summaryArgs");
        source.Should().Contain("Assert-BackstageEvidenceReadiness $summaryJson");
        source.Should().Contain("backstage-print-preview-fidelity");
        source.Should().Contain("backstage-pdf-export-fidelity");
        source.Should().Contain("wpf-fidelity-render");
        source.Should().Contain("avalonia-page-layout-shot");
        source.Should().Contain("[int]$summary.schemaVersion -ne 19");
        source.Should().Contain("Backstage evidence readiness requires FreeW visual evidence summary schema v19");
        source.Should().Contain("$evidenceRows = @($summary.evidence)");
        source.Should().Contain("$requiredWorkflowByScenario");
        source.Should().Contain("'backstage-print-preview-fidelity' = 'print-preview'");
        source.Should().Contain("'backstage-pdf-export-fidelity' = 'pdf-export'");
        source.Should().Contain("missing normalized evidence row for backstage workflow metadata");
        source.Should().Contain("backstageWorkflow '$workflow' expected '$expectedWorkflow'");
        source.Should().Contain("Backstage evidence readiness failed");
        source.Should().Contain("Backstage evidence readiness: trusted required rows=");
        source.Should().Contain("Backstage workflow metadata: verified rows=");
        source.Should().Contain("Word baseline mode: word-png-comparison");
        source.Should().Contain("Word baseline mode: visual-evidence-only");
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
