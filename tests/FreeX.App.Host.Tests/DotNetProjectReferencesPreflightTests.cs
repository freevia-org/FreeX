using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DotNetProjectReferencesPreflightTests
{
    [Fact]
    public void DotNetProjectReferencesPreflight_ValidatesProjectReferenceTargets()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-DotNetProjectReferences.ps1");
        var support = WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1");

        script.Should().Contain("Get-ToolProjectFiles -Directory");
        support.Should().Contain("Test-ToolIgnoredDirectoryName");
        support.Should().Contain("*_wpftmp.csproj");
        support.Should().Contain("\".worktrees\", \".claude\"");
        script.Should().Contain("ProjectReference");
        script.Should().Contain("Duplicate ProjectReference target");
        script.Should().Contain("ProjectReference target escapes project root");
        script.Should().Contain("Missing ProjectReference target");
        script.Should().Contain("Validated ProjectReference targets for $($projectFiles.Count) .NET project file(s).");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "A"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "B"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "src", "A", "A.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\B\B.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "B", "B.csproj"), "<Project />");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetProjectReferences.ps1",
            $"-ProjectRoot \"{tempDirectory}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ProjectReference targets for 2 .NET project file(s).");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_IgnoresTransientAndNestedAgentWorktreeProjects()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "FreeX.App.Host"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".claude", "worktrees", "agent", "src", "Scratch"));

        File.WriteAllText(Path.Combine(tempDirectory, "src", "FreeX.App.Host", "FreeX.App.Host.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(tempDirectory, "src", "FreeX.App.Host", "FreeX.App.Host_abc123_wpftmp.csproj"), "<Project><ItemGroup><ProjectReference Include=\"Missing.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch", "Scratch.csproj"), "<Project><ItemGroup><ProjectReference Include=\"Missing.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(tempDirectory, ".claude", "worktrees", "agent", "src", "Scratch", "Scratch.csproj"), "<Project><ItemGroup><ProjectReference Include=\"Missing.csproj\" /></ItemGroup></Project>");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetProjectReferences.ps1",
            $"-ProjectRoot \"{tempDirectory}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ProjectReference targets for 1 .NET project file(s).");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_FailsForDuplicateProjectReferenceTarget()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "A"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "B"));

        File.WriteAllText(
            Path.Combine(tempDirectory, "src", "A", "A.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\B\B.csproj" />
                <ProjectReference Include="../B/B.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(tempDirectory, "src", "B", "B.csproj"), "<Project />");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetProjectReferences.ps1",
            $"-ProjectRoot \"{tempDirectory}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("Duplicate ProjectReference target");
        combinedOutput.Should().Contain("src/A/A.csproj");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_FailsForReferenceOutsideProjectRoot()
    {
        using var temp = new TestTemporaryDirectory();
        var projectRoot = Path.Combine(temp.Path, "repo");

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(temp.Path, "external"));

        File.WriteAllText(
            Path.Combine(projectRoot, "Broken.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\external\Outside.csproj" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(temp.Path, "external", "Outside.csproj"), "<Project />");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetProjectReferences.ps1",
            $"-ProjectRoot \"{projectRoot}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("target escapes project root");
        combinedOutput.Should().Contain("..\\external\\Outside.csproj");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_FailsForMissingProjectReferenceTarget()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "Broken.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="Missing.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetProjectReferences.ps1",
            $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Project reference validation failed");
        result.CombinedOutput.Should().Contain("Missing.csproj");
    }

}
