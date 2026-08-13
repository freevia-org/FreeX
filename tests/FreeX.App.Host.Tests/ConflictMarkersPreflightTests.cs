using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ConflictMarkersPreflightTests
{
    [Fact]
    public void ConflictMarkersPreflight_ScansTextBackedRepositoryFiles()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-ConflictMarkers.ps1");
        var toolSupport = WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1");

        script.Should().Contain("[string]$ProjectRoot = \".\"");
        script.Should().Contain("[string[]]$SearchRoots = @()");
        script.Should().Contain("git -C $resolvedProjectRoot ls-files");
        script.Should().Contain("if ($SearchRoots.Count -eq 0)");
        script.Should().Contain("\".slnx\"");
        script.Should().Contain("Test-ToolExcludedPath");
        script.Should().Contain("@(\"bin\", \"obj\", \".git\", \".worktrees\", \".claude\")");
        toolSupport.Should().Contain("function Test-ToolExcludedPath");
        toolSupport.Should().Contain("if ($segments -contains $directoryName)");
        script.Should().Contain("$conflictMarkerPattern = '^(<<<<<<<|=======|>>>>>>>)($|[ <].*)'");
        script.Should().Contain("Git conflict marker validation failed");
        script.Should().Contain("Validated $($candidateFiles.Count) text file(s) for Git conflict markers.");
    }

    [Fact]
    public void ConflictMarkersPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-ConflictMarkers.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("text file(s) for Git conflict markers.");
    }

    [Fact]
    public void ConflictMarkersPreflight_DefaultScanUsesTrackedFilesOnly()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "tracked.cs"), "namespace Scratch;");
        File.WriteAllText(Path.Combine(temp.Path, "untracked.cs"), $"<<<<<<< HEAD{Environment.NewLine}");
        TestProcessRunner.Run("git", "init", temp.Path).ExitCode.Should().Be(0);
        AddGitIndexEntry(temp.Path, "tracked.cs");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ConflictMarkers.ps1",
            $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated 1 text file(s) for Git conflict markers.");
    }

    [Fact]
    public void ConflictMarkersPreflight_DefaultScanFailsForTrackedConflictMarker()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.cs"), $"namespace Scratch;{Environment.NewLine}<<<<<<< HEAD{Environment.NewLine}");
        TestProcessRunner.Run("git", "init", temp.Path).ExitCode.Should().Be(0);
        AddGitIndexEntry(temp.Path, "broken.cs");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ConflictMarkers.ps1",
            $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Git conflict marker validation failed");
        result.CombinedOutput.Should().Contain("broken.cs");
    }

    [Theory]
    [InlineData("<<<<<<< HEAD")]
    [InlineData("=======")]
    [InlineData(">>>>>>> feature")]
    public void ConflictMarkersPreflight_FailsWhenConflictMarkerIsPresent(string marker)
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.cs"), $"namespace Scratch;{Environment.NewLine}{marker}{Environment.NewLine}");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ConflictMarkers.ps1",
            $"-SearchRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Git conflict marker validation failed");
        result.CombinedOutput.Should().Contain("broken.cs");
    }

    [Fact]
    public void ConflictMarkersPreflight_FailsWhenSolutionContainsConflictMarker()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.slnx"), $"<Solution>{Environment.NewLine}<<<<<<< HEAD{Environment.NewLine}</Solution>");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ConflictMarkers.ps1",
            $"-SearchRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Git conflict marker validation failed");
        result.CombinedOutput.Should().Contain("broken.slnx");
    }

    private static void AddGitIndexEntry(string repositoryPath, string fileName)
    {
        const string EmptyBlobSha = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391";

        TestProcessRunner.Run("git", $"update-index --add --cacheinfo 100644,{EmptyBlobSha},{fileName}", repositoryPath)
            .ExitCode.Should().Be(0);
    }
}
