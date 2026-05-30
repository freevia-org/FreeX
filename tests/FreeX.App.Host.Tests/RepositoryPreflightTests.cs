using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
        script.Should().Contain("Test-DotNetSdkReadiness.ps1");
        script.Should().Contain("Test-DotNetProjectReferences.ps1");
        script.Should().Contain("Test-SolutionProjects.ps1");
        script.Should().Contain("Test-GeneratedDocs.ps1");
        script.Should().Contain("Test-ConflictMarkers.ps1");
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
        output.Should().Contain("Running .NET SDK readiness preflight...");
        output.Should().Contain("Running .NET project references preflight...");
        output.Should().Contain("Running solution projects preflight...");
        output.Should().Contain("Running generated docs preflight...");
        output.Should().Contain("Running Git conflict markers preflight...");
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

    [Fact]
    public void DotNetSdkReadinessPreflight_FailsWhenWorkflowSdkBandIsMissing()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-dotnet-sdk-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".github", "workflows"));

        try
        {
            var workflowPath = Path.Combine(tempDirectory, ".github", "workflows", "tester-release.yml");
            File.WriteAllText(workflowPath, "name: Tester Release");

            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-DotNetSdkReadiness.ps1");
            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\" -WorkflowPath \"{workflowPath}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("missing a dotnet-version SDK band");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void DotNetSdkReadinessPreflight_FailsWhenProjectTargetsNewerFrameworkThanWorkflowSdk()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-dotnet-sdk-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".github", "workflows"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Future"));

        try
        {
            var workflowPath = Path.Combine(tempDirectory, ".github", "workflows", "tester-release.yml");
            File.WriteAllText(
                workflowPath,
                """
                name: Tester Release
                jobs:
                  build:
                    steps:
                      - uses: actions/setup-dotnet@v5
                        with:
                          dotnet-version: 10.0.x
                """);
            File.WriteAllText(
                Path.Combine(tempDirectory, "src", "Future", "Future.csproj"),
                """
                <Project>
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-DotNetSdkReadiness.ps1");
            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\" -WorkflowPath \"{workflowPath}\"");

            var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
            result.ExitCode.Should().NotBe(0);
            combinedOutput.Should().Contain("newer than workflow SDK 10.0.x");
            combinedOutput.Should().Contain("src/Future/Future.csproj: net11.0");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static PowerShellResult RunPowerShellScript(string scriptPath, string workingDirectory, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new PowerShellResult(process.ExitCode, output, error);
    }

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, "\\s+", " ");

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
