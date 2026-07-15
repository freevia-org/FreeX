using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class XmlFilesPreflightTests
{
    [Fact]
    public void XmlFilesPreflight_ValidatesXmlBackedRepositoryFiles()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-XmlFiles.ps1");

        script.Should().Contain("[string[]]$XmlRoots = @()");
        script.Should().Contain("ToolScriptSupport.ps1");
        WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1").Should().Contain("git -C $RepoRoot ls-files --deduplicate");
        script.Should().Contain("\".slnx\"");
        script.Should().Contain("[System.Xml.XmlReader]::Create");
        script.Should().Contain("XML validation failed");
        script.Should().Contain("Validated $($xmlFiles.Count) XML-backed file(s).");
    }

    [Fact]
    public void XmlFilesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-XmlFiles.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("XML-backed file(s).");
    }

    [Fact]
    public void XmlFilesPreflight_DefaultCoverageMatchesTrackedXmlBackedFiles()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-XmlFiles.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        ExtractValidatedCount(result.Output).Should().Be(GetTrackedXmlBackedFileCount());
    }

    [Fact]
    public void XmlFilesPreflight_FailsWhenSolutionXmlIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.slnx"), "<Solution><Folder></Solution>");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-XmlFiles.ps1",
            $"-XmlRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("XML validation failed");
        result.CombinedOutput.Should().Contain("broken.slnx");
    }

    [Fact]
    public void XmlFilesPreflight_FailsWhenXmlIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.xaml"), "<Window><Grid></Window>");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-XmlFiles.ps1",
            $"-XmlRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("XML validation failed");
        result.CombinedOutput.Should().Contain("broken.xaml");
    }

    private static int GetTrackedXmlBackedFileCount()
    {
        string[] extensions =
        [
            ".xml",
            ".xaml",
            ".axaml",
            ".slnx",
            ".csproj",
            ".props",
            ".targets",
            ".resx",
            ".config",
            ".ruleset",
            ".plist"
        ];

        var extensionSet = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetTrackedFiles()
            .Count(path => extensionSet.Contains(Path.GetExtension(path)) && !HasExcludedSegment(path));
    }

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
        var match = Regex.Match(output, @"Validated (?<count>\d+) XML-backed file\(s\)\.");
        match.Success.Should().BeTrue(output);
        return int.Parse(match.Groups["count"].Value);
    }

}
