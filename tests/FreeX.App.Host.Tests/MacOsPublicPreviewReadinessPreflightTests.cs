using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsPublicPreviewReadinessPreflightTests
{
    [Fact]
    public void PublicPreviewReadinessPreflight_DocumentsEvidenceContractAndToolUsage()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-MacOsPublicPreviewReadiness.ps1");
        var signingRunbook = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-signing-notarization.md");
        var distributionPlan = WorkspaceFileLocator.ReadAllText("docs", "release", "test-distribution.md");
        var hostedRunnerPlan = WorkspaceFileLocator.ReadAllText("docs", "planning", "macos-hosted-runner-build-plan.md");

        script.Should().Contain("artifact_channel");
        script.Should().Contain("distribution_readiness");
        script.Should().Contain("codesign_mode");
        script.Should().Contain("notarization_status");
        script.Should().Contain("stapler_validated");
        script.Should().Contain("gatekeeper_assessment_status");
        script.Should().Contain("gatekeeper_assessment_source");
        script.Should().Contain("zip_sha256");
        script.Should().Contain("RequireAggregateReadinessArtifact");
        script.Should().Contain("RequireReleasePublicationArtifact");
        script.Should().Contain("macos-preview-readiness-manifest.json");
        script.Should().Contain("freex-<run-id>-<run-attempt>-macos-preview-readiness");
        script.Should().Contain("FreeX-latest-macos-distribution-candidate-manifest.json");
        script.Should().Contain("default_open_launch_smoke_report");
        script.Should().Contain("format_cells_style_roundtrip_count");
        script.Should().Contain("command_key_smoke_attempted");
        script.Should().Contain("live_command_key_smoke");
        script.Should().Contain("macos_launch_smoke");
        script.Should().Contain("RequireSeparateDiagnosticsArtifact");
        script.Should().Contain("freex-$Runtime-macos-open-with-launch-smoke.txt");
        script.Should().Contain("freex-$Runtime-macos-default-open-launch-smoke.txt");
        script.Should().Contain("launchservices_default_open_boundary");
        script.Should().Contain("ExpectedRunId");
        script.Should().Contain("freex-<run-id>-<run-attempt>-macos-release-assets");
        script.Should().Contain("multiple downloaded macOS app artifact bundles");
        script.Should().Contain("macOS public-preview evidence preflight passed");

        signingRunbook.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        signingRunbook.Should().Contain("-DistributionCandidate");
        signingRunbook.Should().Contain("-RequireSeparateDiagnosticsArtifact");
        signingRunbook.Should().Contain("-RequireReleasePublicationArtifact");
        signingRunbook.Should().Contain("-ExpectedRunId <run-id>");
        signingRunbook.Should().Contain("-ExpectedRunAttempt <run-attempt>");
        signingRunbook.Should().Contain("freex-<run-id>-<run-attempt>-macos-release-assets");
        signingRunbook.Should().Contain("Keep those wrapper directory names intact under `artifacts/macos-preview`.");
        distributionPlan.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        distributionPlan.Should().Contain("Windows-runnable");
        distributionPlan.Should().Contain("-ExpectedRunId <run-id>");
        distributionPlan.Should().Contain("-ExpectedRunAttempt <run-attempt>");
        distributionPlan.Should().Contain("-RequireReleasePublicationArtifact");
        distributionPlan.Should().Contain("freex-<run-id>-<run-attempt>-macos-release-assets");
        distributionPlan.Should().Contain("Do not flatten wrapper contents directly into the artifact root");
        hostedRunnerPlan.Should().Contain("-ExpectedRunId <run-id>");
        hostedRunnerPlan.Should().Contain("-ExpectedRunAttempt <run-attempt>");
        hostedRunnerPlan.Should().Contain("-RequireReleasePublicationArtifact");
        hostedRunnerPlan.Should().Contain("freex-<run-id>-<run-attempt>-macos-release-assets");
        hostedRunnerPlan.Should().Contain("wrapper directory under the artifact root");

        AssertDistributionCandidatePreflightCommandsRequireReleasePublicationArtifact(
            signingRunbook,
            "docs/release/macos-signing-notarization.md");
        AssertDistributionCandidatePreflightCommandsRequireReleasePublicationArtifact(
            distributionPlan,
            "docs/release/test-distribution.md");
        AssertDistributionCandidatePreflightCommandsRequireReleasePublicationArtifact(
            hostedRunnerPlan,
            "docs/planning/macos-hosted-runner-build-plan.md");
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticInternalPreviewBundlesWithExplicitLiveCommandKeySmoke()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
        result.Output.Should().Contain("osx-arm64");
        result.Output.Should().Contain("osx-x64");
    }

    [Fact]
    public void ReadinessPreflight_PassesWhenHostedLaunchSmokeMarksLiveCommandKeySmokeNotRequired()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        MarkLiveCommandKeySmokeNotRequired(arm64);
        MarkLiveCommandKeySmokeNotRequired(x64);

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticGitHubActionsArtifactWrappersWithExpectedRun()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);

        var result = RunPreflight(temp.Path, "-ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
        result.Output.Should().Contain("freex-42-1-osx-arm64-macos-app");
        result.Output.Should().Contain("freex-42-1-osx-x64-macos-app");
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticAggregateReadinessArtifactWithExpectedRun()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        CreateSyntheticAggregateReadinessArtifact(temp.Path, arm64, x64);

        var result = RunPreflight(
            temp.Path,
            "-ExpectedRunId 42 -ExpectedRunAttempt 1 -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
        result.Output.Should().Contain("freex-42-1-macos-preview-readiness");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenExpectedAggregateReadinessArtifactIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false, includeDiagnosticsArtifact: true);

        var result = RunPreflight(temp.Path, "-RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact");

        AssertPreflightRejected(
            result,
            "macos-preview-readiness-manifest.json",
            "macos-preview-readiness-summary.txt");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenAggregateReadinessArtifactIsFlattened()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        var aggregateDirectory = CreateSyntheticAggregateReadinessArtifact(temp.Path, arm64, x64);
        foreach (var file in Directory.EnumerateFiles(aggregateDirectory))
        {
            File.Move(file, Path.Combine(temp.Path, Path.GetFileName(file)), overwrite: true);
        }

        Directory.Delete(aggregateDirectory);

        var result = RunPreflight(temp.Path, "-RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact");

        AssertPreflightRejected(
            result,
            "macOS aggregate readiness artifact does not preserve a GitHub Actions artifact wrapper directory",
            "freex-<run-id>-<run-attempt>-macos-preview-readiness",
            "Do not flatten aggregate readiness files");
    }

    [Theory]
    [InlineData("manifest", "\"run_id\": \"42\"", "\"run_id\": \"41\"", "macOS aggregate readiness manifest JSON property 'run_id' must be '42'")]
    [InlineData("summary", "run_attempt=1", "run_attempt=2", "macOS aggregate readiness summary must include 'run_attempt=1'")]
    public void ReadinessPreflight_FailsWhenAggregateReadinessUsesStaleRunIdentity(
        string target,
        string oldValue,
        string newValue,
        string expectedNeedle)
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false, includeDiagnosticsArtifact: true);
        var aggregateDirectory = CreateSyntheticAggregateReadinessArtifact(temp.Path, arm64, x64);
        var fileName = target == "manifest"
            ? "macos-preview-readiness-manifest.json"
            : "macos-preview-readiness-summary.txt";
        ReplaceInFile(Path.Combine(aggregateDirectory, fileName), oldValue, newValue);

        var result = RunPreflight(
            temp.Path,
            "-ExpectedRunId 42 -ExpectedRunAttempt 1 -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact");

        AssertPreflightRejected(result, expectedNeedle);
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticDistributionCandidateBundlesWithDiagnosticsArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true, includeDiagnosticsArtifact: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true, includeDiagnosticsArtifact: true);

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireSeparateDiagnosticsArtifact");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
    }

    [Fact]
    public void ReadinessPreflight_PassesForSyntheticDistributionCandidateBundlesWithReleasePublicationArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        CreateSyntheticReleasePublicationArtifact(temp.Path, arm64, x64);

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireReleasePublicationArtifact");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS public-preview evidence preflight passed");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenReleasePublicationArtifactIsFlattened()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        var releaseDirectory = CreateSyntheticReleasePublicationArtifact(temp.Path, arm64, x64);
        foreach (var file in Directory.EnumerateFiles(releaseDirectory))
        {
            File.Move(file, Path.Combine(temp.Path, Path.GetFileName(file)), overwrite: true);
        }

        Directory.Delete(releaseDirectory);

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireReleasePublicationArtifact");

        AssertPreflightRejected(
            result,
            "macOS release publication artifact does not preserve a GitHub Actions artifact wrapper directory",
            "freex-<run-id>-<run-attempt>-macos-release-assets",
            "Do not flatten release assets");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenReleasePublicationManifestAndInstructionsAreSplitAcrossWrappers()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        var releaseDirectory = CreateSyntheticReleasePublicationArtifact(temp.Path, arm64, x64);
        var staleReleaseDirectory = Path.Combine(temp.Path, "freex-41-1-macos-release-assets");
        Directory.CreateDirectory(staleReleaseDirectory);

        const string instructionsName = "FreeX-latest-macos-distribution-candidate-instructions.md";
        File.Move(
            Path.Combine(releaseDirectory, instructionsName),
            Path.Combine(staleReleaseDirectory, instructionsName));

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireReleasePublicationArtifact");

        AssertPreflightRejected(
            result,
            "macOS release publication manifest and instructions must be in the same downloaded",
            "release-assets wrapper directory",
            "freex-42-1-macos-release-assets",
            "freex-41-1-macos-release-assets",
            "cleanup_action=remove_split_or_stale_release_assets");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenReleasePublicationWrapperUsesStaleRunIdentity()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        var releaseDirectory = CreateSyntheticReleasePublicationArtifact(temp.Path, arm64, x64);
        var staleReleaseDirectory = Path.Combine(temp.Path, "freex-41-1-macos-release-assets");
        Directory.Move(releaseDirectory, staleReleaseDirectory);

        var result = RunPreflight(
            temp.Path,
            "-DistributionCandidate -RequireReleasePublicationArtifact -ExpectedRunId 42 -ExpectedRunAttempt 1");

        AssertPreflightRejected(
            result,
            "macOS release publication artifact is from GitHub Actions run '41', expected run '42'",
            "freex-41-1-macos-release-assets",
            "release_assets_artifact");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenReleasePublicationArtifactUsesStaleRunIdentity()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        var releaseDirectory = CreateSyntheticReleasePublicationArtifact(temp.Path, arm64, x64);
        var manifestPath = Path.Combine(releaseDirectory, "FreeX-latest-macos-distribution-candidate-manifest.json");
        ReplaceInFile(manifestPath, "\"run_id\": \"42\"", "\"run_id\": \"41\"");
        ReplaceInFile(manifestPath, "freex-42-1-*-macos-app", "freex-41-1-*-macos-app");
        ReplaceInFile(
            Path.Combine(releaseDirectory, "FreeX-latest-macos-arm64-evidence.txt"),
            "github_run_id=42",
            "github_run_id=41");

        var result = RunPreflight(
            temp.Path,
            "-DistributionCandidate -RequireReleasePublicationArtifact -ExpectedRunId 42 -ExpectedRunAttempt 1");

        AssertPreflightRejected(
            result,
            "macOS release publication manifest JSON property 'run_id' must be '42'",
            "source_artifact_pattern",
            "osx-arm64 release publication evidence asset must include 'github_run_id=42'");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenReleasePublicationStableZipHashIsStale()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        var releaseDirectory = CreateSyntheticReleasePublicationArtifact(temp.Path, arm64, x64);
        File.AppendAllText(Path.Combine(releaseDirectory, "FreeX-latest-macos-arm64.zip"), "corrupt");

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireReleasePublicationArtifact");

        AssertPreflightRejected(
            result,
            "osx-arm64 release publication manifest asset sha256 must match stable ZIP",
            "osx-arm64 release publication checksum hash must match stable ZIP",
            "FreeX-latest-macos-arm64.zip");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenSeparateDiagnosticsArtifactUsesStaleRunIdentity()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        CreateSyntheticDiagnosticsArtifact(temp.Path, arm64, runId: "41");
        CreateSyntheticDiagnosticsArtifact(temp.Path, x64);

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireSeparateDiagnosticsArtifact");

        AssertPreflightRejected(
            result,
            "osx-arm64 diagnostics artifact is from GitHub Actions run '41' attempt '1'",
            "osx-arm64 app",
            "artifact is from run '42' attempt '1'",
            "Remove stale",
            "artifact",
            "folders");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDistributionCandidateLacksSigningEvidence()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        ReplaceInFile(arm64.EvidencePath, "codesign_mode=developer-id", "codesign_mode=ad-hoc");
        ReplaceInFile(arm64.EvidencePath, "notarization_status=accepted", "notarization_status=skipped_missing_credentials");
        ReplaceInFile(arm64.EvidencePath, "stapler_validated=true", "stapler_validated=false");

        var result = RunPreflight(temp.Path, "-DistributionCandidate");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("codesign_mode=developer-id");
        result.CombinedOutput.Should().Contain("notarization_status=accepted");
        result.CombinedOutput.Should().Contain("stapler_validated=true");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDistributionCandidateEvidenceAppendsConflictingDuplicateSigningKey()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        File.AppendAllText(arm64.EvidencePath, Lines("codesign_mode=ad-hoc"));

        var result = RunPreflight(temp.Path, "-DistributionCandidate");

        AssertPreflightRejected(
            result,
            "conflicting duplicate 'codesign_mode' values",
            "codesign_mode=developer-id",
            "ad-hoc",
            "Remove stale or contradictory entries");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDistributionCandidateLacksAcceptedGatekeeperAssessment()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);
        ReplaceInFile(arm64.EvidencePath, "gatekeeper_assessment_exit_code=0", "gatekeeper_assessment_exit_code=3");
        ReplaceInFile(arm64.EvidencePath, "gatekeeper_assessment_status=accepted", "gatekeeper_assessment_status=rejected");
        ReplaceInFile(arm64.EvidencePath, "gatekeeper_assessment_source=Notarized Developer ID", "gatekeeper_assessment_source=unavailable");

        var result = RunPreflight(temp.Path, "-DistributionCandidate");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("gatekeeper_assessment_exit_code=0");
        result.CombinedOutput.Should().Contain("gatekeeper_assessment_status=accepted");
        result.CombinedOutput.Should().Contain("gatekeeper_assessment_source");
        result.CombinedOutput.Should().Contain("Notarized");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenInternalPreviewDoesNotRecordGatekeeperAttempt()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(arm64.EvidencePath, "gatekeeper_assessment_attempted=true", "gatekeeper_assessment_attempted=false");

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("gatekeeper_assessment_attempted=true");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDistributionCandidateKeepsInternalPreviewInstructions()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true, includeInternalPreviewTesterGuidance: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);

        var result = RunPreflight(temp.Path, "-DistributionCandidate");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("must not include internal-preview-only");
        result.CombinedOutput.Should().Contain("guidance");
        result.CombinedOutput.Should().Contain("For artifact_channel=internal-preview");
        result.CombinedOutput.Should().Contain("Control-click or right-click > Open");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenExpectedReleasePublicationArtifactIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: true);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: true);

        var result = RunPreflight(temp.Path, "-DistributionCandidate -RequireReleasePublicationArtifact");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("FreeX-latest-macos-distribution-candidate-manifest.json");
        result.CombinedOutput.Should().Contain("FreeX-latest-macos-distribution-candidate-instructions.md");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenFormatCellsRoundtripCountIsTooLow()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(arm64.EvidencePath, "format_cells_style_roundtrip_count=2", "format_cells_style_roundtrip_count=1");
        File.WriteAllText(
            arm64.PackagingSmokePath,
            Lines(
                "Packaging smoke opened macOS Preview Workbook.",
                "edited, saved, and reopened.",
                "format_cells_style_roundtrip=true"));

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("format_cells_style_roundtrip_count");
        result.CombinedOutput.Should().Contain("at least two Format Cells style roundtrip confirmations");
    }

    [Theory]
    [InlineData("macos_launch_smoke=passed", "macos_launch_smoke=failed", "macos_launch_smoke=passed")]
    [InlineData("opened_source_path=/tmp/freex-osx-arm64-open-with.csv", "opened_source_path=/tmp/freex-osx-arm64-launch.csv", "freex-osx-arm64-open-with\\.csv")]
    public void ReadinessPreflight_FailsWhenOpenWithSmokeEvidenceIsStaleOrWeakened(
        string oldValue,
        string newValue,
        string expectedNeedle)
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(GetRuntimeArtifactPath(arm64, names => names.OpenWithSmoke), oldValue, newValue);

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(result, "osx-arm64 Open-With smoke", expectedNeedle);
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDefaultOpenSmokeUsesAppOverride()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(
            GetRuntimeArtifactPath(arm64, names => names.DefaultOpenSmoke),
            "launchservices_default_open_app_override=false",
            "launchservices_default_open_app_override=true");

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(
            result,
            "osx-arm64 .fxl default-open boundary",
            "launchservices_default_open_app_override=false");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenDefaultOpenSmokeOmitsCiLaunchServicesBoundary()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        RemoveLinesContaining(
            GetRuntimeArtifactPath(arm64, names => names.DefaultOpenSmoke),
            "launchservices_default_open_boundary=");

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(
            result,
            "osx-arm64 .fxl default-open boundary",
            "launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click");
    }

    [Theory]
    [InlineData("live_command_key_smoke=passed", "live_command_key_smoke=failed", "live_command_key_smoke=passed")]
    [InlineData("live_cmd_select_all_state_changed=true", "live_cmd_select_all_state_changed=false", "live_cmd_select_all_state_changed=true")]
    [InlineData("live_cmd_bold_state_changed=true", "live_cmd_bold_state_changed=false", "live_cmd_bold_state_changed=true")]
    [InlineData("live_cmd_italic_state_changed=true", "live_cmd_italic_state_changed=false", "live_cmd_italic_state_changed=true")]
    [InlineData("live_cmd_underline_state_changed=true", "live_cmd_underline_state_changed=false", "live_cmd_underline_state_changed=true")]
    public void ReadinessPreflight_FailsWhenExplicitLiveCommandKeySmokeEvidenceIsStaleOrWeakened(
        string oldValue,
        string newValue,
        string expectedNeedle)
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(GetRuntimeArtifactPath(arm64, names => names.LaunchSmoke), oldValue, newValue);

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(result, "osx-arm64 command key smoke", expectedNeedle);
    }

    [Theory]
    [InlineData("command_key_smoke=passed", "command_key_smoke=failed", "command_key_smoke=passed")]
    [InlineData("command_key_smoke_attempted=true", "command_key_smoke_attempted=false", "command_key_smoke_attempted=true")]
    [InlineData("cmd_find_direct_route_source_guard=true", "cmd_find_direct_route_source_guard=false", "cmd_find_direct_route_source_guard=true")]
    [InlineData("cmd_bold_menu_gesture=true", "cmd_bold_menu_gesture=false", "cmd_bold_menu_gesture=true")]
    public void ReadinessPreflight_FailsWhenHostedCommandKeySmokeLosesNonLiveProof(
        string oldValue,
        string newValue,
        string expectedNeedle)
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        var x64 = CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        MarkLiveCommandKeySmokeNotRequired(arm64);
        MarkLiveCommandKeySmokeNotRequired(x64);
        ReplaceInFile(GetRuntimeArtifactPath(arm64, names => names.LaunchSmoke), oldValue, newValue);

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(result, "osx-arm64 command key smoke", expectedNeedle);
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenCommandKeySmokeAppendsConflictingDuplicateStatus()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        File.AppendAllText(
            GetRuntimeArtifactPath(arm64, names => names.LaunchSmoke),
            Lines("live_command_key_smoke=failed"));

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(
            result,
            "osx-arm64 command key smoke",
            "conflicting duplicate 'live_command_key_smoke' values",
            "live_command_key_smoke=passed",
            "failed",
            "Remove stale or contradictory entries");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenChecksumDoesNotMatchZip()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        File.AppendAllText(arm64.ZipPath, "corrupt");

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("checksum file hash must match");
        result.CombinedOutput.Should().Contain("zip_sha256");
    }

    [Fact]
    public void ReadinessPreflight_FailsWithClearMessageWhenEvidenceFileIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        File.Delete(arm64.EvidencePath);

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("osx-arm64 app artifact is incomplete");
        result.CombinedOutput.Should().Contain("freex-osx-arm64-macos-evidence.txt");
        result.CombinedOutput.Should().Contain("GitHub Actions artifact wrapper first");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenRuntimeHasStaleDuplicateDownloadedAppArtifacts()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false, runId: "41");
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("osx-arm64 has multiple downloaded macOS app artifact bundles");
        result.CombinedOutput.Should().Contain("freex-41-1-osx-arm64-macos-app");
        result.CombinedOutput.Should().Contain("freex-42-1-osx-arm64-macos-app");
        result.CombinedOutput.Should().Contain("Remove stale");
        result.CombinedOutput.Should().Contain("artifact");
        result.CombinedOutput.Should().Contain("folders");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenAppEvidenceUsesStaleRunAttempt()
    {
        using var temp = new TestTemporaryDirectory();
        var arm64 = CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false);
        ReplaceInFile(arm64.EvidencePath, "github_run_attempt=1", "github_run_attempt=2");

        var result = RunPreflight(temp.Path);

        AssertPreflightRejected(
            result,
            "osx-arm64 evidence GitHub Actions identity",
            "github_run_attempt=1",
            "Actual value(s):",
            "2.");
    }

    [Fact]
    public void ReadinessPreflight_FailsWhenRuntimeArtifactsComeFromMixedRuns()
    {
        using var temp = new TestTemporaryDirectory();
        CreateSyntheticBundle(temp.Path, "osx-arm64", distributionCandidate: false);
        CreateSyntheticBundle(temp.Path, "osx-x64", distributionCandidate: false, runId: "41");

        var result = RunPreflight(temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("mixed GitHub Actions runs");
        result.CombinedOutput.Should().Contain("osx-arm64 uses run 42");
        result.CombinedOutput.Should().Contain("osx-x64 uses run 41");
        result.CombinedOutput.Should().Contain("cleanup_action=remove_stale_artifact_folders");
    }

    private static PowerShellResult RunPreflight(string artifactRoot, string arguments = "")
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        return PowerShellScriptRunner.RunToolScript(
            "Test-MacOsPublicPreviewReadiness.ps1",
            repoRoot,
            $"-ArtifactRoot \"{artifactRoot}\" {arguments}");
    }

    private static void AssertDistributionCandidatePreflightCommandsRequireReleasePublicationArtifact(
        string document,
        string documentName)
    {
        var commandLines = document
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line =>
                line.Contains("tools/Test-MacOsPublicPreviewReadiness.ps1", StringComparison.Ordinal) &&
                line.Contains("-DistributionCandidate", StringComparison.Ordinal))
            .ToArray();

        commandLines.Should().NotBeEmpty($"{documentName} should document public-preview distribution-candidate preflight usage");
        commandLines.Should().OnlyContain(
            line => line.Contains("-RequireReleasePublicationArtifact", StringComparison.Ordinal),
            $"{documentName} must require release-publication artifact validation in distribution-candidate command examples");
    }

    private static SyntheticBundle CreateSyntheticBundle(
        string root,
        string runtime,
        bool distributionCandidate,
        bool includeDiagnosticsArtifact = false,
        bool includeInternalPreviewTesterGuidance = false,
        string runId = "42",
        string runAttempt = "1")
    {
        var names = RuntimeArtifactNames.For(runtime);
        var bundleDirectory = Path.Combine(root, $"freex-{runId}-{runAttempt}-{runtime}-macos-app");
        Directory.CreateDirectory(bundleDirectory);

        var zipPath = Path.Combine(bundleDirectory, names.Zip);
        File.WriteAllText(zipPath, $"Synthetic FreeX.app zip for {runtime}.");
        var zipHash = ComputeSha256(zipPath);
        File.WriteAllText(Path.Combine(bundleDirectory, names.Checksum), $"{zipHash}  {names.Zip}{Environment.NewLine}");

        var channel = distributionCandidate ? "distribution-candidate" : "internal-preview";
        var candidate = distributionCandidate ? "true" : "false";
        var contract = distributionCandidate
            ? "distribution_candidate_requires_developer_id_notarization_stapling"
            : "internal_preview_not_for_distribution_notarization_optional";
        var readiness = distributionCandidate
            ? "distribution_candidate_ready"
            : "internal_preview_not_for_distribution";
        var codesignMode = distributionCandidate ? "developer-id" : "ad-hoc";
        var notarizationStatus = distributionCandidate ? "accepted" : "skipped_missing_credentials";
        var staplerValidated = distributionCandidate ? "true" : "false";
        var gatekeeperAssessmentRequired = distributionCandidate ? "true" : "false";
        var gatekeeperAssessmentExitCode = distributionCandidate ? "0" : "3";
        var gatekeeperAssessmentStatus = distributionCandidate ? "accepted" : "rejected";
        var gatekeeperAssessmentSource = distributionCandidate ? "Notarized Developer ID" : "unavailable";

        var evidenceLines = new List<string>
        {
            $"runtime={runtime}",
            $"github_run_id={runId}",
            $"github_run_attempt={runAttempt}",
            $"artifact_channel={channel}",
            $"distribution_candidate={candidate}",
            $"distribution_contract={contract}",
            $"distribution_readiness={readiness}",
            $"zip_name={names.Zip}",
            "codesign_verified=true",
            $"codesign_mode={codesignMode}",
            $"notarization_status={notarizationStatus}",
            $"stapler_validated={staplerValidated}",
            "gatekeeper_assessment_attempted=true",
            $"gatekeeper_assessment_required={gatekeeperAssessmentRequired}",
            "gatekeeper_assessment_subject=unzipped_app_bundle",
            "gatekeeper_assessment_type=execute",
            $"gatekeeper_assessment_exit_code={gatekeeperAssessmentExitCode}",
            $"gatekeeper_assessment_status={gatekeeperAssessmentStatus}",
            $"gatekeeper_assessment_source={gatekeeperAssessmentSource}",
            $"zip_sha256={zipHash}",
            "format_cells_style_roundtrip=true",
            "format_cells_style_roundtrip_count=2",
            "smoke_status=passed"
        };

        var evidencePath = Path.Combine(bundleDirectory, names.Evidence);
        File.WriteAllText(evidencePath, Lines(evidenceLines.ToArray()));

        var packagingSmokePath = Path.Combine(bundleDirectory, names.PackagingSmoke);
        File.WriteAllText(
            packagingSmokePath,
            Lines(
                "Packaging smoke opened macOS Preview Workbook.",
                "drawing_object_previews=3",
                "roundtrip_drawing_object_previews=3",
                "edited, saved, and reopened.",
                "format_cells_style_roundtrip=true",
                "Packaging smoke opened freex fixture csv.",
                "edited, saved, and reopened.",
                "format_cells_style_roundtrip=true"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.LaunchSmoke),
            Lines(
                "macos_launch_smoke=passed",
                "window_shown=true",
                $"opened_source_path=/tmp/freex-{runtime}-launch.csv",
                "viewport_rows=24",
                "viewport_columns=8",
                "native_open_recent_menu_item=true",
                "native_open_recent_item_count=1",
                "command_key_smoke=passed",
                "command_key_smoke_attempted=true",
                "cmd_new_workbook_menu_gesture=true",
                "cmd_open_menu_gesture=true",
                "cmd_save_menu_gesture=true",
                "cmd_save_as_menu_gesture=true",
                "cmd_close_workbook_menu_gesture=true",
                "cmd_quit_menu_gesture=true",
                "cmd_select_all_menu_gesture=true",
                "cmd_find_menu_gesture=true",
                "cmd_find_direct_route_source_guard=true",
                "cmd_page_up_direct_route_source_guard=true",
                "cmd_page_down_direct_route_source_guard=true",
                "cmd_bold_menu_gesture=true",
                "cmd_italic_menu_gesture=true",
                "cmd_underline_menu_gesture=true",
                "live_command_key_smoke_required=true",
                "live_command_key_smoke=passed",
                "live_command_key_smoke_attempted=true",
                "live_command_key_smoke_ready=true",
                "live_cmd_select_all_state_changed=true",
                "live_cmd_bold_state_changed=true",
                "live_cmd_italic_state_changed=true",
                "live_cmd_underline_state_changed=true"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.OpenWithSmoke),
            Lines(
                "macos_launch_smoke=passed",
                "window_shown=true",
                $"opened_source_path=/tmp/freex-{runtime}-open-with.csv",
                "viewport_rows=24",
                "viewport_columns=8",
                "native_open_recent_menu_item=true",
                "native_open_recent_item_count=1"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.DefaultOpenSmoke),
            Lines(
                "macos_launch_smoke=passed",
                "window_shown=true",
                $"opened_source_path=/tmp/freex-{runtime}-default-open.fxl",
                "viewport_rows=24",
                "viewport_columns=8",
                "native_open_recent_menu_item=true",
                "native_open_recent_item_count=1",
                "launchservices_default_open_attempted=true",
                "launchservices_default_open_app_override=false",
                "launchservices_default_open_document_extension=fxl",
                "launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click"));

        File.WriteAllText(
            Path.Combine(bundleDirectory, names.NotarizationLog),
            distributionCandidate
                ? Lines(
                    "artifact_channel=distribution-candidate",
                    "distribution_candidate=true",
                    $"distribution_contract={contract}",
                    "{\"status\":\"Accepted\"}",
                    "xcrun stapler validate FreeX.app")
                : Lines(
                    "artifact_channel=internal-preview",
                    "distribution_candidate=false",
                    $"distribution_contract={contract}",
                    "notarization_status=skipped_missing_credentials"));

        var instructionLines = new List<string>
        {
            $"# FreeX macOS App ({channel}, {runtime})",
            distributionCandidate
                ? "This distribution-candidate artifact is for public-preview validation and must show Developer ID signing, accepted notarization, and stapling evidence."
                : "This artifact is a macOS port validation build. Internal-preview artifacts are not a public release channel.",
            $"Download {names.Zip}, {names.Checksum}, {names.Evidence}, {names.PackagingSmoke}, {names.LaunchSmoke}, {names.OpenWithSmoke}, {names.DefaultOpenSmoke}, {names.NotarizationLog}.",
            $"Run shasum -a 256 -c {names.Checksum}.",
            $"artifact_channel={channel}",
            $"distribution_readiness={readiness}",
            $"codesign_mode={codesignMode}",
            $"notarization_status={notarizationStatus}",
            $"stapler_validated={staplerValidated}",
            $"gatekeeper_assessment_status={gatekeeperAssessmentStatus}",
            $"gatekeeper_assessment_source={gatekeeperAssessmentSource}",
            $"zip_sha256={zipHash}"
        };

        if (distributionCandidate)
        {
            instructionLines.Add("If artifact_channel=distribution-candidate, reject the artifact unless it has Developer ID signing, accepted notarization, stapling evidence, and gatekeeper_assessment_status=accepted from gatekeeper_assessment_source=Notarized Developer ID.");
        }

        if (!distributionCandidate || includeInternalPreviewTesterGuidance)
        {
            instructionLines.Add("For artifact_channel=internal-preview: This artifact is a preview build for macOS port validation. It is not a public release channel.");
            instructionLines.Add("For artifact_channel=internal-preview: Ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.");
        }

        File.WriteAllText(Path.Combine(bundleDirectory, names.TesterInstructions), Lines(instructionLines.ToArray()));

        if (includeDiagnosticsArtifact)
        {
            CreateSyntheticDiagnosticsArtifact(root, new SyntheticBundle(runtime, bundleDirectory, zipPath, evidencePath, packagingSmokePath), runId, runAttempt);
        }

        return new SyntheticBundle(
            runtime,
            bundleDirectory,
            zipPath,
            evidencePath,
            packagingSmokePath);
    }

    private static string CreateSyntheticDiagnosticsArtifact(
        string root,
        SyntheticBundle bundle,
        string runId = "42",
        string runAttempt = "1")
    {
        var diagnosticsDirectory = Path.Combine(root, $"freex-{runId}-{runAttempt}-{bundle.Runtime}-macos-diagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);
        foreach (var file in Directory.EnumerateFiles(bundle.BundleDirectory))
        {
            File.Copy(file, Path.Combine(diagnosticsDirectory, Path.GetFileName(file)), overwrite: true);
        }

        return diagnosticsDirectory;
    }

    private static string CreateSyntheticAggregateReadinessArtifact(string root, params SyntheticBundle[] bundles)
    {
        var aggregateDirectory = Path.Combine(root, "freex-42-1-macos-preview-readiness");
        Directory.CreateDirectory(aggregateDirectory);

        var runtimeEntries = new List<Dictionary<string, object?>>();
        var summaryLines = new List<string>
        {
            "macos_preview_readiness=passed",
            "run_id=42",
            "run_attempt=1",
            "source_artifact_pattern=freex-42-1-osx-*-macos-*"
        };

        foreach (var bundle in bundles)
        {
            var names = RuntimeArtifactNames.For(bundle.Runtime);
            var zipHash = ComputeSha256(bundle.ZipPath);
            var appArtifact = $"freex-42-1-{bundle.Runtime}-macos-app";
            var diagnosticsArtifact = $"freex-42-1-{bundle.Runtime}-macos-diagnostics";
            runtimeEntries.Add(new Dictionary<string, object?>
            {
                ["runtime"] = bundle.Runtime,
                ["app_artifact"] = appArtifact,
                ["app_artifact_digest"] = $"sha256:{zipHash}",
                ["diagnostics_artifact"] = diagnosticsArtifact,
                ["diagnostics_artifact_digest"] = $"sha256:{zipHash}",
                ["zip_sha256"] = zipHash,
                ["checksum_file"] = $"{zipHash}  {names.Zip}",
                ["evidence_file"] = names.Evidence,
                ["evidence_markers"] = new Dictionary<string, string>
                {
                    ["github_run_id"] = "42",
                    ["github_run_attempt"] = "1",
                    ["artifact_channel"] = "internal-preview",
                    ["distribution_candidate"] = "false",
                    ["distribution_readiness"] = "internal_preview_not_for_distribution",
                    ["smoke_status"] = "passed",
                    ["codesign_mode"] = "ad-hoc",
                    ["notarization_status"] = "skipped_missing_credentials",
                    ["stapler_validated"] = "false",
                    ["gatekeeper_assessment_status"] = "rejected",
                    ["gatekeeper_assessment_source"] = "unavailable",
                    ["zip_sha256"] = zipHash
                }
            });

            summaryLines.Add($"runtime={bundle.Runtime}");
            summaryLines.Add($"app_artifact={appArtifact}");
            summaryLines.Add($"app_artifact_digest=sha256:{zipHash}");
            summaryLines.Add($"diagnostics_artifact={diagnosticsArtifact}");
            summaryLines.Add($"diagnostics_artifact_digest=sha256:{zipHash}");
            summaryLines.Add($"zip_sha256={zipHash}");
            summaryLines.Add("artifact_channel=internal-preview");
            summaryLines.Add("distribution_readiness=internal_preview_not_for_distribution");
            summaryLines.Add("smoke_status=passed");
        }

        var manifest = new Dictionary<string, object?>
        {
            ["schema"] = "io.github.tony-xmelon.freex.macos-preview-readiness.v1",
            ["repository"] = "tony-xmelon/FreeX",
            ["workflow"] = "macOS App Preview",
            ["run_id"] = "42",
            ["run_attempt"] = "1",
            ["commit"] = "0123abcdef0123456789abcdef0123456789abcd",
            ["generated_at_utc"] = "2026-06-08T00:00:00.0000000Z",
            ["source_artifact_pattern"] = "freex-42-1-osx-*-macos-*",
            ["readiness_script"] = "tools/Test-MacOsPublicPreviewReadiness.ps1",
            ["require_separate_diagnostics_artifact"] = true,
            ["runtimes"] = runtimeEntries
        };

        File.WriteAllText(
            Path.Combine(aggregateDirectory, "macos-preview-readiness-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(
            Path.Combine(aggregateDirectory, "macos-preview-readiness-summary.txt"),
            Lines(summaryLines.ToArray()));

        return aggregateDirectory;
    }

    private static string CreateSyntheticReleasePublicationArtifact(string root, params SyntheticBundle[] bundles)
    {
        var releaseDirectory = Path.Combine(root, "freex-42-1-macos-release-assets");
        Directory.CreateDirectory(releaseDirectory);

        var assets = new List<Dictionary<string, string>>();
        foreach (var bundle in bundles)
        {
            var names = RuntimeArtifactNames.For(bundle.Runtime);
            var assetLabel = bundle.Runtime == "osx-arm64" ? "macos-arm64" : "macos-x64";
            var stableZip = bundle.Runtime == "osx-arm64"
                ? "FreeX-latest-macos-arm64.zip"
                : "FreeX-latest-macos-x64.zip";
            var stableZipPath = Path.Combine(releaseDirectory, stableZip);
            File.Copy(bundle.ZipPath, stableZipPath, overwrite: true);
            var stableZipHash = ComputeSha256(stableZipPath);
            File.WriteAllText(Path.Combine(releaseDirectory, $"{stableZip}.sha256"), $"{stableZipHash}  {stableZip}{Environment.NewLine}");

            var stableEvidence = CopyReleaseAsset(bundle, names.Evidence, releaseDirectory, $"FreeX-latest-{assetLabel}-evidence.txt");
            var stablePackagingSmoke = CopyReleaseAsset(bundle, names.PackagingSmoke, releaseDirectory, $"FreeX-latest-{assetLabel}-packaging-smoke.log");
            var stableLaunchSmoke = CopyReleaseAsset(bundle, names.LaunchSmoke, releaseDirectory, $"FreeX-latest-{assetLabel}-launch-smoke.txt");
            var stableOpenWithSmoke = CopyReleaseAsset(bundle, names.OpenWithSmoke, releaseDirectory, $"FreeX-latest-{assetLabel}-open-with-launch-smoke.txt");
            var stableDefaultOpenSmoke = CopyReleaseAsset(bundle, names.DefaultOpenSmoke, releaseDirectory, $"FreeX-latest-{assetLabel}-default-open-launch-smoke.txt");
            var stableNotarization = CopyReleaseAsset(bundle, names.NotarizationLog, releaseDirectory, $"FreeX-latest-{assetLabel}-notarization.log");
            var stableInstructions = CopyReleaseAsset(bundle, names.TesterInstructions, releaseDirectory, $"FreeX-latest-{assetLabel}-tester-instructions.md");

            assets.Add(new Dictionary<string, string>
            {
                ["runtime"] = bundle.Runtime,
                ["asset_label"] = assetLabel,
                ["original_zip"] = names.Zip,
                ["stable_zip"] = stableZip,
                ["stable_zip_checksum"] = $"{stableZip}.sha256",
                ["sha256"] = stableZipHash,
                ["evidence"] = stableEvidence,
                ["packaging_smoke_log"] = stablePackagingSmoke,
                ["launch_smoke_report"] = stableLaunchSmoke,
                ["open_with_launch_smoke_report"] = stableOpenWithSmoke,
                ["default_open_launch_smoke_report"] = stableDefaultOpenSmoke,
                ["notarization_log"] = stableNotarization,
                ["tester_instructions"] = stableInstructions
            });
        }

        var manifest = new Dictionary<string, object?>
        {
            ["schema"] = "io.github.tony-xmelon.freex.macos-distribution-candidate.v1",
            ["release_id"] = "macos-distribution-candidate-run42-attempt1-0123abcd",
            ["tag"] = "macos-distribution-candidate-42-1-0123abcd",
            ["repository"] = "tony-xmelon/FreeX",
            ["workflow"] = "macOS App Preview",
            ["run_id"] = "42",
            ["run_attempt"] = "1",
            ["release_assets_artifact"] = "freex-42-1-macos-release-assets",
            ["commit"] = "0123abcdef0123456789abcdef0123456789abcd",
            ["generated_at_utc"] = "2026-06-08T00:00:00.0000000Z",
            ["source_artifact_pattern"] = "freex-42-1-*-macos-app",
            ["distribution_candidate_required_markers"] = new[]
            {
                "artifact_channel=distribution-candidate",
                "distribution_candidate=true",
                "distribution_readiness=distribution_candidate_ready",
                "codesign_mode=developer-id",
                "notarization_status=accepted",
                "stapler_validated=true",
                "gatekeeper_assessment_attempted=true",
                "gatekeeper_assessment_required=true",
                "gatekeeper_assessment_exit_code=0",
                "gatekeeper_assessment_status=accepted",
                "gatekeeper_assessment_source=Notarized Developer ID"
            },
            ["assets"] = assets
        };

        File.WriteAllText(
            Path.Combine(releaseDirectory, "FreeX-latest-macos-distribution-candidate-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(
            Path.Combine(releaseDirectory, "FreeX-latest-macos-distribution-candidate-instructions.md"),
            Lines(
                "# FreeX macOS Distribution Candidate",
                "Published release assets include FreeX-latest-macos-arm64.zip and FreeX-latest-macos-x64.zip.",
                "Preserve the freex-42-1-macos-release-assets Actions artifact wrapper with this manifest and instructions.",
                "Use FreeX-latest-macos-distribution-candidate-manifest.json to verify the release asset set.",
                "Each runtime includes default-open launch smoke, evidence, notarization, and tester instruction assets.",
                "Reject the distribution-candidate unless Developer ID signing, accepted notarization, stapler validation, Gatekeeper, and gatekeeper_assessment_status=accepted are present."));

        return releaseDirectory;
    }

    private static string CopyReleaseAsset(SyntheticBundle bundle, string sourceName, string releaseDirectory, string destinationName)
    {
        File.Copy(Path.Combine(bundle.BundleDirectory, sourceName), Path.Combine(releaseDirectory, destinationName), overwrite: true);
        return destinationName;
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ReplaceInFile(string path, string oldValue, string newValue)
    {
        var text = File.ReadAllText(path);
        text.Should().Contain(oldValue);
        File.WriteAllText(path, text.Replace(oldValue, newValue));
    }

    private static void RemoveLinesContaining(string path, string value)
    {
        var lines = File.ReadAllLines(path);
        lines.Should().Contain(line => line.Contains(value, StringComparison.Ordinal));
        File.WriteAllLines(path, lines.Where(line => !line.Contains(value, StringComparison.Ordinal)));
    }

    private static void MarkLiveCommandKeySmokeNotRequired(SyntheticBundle bundle)
    {
        var launchSmokePath = GetRuntimeArtifactPath(bundle, names => names.LaunchSmoke);
        ReplaceInFile(launchSmokePath, "live_command_key_smoke_required=true", "live_command_key_smoke_required=false");
        ReplaceInFile(launchSmokePath, "live_command_key_smoke=passed", "live_command_key_smoke=not_required");
        ReplaceInFile(launchSmokePath, "live_command_key_smoke_attempted=true", "live_command_key_smoke_attempted=false");
        ReplaceInFile(launchSmokePath, "live_command_key_smoke_ready=true", "live_command_key_smoke_ready=false");
        ReplaceInFile(launchSmokePath, "live_cmd_select_all_state_changed=true", "live_cmd_select_all_state_changed=false");
        ReplaceInFile(launchSmokePath, "live_cmd_bold_state_changed=true", "live_cmd_bold_state_changed=false");
        ReplaceInFile(launchSmokePath, "live_cmd_italic_state_changed=true", "live_cmd_italic_state_changed=false");
        ReplaceInFile(launchSmokePath, "live_cmd_underline_state_changed=true", "live_cmd_underline_state_changed=false");
    }

    private static string GetRuntimeArtifactPath(SyntheticBundle bundle, Func<RuntimeArtifactNames, string> selectName) =>
        Path.Combine(bundle.BundleDirectory, selectName(RuntimeArtifactNames.For(bundle.Runtime)));

    private static void AssertPreflightRejected(PowerShellResult result, params string[] expectedNeedles)
    {
        result.ExitCode.Should().NotBe(0);
        foreach (var needle in expectedNeedles)
        {
            result.CombinedOutput.Should().Contain(needle);
        }
    }

    private sealed record SyntheticBundle(
        string Runtime,
        string BundleDirectory,
        string ZipPath,
        string EvidencePath,
        string PackagingSmokePath);

    private sealed record RuntimeArtifactNames(
        string Zip,
        string Checksum,
        string Evidence,
        string PackagingSmoke,
        string LaunchSmoke,
        string OpenWithSmoke,
        string DefaultOpenSmoke,
        string NotarizationLog,
        string TesterInstructions)
    {
        public static RuntimeArtifactNames For(string runtime) =>
            new(
                $"freex-{runtime}-macos-app.zip",
                $"freex-{runtime}-macos-app.zip.sha256",
                $"freex-{runtime}-macos-evidence.txt",
                $"freex-{runtime}-macos-packaging-smoke.log",
                $"freex-{runtime}-macos-launch-smoke.txt",
                $"freex-{runtime}-macos-open-with-launch-smoke.txt",
                $"freex-{runtime}-macos-default-open-launch-smoke.txt",
                $"freex-{runtime}-macos-notarization.log",
                $"freex-{runtime}-macos-tester-instructions.md");
    }
}
