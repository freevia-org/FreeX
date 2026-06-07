using System;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TestDistributionPlanTests
{
    [Fact]
    public void DistributionPlan_MarksImplementedDistributionPhasesComplete()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));

        source.Should().Contain("| 4. Hosted release channel | Complete |");
        source.Should().Contain("| 5. Crash analytics | Complete |");
        source.Should().Contain("| 6. Lightweight usage analytics | Complete |");
        source.Should().Contain("| 7. Auto-update readiness | Complete |");
        source.Should().Contain("Future Velopack auto-update work");
    }

    [Fact]
    public void DistributionPlan_DocumentsPhaseSixUsageAnalyticsContract()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));

        source.Should().Contain("7. Auto-update readiness");
        source.Should().Contain("Help > Check for Updates");
        source.Should().Contain("stable latest release page");
        source.Should().Contain("Velopack");
        source.Should().Contain("custom `Main`");
        source.Should().Contain("no background update download");
    }

    [Fact]
    public void DistributionPlan_DocumentsDefaultAgentBuildVerificationCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));

        source.Should().Contain("## Default Agent Build Verification");
        source.Should().Contain("tools\\Test-RepositoryPreflight.ps1");
        source.Should().Contain("dotnet build FreeX.slnx --configuration Release");
        source.Should().Contain("dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build");
        source.Should().Contain("Default agent verification does not run the UI lane");
        source.Should().Contain("does not use `dotnet test FreeX.slnx`");
        source.Should().Contain("validates tracked JSON/XML-backed files");
        source.Should().Contain("keeps build servers, shared compilation, node reuse, and MSBuild parallelism enabled");
        source.Should().Contain("## Conservative Rerun Fallback");
        source.Should().Contain("--disable-build-servers");
        source.Should().Contain("-p:UseSharedCompilation=false");
        source.Should().Contain("-p:NodeReuse=false");
        source.Should().Contain("/nr:false");
        source.Should().Contain("-m:1");
        source.Should().Contain("the default Release test lane reports zero failed tests");
        source.Should().Contain("stale `dotnet`, `MSBuild`, `VBCSCompiler`, or `testhost` process");

        var defaultSectionIndex = source.IndexOf("## Default Agent Build Verification", StringComparison.Ordinal);
        var uiSectionIndex = source.IndexOf("## UI Lane Verification", StringComparison.Ordinal);

        defaultSectionIndex.Should().BeGreaterThanOrEqualTo(0);
        uiSectionIndex.Should().BeGreaterThan(defaultSectionIndex);
        source[defaultSectionIndex..uiSectionIndex].Should().NotContain("FreeX.UiTests.slnx");
        source[defaultSectionIndex..uiSectionIndex].Should().NotContain("dotnet restore FreeX.slnx");
        source[defaultSectionIndex..uiSectionIndex].Should().NotContain("--disable-build-servers");
        source[uiSectionIndex..].Should().Contain("dotnet test FreeX.UiTests.slnx --configuration Release --no-build");
        source[uiSectionIndex..].Should().Contain("Tester Release");
        source[uiSectionIndex..].Should().Contain("still runs both the default and UI test lanes");
    }

    [Fact]
    public void DistributionPlan_DocumentsMacOsPreviewChecksumAndTesterInstructions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));

        source.Should().Contain("macOS App Preview");
        source.Should().Contain("[release/macos-signing-notarization.md](macos-signing-notarization.md)");
        source.Should().Contain("self-checks each SHA-256 file with `shasum -a 256 -c`");
        source.Should().Contain("records `zip_sha256` in evidence");
        source.Should().Contain("not a public release channel");
        source.Should().Contain("GitHub-hosted macOS runners can produce downloadable macOS app artifacts without local macOS hardware");
        source.Should().Contain("GitHub Actions > `macOS App Preview` > the completed run");
        source.Should().Contain("freex-<run-id>-<run-attempt>-osx-arm64-macos-app");
        source.Should().Contain("freex-<run-id>-<run-attempt>-osx-x64-macos-app");
        source.Should().Contain("does not publish GitHub Release assets or stable `latest` links");
        source.Should().Contain("Signed and internal ad-hoc outputs use the same artifact names");
        source.Should().Contain("Use `osx-arm64` for Apple Silicon Macs and `osx-x64` for Intel Macs");
        source.Should().Contain("Actions artifact wrapper");
        source.Should().Contain("freex-<runtime>-macos-tester-instructions.md");
        source.Should().Contain("freex-<runtime>-macos-evidence.txt");
        source.Should().Contain("shasum -a 256 -c freex-<runtime>-macos-app.zip.sha256");
        source.Should().Contain("open `FreeX.app`");
        source.Should().Contain("Human validation of Finder open, Gatekeeper prompts, basic workbook workflows");
        source.Should().Contain("codesign_mode");
        source.Should().Contain("notarization_status");
        source.Should().Contain("stapler_validated");
        source.Should().Contain("zip_sha256");
        source.Should().Contain("Control-click or right-click > Open");
        source.Should().Contain("Public distribution still requires Developer ID signing, accepted notarization, and stapling evidence.");
    }

    [Fact]
    public void MacOsSigningRunbook_DocumentsDeveloperIdSecretsAndHostedEvidence()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/macos-signing-notarization.md"));
        var readme = File.ReadAllText(WorkspaceFileLocator.Find("docs", "README.md"));

        readme.Should().Contain("[release/macos-signing-notarization.md](release/macos-signing-notarization.md)");
        source.Should().Contain("macOS Signing And Notarization Runbook");
        source.Should().Contain("Artifact Retrieval");
        source.Should().Contain("Quick retrieval checklist");
        source.Should().Contain("Pick `osx-arm64` for Apple Silicon Macs or `osx-x64` for Intel Macs.");
        source.Should().Contain("Download the matching Actions artifact wrapper from the completed workflow run.");
        source.Should().Contain("Unzip the wrapper, then verify the inner `freex-<runtime>-macos-app.zip` with its `.sha256` file.");
        source.Should().Contain("Keep `freex-<runtime>-macos-evidence.txt` and the smoke/notarization logs with any tester report.");
        source.Should().Contain("workflow_dispatch");
        source.Should().Contain("pull request events intentionally fall back to ad-hoc signing");
        source.Should().Contain("gh run download <run-id>");
        source.Should().Contain("codesign_mode=ad-hoc");
        source.Should().Contain("notarization_status=skipped_missing_credentials");
        source.Should().Contain("No local Mac is needed to produce the downloadable artifacts");
        source.Should().Contain("MACOS_CODESIGN_CERTIFICATE_P12");
        source.Should().Contain("MACOS_CODESIGN_CERTIFICATE_PASSWORD");
        source.Should().Contain("MACOS_DEVELOPER_ID_APPLICATION");
        source.Should().Contain("MACOS_NOTARY_APPLE_ID");
        source.Should().Contain("MACOS_NOTARY_TEAM_ID");
        source.Should().Contain("MACOS_NOTARY_PASSWORD");
        source.Should().Contain("security find-identity -v -p codesigning");
        source.Should().Contain("base64 -i DeveloperIDApplication.p12");
        source.Should().Contain("codesign_mode=developer-id");
        source.Should().Contain("notarization_status=accepted");
        source.Should().Contain("stapler_validated=true");
        source.Should().Contain("freex-<runtime>-macos-notarization.log");
        source.Should().Contain("freex-<runtime>-macos-launch-smoke.txt");
        source.Should().Contain("The workflow submits the zipped `FreeX.app`, waits for an accepted notarization result, staples the ticket to the app bundle, validates stapling, then recreates the zip.");
        source.Should().Contain("the current workflow uploads Actions artifacts only and has `contents: read`");
        source.Should().Contain("https://developer.apple.com/support/developer-id/");
        source.Should().Contain("https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution");
        source.Should().Contain("https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution/customizing_the_notarization_workflow");
    }

    [Fact]
    public void DistributionPlan_DocumentsAccessibilityValidationGate()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/test-distribution.md"));
        var outstanding = File.ReadAllText(WorkspaceFileLocator.Find("docs", "planning/outstanding-build.md"));

        source.Should().Contain("| 8. Accessibility validation | Complete");
        source.Should().Contain("Keyboard-only smoke validation");
        source.Should().Contain("Screen-reader smoke validation");
        source.Should().Contain("UI Automation catalog review");
        source.Should().Contain("known-issues section");
        source.Should().Contain("internal-only");
        source.Should().Contain("[release/tester-release-checklist.md](tester-release-checklist.md)");
        outstanding.Should().Contain("accessibility validation gate from `release/test-distribution.md` has been audited");
        outstanding.Should().Contain("live keyboard-only and screen-reader validation");
    }

    [Fact]
    public void TesterReleaseChecklist_CapturesReleaseAndAccessibilityGateEvidence()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/tester-release-checklist.md"));

        source.Should().Contain("Tester Release");
        source.Should().Contain("release_notes");
        source.Should().Contain("Repository preflight, build, and test");
        source.Should().Contain("Versioned `.exe`, latest `.exe`, versioned MSIX, latest MSIX, and checksum artifacts");
        source.Should().Contain("release/progress.json");
        source.Should().Contain("Keyboard-only smoke validation");
        source.Should().Contain("Screen-reader smoke validation");
        source.Should().Contain("UI Automation catalog review");
        source.Should().Contain("Known accessibility issues");
        source.Should().Contain("internal-only");
        source.Should().Contain("public-preview candidate");
    }
}
