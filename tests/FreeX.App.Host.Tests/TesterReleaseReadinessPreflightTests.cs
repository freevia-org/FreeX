using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TesterReleaseReadinessPreflightTests
{
    [Fact]
    public void ReadinessPreflight_ValidatesReleaseMetadataAndAccessibilityGate()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-TesterReleaseReadiness.ps1");

        script.Should().Contain("release/progress.json is missing required property");
        script.Should().Contain("release/progress.json overallCompletion must be between 0 and 100.");
        script.Should().Contain("Unsupported releasePatchSource");
        script.Should().Contain("Unsupported release channel");
        script.Should().Contain("tools/Test-RepositoryPreflight.ps1");
        script.Should().Contain("group: tester-release");
        script.Should().Contain("signParameters.AllowUnsignedMsix = `$true");
        script.Should().Contain("Velopack installer/portable/feed artifacts");
        script.Should().Contain("Public-preview preflight requires completed accessibility gate inputs");
        script.Should().Contain("Tester release readiness preflight passed.");
    }

    [Fact]
    public void DistributionPlan_DocumentsReadinessPreflight()
    {
        var plan = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");

        plan.Should().Contain("tools/Test-TesterReleaseReadiness.ps1");
        plan.Should().Contain("-PublicPreviewCandidate");
        plan.Should().Contain("-AccessibilityKeyboardOnly");
        plan.Should().Contain("-AccessibilityScreenReader");
        plan.Should().Contain("-AccessibilityUiaCatalog");
        plan.Should().Contain("-AccessibilityKnownIssues");
        plan.Should().Contain("Release dispatches must run from `main`");
        plan.Should().Contain("signs the package when `FREEX_MSIX_CERTIFICATE_BASE64` is configured");
        plan.Should().Contain("publishes an unsigned MSIX for tester continuity");
    }

    [Fact]
    public void ReadinessPreflight_PassesForInternalTesterBuild()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript("Test-TesterReleaseReadiness.ps1", repoRoot, "-RunNumber 42");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Tester release readiness preflight passed.");
        result.Output.Should().Contain("Default tester version for run 42: v0.8.42");
        result.Output.Should().Contain("Tester stream: v0.8.<run>");
        result.Output.Should().Contain("Promotion status: internal-only");
    }

    [Fact]
    public void ReadinessPreflight_BlocksPublicPreviewWhenAccessibilityGateIsIncomplete()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Test-TesterReleaseReadiness.ps1",
            repoRoot,
            "-RunNumber 42 -PublicPreviewCandidate");

        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("Public-preview preflight requires completed accessibility gate inputs");
        result.Error.Should().Contain("Keyboard-only smoke validation");
        result.Error.Should().Contain("Screen-reader smoke validation");
        result.Error.Should().Contain("UI Automation catalog review");
        result.Error.Should().Contain("Known accessibility issues reviewed/listed");
    }

    [Fact]
    public void ReadinessPreflight_AllowsPublicPreviewWhenAccessibilityGateIsComplete()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Test-TesterReleaseReadiness.ps1",
            repoRoot,
            "-RunNumber 42 -PublicPreviewCandidate -AccessibilityKeyboardOnly -AccessibilityScreenReader -AccessibilityUiaCatalog -AccessibilityKnownIssues");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Tester release readiness preflight passed.");
        result.Output.Should().Contain("Default tester version for run 42: v0.8.42");
        result.Output.Should().Contain("Tester stream: v0.8.<run>");
        result.Output.Should().Contain("Promotion status: public-preview eligible");
    }

}
