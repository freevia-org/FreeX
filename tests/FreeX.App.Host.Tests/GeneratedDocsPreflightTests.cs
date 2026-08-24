using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GeneratedDocsPreflightTests
{
    [Fact]
    public void GeneratedDocsPreflight_RunsAllGeneratedDocumentationChecks()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-GeneratedDocs.ps1");

        script.Should().Contain("Generate-CommandInventoryDocs.ps1");
        script.Should().Contain("Generate-FreeWCommandInventory.ps1");
        script.Should().Contain("& pwsh -NoProfile -File $resolvedScriptPath -Check");
        script.Should().Contain("Generated documentation checks passed.");
    }

    [Fact]
    public void GeneratedDocsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-GeneratedDocs.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Checking command inventory generated docs...");
        result.Output.Should().Contain("Checking FreeW command inventory generated docs...");
        result.Output.Should().Contain("Generated documentation checks passed.");
    }
}
