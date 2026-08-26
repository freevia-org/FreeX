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
        integrationEvidence.GetProperty("testedSourceCommit").GetString().Should().Be("dd17f21fd06bd03aa0b3f151de311affa01adcbe");
        integrationEvidence.GetProperty("evidenceStatus").GetString().Should().Be("stale-pending-rerun");
        integrationEvidence.GetProperty("staleReason").GetString().Should().Be(
            "The retained Wave194 evidence was produced before origin/main portability tooling hardening at cbddefd732; rerun the Release, default non-UI, focused evidence, and repository integration gates before re-anchoring current acceptance.");
        root.GetProperty("cumulativeAppSlicesStatus").GetString().Should().Be("accepted-historical-pending-refresh");
        root.GetProperty("integrationGateStatus").GetString().Should().Be("pending");
        root.GetProperty("pendingIntegrationGates")[0].GetString().Should().Be(
            "Rerun Wave194 Release, default non-UI, focused evidence, and repository integration gates after origin/main portability tooling hardening at cbddefd732.");
        integrationEvidence.TryGetProperty("integrationHead", out _).Should().BeFalse();
        integrationEvidence.GetProperty("acceptanceRefreshNote").GetString().Should().Be(
            "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit.");
        integrationEvidence.GetProperty("repositoryPreflight").GetString().Should().Be(
            "Passed at tested source commit dd17f21fd06bd03aa0b3f151de311affa01adcbe: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\\Test-RepositoryPreflight.ps1 exited 0; 294 JSON, 310 XML-backed, 126 PowerShell scripts, 11 GitHub workflows, 12 test gates/48 assigned projects, 13,994 conflict-marker files checked, and all generated docs/evidence current; elapsed 00:03:23.3371196.");
        integrationEvidence.GetProperty("fullReleaseBuild").GetString().Should().Be(
            "Passed at tested source commit dd17f21fd06bd03aa0b3f151de311affa01adcbe: dotnet build FreeX.slnx --configuration Release -m:1 passed with 0 warnings and 0 errors; MSBuild-retained Time Elapsed 00:13:46.21; wrapper stopwatch 00:13:46.4785808.");
        integrationEvidence.GetProperty("fullReleaseBuildMsBuildElapsed").GetString().Should().Be("00:13:46.21");
        integrationEvidence.GetProperty("fullReleaseBuildWrapperElapsed").GetString().Should().Be("00:13:46.4785808");
        integrationEvidence.GetProperty("defaultNonUiTestLane").GetString().Should().Contain("43,548 passed, 134 intentional skips, 0 failed, 43,682 total");
        integrationEvidence.GetProperty("defaultNonUiTestLane").GetString().Should().Contain("wrapper stopwatch 00:17:37.4967641; independently parsed 31-TRX timestamp span 12:41:31.975 to 12:59:08.603 (+03:00); duration 00:17:36.628");
        integrationEvidence.GetProperty("defaultNonUiTestLaneWrapperElapsed").GetString().Should().Be("00:17:37.4967641");
        integrationEvidence.GetProperty("defaultNonUiTestLaneTrxTimestampSpan").GetString().Should().Be("12:41:31.975 to 12:59:08.603 (+03:00)");
        integrationEvidence.GetProperty("defaultNonUiTestLaneTrxDuration").GetString().Should().Be("00:17:36.628");
        integrationEvidence.GetProperty("independentReviewStatus").GetString().Should().Be("pending");
        integrationEvidence.GetProperty("independentReview").GetString().Should().Be(
            "Pending: an independent final cross-app acceptance review of tested source commit dd17f21fd06bd03aa0b3f151de311affa01adcbe remains outstanding and must be completed later; this refresh does not claim that review passed. This status does not alter the tested-source boundary, counts, timings, or visual claim boundaries.");
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
