using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class TestDistributionPlanTests
{
    [Fact]
    public void DistributionPlan_MarksImplementedDistributionPhasesComplete()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        source.Should().Contain("| 4. Hosted release channel | Complete |");
        source.Should().Contain("| 5. Crash analytics | Complete |");
        source.Should().Contain("| 6. Lightweight usage analytics | Complete |");
        source.Should().Contain("| 7. Auto-update readiness | Complete |");
        source.Should().Contain("Future Velopack auto-update work");
    }

    [Fact]
    public void DistributionPlan_DocumentsPhaseSixUsageAnalyticsContract()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        source.Should().Contain("6. Lightweight usage analytics");
        source.Should().Contain("app lifecycle");
        source.Should().Contain("command/dialog opened");
        source.Should().Contain("file import/export type");
        source.Should().Contain("crash/session linkage");
        source.Should().Contain("workbook contents, formulas, filenames, or paths");
        source.Should().Contain("exception messages and stack traces can occasionally contain sensitive values");
        source.Should().Contain("FREEXCEL_DIAGNOSTICS=0");
    }

    [Fact]
    public void DistributionPlan_DocumentsPhaseSevenAutoUpdateReadiness()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        source.Should().Contain("7. Auto-update readiness");
        source.Should().Contain("Help > Check for Updates");
        source.Should().Contain("stable latest release page");
        source.Should().Contain("Velopack");
        source.Should().Contain("custom `Main`");
        source.Should().Contain("no background update download");
    }

    [Fact]
    public void DistributionPlan_DocumentsCanonicalBuildVerificationCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        source.Should().Contain("## Canonical Build Verification");
        source.Should().Contain("dotnet restore Freexcel.slnx");
        source.Should().Contain("dotnet build Freexcel.slnx --configuration Release --no-restore");
        source.Should().Contain("dotnet test Freexcel.slnx --configuration Release --no-build");
        source.Should().Contain("--disable-build-servers");
        source.Should().Contain("-p:UseSharedCompilation=false");
        source.Should().Contain("-p:NodeReuse=false");
        source.Should().Contain("/nr:false");
        source.Should().Contain("-m:1");
        source.Should().Contain("zero failed tests");
        source.Should().Contain("stale `dotnet`, `MSBuild`, `VBCSCompiler`, or `testhost` process");
    }

    [Fact]
    public void DistributionPlan_DocumentsAccessibilityValidationGate()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));
        var outstanding = File.ReadAllText(WorkspaceFileLocator.Find("docs", "OUTSTANDING_BUILD.md"));

        source.Should().Contain("| 8. Accessibility validation | Required before public preview |");
        source.Should().Contain("Keyboard-only smoke validation");
        source.Should().Contain("Screen-reader smoke validation");
        source.Should().Contain("UI Automation catalog review");
        source.Should().Contain("known-issues section");
        source.Should().Contain("internal-only");
        source.Should().Contain("[TESTER_RELEASE_CHECKLIST.md](TESTER_RELEASE_CHECKLIST.md)");
        outstanding.Should().Contain("documented accessibility validation gate");
        outstanding.Should().Contain("keyboard-only, screen-reader, UI Automation catalog, and known-issues");
    }

    [Fact]
    public void TesterReleaseChecklist_CapturesReleaseAndAccessibilityGateEvidence()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TESTER_RELEASE_CHECKLIST.md"));

        source.Should().Contain("Tester Release");
        source.Should().Contain("release_notes");
        source.Should().Contain("Restore, build, and test");
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
