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
        root.GetProperty("schema").GetString().Should().Be("freex.parity.cross-app-dashboard.v2");
        root.GetProperty("scopeBoundary").GetString().Should().Contain("do not prove visual parity");

        var freeX = root.GetProperty("apps")[0];
        var visualEvidence = freeX.GetProperty("dialogVisualEvidence");
        visualEvidence.GetProperty("pairedCapturedSurfaceIds").GetInt32().Should().BeGreaterThan(0);
        visualEvidence.GetProperty("pairedDimensionMismatches").GetInt32().Should().Be(0);
        visualEvidence.GetProperty("visualReviewCandidateCount").GetInt32().Should().BeGreaterThan(0);
        visualEvidence.GetProperty("visualReviewTriageThreshold").GetDouble().Should().Be(0.4);
        visualEvidence.GetProperty("visualReviewCandidates").GetArrayLength().Should()
            .Be(visualEvidence.GetProperty("visualReviewCandidateCount").GetInt32());
        visualEvidence.GetProperty("visualReviewCandidateSurfaceIds").GetArrayLength().Should()
            .Be(visualEvidence.GetProperty("visualReviewCandidateCount").GetInt32());

        var markdown = File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.md"));
        markdown.Should().Contain("These are coverage/triage metrics, not a visual-parity claim.");
        markdown.Should().Contain("## FreeX Visual Review Queue");
        markdown.Should().Contain("dialog.PrintPreview");
        markdown.Should().Contain("0.511967");
        markdown.Should().NotContain("System.Object[]");
    }
}
