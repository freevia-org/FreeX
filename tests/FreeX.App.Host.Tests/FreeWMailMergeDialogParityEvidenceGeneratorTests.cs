using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FreeWMailMergeDialogParityEvidenceGeneratorTests
{
    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void Check_PassesAgainstTheCommittedEvidenceOnTheRealRepositoryTree()
    {
        var repositoryRoot = WorkspaceFileLocator.FindWorkspaceRoot();

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-FreeWMailMergeDialogParityEvidence.ps1",
            repositoryRoot,
            "-Check");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().NotContain("Missing evidence input");
        result.Output.Should().Contain("Fresh: ");
    }

    [Fact]
    public void GeneratedInputs_OnlyNameSourceFilesThatActuallyExist()
    {
        // Sibling/no-regression guard for the same defect class: every path the generator hashes
        // must resolve on disk. This is what would have caught the deleted
        // FreeWAvaloniaRibbonDefinition.cs reference before it ever reached -Check.
        var repositoryRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var jsonPath = WorkspaceFileLocator.Find("docs", "parity", "freew-mail-merge-dialog-parity-20260720.json");
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));

        var generatedInputs = document.RootElement.GetProperty("generatedInputs");
        generatedInputs.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var input in generatedInputs.EnumerateArray())
        {
            var relativePath = input.GetString()!;
            var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(fullPath).Should().BeTrue($"generatedInputs entry '{relativePath}' must resolve to a real file");
        }
    }

    [Fact]
    public void GeneratedMarkdown_InterpolatesSchemaAndAuthorityInsteadOfLeakingDictionaryTypeNames()
    {
        // Sibling/no-regression guard for the garbled-placeholder half of the same finding:
        // the markdown template must not leak `$(...)`/OrderedDictionary ToString() text.
        var markdown = WorkspaceFileLocator.ReadAllText("docs", "parity", "freew-mail-merge-dialog-parity-20260720.md");

        markdown.Should().NotContain("System.Collections.Specialized.OrderedDictionary");
        markdown.Should().NotContain("$(evidence");
        markdown.Should().Contain("- Schema: `freex.freew.mail-merge-dialog-parity.v1`");
        markdown.Should().Contain("- Authority: `FreeW.App.Host WPF dialog and command behavior`");
    }
}
