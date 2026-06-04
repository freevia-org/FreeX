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
        source.Should().Contain("dotnet restore FreeX.slnx");
        source.Should().Contain("dotnet build FreeX.slnx --configuration Release --no-restore");
        source.Should().Contain("dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build");
        source.Should().Contain("Default agent verification does not run the UI lane");
        source.Should().Contain("does not use `dotnet test FreeX.slnx`");
        source.Should().Contain("validates tracked JSON/XML-backed files");
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
        source[uiSectionIndex..].Should().Contain("dotnet test FreeX.UiTests.slnx --configuration Release --no-build");
        source[uiSectionIndex..].Should().Contain("Tester Release");
        source[uiSectionIndex..].Should().Contain("still runs both the default and UI test lanes");
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
        source.Should().Contain("Repository preflight, restore, build, and test");
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
