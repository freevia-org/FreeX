using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

[Collection(ToolsTestCollections.GeneratedDocsBuild)]
public sealed class RepositoryPreflightTests
{


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
    public void RepositoryPreflight_KillsRetriesAndNamesACheckThatNeverReturns()
    {
        // The static batch used to stall forever when one check's child process hung: nothing
        // could time out a runspace. Every check is now a watched process, so a check that never
        // returns is killed, retried once, and then fails the gate by name instead of hanging it.
        using var temp = new TestTemporaryDirectory();

        var passingScript = CreatePassingPreflightScript(temp.Path, "Test-Passing.ps1");
        var hangingScript = Path.Combine(temp.Path, "Test-Hanging.ps1");
        File.WriteAllText(
            hangingScript,
            """
            Write-Host "Synthetic preflight started."
            Start-Sleep -Seconds 600
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-RepositoryPreflight.ps1",
            // Every preflight check script parameter is stubbed (a missed one would run the real
            // check). The limit is generous enough for a loaded runner to start pwsh for a passing
            // stub; only the hanging check should ever reach it.
            "-Mode Static -CheckTimeoutSeconds 20 " +
            $"-JsonFilesScriptPath \"{passingScript}\" " +
            $"-XmlFilesScriptPath \"{hangingScript}\" " +
            $"-GitHubWorkflowsScriptPath \"{passingScript}\" " +
            $"-TestGateContractScriptPath \"{passingScript}\" " +
            $"-DotNetSdkReadinessScriptPath \"{passingScript}\" " +
            $"-DotNetProjectReferencesScriptPath \"{passingScript}\" " +
            $"-SolutionProjectsScriptPath \"{passingScript}\" " +
            $"-CodeQlSolutionScriptPath \"{passingScript}\" " +
            $"-CrossPlatformPortabilityScriptPath \"{passingScript}\" " +
            $"-ConflictMarkersScriptPath \"{passingScript}\" " +
            $"-LocalOnlyEvidencePathsScriptPath \"{passingScript}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("XML files preflight timed out after 20 second(s) on attempt 1 of 2");
        // Exactly one check (the hanging one) was killed and retried: the retry warning is printed
        // once per killed first attempt, so any other check hitting the limit would add to the count.
        System.Text.RegularExpressions.Regex.Matches(result.CombinedOutput, "preflight timed out after").Count
            .Should().Be(1, "only the hanging check may be killed and retried; every other check is a passing stub");
        result.CombinedOutput.Should().Contain("Retrying once.");
        result.CombinedOutput.Should().Contain("attempt 2 of 2");
        result.CombinedOutput.Should().Contain("Synthetic preflight started.");
        result.CombinedOutput.Should().Contain("Running JSON files preflight...");
        result.CombinedOutput.Should().NotContain("Repository preflight checks passed.");
    }

    [Fact]
    public void DotNetSdkReadinessPreflight_FailsWhenWorkflowSdkBandIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = temp.Path;

        Directory.CreateDirectory(Path.Combine(tempDirectory, ".github", "workflows"));

        var workflowPath = Path.Combine(tempDirectory, ".github", "workflows", "full-release.yml");
        File.WriteAllText(workflowPath, "name: Full Signed Release");

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

        var workflowPath = Path.Combine(tempDirectory, ".github", "workflows", "full-release.yml");
        File.WriteAllText(
            workflowPath,
            """
            name: Full Signed Release
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

        var workflowPath = Path.Combine(tempDirectory, ".github", "workflows", "full-release.yml");
        File.WriteAllText(
            workflowPath,
            """
            name: Full Signed Release
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
