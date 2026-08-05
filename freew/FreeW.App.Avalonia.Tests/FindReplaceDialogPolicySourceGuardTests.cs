using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class FindReplaceDialogPolicySourceGuardTests
{
    [Fact]
    public void FindReplaceDialog_DelegatesOptionPolicyAndWorkflowToPresentationSession()
    {
        var source = ReadAvaloniaSource("FindReplaceDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("FindReplaceDialogPlanner.LabelFor(");
        source.Should().Contain("new FindReplaceDialogSession(");
        source.Should().Contain("SyncSessionInput()");
        source.Should().Contain("_session.FindNext()");
        source.Should().Contain("_session.ReplaceNext()");
        source.Should().Contain("_session.ReplaceAll()");
        source.Should().Contain("ApplyCompactCheckBox(_matchCase");
        source.Should().Contain("ApplyCompactCheckBox(_wholeWord");
        source.Should().Contain("ApplyCompactCheckBox(_useWildcards");
        source.Should().Contain("editor.FindNext(request.Term, request.Options)");
        source.Should().Contain("editor.ReplaceNext(request.Term, request.Replacement, request.Options)");
        source.Should().Contain("editor.ReplaceAll(request.Term, request.Replacement, request.Options)");
        source.Should().NotContain("TextSearch.FindAll(");
        source.Should().NotContain("internal static int CountMatches(");
        source.Should().NotContain("FindReplaceDialogPlanner.TryCreateSearchRequest(");
        source.Should().NotContain("FindReplaceDialogPlanner.TryCreateReplaceRequest(");
        source.Should().NotContain("FindReplaceDialogPlanner.BuildFindStatus(");
        source.Should().NotContain("FindReplaceDialogPlanner.BuildReplaceStatus(");
        source.Should().NotContain("FindReplaceDialogPlanner.BuildReplaceAllStatus(");
        source.Should().NotContain("Content = \"Match case\"");
        source.Should().NotContain("Content = \"Whole word\"");
        source.Should().NotContain("Content = \"Use wildcards");
        source.Should().NotContain("\"Enter a search term.\"");
        source.Should().NotContain("not found.\"");
        source.Should().NotContain("Replaced {count}");
    }

    [Fact]
    public void FindReplaceDialog_MatchesWpfChromeAndReactivationContract()
    {
        var avalonia = ReadAvaloniaSource("FindReplaceDialog.cs");
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FindReplaceDialog.cs"));

        avalonia.Should().Contain("Width = 420");
        avalonia.Should().Contain("new Thickness(14, 14, 14, 0)");
        avalonia.Should().Contain("new Thickness(14, 10, 14, 14)");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.FocusAndSelect(");
        avalonia.Should().NotContain("PlaceholderText =");

        wpf.Should().Contain("Width = 420");
        wpf.Should().Contain("DialogFocus.FocusAndSelect(");
        wpf.Should().Contain("new Thickness(14)");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia", fileName);
        return File.ReadAllText(path);
    }

}
