using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ConflictMarkersPreflightTests
{
    [Fact]
    public void ConflictMarkersPreflight_ScansTextBackedRepositoryFiles()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-ConflictMarkers.ps1"));

        script.Should().Contain("[string[]]$SearchRoots = @(\"AGENTS.md\", \"Directory.Build.props\", \"FreeX.slnx\", \"THIRD_PARTY_NOTICES.md\", \".github\", \"docs\", \"release\", \"src\", \"tests\", \"tools\")");
        script.Should().Contain("\".slnx\"");
        script.Should().Contain("$conflictMarkerPattern = '^(<<<<<<<|=======|>>>>>>>)($|[ <].*)'");
        script.Should().Contain("Git conflict marker validation failed");
        script.Should().Contain("Validated $($candidateFiles.Count) text file(s) for Git conflict markers.");
    }

    [Fact]
    public void ConflictMarkersPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ConflictMarkers.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("text file(s) for Git conflict markers.");
    }

    [Theory]
    [InlineData("<<<<<<< HEAD")]
    [InlineData("=======")]
    [InlineData(">>>>>>> feature")]
    public void ConflictMarkersPreflight_FailsWhenConflictMarkerIsPresent(string marker)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-conflict-marker-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.cs"), $"namespace Scratch;{Environment.NewLine}{marker}{Environment.NewLine}");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ConflictMarkers.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-SearchRoots \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("Git conflict marker validation failed");
            (result.Output + result.Error).Should().Contain("broken.cs");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ConflictMarkersPreflight_FailsWhenSolutionContainsConflictMarker()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-conflict-marker-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.slnx"), $"<Solution>{Environment.NewLine}<<<<<<< HEAD{Environment.NewLine}</Solution>");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ConflictMarkers.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-SearchRoots \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("Git conflict marker validation failed");
            (result.Output + result.Error).Should().Contain("broken.slnx");
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
