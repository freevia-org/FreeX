using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ProofingArchitectureSourceGuardTests
{
    [Fact]
    public void Renderers_DelegateSpellPolicyToSharedSession()
    {
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.Spelling.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new SpellCheckSessionController(new SpellCheckSessionAdapter(");
            source.Should().Contain("controller.Apply(");
            source.Should().NotContain("SpellCheckWorkflowPlanner.ScanWorksheet(");
            source.Should().NotContain("SpellCheckWorkflowPlanner.BuildReplacementCommand(");
            source.Should().NotContain("SpellCheckWorkflowPlanner.BuildReplaceAllCommand(");
            source.Should().NotContain("HashSet<SpellingIssueKey>");
        }

        avalonia.Should().NotContain("TokenizeWords(");
        avalonia.Should().NotContain("LocateWord(");
        File.Exists(Path.Combine(RepositoryRoot(), "src", "FreeX.App.Avalonia", "SpellingWordList.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void ProofingOptionsThesaurusAndTranslation_UseSharedPolicies()
    {
        var wpfOptions = Read("src", "FreeX.App.Host", "OptionsDialog.xaml.cs");
        var avaloniaOptions = Read("src", "FreeX.App.Avalonia", "MainWindow.Options.cs");
        var avaloniaProofing = Read("src", "FreeX.App.Avalonia", "MainWindow.Proofing.cs");
        var optionsSession = Read("src", "FreeX.App.Services", "FreeXOptionsDialogSession.cs");

        optionsSession.Should().Contain("CustomDictionary = new CustomDictionaryEditorSession(");
        optionsSession.Should().Contain("public CustomDictionaryEditorSession CustomDictionary { get; }");
        wpfOptions.Should().Contain("private readonly FreeXOptionsDialogSession _dialogSession;");
        wpfOptions.Should().Contain("_customDictionaryEditor = _dialogSession.CustomDictionary;");
        avaloniaOptions.Should().Contain("var customDictionaryEditor = optionsDialogSession.CustomDictionary;");
        wpfOptions.Should().NotContain("new CustomDictionaryEditorSession(");
        avaloniaOptions.Should().NotContain("new CustomDictionaryEditorSession(");
        wpfOptions.Should().NotContain("SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext");
        avaloniaOptions.Should().NotContain("SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext");

        avaloniaProofing.Should().Contain("ThesaurusWorkflowPlanner.TryCreateLookup(");
        avaloniaProofing.Should().Contain("ThesaurusWorkflowPlanner.ApplyReplacement(");
        avaloniaProofing.Should().Contain("TranslateDialogPlanner.BuildCommand(plan)");
        avaloniaProofing.Should().NotContain("FirstAlphabeticWord(");
        File.Exists(Path.Combine(RepositoryRoot(), "src", "FreeX.App.Avalonia", "ThesaurusData.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void ReviewWorkflow_ReusesProofingNavigationAndDisplayPolicies()
    {
        var reviewPlanner = Read("src", "FreeX.App.Services", "ReviewWorkflowPlanner.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");

        reviewPlanner.Should().Contain("SpellCheckWorkflowPlanner.FilterIssues(");
        reviewPlanner.Should().Contain("CommentNavigationPlanner.OrderedNoteAddresses(");
        reviewPlanner.Should().Contain("CommentNavigationPlanner.FindNext(");
        reviewPlanner.Should().NotContain("BuildCommandForIssueText(");
        reviewPlanner.Should().NotContain("FindFirstAfter(");
        avalonia.Should().Contain("ReviewWorkflowPlanner.CreateDisplayModel(plan)");
        avalonia.Should().NotContain("private static string FormatReviewWorkflowSummary(");
        avalonia.Should().NotContain("private static string FormatSpellingIssueSource(");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. path]));

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
