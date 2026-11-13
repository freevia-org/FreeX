using System;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TestDistributionPlanTests
{
    [Fact]
    public void DistributionPlan_MarksImplementedDistributionPhasesComplete()
    {
        var source = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");

        source.Should().Contain("| 4. Hosted release channel | Complete |");
        source.Should().Contain("| 5. Crash analytics | Complete |");
        source.Should().Contain("| 6. Lightweight usage analytics | Complete |");
        source.Should().Contain("| 7. Auto-update readiness | Complete |");
        source.Should().Contain("Velopack packaging and the in-app update check/download/apply-and-restart flow are implemented");
        source.Should().Contain("Remaining follow-up work is release-process hardening");
    }

    [Fact]
    public void DistributionPlan_DocumentsPhaseSixUsageAnalyticsContract()
    {
        var source = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");

        source.Should().Contain("6. Lightweight usage analytics");
        source.Should().Contain("app lifecycle");
        source.Should().Contain("command/dialog opened");
        source.Should().Contain("file import/export type");
        source.Should().Contain("crash/session linkage");
        source.Should().Contain("workbook contents, formulas, filenames, or paths");
        source.Should().Contain("exception messages and stack traces can occasionally contain sensitive values");
        source.Should().Contain("FREEX_DIAGNOSTICS=0");
    }

    [Fact]
    public void DistributionPlan_DocumentsPhaseSevenAutoUpdateReadiness()
    {
        var source = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");

        source.Should().Contain("7. Auto-update readiness");
        source.Should().Contain("Help > Check for Updates");
        source.Should().Contain("stable latest release page");
        source.Should().Contain("Velopack");
        source.Should().Contain("`Program.Main` runs `VelopackBootstrap.Configure().Run()`");
        source.Should().Contain("to check, download, and apply updates with a restart");
        source.Should().Contain("the plain `FreeX-latest-win-x64.exe` single-file build and MSIX installs are not Velopack-managed");
    }

    [Fact]
    public void DistributionPlan_DocumentsManifestDrivenCommitAndReleaseGates()
    {
        var source = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");

        source.Should().Contain("## Branch Build And Main Integration Verification");
        source.Should().Contain("tools/Test-RepositoryPreflight.ps1");
        source.Should().Contain("dotnet build FreeX.slnx --configuration Release");
        source.Should().Contain("manifest-driven integration");
        source.Should().Contain("immutable candidate");
        source.Should().Contain("without repeating");
        source.Should().Contain("## Conservative Rerun Fallback");
        source.Should().Contain("--disable-build-servers");
        source.Should().Contain("-p:UseSharedCompilation=false");
        source.Should().Contain("-p:NodeReuse=false");
        source.Should().Contain("/nr:false");
        source.Should().Contain("-m:1");
        source.Should().Contain("after clearing stale processes");
    }

    [Fact]
    public void DistributionPlan_DocumentsMacOsPreviewChecksumAndTesterInstructions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));

        source.Should().Contain("macOS App Preview");
        source.Should().Contain("[release/macos-signing-notarization.md](macos-signing-notarization.md)");
        source.Should().Contain("self-checks each SHA-256 file with `shasum -a 256 -c`");
        source.Should().Contain("records `zip_sha256` in evidence");
        source.Should().Contain("separate diagnostics artifacts");
        source.Should().Contain("post-matrix aggregate readiness artifact");
        source.Should().Contain("not a public release channel");
        source.Should().Contain("GitHub-hosted macOS runners can produce downloadable macOS app artifacts without local macOS hardware");
        source.Should().Contain("GitHub Actions > `macOS App Preview` > the completed run");
        source.Should().Contain("freex-<run-id>-<run-attempt>-osx-arm64-macos-app");
        source.Should().Contain("freex-<run-id>-<run-attempt>-osx-arm64-macos-diagnostics");
        source.Should().Contain("freex-<run-id>-<run-attempt>-osx-x64-macos-app");
        source.Should().Contain("freex-<run-id>-<run-attempt>-osx-x64-macos-diagnostics");
        source.Should().Contain("freex-<run-id>-<run-attempt>-macos-preview-readiness");
        source.Should().Contain("Actions artifact archive downloads require authentication");
        source.Should().Contain("browser session signed in to GitHub");
        source.Should().Contain("gh run download");
        source.Should().Contain("GITHUB_TOKEN");
        source.Should().Contain("Internal-preview workflow outputs can also be bundled into the normal `Tester Release` GitHub Release when `include_macos_preview=true`");
        source.Should().Contain("FreeX-latest-macos-arm64.zip");
        source.Should().Contain("FreeX-latest-macos-x64.zip");
        source.Should().Contain("FreeX-latest-macos-<runtime>-instructions.md");
        source.Should().Contain("FreeX-latest-macos-<runtime>-evidence.txt");
        source.Should().Contain("Distribution-candidate dispatches also run a guarded publication job");
        source.Should().Contain("Signed and internal ad-hoc outputs use the same artifact names");
        source.Should().Contain("Use `osx-arm64` for Apple Silicon Macs and `osx-x64` for Intel Macs");
        source.Should().Contain("Actions artifact wrapper");
        source.Should().Contain("Preserve the `freex-<run-id>-<run-attempt>-<runtime>-macos-app`, `freex-<run-id>-<run-attempt>-<runtime>-macos-diagnostics`, and `freex-<run-id>-<run-attempt>-macos-release-assets` wrapper directory names under the artifact root");
        source.Should().Contain("Do not flatten wrapper contents directly into the artifact root");
        source.Should().Contain("the post-matrix `macos-preview-readiness` job downloads the current run's app and diagnostics artifact wrappers");
        source.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1 -RequireSeparateDiagnosticsArtifact");
        source.Should().Contain("Keep `-RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact` on downloaded hosted evidence validation");
        source.Should().Contain("-ExpectedRunAttempt <run-attempt> -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact");
        source.Should().Contain("macos-preview-readiness-manifest.json");
        source.Should().Contain("macos-preview-readiness-summary.txt");
        source.Should().Contain("app and diagnostics artifact names and digests");
        source.Should().Contain("It is not a public distribution channel");
        source.Should().Contain("Gatekeeper assessment and first-launch evidence");
        source.Should().Contain("-ExpectedRunId <run-id>");
        source.Should().Contain("-ExpectedRunAttempt <run-attempt>");
        source.Should().Contain("freex-<runtime>-macos-tester-instructions.md");
        source.Should().Contain("freex-<runtime>-macos-evidence.txt");
        source.Should().Contain("shasum -a 256 -c freex-<runtime>-macos-app.zip.sha256");
        source.Should().Contain("open `FreeX.app`");
        source.Should().Contain("open a `.fxl` document without an app override as CI-verifiable default-open evidence");
        source.Should().Contain("Human validation of Finder double-click open, Gatekeeper prompts, basic workbook workflows");
        source.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
        source.Should().Contain("validate the completed release-record copy");
        source.Should().Contain("codesign_mode");
        source.Should().Contain("notarization_status");
        source.Should().Contain("stapler_validated");
        source.Should().Contain("zip_sha256");
        source.Should().Contain("Control-click or right-click > Open");
        source.Should().Contain("System Settings > Privacy & Security");
        source.Should().Contain("Open Anyway");
        source.Should().Contain("Do not disable Gatekeeper globally");
        source.Should().Contain("Tester-facing warning: Windows direct-download executables are signed by Freevia and timestamped by Microsoft.");
        source.Should().Contain("Public distribution still requires Developer ID signing, accepted notarization, stapling, and Gatekeeper evidence.");
    }



    [Fact]
    public void TesterReleaseChecklist_CapturesReleaseAndAccessibilityGateEvidence()
    {
        var source = WorkspaceFileLocator.ReadAllText("docs", "release/tester-release-checklist.md");

        source.Should().Contain("Full Signed Release");
        source.Should().Contain("exact-SHA CI and CodeQL");
        source.Should().Contain("standalone executables");
        source.Should().Contain("Velopack");
        source.Should().Contain("Free Suite bootstrapper");
        source.Should().Contain("checksums, SBOMs, and manifests");
        source.Should().Contain("Keyboard-only smoke validation");
        source.Should().Contain("Screen-reader smoke validation");
        source.Should().Contain("UI Automation names, IDs, patterns, and focus order");
        source.Should().Contain("Known accessibility issues");
        source.Should().Contain("internal-only");
        source.Should().Contain("public-preview accessibility");
    }
}
