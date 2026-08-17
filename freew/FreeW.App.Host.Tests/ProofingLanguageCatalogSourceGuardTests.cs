using System;
using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ProofingLanguageCatalogSourceGuardTests
{
    // The dialog moved out of FreeWRibbonCommands into its own ProofingLanguageDialog, so the guard
    // follows it: the ribbon must only delegate, and the dialog must be the one consuming the shared
    // planner rather than re-deriving the language catalog or its labels.
    [Fact]
    public void ProofingLanguageDialog_UsesSharedProofingLanguageCatalog()
    {
        var ribbon = ReadHostSource("Ribbon", "FreeWRibbonCommands.cs");
        var dialog = ReadHostSource("ProofingLanguageDialog.cs");

        ribbon.Should().Contain("ProofingLanguageDialog.Choose(owner, current)");
        ribbon.Should().NotContain("ProofingLanguageDialogPlanner.Build(");
        dialog.Should().Contain("ProofingLanguageDialogPlanner.Build(currentTag, UiText.Get)");
        dialog.Should().Contain("Title = plan.Text.Title");
        dialog.Should().NotContain("private static readonly (string Tag, string Label)[] Languages");
    }

    [Fact]
    public void ProofingLanguageDialog_UsesSharedProofingLanguageDialogPlanner()
    {
        var dialog = ReadHostSource("ProofingLanguageDialog.cs");

        dialog.Should().Contain("ProofingLanguageDialogPlanner.Build(currentTag, UiText.Get)");
        dialog.Should().Contain("choice.DisplayText");
        dialog.Should().Contain("acceptContent: plan.Text.OkLabel");
        dialog.Should().Contain("cancelContent: plan.Text.CancelLabel");
        dialog.Should().NotContain("Content = $\"{choice.Label} [{choice.Tag}]\"");
    }

    private static string ReadHostSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            [FindRepositoryRoot(), "freew", "FreeW.App.Host", .. parts]));

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}
