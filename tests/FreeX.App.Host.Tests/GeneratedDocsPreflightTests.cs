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

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void GeneratedDocsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-GeneratedDocs.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Checking command inventory generated docs...");
        result.Output.Should().Contain("Checking FreeW command inventory generated docs...");
        result.Output.Should().Contain("Generated documentation checks passed.");
    }

    [Fact]
    public void FreeWJsonEvidenceGenerators_UseOneCanonicalJsonHostAcrossPlatforms()
    {
        var support = WorkspaceFileLocator.ReadAllText("tools", "ToolScriptSupport.ps1");
        support.Should().Contain("function Invoke-ToolCanonicalPwshHost");
        support.Should().Contain("if ($PSVersionTable.PSEdition -ne 'Desktop')");
        support.Should().Contain("& $pwshCommand.Source -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @ForwardedArguments");

        var generators = new[]
        {
            "Generate-FreeWEditingReferenceParityEvidence.ps1",
            "Generate-FreeWMailMergeDialogParityEvidence.ps1",
            "Generate-FreeWPageLayoutDialogParityEvidence.ps1",
            "Generate-FreeWShellPlatformParityEvidence.ps1",
            "Generate-FreeWShellVisualEvidence.ps1"
        };

        foreach (var generator in generators)
        {
            var script = WorkspaceFileLocator.ReadAllText("tools", generator);
            script.Should().Contain("Invoke-ToolCanonicalPwshHost", $"{generator} emits deterministic JSON");
        }
    }
}
