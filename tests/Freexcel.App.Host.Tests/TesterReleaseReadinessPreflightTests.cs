using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class TesterReleaseReadinessPreflightTests
{
    [Fact]
    public void ReadinessPreflight_ValidatesReleaseMetadataAndAccessibilityGate()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-TesterReleaseReadiness.ps1"));

        script.Should().Contain("release/progress.json is missing required property");
        script.Should().Contain("release/progress.json overallCompletion must be between 0 and 100.");
        script.Should().Contain("Unsupported releasePatchSource");
        script.Should().Contain("Unsupported release channel");
        script.Should().Contain("Public-preview preflight requires completed accessibility gate inputs");
        script.Should().Contain("Tester release readiness preflight passed.");
    }

    [Fact]
    public void DistributionPlan_DocumentsReadinessPreflight()
    {
        var plan = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));

        plan.Should().Contain("tools/Test-TesterReleaseReadiness.ps1");
        plan.Should().Contain("-PublicPreviewCandidate");
        plan.Should().Contain("-AccessibilityKeyboardOnly");
        plan.Should().Contain("-AccessibilityScreenReader");
        plan.Should().Contain("-AccessibilityUiaCatalog");
        plan.Should().Contain("-AccessibilityKnownIssues");
    }
}
