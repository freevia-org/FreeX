using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CrossAppParityDashboardTests
{
    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void CrossAppParityDashboard_DistinguishesCoverageFromVisualReview()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-CrossAppParityDashboard.ps1",
            repoRoot,
            "-Check");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        var hostGuard = PowerShellScriptRunner.RunToolScript(
            "Test-CrossAppParityDashboard.ps1",
            repoRoot);
        hostGuard.ExitCode.Should().Be(0, hostGuard.CombinedOutput);
        hostGuard.CombinedOutput.Should().Contain("generator -Check passed under pwsh");
        hostGuard.CombinedOutput.Should().Contain("generator -Check passed under powershell.exe");

        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.json")));
        var root = json.RootElement;
        root.GetProperty("schema").GetString().Should().Be("freex.parity.cross-app-dashboard.v3");
        root.GetProperty("scopeBoundary").GetString().Should().Contain("do not prove complete visual parity");

        var integrationEvidence = root.GetProperty("integrationGateEvidence");
        integrationEvidence.GetProperty("testedSourceCommit").GetString().Should().Be("f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f");
        root.GetProperty("cumulativeAppSlicesStatus").GetString().Should().Be("accepted-final-integration-gates");
        root.GetProperty("integrationGateStatus").GetString().Should().Be("accepted");
        root.GetProperty("pendingIntegrationGates").GetArrayLength().Should().Be(0);
        integrationEvidence.TryGetProperty("integrationHead", out _).Should().BeFalse();
        integrationEvidence.GetProperty("acceptanceRefreshNote").GetString().Should().Be(
            "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit.");
        integrationEvidence.GetProperty("repositoryPreflight").GetString().Should().Be(
            "Passed at tested source commit f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-RepositoryPreflight.ps1 with isolated SDK C:\\Users\\quinn\\.dotnet-codex-10.0.400 and Git Bash first on PATH exited 0; 294 JSON, 310 XML-backed, 127 PowerShell scripts, 11 GitHub workflows, 12 test gates/48 assigned projects, 13,996 conflict-marker files checked, and all generated docs/evidence current; elapsed 00:01:55.8304515.");
        integrationEvidence.GetProperty("fullReleaseBuild").GetString().Should().Be(
            "Passed at tested source commit f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f: dotnet build FreeX.slnx --configuration Release -m:1 passed with 0 warnings and 0 errors; MSBuild-retained Time Elapsed 00:09:49.19; wrapper stopwatch 00:09:49.4386774.");
        integrationEvidence.GetProperty("fullReleaseBuildMsBuildElapsed").GetString().Should().Be("00:09:49.19");
        integrationEvidence.GetProperty("fullReleaseBuildWrapperElapsed").GetString().Should().Be("00:09:49.4386774");
        integrationEvidence.GetProperty("defaultNonUiTestLane").GetString().Should().Contain("43,548 passed, 134 intentional skips, 0 failed, 43,682 total");
        integrationEvidence.GetProperty("defaultNonUiTestLane").GetString().Should().Contain("wrapper stopwatch 00:16:54.2974514; independently parsed 31-TRX timestamp span 14:03:31.8502271 to 14:20:25.1692656 (+03:00); duration 00:16:53.3190385");
        integrationEvidence.GetProperty("defaultNonUiTestLaneWrapperElapsed").GetString().Should().Be("00:16:54.2974514");
        integrationEvidence.GetProperty("defaultNonUiTestLaneTrxTimestampSpan").GetString().Should().Be("14:03:31.8502271 to 14:20:25.1692656 (+03:00)");
        integrationEvidence.GetProperty("defaultNonUiTestLaneTrxDuration").GetString().Should().Be("00:16:53.3190385");
        integrationEvidence.GetProperty("independentReviewStatus").GetString().Should().Be("passed");
        integrationEvidence.GetProperty("independentReview").GetString().Should().Be(
            "Passed: an independent final cross-app acceptance review of tested source commit f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f completed in an isolated worktree at integration head 2ee42a45efd651ad9ad1c015403d788570ae02d9; no findings. This review preserves the tested-source boundary, counts, timings, and visual claim boundaries.");
        integrationEvidence.GetProperty("sliceAccounting").GetString().Should().Be(
            "582 cumulative app slices (194 per app) remain the processed Wave194 accounting; later wave feature commits are included in the tested source and do not add Wave194 slices.");

        var freeX = root.GetProperty("apps")[0];
        freeX.GetProperty("functionalMatrix").GetProperty("totalCommands").GetInt32().Should().Be(575);
        freeX.GetProperty("functionalMatrix").GetProperty("parity").GetInt32().Should().Be(569);
        freeX.GetProperty("functionalMatrix").GetProperty("avaloniaMissing").GetInt32().Should().Be(0);
        freeX.GetProperty("functionalMatrix").GetProperty("realBehaviorGaps").GetInt32().Should().Be(0);
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

        var freeW = root.GetProperty("apps")[1];
        freeW.GetProperty("commandInventory").GetProperty("totalCommands").GetInt32().Should().Be(954);
        freeW.GetProperty("commandInventory").GetProperty("bothProfiles").GetInt32().Should().Be(733);
        freeW.GetProperty("commandInventory").GetProperty("actionableGaps").GetInt32().Should().Be(0);
        freeW.GetProperty("renderedEvidence").GetProperty("artifactCoverage").GetProperty("evidenceRowCount").GetInt32().Should().Be(291);
        freeW.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("passCount").GetInt32().Should().Be(80);
        freeW.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("mismatchCount").GetInt32().Should().Be(141);
        freeW.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("avaloniaOnlyScenarioCount").GetInt32().Should().Be(70);

        var freeP = root.GetProperty("apps")[2];
        freeP.GetProperty("commandInventory").GetProperty("totalCommands").GetInt32().Should().Be(719);
        freeP.GetProperty("commandInventory").GetProperty("bothProfiles").GetInt32().Should().Be(719);
        freeP.GetProperty("commandInventory").GetProperty("actionableMissingWpf").GetInt32().Should().Be(0);
        freeP.GetProperty("commandInventory").GetProperty("actionableMissingAvalonia").GetInt32().Should().Be(0);
        freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("pairedScenarioCount").GetInt32().Should().Be(64);
        freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("passCount").GetInt32().Should().Be(63);
        freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("mismatchCount").GetInt32().Should().Be(1);

        var markdown = File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.md"));
        markdown.Should().Contain("These are coverage/triage metrics, not a visual-parity claim.");
        markdown.Should().Contain("## FreeX Visual Review Queue");
        markdown.Should().Contain("equal dimensions or paired ids do not establish visual parity.");
        markdown.Should().Contain("unresolved high-delta visual review candidates at triage score >= 0.4");
        markdown.Should().NotContain("System.Object[]");
    }

    [Fact]
    public void CrossAppParityDashboard_AcceptanceBoundaryMutationCoverageRejectsUnsafeHistories()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Test-CrossAppParityDashboard.ps1",
            repoRoot,
            "-BoundarySelfTest");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain(
            "Acceptance boundary mutation coverage passed: unexpected-path and non-ancestor histories rejected.");
    }
}
