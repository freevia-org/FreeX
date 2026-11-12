using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CrossPlatformPortabilityPreflightTests
{
    [Fact]
    public void CrossPlatformPortabilityPreflight_GuardsPathsShellsAndReleaseTooling()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-CrossPlatformPortability.ps1");

        script.Should().Contain("Case-insensitive tracked-path collision");
        script.Should().Contain("Windows-incompatible tracked path");
        // Shell scripts are still syntax-checked with bash -n, but the interpreter is now resolved
        // and probed first so a Windows/WSL stub that satisfies Get-Command and then fails every
        // invocation cannot fail the gate. That moved the executable into a variable, so the literal
        // "bash -n" no longer appears; pin the two halves of the behaviour instead.
        script.Should().Contain("Resolve-WorkingInterpreter -Names @('bash')");
        script.Should().Contain("$bashCommand.Source -n ");
        script.Should().Contain("passes a Windows-separated child path to Join-Path");
        script.Should().Contain("Path.GetRelativePath, which is unavailable in Windows PowerShell 5.1");
        script.Should().Contain("full-release.yml");
        script.Should().Contain("all $($powerShellScripts.Count) PowerShell scripts");
        script.Should().Contain("Unicode/case-normalized tracked-path collision");
        script.Should().Contain("must use LF endings");
        script.Should().Contain("must be tracked executable (Git mode 100755)");
        script.Should().Contain("Dictionary[string, string]");
        script.Should().NotContain("Group-Object");
        script.Should().Contain("git -C $repoRoot grep -l -F $needle");
        script.Should().Contain("$managedSourceCandidates");

        var toolScriptTests = WorkspaceFileLocator.ReadAllText("tools", "Test-ToolScripts.ps1");
        toolScriptTests.Should().Contain("ToolScriptSupport.ps1");

        var toolScriptSupport = WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1");
        toolScriptSupport.Should().Contain("ResolveLinkTarget($true)");

        var ciWorkflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "ci.yml");
        ciWorkflow.Should().Contain("if: matrix.runStaticPreflight");
        ciWorkflow.Should().Contain("if: matrix.runPlatformPreflight");
        ciWorkflow.Should().Contain("Test-RepositoryPreflight.ps1 -Mode Static");
        ciWorkflow.Should().Contain("Test-RepositoryPreflight.ps1 -Mode Platform");
        ciWorkflow.Should().Contain("pwsh -NoProfile -File tools/Test-RepositoryPreflight.ps1");

        var linuxPackagingTests = WorkspaceFileLocator.ReadAllText("tools", "Test-LinuxPackagingScripts.ps1");
        linuxPackagingTests.Should().Contain("DirectorySeparatorChar -eq '\\'");
        linuxPackagingTests.Should().Contain("chmod +x \"$1\"");
        linuxPackagingTests.Should().NotContain("chmod +x --");
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7, ExternalToolPreconditions.Python)]
    public void CrossPlatformPortabilityPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-CrossPlatformPortability.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Cross-platform portability checks passed");
    }
}
