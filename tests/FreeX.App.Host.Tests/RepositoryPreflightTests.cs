using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RepositoryPreflightTests
{
    [Fact]
    public void RepositoryPreflight_RunsStructuralPreflightScripts()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-RepositoryPreflight.ps1"));

        script.Should().Contain("Test-JsonFiles.ps1");
        script.Should().Contain("Test-XmlFiles.ps1");
        script.Should().Contain("Test-ToolScripts.ps1");
        script.Should().Contain("Test-GitHubWorkflows.ps1");
        script.Should().Contain("Test-DotNetProjectReferences.ps1");
        script.Should().Contain("Test-SolutionProjects.ps1");
        script.Should().Contain("Test-GeneratedDocs.ps1");
        script.Should().Contain("Repository preflight checks passed.");
    }

    [Fact]
    public void RepositoryPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-RepositoryPreflight.ps1");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, error);
        output.Should().Contain("Running JSON files preflight...");
        output.Should().Contain("Running XML files preflight...");
        output.Should().Contain("Running PowerShell tools preflight...");
        output.Should().Contain("Running GitHub workflows preflight...");
        output.Should().Contain("Running .NET project references preflight...");
        output.Should().Contain("Running solution projects preflight...");
        output.Should().Contain("Running generated docs preflight...");
        output.Should().Contain("Repository preflight checks passed.");
    }

    [Fact]
    public void RepositoryPreflight_FailsWhenChildPreflightScriptIsMissing()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-RepositoryPreflight.ps1");
        var missingScriptPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.ps1");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -XmlFilesScriptPath \"{missingScriptPath}\"",
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().NotBe(0);
        (output + error).Should().Contain("XML files preflight script was not found");
    }
}
