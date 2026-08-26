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
        script.Should().Contain("bash -n");
        script.Should().Contain("passes a Windows-separated child path to Join-Path");
        script.Should().Contain("Path.GetRelativePath, which is unavailable in Windows PowerShell 5.1");
        script.Should().Contain("app-tester-release.yml");
    }

    [Fact]
    public void CrossPlatformPortabilityPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-CrossPlatformPortability.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Cross-platform portability checks passed");
    }
}
