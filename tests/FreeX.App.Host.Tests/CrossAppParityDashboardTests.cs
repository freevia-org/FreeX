using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        var generatorSource = File.ReadAllText(
            Path.Combine(repoRoot, "tools", "Generate-CrossAppParityDashboard.ps1"));
        generatorSource.Should().NotContain(
            "tools\\\\Test-RepositoryPreflight.ps1",
            "portable PowerShell scripts must use repository paths with forward slashes");
        generatorSource.Should().Contain("tools/Test-RepositoryPreflight.ps1");

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
        root.GetProperty("wave").GetInt32().Should().Be(198);
        root.GetProperty("cumulativeAppSlices").GetInt32().Should().Be(594);
        root.GetProperty("cumulativeAppSlicesStatus").GetString().Should().Be("accepted-local-gates");
        root.GetProperty("integrationGateStatus").GetString().Should().Be("accepted-local-gates");
        root.GetProperty("pendingIntegrationGates").GetArrayLength().Should().Be(0);

        var integrationEvidence = root.GetProperty("integrationGateEvidence");
        integrationEvidence.GetProperty("status").GetString().Should().Be("accepted-local-gates");
        integrationEvidence.GetProperty("acceptanceStatus").GetString().Should().Be("accepted-local-gates");
        integrationEvidence.GetProperty("testedSourceCommit").GetString().Should().Be("1c6cb5e8019dd0098465c67f8f0261929a3d3bbc");
        integrationEvidence.GetProperty("pendingIntegrationGates").GetArrayLength().Should().Be(0);
        integrationEvidence.GetProperty("acceptedLocalGates").GetArrayLength().Should().Be(2);
        integrationEvidence.GetProperty("acceptanceRefreshAllowedPaths").GetArrayLength().Should().Be(6);
        integrationEvidence.GetProperty("sliceAccounting").GetString().Should().Be(
            "Wave 198 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 594 app slices (198 per app).");
        integrationEvidence.GetProperty("gateBoundary").GetString().Should().Contain("1c6cb5e8019dd0098465c67f8f0261929a3d3bbc");
        integrationEvidence.GetProperty("gateBoundary").GetString().Should().Contain("six allowlisted acceptance/report paths");
        integrationEvidence.GetProperty("gateBoundary").GetString().Should().Contain("full Avalonia/WPF parity is not claimed");
        integrationEvidence.GetProperty("delegatedGitHubGateStatus").GetString().Should().Be("not-run-locally");
        integrationEvidence.GetProperty("localGatePolicy").GetString().Should().Contain("repository preflight and the full Release build");
        integrationEvidence.GetProperty("localGatePolicy").GetString().Should().Contain("delegated to GitHub");
        integrationEvidence.GetProperty("delegatedGitHubGates").GetArrayLength().Should().Be(2);

        integrationEvidence.GetProperty("focusedTests").GetString().Should().Contain("FreeX Wave198 3/3 (combined Wave198/Wave197 command 5/5); shared DialogTabChromeParityTests 3/3; FreeW target suite 32/32 plus FontDialog/Wave198 review suite 6/6; FreeP Wave198 evidence 2/2");
        integrationEvidence.GetProperty("fullReleaseBuild").GetString().Should().Contain("MSBuild 00:09:30.47; wrapper 00:09:30.8983681");
        integrationEvidence.GetProperty("independentReview").GetString().Should().Contain("no P1 findings");

        var historicalWave197 = integrationEvidence.GetProperty("historicalWave197Acceptance");
        historicalWave197.GetProperty("status").GetString().Should().Be("accepted-local-gates");
        historicalWave197.GetProperty("testedSourceCommit").GetString().Should().Be("a6b1f27e02d15a7495644db64c9bda3a839f126a");
        historicalWave197.GetProperty("sliceAccounting").GetString().Should().Be(
            "Wave 197 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 591 app slices (197 per app).");
        var historicalWave196 = historicalWave197.GetProperty("historicalWave196Acceptance");
        historicalWave196.GetProperty("status").GetString().Should().Be("accepted-local-gates");
        historicalWave196.GetProperty("testedSourceCommit").GetString().Should().Be("100f4aea399e3bc9d194c15cf962ded7d0cf3772");
        historicalWave196.GetProperty("pendingIntegrationGates").GetArrayLength().Should().Be(0);
        historicalWave196.GetProperty("sliceAccounting").GetString().Should().Be(
            "Wave 196 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 588 app slices (196 per app).");
        historicalWave196.GetProperty("focusedTests").GetString().Should().Contain("FreeX focused 22/22; FreeW focused 2/2; FreeP renderer/evidence 10/10 and resolved model 1/1");

        var historicalWave195 = historicalWave196.GetProperty("historicalWave195Acceptance");
        historicalWave195.GetProperty("status").GetString().Should().Be("accepted-local-gates");
        historicalWave195.GetProperty("testedSourceCommit").GetString().Should().Be("feff4d47c02d57112c6cb191bcc85e1d60ea4e06");
        historicalWave195.GetProperty("pendingIntegrationGates").GetArrayLength().Should().Be(0);
        historicalWave195.GetProperty("sliceAccounting").GetString().Should().Be(
            "Wave 195 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 585 app slices (195 per app).");
        historicalWave195.GetProperty("focusedTests").GetString().Should().Contain("FreeX Wave195 physical 2/2");

        var historicalWave194 = historicalWave195.GetProperty("historicalWave194Acceptance");
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
        var freeXWave196 = freeX.GetProperty("renderedEvidence").GetProperty("physicalEvidence").GetProperty("wave196");
        freeXWave196.GetProperty("status").GetString().Should().Be("evidence-recorded");
        freeXWave196.GetProperty("physicalPassed").GetInt32().Should().Be(1);
        freeXWave196.GetProperty("physicalTotal").GetInt32().Should().Be(1);
        freeXWave196.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(22);
        freeXWave196.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(22);
        freeXWave196.GetProperty("persistedStyle").GetString().Should().Be("style-id=1|font-id=1|bold=true");
        freeXWave196.GetProperty("saveClean").GetBoolean().Should().BeTrue();
        var freeXWave197 = freeX.GetProperty("renderedEvidence").GetProperty("physicalEvidence").GetProperty("wave197");
        freeXWave197.GetProperty("status").GetString().Should().Be("evidence-recorded");
        freeXWave197.GetProperty("physicalPassed").GetInt32().Should().Be(1);
        freeXWave197.GetProperty("physicalTotal").GetInt32().Should().Be(1);
        freeXWave197.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(16);
        freeXWave197.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(16);
        freeXWave197.GetProperty("productionDockerX11Report").GetString().Should().Be("20260829T013532Z");
        freeXWave197.GetProperty("persistedStyle").GetString().Should().Be("style-id=1|numFmtId=2|number-format=true");
        freeXWave197.GetProperty("saveClean").GetBoolean().Should().BeTrue();
        freeXWave197.GetProperty("ordinaryBubbleKeyRouting").GetString().Should().Be("retained");
        freeXWave197.GetProperty("deferredComboDismissFocusRestore").GetString().Should().Contain("rechecks focus immediately and synchronously restores worksheet focus");
        var freeXWave198 = freeX.GetProperty("renderedEvidence").GetProperty("physicalEvidence").GetProperty("wave198");
        freeXWave198.GetProperty("status").GetString().Should().Be("evidence-recorded");
        freeXWave198.GetProperty("physicalPassed").GetInt32().Should().Be(1);
        freeXWave198.GetProperty("physicalTotal").GetInt32().Should().Be(1);
        freeXWave198.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(3);
        freeXWave198.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(3);
        freeXWave198.GetProperty("persistedStyle").GetString().Should().Be("style-id=1|font-id=1|font-name=Arial|font-family=true");
        freeXWave198.GetProperty("saveClean").GetBoolean().Should().BeTrue();
        freeXWave198.GetProperty("automaticComboCloseFocus").GetString().Should().Be("not-measured");
        freeXWave198.GetProperty("checksumStatus").GetString().Should().Contain("verified");

        var freeXEvidenceDirectory = Path.Combine(repoRoot, "docs", "parity", "freex-wave198-ribbon-font-family", "evidence");
        foreach (var line in File.ReadAllLines(Path.Combine(freeXEvidenceDirectory, "SHA256SUMS.txt")))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            parts.Should().HaveCount(2);
            using var stream = File.OpenRead(Path.Combine(freeXEvidenceDirectory, parts[1]));
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            actualHash.Should().Be(parts[0].ToLowerInvariant());
        }
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
        var freeWWave196 = freeW.GetProperty("renderedEvidence").GetProperty("wave196");
        freeWWave196.GetProperty("status").GetString().Should().Be("evidence-recorded");
        freeWWave196.GetProperty("scenarios").EnumerateArray().Select(item => item.GetString())
            .Should().Contain("ConsecutiveTrailingInlineFlowBreaks_PlaceCaretAtTheFinalPostBreakBoundary");
        freeWWave196.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(2);
        freeWWave196.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(2);
        freeWWave196.GetProperty("consecutiveBreakCoverage").GetBoolean().Should().BeTrue();
        var freeWWave197 = freeW.GetProperty("renderedEvidence").GetProperty("wave197");
        freeWWave197.GetProperty("status").GetString().Should().Be("candidate-refuted");
        freeWWave197.GetProperty("scenarioCount").GetInt32().Should().Be(6);
        freeWWave197.GetProperty("uniqueScenarioCount").GetInt32().Should().Be(6);
        freeWWave197.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(20);
        freeWWave197.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(20);
        freeWWave197.GetProperty("surfaceMarginCandidate").GetString().Should().Contain("regressed all six");
        freeWWave197.GetProperty("lineBoxCandidate").GetString().Should().Contain("improved two long rows and regressed two");
        freeWWave197.GetProperty("productionCandidateRetained").GetBoolean().Should().BeFalse();
        var freeWWave198 = freeW.GetProperty("renderedEvidence").GetProperty("wave198");
        freeWWave198.GetProperty("status").GetString().Should().Be("evidence-recorded");
        freeWWave198.GetProperty("targetScenarioCount").GetInt32().Should().Be(7);
        freeWWave198.GetProperty("controlScenarioCount").GetInt32().Should().Be(3);
        freeWWave198.GetProperty("scenarioCount").GetInt32().Should().Be(10);
        freeWWave198.GetProperty("targetBeforeChangedPixels").GetInt32().Should().Be(191369);
        freeWWave198.GetProperty("targetAfterChangedPixels").GetInt32().Should().Be(187872);
        freeWWave198.GetProperty("targetChangedPixelsReduction").GetInt32().Should().Be(3497);
        freeWWave198.GetProperty("controlBeforeChangedPixels").GetInt32().Should().Be(106540);
        freeWWave198.GetProperty("controlAfterChangedPixels").GetInt32().Should().Be(104932);
        freeWWave198.GetProperty("controlChangedPixelsReduction").GetInt32().Should().Be(1608);
        freeWWave198.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(6);
        freeWWave198.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(6);
        freeWWave198.GetProperty("evidenceBoundary").GetString().Should().Contain("metadata-only").And.Contain("untracked");

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
        var freePWave196 = freeP.GetProperty("renderedEvidence").GetProperty("wave196");
        freePWave196.GetProperty("status").GetString().Should().Be("evidence-recorded");
        freePWave196.GetProperty("target").GetString().Should().Be("17-bullets-autofit / slide-02");
        freePWave196.GetProperty("textHintingModeAfter").GetString().Should().Be("Light");
        freePWave196.GetProperty("controlUnchanged").GetBoolean().Should().BeTrue();
        freePWave196.GetProperty("imageHashCount").GetInt32().Should().Be(4);
        var freePWave197 = freeP.GetProperty("renderedEvidence").GetProperty("wave197");
        freePWave197.GetProperty("status").GetString().Should().Be("candidate-refuted");
        freePWave197.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(4);
        freePWave197.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(4);
        freePWave197.GetProperty("productionCandidateRetained").GetBoolean().Should().BeFalse();
        freePWave197.GetProperty("trackedImageBytesAndHashes").GetString().Should()
            .Contain("Leading-candidate").And
            .Contain("verified").And
            .Contain("four missing untracked candidate images").And
            .Contain("no current byte-integrity claim");
        freePWave197.GetProperty("residualBoundary").GetString().Should().Contain("unresolved text-raster residual").And.Contain("not a fallback-font diagnosis");
        var freePWave198 = freeP.GetProperty("renderedEvidence").GetProperty("wave198");
        freePWave198.GetProperty("status").GetString().Should().Be("candidate-refuted");
        freePWave198.GetProperty("focusedSourceTestsPassed").GetInt32().Should().Be(2);
        freePWave198.GetProperty("focusedSourceTestsTotal").GetInt32().Should().Be(2);
        freePWave198.GetProperty("productionCandidateRetained").GetBoolean().Should().BeFalse();
        freePWave198.GetProperty("avaloniaOfficeDeltaPercentagePoints").GetDouble().Should().Be(-0.0237);
        freePWave198.GetProperty("wpfAvaloniaDeltaPercentagePoints").GetDouble().Should().Be(0.0092);
        freePWave198.GetProperty("generationLinkage").GetString().Should().Be("not-independently-proven");

        var markdown = File.ReadAllText(Path.Combine(repoRoot, "docs", "parity", "avalonia-wpf-cross-app-dashboard.md"));
        markdown.Should().Contain("These are coverage/triage metrics, not a visual-parity claim.");
        markdown.Should().Contain("Wave198 local integration gates are **accepted**");
        markdown.Should().Contain("1c6cb5e8019dd0098465c67f8f0261929a3d3bbc");
        markdown.Should().Contain("Pending local gates: none");
        markdown.Should().Contain("cumulative 594 app slices (198 per app)");
        markdown.Should().Contain("Wave197 remains historical acceptance context");
        markdown.Should().Contain("Wave196 remains historical acceptance context");
        markdown.Should().Contain("FreeW Wave196 evidence: the committed trailing inline flow-break caret oracle");
        markdown.Should().Contain("**22/22** focused source tests");
        markdown.Should().Contain("**20/20** focused tests");
        markdown.Should().Contain("**4/4** focused tests");
        markdown.Should().Contain("FreeW Wave197 evidence: **20/20** focused tests cover exactly **6** unique Legal Notices scenarios");
        markdown.Should().Contain("FreeX Wave198 evidence: **1/1** production Docker/X11 font-family probe");
        markdown.Should().Contain("FreeW Wave198 evidence: shared tab-pane trailing-frame correction");
        markdown.Should().Contain("FreeP Wave198 evidence: SubpixelAntialias is rejected");
        markdown.Should().Contain("surface-margin candidate regressed all six rows");
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
