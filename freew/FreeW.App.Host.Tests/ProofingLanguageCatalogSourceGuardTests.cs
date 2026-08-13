using System;
using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ProofingLanguageCatalogSourceGuardTests
{
    [Fact]
    public void FreeWRibbonCommands_UsesSharedProofingLanguageCatalog()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));

        source.Should().Contain("ProofingLanguageDialogPlanner.Build(current, UiText.Get)");
        source.Should().Contain("Title = plan.Text.Title");
        source.Should().NotContain("private static readonly (string Tag, string Label)[] Languages");
    }

    [Fact]
    public void FreeWRibbonCommands_UsesSharedProofingLanguageDialogPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));

        source.Should().Contain("ProofingLanguageDialogPlanner.Build(current, UiText.Get)");
        source.Should().Contain("choice.DisplayText");
        source.Should().Contain("Content = plan.Text.OkLabel");
        source.Should().NotContain("Content = $\"{choice.Label} [{choice.Tag}]\"");
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}
