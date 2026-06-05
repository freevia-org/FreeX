using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SolutionProjectsPreflightTests
{
    [Fact]
    public void SolutionProjectsPreflight_ValidatesSolutionMembership()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1"));

        script.Should().Contain("FreeX.slnx");
        script.Should().Contain("SelectNodes(\"//*[local-name()='Project']\")");
        script.Should().Contain("Get-ProjectFiles -Directory");
        script.Should().Contain("Test-IsIgnoredDirectoryName");
        script.Should().Contain("*_wpftmp.csproj");
        script.Should().Contain("$segments -contains \".worktrees\"");
        script.Should().Contain("$segments -contains \".claude\"");
        script.Should().Contain("$_.StartsWith(\"tools/\")");
        script.Should().Contain("Duplicate solution project entry");
        script.Should().Contain("Solution project path escapes solution root");
        script.Should().Contain("Project missing from solution");
        script.Should().Contain("Solution references missing project");
        script.Should().Contain("Validated $($solutionProjectPaths.Count) solution project entry(s).");

        var solution = File.ReadAllText(WorkspaceFileLocator.Find("FreeX.slnx"));
        solution.Should().Contain("<Folder Name=\"/tools/\">");
        solution.Should().Contain("tools/FreeX.ChartInteropCompare/FreeX.ChartInteropCompare.csproj");
        solution.Should().Contain("tools/FreeX.ExcelOpenSmoke/FreeX.ExcelOpenSmoke.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));

        var solutionPath = Path.Combine(tempDirectory, "FreeX.slnx");
        File.WriteAllText(
            solutionPath,
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Included/Included.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{solutionPath}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated 1 solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_RecognizesNestedSolutionFolders()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Nested"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Folder Name="/src/Nested/">
                  <Project Path="src/Nested/Nested.csproj" />
                </Folder>
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Nested", "Nested.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated 1 solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_IgnoresTransientAndNestedAgentWorktreeProjects()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "FreeX.App.Host"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".claude", "worktrees", "agent", "src", "Scratch"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Included/Included.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, "src", "FreeX.App.Host", "FreeX.App.Host_abc123_wpftmp.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch", "Scratch.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, ".claude", "worktrees", "agent", "src", "Scratch", "Scratch.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated 1 solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenSolutionContainsDuplicateProjectEntry()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Duplicate"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Duplicate/Duplicate.csproj" />
                <Project Path="src/Duplicate/Duplicate.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Duplicate", "Duplicate.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("Duplicate solution project entry");
        combinedOutput.Should().Contain("src/Duplicate/Duplicate.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenSolutionProjectPathEscapesSolutionRoot()
    {
        using var temp = new TestTemporaryDirectory();
        var solutionRoot = Path.Combine(temp.Path, "repo");

        Directory.CreateDirectory(solutionRoot);
        Directory.CreateDirectory(Path.Combine(temp.Path, "external"));

        File.WriteAllText(
            Path.Combine(solutionRoot, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="../external/Outside.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(temp.Path, "external", "Outside.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{solutionRoot}\" -SolutionPath \"{Path.Combine(solutionRoot, "FreeX.slnx")}\"");

        var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("escapes solution root");
        combinedOutput.Should().Contain("../external/Outside.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenToolProjectIsMissingFromSolution()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "tools", "MissingTool"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Included/Included.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, "tools", "MissingTool", "MissingTool.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("missing from solution");
        combinedOutput.Should().Contain("tools/MissingTool/MissingTool.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenProjectIsMissingFromSolution()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Missing"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Included/Included.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Missing", "Missing.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("missing from solution");
        combinedOutput.Should().Contain("src/Missing/Missing.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenSolutionReferencesMissingProject()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.slnx"),
            """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Included/Included.csproj" />
                <Project Path="src/Missing/Missing.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("references missing project");
        combinedOutput.Should().Contain("src/Missing/Missing.csproj");
    }

    private static PowerShellResult RunScriptFromTemporaryWorkingDirectory(string scriptPath, string arguments)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, arguments);
    }

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, "\\s+", " ");

}
