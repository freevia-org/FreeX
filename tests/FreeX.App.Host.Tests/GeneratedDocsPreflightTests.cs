using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GeneratedDocsPreflightTests
{
    [Fact]
    public void GeneratedDocsPreflight_RunsAllGeneratedDocumentationChecks()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-GeneratedDocs.ps1"));

        script.Should().Contain("Generate-CommandInventoryDocs.ps1");
        script.Should().Contain("& $resolvedScriptPath -Check");
        script.Should().Contain("Generated documentation checks passed.");
    }

    [Fact]
    public void GeneratedDocsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GeneratedDocs.ps1");
        using var workingDirectory = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path);

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Checking command inventory generated docs...");
        result.Output.Should().Contain("Generated documentation checks passed.");
    }
}
