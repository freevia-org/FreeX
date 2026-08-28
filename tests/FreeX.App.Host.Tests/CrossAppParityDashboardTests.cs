using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var acceptanceRefresh = PowerShellScriptRunner.RunToolScript(
            "Generate-CrossAppParityDashboard.ps1",
            repoRoot,
            "-AcceptanceRefresh",
            "-AcceptanceRefreshTestedSourceCommit",
            "HEAD");
        acceptanceRefresh.ExitCode.Should().Be(0, acceptanceRefresh.CombinedOutput);
        acceptanceRefresh.CombinedOutput.Should().Contain(
            "Acceptance refresh real-repository boundary passed");

        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.json")));
        var root = json.RootElement;
        root.GetProperty("schema").GetString().Should().Be("freex.parity.cross-app-dashboard.v3");
        root.GetProperty("scopeBoundary").GetString().Should().Contain("do not prove complete visual parity");
        root.GetProperty("wave").GetInt32().Should().Be(195);
        root.GetProperty("cumulativeAppSlices").GetInt32().Should().Be(585);
        root.GetProperty("cumulativeAppSlicesStatus").GetString().Should().Be("pending-integration-gates");
        root.GetProperty("integrationGateStatus").GetString().Should().Be("pending");
        root.GetProperty("pendingIntegrationGates").GetArrayLength().Should().Be(2);

        var integrationEvidence = root.GetProperty("integrationGateEvidence");
        integrationEvidence.GetProperty("status").GetString().Should().Be("pending");
        integrationEvidence.GetProperty("sliceAccounting").GetString().Should().Be(
            "Wave 195 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 585 app slices (195 per app).");
        integrationEvidence.GetProperty("gateBoundary").GetString().Should().Contain("pending and not accepted");
        integrationEvidence.GetProperty("gateBoundary").GetString().Should().Contain("final exact-head acceptance facts");
        integrationEvidence.GetProperty("localGatePolicy").GetString().Should().Contain("repository preflight and the full Release build");
        integrationEvidence.GetProperty("localGatePolicy").GetString().Should().Contain("delegated to GitHub");
        integrationEvidence.GetProperty("delegatedGitHubGates").GetArrayLength().Should().Be(2);

        var historicalWave194 = integrationEvidence.GetProperty("historicalWave194Acceptance");
        historicalWave194.GetProperty("testedSourceCommit").GetString().Should().Be("f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f");
        historicalWave194.TryGetProperty("integrationHead", out _).Should().BeFalse();
        historicalWave194.GetProperty("acceptanceRefreshNote").GetString().Should().Be(
            "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit.");
        historicalWave194.GetProperty("repositoryPreflight").GetString().Should().Contain(
            "Passed at tested source commit f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f");
        historicalWave194.GetProperty("fullReleaseBuild").GetString().Should().Contain(
            "Passed at tested source commit f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f: dotnet build FreeX.slnx --configuration Release -m:1 passed with 0 warnings and 0 errors; MSBuild-retained Time Elapsed 00:09:49.19; wrapper stopwatch 00:09:49.4386774.");
        historicalWave194.GetProperty("defaultNonUiTestLane").GetString().Should().Contain("43,548 passed, 134 intentional skips, 0 failed, 43,682 total");
        historicalWave194.GetProperty("independentReviewStatus").GetString().Should().Be("passed");
        historicalWave194.GetProperty("sliceAccounting").GetString().Should().Be(
            "582 cumulative app slices (194 per app) remain the processed Wave194 accounting; later wave feature commits are included in the tested source and do not add Wave194 slices.");

        var freeX = root.GetProperty("apps")[0];
        freeX.GetProperty("functionalMatrix").GetProperty("totalCommands").GetInt32().Should().Be(575);
        freeX.GetProperty("functionalMatrix").GetProperty("parity").GetInt32().Should().Be(569);
        freeX.GetProperty("functionalMatrix").GetProperty("avaloniaMissing").GetInt32().Should().Be(0);
        freeX.GetProperty("functionalMatrix").GetProperty("realBehaviorGaps").GetInt32().Should().Be(0);
        var visualEvidence = freeX.GetProperty("dialogVisualEvidence");
        visualEvidence.GetProperty("pairedCapturedSurfaceIds").GetInt32().Should().BeGreaterThan(0);
        visualEvidence.GetProperty("pairedDimensionMismatches").GetInt32().Should().Be(0);
        var freeXWave195 = freeX.GetProperty("renderedEvidence").GetProperty("physicalEvidence").GetProperty("wave195");
        freeXWave195.GetProperty("status").GetString().Should().Be("passed");
        freeXWave195.GetProperty("physicalPassed").GetInt32().Should().Be(2);
        freeXWave195.GetProperty("physicalTotal").GetInt32().Should().Be(2);
        freeXWave195.GetProperty("evidenceArtifactCount").GetInt32().Should().Be(75);
        freeXWave195.GetProperty("screenshotCount").GetInt32().Should().Be(58);
        freeXWave195.GetProperty("reloadWitnessPassed").GetInt32().Should().Be(2);
        freeXWave195.GetProperty("reloadWitnessTotal").GetInt32().Should().Be(2);
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
        var freeWWave195 = freeW.GetProperty("renderedEvidence").GetProperty("wave195");
        freeWWave195.GetProperty("catalogRowCount").GetInt32().Should().Be(291);
        freeWWave195.GetProperty("passCount").GetInt32().Should().Be(80);
        freeWWave195.GetProperty("genuineVisualMismatchCount").GetInt32().Should().Be(141);
        freeWWave195.GetProperty("avaloniaExtensionCount").GetInt32().Should().Be(70);

        using var freeWComparison = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "freew-dialog-harness", "freew_dialog_visual_comparison.json")));
        var comparisonRows = freeWComparison.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var legalRows = comparisonRows
            .Where(row => row.GetProperty("scenarioId").GetString()!.StartsWith("legal-notices.", StringComparison.Ordinal))
            .ToArray();
        var note = File.ReadAllText(Path.Combine(repoRoot, "freew", "docs", "parity", "avalonia-parity-wave195-freew-legal-notices-20260828.md"));
        var baselinePattern = new Regex(
            "^\\|\\s*`(?<id>[^`]+)`\\s*\\|\\s*(?<before>[\\d,]+)\\s*/[^|]+\\|\\s*(?<after>[\\d,]+)\\s*/[^|]+\\|\\s*(?<delta>[+-]?[\\d,]+)\\s*\\|\\s*$",
            RegexOptions.Multiline);
        var baselineById = baselinePattern.Matches(note)
            .ToDictionary(
                match => match.Groups["id"].Value,
                match => (
                    Before: int.Parse(match.Groups["before"].Value.Replace(",", "", StringComparison.Ordinal)),
                    After: int.Parse(match.Groups["after"].Value.Replace(",", "", StringComparison.Ordinal)),
                    Delta: int.Parse(match.Groups["delta"].Value.Replace(",", "", StringComparison.Ordinal))));
        comparisonRows.Length.Should().Be(freeWWave195.GetProperty("catalogRowCount").GetInt32());
        comparisonRows.Count(row => row.GetProperty("classification").GetString() == "pass")
            .Should().Be(freeWWave195.GetProperty("passCount").GetInt32());
        comparisonRows.Count(row => row.GetProperty("classification").GetString() == "genuine-visual-mismatch")
            .Should().Be(freeWWave195.GetProperty("genuineVisualMismatchCount").GetInt32());
        comparisonRows.Count(row => row.GetProperty("classification").GetString() == "avalonia-extension")
            .Should().Be(freeWWave195.GetProperty("avaloniaExtensionCount").GetInt32());
        legalRows.Length.Should().Be(baselineById.Count);
        foreach (var row in legalRows)
        {
            var scenarioId = row.GetProperty("scenarioId").GetString()!;
            var currentChangedPixels = row.GetProperty("metrics").GetProperty("changedPixels").GetInt32();
            currentChangedPixels.Should().Be(baselineById[scenarioId].After);
            (currentChangedPixels - baselineById[scenarioId].Before)
                .Should().Be(baselineById[scenarioId].Delta);
        }
        var expectedBaselineChangedPixels = legalRows.Sum(row => baselineById[row.GetProperty("scenarioId").GetString()!].Before);
        var expectedChangedPixels = legalRows.Sum(row => row.GetProperty("metrics").GetProperty("changedPixels").GetInt32());
        var expectedAggregateDelta = legalRows.Sum(row => row.GetProperty("metrics").GetProperty("changedPixels").GetInt32()
            - baselineById[row.GetProperty("scenarioId").GetString()!].Before);
        expectedBaselineChangedPixels.Should().Be(freeWWave195.GetProperty("legalNoticesBaselineChangedPixels").GetInt32());
        expectedChangedPixels.Should().Be(freeWWave195.GetProperty("legalNoticesChangedPixels").GetInt32());
        expectedAggregateDelta.Should().Be(freeWWave195.GetProperty("legalNoticesAggregateDelta").GetInt32());
        baselineById.Values.Sum(value => value.Delta)
            .Should().Be(freeWWave195.GetProperty("legalNoticesAggregateDelta").GetInt32());
        legalRows.Sum(row => row.GetProperty("metrics").GetProperty("changedPixels").GetInt32()
            - baselineById[row.GetProperty("scenarioId").GetString()!].Before)
            .Should().Be(expectedAggregateDelta);
        (comparisonRows.Length - legalRows.Length)
            .Should().Be(freeWWave195.GetProperty("nonLegalRowsStructurallyUnchanged").GetInt32());

        var freeP = root.GetProperty("apps")[2];
        freeP.GetProperty("commandInventory").GetProperty("totalCommands").GetInt32().Should().Be(719);
        freeP.GetProperty("commandInventory").GetProperty("bothProfiles").GetInt32().Should().Be(719);
        freeP.GetProperty("commandInventory").GetProperty("actionableMissingWpf").GetInt32().Should().Be(0);
        freeP.GetProperty("commandInventory").GetProperty("actionableMissingAvalonia").GetInt32().Should().Be(0);
        freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("pairedScenarioCount").GetInt32().Should().Be(64);
        freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("passCount").GetInt32().Should().Be(64);
        freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence").GetProperty("mismatchCount").GetInt32().Should().Be(0);
        var freePWave195 = freeP.GetProperty("renderedEvidence").GetProperty("wave195");
        freePWave195.GetProperty("wholeWindowScenarioCount").GetInt32().Should().Be(36);
        freePWave195.GetProperty("wholeWindowPassCount").GetInt32().Should().Be(36);
        freePWave195.GetProperty("wholeWindowMismatchCount").GetInt32().Should().Be(0);
        var freePPairedEvidence = freeP.GetProperty("renderedEvidence").GetProperty("pairedEvidence");
        freePWave195.GetProperty("combinedRenderedEvidenceCount").GetInt32().Should().Be(
            freePPairedEvidence.GetProperty("pairedScenarioCount").GetInt32());
        freePWave195.GetProperty("combinedRenderedEvidencePassCount").GetInt32().Should().Be(
            freePPairedEvidence.GetProperty("passCount").GetInt32());
        freePWave195.GetProperty("combinedRenderedEvidenceMismatchCount").GetInt32().Should().Be(
            freePPairedEvidence.GetProperty("mismatchCount").GetInt32());
        freePWave195.GetProperty("combinedRenderedEvidenceCount").GetInt32().Should().Be(64);
        freePWave195.GetProperty("combinedRenderedEvidencePassCount").GetInt32().Should().Be(64);
        freePWave195.GetProperty("combinedRenderedEvidenceMismatchCount").GetInt32().Should().Be(0);
        var selection = freePWave195.GetProperty("richTextSelection");
        selection.GetProperty("changedPixelRatioBefore").GetDouble().Should().Be(0.2185757);
        selection.GetProperty("changedPixelRatioAfter").GetDouble().Should().Be(0.1809518682);
        selection.GetProperty("meanChannelDelta").GetDouble().Should().Be(9.7919313736);
        selection.GetProperty("perceptualHashDistance").GetInt32().Should().Be(11);
        selection.GetProperty("cropDimensions").GetString().Should().Be("251x74");

        var markdown = File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.md"));
        markdown.Should().Contain("These are coverage/triage metrics, not a visual-parity claim.");
        markdown.Should().Contain("Wave195 current status is **pending/not accepted**");
        markdown.Should().Contain("cumulative 585 app slices (195 per app)");
        markdown.Should().Contain($"{expectedBaselineChangedPixels} to {expectedChangedPixels}");
        markdown.Should().Contain("0.1809518682");
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
