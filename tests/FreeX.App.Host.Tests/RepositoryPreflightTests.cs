using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RepositoryPreflightTests
{
    [Fact]
    public void RepositoryPreflight_RunsStructuralPreflightScripts()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-RepositoryPreflight.ps1");

        script.Should().Contain("Test-JsonFiles.ps1");
        script.Should().Contain("Test-XmlFiles.ps1");
        script.Should().Contain("Test-ToolScripts.ps1");
        script.Should().Contain("Test-GitHubWorkflows.ps1");
        script.Should().Contain("Test-DotNetSdkReadiness.ps1");
        script.Should().Contain("Test-DotNetProjectReferences.ps1");
        script.Should().Contain("Test-SolutionProjects.ps1");
        script.Should().Contain("FreeX.DefaultTests.slnx");
        script.Should().Contain("ExcludedProjectPathPrefixes");
        script.Should().Contain("Test-MacOsAppReadiness.ps1");
        script.Should().Contain("Test-LinuxPackagingScripts.ps1");
        script.Should().Contain("Test-GeneratedDocs.ps1");
        script.Should().Contain("Test-ConflictMarkers.ps1");
        script.Should().Contain("Repository preflight checks passed.");
    }

    [Fact]
    public void RepositoryPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        using var temp = new TestTemporaryDirectory();

        var jsonScript = CreatePassingPreflightScript(temp.Path, "Test-JsonFiles.ps1");
        var xmlScript = CreatePassingPreflightScript(temp.Path, "Test-XmlFiles.ps1");
        var toolScriptsScript = CreatePassingPreflightScript(temp.Path, "Test-ToolScripts.ps1");
        var workflowsScript = CreatePassingPreflightScript(temp.Path, "Test-GitHubWorkflows.ps1");
        var sdkScript = CreatePassingPreflightScript(temp.Path, "Test-DotNetSdkReadiness.ps1");
        var projectReferencesScript = CreatePassingPreflightScript(temp.Path, "Test-DotNetProjectReferences.ps1");
        var solutionProjectsScript = CreatePassingPreflightScript(temp.Path, "Test-SolutionProjects.ps1");
        var macOsAppReadinessScript = CreatePassingPreflightScript(temp.Path, "Test-MacOsAppReadiness.ps1");
        var linuxPackagingScriptsScript = CreatePassingPreflightScript(temp.Path, "Test-LinuxPackagingScripts.ps1");
        var generatedDocsScript = CreatePassingPreflightScript(temp.Path, "Test-GeneratedDocs.ps1");
        var conflictMarkersScript = CreatePassingPreflightScript(temp.Path, "Test-ConflictMarkers.ps1");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-RepositoryPreflight.ps1",
            $"-JsonFilesScriptPath \"{jsonScript}\" " +
            $"-XmlFilesScriptPath \"{xmlScript}\" " +
            $"-ToolScriptsScriptPath \"{toolScriptsScript}\" " +
            $"-GitHubWorkflowsScriptPath \"{workflowsScript}\" " +
            $"-DotNetSdkReadinessScriptPath \"{sdkScript}\" " +
            $"-DotNetProjectReferencesScriptPath \"{projectReferencesScript}\" " +
            $"-SolutionProjectsScriptPath \"{solutionProjectsScript}\" " +
            $"-MacOsAppReadinessScriptPath \"{macOsAppReadinessScript}\" " +
            $"-LinuxPackagingScriptsScriptPath \"{linuxPackagingScriptsScript}\" " +
            $"-GeneratedDocsScriptPath \"{generatedDocsScript}\" " +
            $"-ConflictMarkersScriptPath \"{conflictMarkersScript}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Running JSON files preflight...");
        result.Output.Should().Contain("Running XML files preflight...");
        result.Output.Should().Contain("Running PowerShell tools preflight...");
        result.Output.Should().Contain("Running GitHub workflows preflight...");
        result.Output.Should().Contain("Running .NET SDK readiness preflight...");
        result.Output.Should().Contain("Running .NET project references preflight...");
        result.Output.Should().Contain("Running solution projects preflight...");
        result.Output.Should().Contain("Running default test solution projects preflight...");
        result.Output.Should().Contain("Running FreeW solution projects preflight...");
        result.Output.Should().Contain("Running FreeP solution projects preflight...");
        result.Output.Should().Contain("Running macOS app readiness preflight...");
        result.Output.Should().Contain("Running Linux packaging scripts preflight...");
        result.Output.Should().Contain("Running generated docs preflight...");
        result.Output.Should().Contain("Running Git conflict markers preflight...");
        result.Output.Should().Contain("Repository preflight checks passed.");
    }

    [Fact]
    public void RepositoryPreflight_FailsWhenChildPreflightScriptIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        var missingScriptPath = Path.Combine(temp.Path, "missing.ps1");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-RepositoryPreflight.ps1",
            $"-XmlFilesScriptPath \"{missingScriptPath}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("XML files preflight script was not found");
    }

    [Fact]
    public void DotNetSdkReadinessPreflight_FailsWhenWorkflowSdkBandIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, ".github", "workflows"));

        var workflowPath = Path.Combine(tempDirectory, ".github", "workflows", "tester-release.yml");
        File.WriteAllText(workflowPath, "name: Tester Release");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetSdkReadiness.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -WorkflowPath \"{workflowPath}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("missing a dotnet-version SDK");
    }

    [Fact]
    public void DotNetSdkReadinessPreflight_IgnoresNestedClaudeWorktreeProjects()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, ".github", "workflows"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Current"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".claude", "worktrees", "agent", "src", "Future"));

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
            Path.Combine(tempDirectory, "src", "Current", "Current.csproj"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(tempDirectory, ".claude", "worktrees", "agent", "src", "Future", "Future.csproj"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetSdkReadiness.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -WorkflowPath \"{workflowPath}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("across 1 project file(s).");
    }

    [Fact]
    public void DotNetSdkReadinessPreflight_FailsWhenProjectTargetsNewerFrameworkThanWorkflowSdk()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, ".github", "workflows"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Future"));

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

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-DotNetSdkReadiness.ps1",
            $"-ProjectRoot \"{tempDirectory}\" -WorkflowPath \"{workflowPath}\"");

        var combinedOutput = result.NormalizedCombinedOutput;
        result.ExitCode.Should().NotBe(0);
        combinedOutput.Should().Contain("newer than workflow SDK 10.0.x");
        combinedOutput.Should().Contain("src/Future/Future.csproj: net11.0");
    }

    private static string CreatePassingPreflightScript(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(
            path,
            """
            $ErrorActionPreference = "Stop"
            Write-Host "Synthetic preflight passed."
            """);
        return path;
    }

}
