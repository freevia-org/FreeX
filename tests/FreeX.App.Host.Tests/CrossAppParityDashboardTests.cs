using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CrossAppParityDashboardTests
{
    [Fact]
    public void CrossAppParityDashboard_DistinguishesCoverageFromVisualReview()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-CrossAppParityDashboard.ps1",
            repoRoot,
            "-Check");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.json")));
        var root = json.RootElement;
        root.GetProperty("schema").GetString().Should().Be("freex.parity.cross-app-dashboard.v3");
        root.GetProperty("scopeBoundary").GetString().Should().Contain("do not prove visual parity");

        var integrationEvidence = root.GetProperty("integrationGateEvidence");
        integrationEvidence.GetProperty("testedSourceCommit").GetString().Should().Be("615b53f474dfa1849ae965018d890cba4a138d42");
        integrationEvidence.TryGetProperty("integrationHead", out _).Should().BeFalse();
        integrationEvidence.GetProperty("acceptanceRefreshNote").GetString().Should().Be(
            "This dashboard/report is a later docs-only acceptance refresh; it does not alter the tested source commit.");
        integrationEvidence.GetProperty("defaultNonUiTestLane").GetString().Should().Contain("Avalonia 2,182 passed");

        var freeX = root.GetProperty("apps")[0];
        var visualEvidence = freeX.GetProperty("dialogVisualEvidence");
        visualEvidence.GetProperty("pairedCapturedSurfaceIds").GetInt32().Should().BeGreaterThan(0);
        visualEvidence.GetProperty("pairedDimensionMismatches").GetInt32().Should().Be(0);
        var candidateCount = visualEvidence.GetProperty("visualReviewCandidateCount").GetInt32();
        var threshold = visualEvidence.GetProperty("visualReviewTriageThreshold").GetDouble();
        var highestScore = visualEvidence.GetProperty("highestTriageScore").GetDouble();
        var candidates = visualEvidence.GetProperty("visualReviewCandidates");

        threshold.Should().Be(0.4);
        candidateCount.Should().BeGreaterThanOrEqualTo(0);
        candidates.GetArrayLength().Should().Be(candidateCount);
        visualEvidence.GetProperty("visualReviewCandidateSurfaceIds").GetArrayLength().Should().Be(candidateCount);

        foreach (var candidate in candidates.EnumerateArray())
            candidate.GetProperty("triageScore").GetDouble().Should().BeGreaterThanOrEqualTo(threshold);

        if (candidateCount == 0)
            highestScore.Should().BeLessThan(threshold);
        else
            highestScore.Should().BeGreaterThanOrEqualTo(threshold);

        var markdown = File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.md"));
        markdown.Should().Contain("These are coverage/triage metrics, not a visual-parity claim.");
        markdown.Should().Contain("## FreeX Visual Review Queue");
        markdown.Should().Contain("equal dimensions or paired ids do not establish visual parity.");
        markdown.Should().Contain("unresolved high-delta visual review candidates at triage score >= 0.4");
        markdown.Should().NotContain("System.Object[]");
    }
}
