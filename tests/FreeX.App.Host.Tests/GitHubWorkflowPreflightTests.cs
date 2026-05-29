using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GitHubWorkflowPreflightTests
{
    [Fact]
    public void GitHubWorkflowPreflight_ValidatesPinnedActionsAndPermissions()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1"));

        script.Should().Contain(".github\\workflows");
        script.Should().Contain("(?:-\\s*)?uses:");
        script.Should().Contain("workflow must declare top-level permissions explicitly");
        script.Should().Contain("must be pinned to an explicit major version");
        script.Should().Contain("workflow YAML must use spaces for indentation");
        script.Should().Contain("Validated $($workflows.Count) GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsForFloatingActionReference()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
                """
                name: Broken

                on:
                  workflow_dispatch:

                permissions:
                  contents: read

                jobs:
                  build:
                    runs-on: windows-latest
                    steps:
                      - uses: actions/checkout@main
                """);
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("GitHub workflow validation failed");
            (result.Output + result.Error).Should().Contain("actions/checkout@main");
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

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
