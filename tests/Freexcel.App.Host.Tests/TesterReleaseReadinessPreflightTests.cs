using System.Diagnostics;
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

    [Fact]
    public void ReadinessPreflight_PassesForInternalTesterBuild()
    {
        var result = RunReadinessPreflight("-RunNumber 42");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Tester release readiness preflight passed.");
        result.Output.Should().Contain("Default tester version for run 42: v0.7.42");
        result.Output.Should().Contain("Tester stream: v0.7.<run>");
        result.Output.Should().Contain("Promotion status: internal-only");
    }

    [Fact]
    public void ReadinessPreflight_BlocksPublicPreviewWhenAccessibilityGateIsIncomplete()
    {
        var result = RunReadinessPreflight("-RunNumber 42 -PublicPreviewCandidate");

        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("Public-preview preflight requires completed accessibility gate inputs");
        result.Error.Should().Contain("Keyboard-only smoke validation");
        result.Error.Should().Contain("Screen-reader smoke validation");
        result.Error.Should().Contain("UI Automation catalog review");
        result.Error.Should().Contain("Known accessibility issues reviewed/listed");
    }

    private static (int ExitCode, string Output, string Error) RunReadinessPreflight(string arguments)
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-TesterReleaseReadiness.ps1");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(scriptPath)!, ".."));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, error);
    }
}
