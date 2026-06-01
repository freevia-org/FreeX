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

        script.Should().Contain("[string]$ProjectRoot = \".\"");
        script.Should().Contain("[string[]]$SearchRoots = @()");
        script.Should().Contain("git -C $resolvedProjectRoot ls-files");
        script.Should().Contain("if ($SearchRoots.Count -eq 0)");
        script.Should().Contain("\".slnx\"");
        script.Should().Contain("$segments -contains \".worktrees\"");
        script.Should().Contain("$segments -contains \".claude\"");
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

    [Fact]
    public void ConflictMarkersPreflight_DefaultScanUsesTrackedFilesOnly()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-conflict-marker-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "tracked.cs"), "namespace Scratch;");
            File.WriteAllText(Path.Combine(tempDirectory, "untracked.cs"), $"<<<<<<< HEAD{Environment.NewLine}");
            RunProcess("git", "init", tempDirectory).ExitCode.Should().Be(0);
            RunProcess("git", "add tracked.cs", tempDirectory).ExitCode.Should().Be(0);
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ConflictMarkers.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\"");

            result.ExitCode.Should().Be(0, result.Error);
            result.Output.Should().Contain("Validated 1 text file(s) for Git conflict markers.");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ConflictMarkersPreflight_DefaultScanFailsForTrackedConflictMarker()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-conflict-marker-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.cs"), $"namespace Scratch;{Environment.NewLine}<<<<<<< HEAD{Environment.NewLine}");
            RunProcess("git", "init", tempDirectory).ExitCode.Should().Be(0);
            RunProcess("git", "add broken.cs", tempDirectory).ExitCode.Should().Be(0);
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ConflictMarkers.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("Git conflict marker validation failed");
            (result.Output + result.Error).Should().Contain("broken.cs");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
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
            DeleteDirectory(tempDirectory);
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
            DeleteDirectory(tempDirectory);
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

    private static PowerShellResult RunProcess(string fileName, string arguments, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
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

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);

        Directory.Delete(directory, recursive: true);
    }

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
