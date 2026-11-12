using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SolutionProjectsPreflightTests
{
    [Fact]
    public void SolutionProjectsPreflight_ValidatesSolutionMembership()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-SolutionProjects.ps1");
        var toolSupport = WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1");

        script.Should().Contain("FreeX.slnx");
        script.Should().Contain("SelectNodes(\"//*[local-name()='Project']\")");
        script.Should().Contain(". (Join-Path $PSScriptRoot \"ToolScriptSupport.ps1\")");
        script.Should().Contain("Get-ToolProjectFiles -Directory");
        script.Should().Contain("ProjectPathPrefixes");
        script.Should().Contain("ExcludedProjectPathPrefixes");
        script.Should().Contain("Test-IsIncludedProjectPath");
        toolSupport.Should().Contain("function Get-ToolProjectFiles");
        toolSupport.Should().Contain("function Test-ToolIgnoredDirectoryName");
        toolSupport.Should().Contain("*_wpftmp.csproj");
        toolSupport.Should().Contain("\".worktrees\"");
        toolSupport.Should().Contain("\".claude\"");
        script.Should().Contain("\"tools/\"");
        script.Should().Contain("\"shared/\"");
        script.Should().Contain("Duplicate solution project entry");
        script.Should().Contain("Solution project path escapes solution root");
        script.Should().Contain("Project missing from solution");
        script.Should().Contain("Solution references missing project");
        script.Should().Contain("Validated $($solutionProjectPaths.Count) solution project entry(s).");

        var solution = WorkspaceFileLocator.ReadAllText("FreeX.slnx");
        solution.Should().Contain("<Folder Name=\"/tools/\">");
        solution.Should().Contain("tools/FreeX.ChartInteropCompare/FreeX.ChartInteropCompare.csproj");
        solution.Should().Contain("tools/FreeX.ExcelOpenSmoke/FreeX.ExcelOpenSmoke.csproj");

        var defaultTests = WorkspaceFileLocator.ReadAllText("FreeX.DefaultTests.slnx");
        defaultTests.Should().Contain("tests/Free.Shared.Pdf.Tests/Free.Shared.Pdf.Tests.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{solutionPath}\"");

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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated 1 solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_ValidatesCustomProjectPathPrefixes()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "freew", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "freep", "Ignored"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeW.slnx"),
            """
            <Solution>
              <Folder Name="/freew/">
                <Project Path="freew/Included/Included.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "freew", "Included", "Included.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, "freep", "Ignored", "Ignored.csproj"), "<Project />");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeW.slnx")}\" -ProjectPathPrefixes freew/");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated 1 solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_ValidatesExcludedProjectPathPrefixes()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "tests", "Included.Tests"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "tests", "Excluded.UiTests"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "FreeX.DefaultTests.slnx"),
            """
            <Solution>
              <Folder Name="/tests/">
                <Project Path="tests/Included.Tests/Included.Tests.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "tests", "Included.Tests", "Included.Tests.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, "tests", "Excluded.UiTests", "Excluded.UiTests.csproj"), "<Project />");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.DefaultTests.slnx")}\" -ProjectPathPrefixes tests/ -ExcludedProjectPathPrefixes tests/Excluded.UiTests/");

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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{solutionRoot}\" -SolutionPath \"{Path.Combine(solutionRoot, "FreeX.slnx")}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("missing from solution");
        combinedOutput.Should().Contain("tools/MissingTool/MissingTool.csproj");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenSharedProjectIsMissingFromSolution()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "shared", "MissingShared"));

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
        File.WriteAllText(Path.Combine(tempDirectory, "shared", "MissingShared", "MissingShared.csproj"), "<Project />");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("missing from solution");
        combinedOutput.Should().Contain("shared/MissingShared/MissingShared.csproj");
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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-SolutionProjects.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("references missing project");
        combinedOutput.Should().Contain("src/Missing/Missing.csproj");
    }

}
