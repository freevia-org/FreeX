using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class JsonFilesPreflightTests
{
    [Fact]
    public void JsonFilesPreflight_ValidatesTrackedJsonFiles()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-JsonFiles.ps1");

        script.Should().Contain("[string[]]$JsonRoots = @()");
        script.Should().Contain("ToolScriptSupport.ps1");
        WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1").Should().Contain("git -C $RepoRoot ls-files --deduplicate");
        script.Should().Contain("JSON path was not found");
        script.Should().Contain("$rootItem -is [System.IO.FileInfo]");
        script.Should().Contain("ConvertFrom-Json");
        script.Should().Contain("JSON validation failed");
        script.Should().Contain("Validated $($jsonFiles.Count) JSON file(s).");
    }

    [Fact]
    public void JsonFilesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-JsonFiles.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("JSON file(s).");
    }

    [Fact]
    public void JsonFilesPreflight_DefaultCoverageMatchesTrackedJsonFiles()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-JsonFiles.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        ExtractValidatedCount(result.Output).Should().Be(GetTrackedJsonFileCount());
    }

    [Fact]
    public void JsonFilesPreflight_FailsWhenJsonIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.json"), "{ \"name\": ");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-JsonFiles.ps1",
            $"-JsonRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("JSON validation failed");
        result.CombinedOutput.Should().Contain("broken.json");
    }

    private static int GetTrackedJsonFileCount() =>
        GetTrackedFiles().Count(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) && !HasExcludedSegment(path));

    private static IEnumerable<string> GetTrackedFiles()
    {
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = TestProcessRunner.Run("git", $"-C \"{root}\" ls-files --deduplicate", root);
        result.ExitCode.Should().Be(0, result.Error);
        return result.Output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool HasExcludedSegment(string path)
    {
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
            segments.Contains("obj", StringComparer.OrdinalIgnoreCase) ||
            segments.Contains(".worktrees", StringComparer.OrdinalIgnoreCase) ||
            segments.Contains(".claude", StringComparer.OrdinalIgnoreCase);
    }

    private static int ExtractValidatedCount(string output)
    {
        var match = Regex.Match(output, @"Validated (?<count>\d+) JSON file\(s\)\.");
        match.Success.Should().BeTrue(output);
        return int.Parse(match.Groups["count"].Value);
    }

}
