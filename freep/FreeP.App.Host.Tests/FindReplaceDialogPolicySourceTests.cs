using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class FindReplaceDialogPolicySourceTests
{
    [Fact]
    public void FindReplaceDialogs_DelegateWorkflowOwnershipToPortableSession()
    {
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = ReadSource(repositoryRoot, "FreeP.App.Host");
        var avalonia = ReadSource(repositoryRoot, "FreeP.App.Avalonia");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FindReplaceDialogPlanner.BuildSurfacePlan()");
            source.Should().Contain("private readonly FindReplaceDialogSession _session;");
            source.Should().Contain("_session.SetQuery(");
            source.Should().Contain("_session.SetReplacement(");
            source.Should().Contain("_session.SetMatchCase(");
            source.Should().Contain("_session.SetWholeWord(");
            source.Should().Contain("_session.SetShowReplace(");
            source.Should().Contain("_session.Dispatch(");
            source.Should().Contain("ApplyWorkflowPlan(");
            source.Should().Contain("SetInputForTests(");
            source.Should().Contain("NavigateForTests(");
            source.Should().Contain("ReplaceAllForTests(");
            source.Should().Contain("LastWorkflowPlan => _session.LastWorkflowPlan");
            source.Should().Contain("ShowReplace => _session.ShowReplace");

            source.Should().NotContain("List<TextSearchMatch>");
            source.Should().NotContain("_currentMatchIndex");
            source.Should().NotContain("_editor.FindAll(");
            source.Should().NotContain("_editor.NavigateTo(");
            source.Should().NotContain("_editor.ReplaceOne(");
            source.Should().NotContain("_editor.ReplaceAll(");
            source.Should().NotContain("_session.Navigate(");
            source.Should().NotContain("_session.ReplaceCurrent(");
            source.Should().NotContain("_session.ReplaceAll(");
            source.Should().NotContain("FindReplaceDialogPlanner.Navigate(");
            source.Should().NotContain("\"Find what:\"");
            source.Should().NotContain("\"Replace with:\"");
            source.Should().NotContain("\"Match case\"");
            source.Should().NotContain("\"Whole word\"");
            source.Should().NotContain("\"Find Next\"");
            source.Should().NotContain("\"Find Previous\"");
            source.Should().NotContain("\"Replace All\"");
            source.Should().NotContain("\"No matches found.\"");
            source.Should().NotContain("\"No replacements made.\"");
            source.Should().NotContain("replacement(s) made.");
        }
    }

    [Fact]
    public void FindReplaceDialogSession_OwnsWorkflowAndRemainsRendererNeutral()
    {
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sessionSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "freep",
            "FreeP.App.Presentation",
            "FindReplaceDialogSession.cs"));
        var plannerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "freep",
            "FreeP.App.Presentation",
            "FindReplaceDialogPlanner.cs"));

        sessionSource.Should().Contain("private readonly List<TextSearchMatch> _matches");
        sessionSource.Should().Contain("private int _currentMatchIndex = -1;");
        sessionSource.Should().Contain("_editor.FindAll(");
        sessionSource.Should().Contain("_editor.NavigateTo(");
        sessionSource.Should().Contain("_editor.ReplaceOne(");
        sessionSource.Should().Contain("_editor.ReplaceAll(");
        sessionSource.Should().Contain("FindReplaceDialogPlanner.ReplacementTargetIndex(");
        sessionSource.Should().Contain("FindReplaceDialogPlanner.CanReplaceAll(");
        sessionSource.Should().Contain("FindReplaceDialogPlanner.ReplacementStatus(");
        sessionSource.Should().Contain("FindReplaceDialogPlanner.Navigate(");
        sessionSource.Should().Contain("FindReplaceDialogPlanner.BuildOptions(");
        sessionSource.Should().Contain("FindReplaceDialogPlanner.BuildWorkflowPlan(");
        sessionSource.Should().Contain("public FindReplaceWorkflowPlan Dispatch(FindReplaceDialogAction action)");
        sessionSource.Should().Contain("FindReplaceDialogAction.FindNext => Navigate(+1)");
        sessionSource.Should().Contain("FindReplaceDialogAction.FindPrevious => Navigate(-1)");
        sessionSource.Should().Contain("FindReplaceDialogAction.ReplaceCurrent => ReplaceCurrent()");
        sessionSource.Should().Contain("FindReplaceDialogAction.ReplaceAll => ReplaceAll()");
        sessionSource.Should().NotContain("System.Windows");
        sessionSource.Should().NotContain("Avalonia");

        plannerSource.Should().Contain("FindReplaceNavigationPolicyPlan Navigate(");
        plannerSource.Should().Contain("FindReplaceReplacementPolicyStatus ReplacementStatus(");
        plannerSource.Should().Contain("FindReplaceDialogPolicy.Navigate(");
        plannerSource.Should().Contain("FindReplaceDialogPolicy.BuildReplacementStatus(");
        plannerSource.Should().Contain("public sealed record FindReplaceDialogSurfacePlan(");
        plannerSource.Should().Contain("public static FindReplaceDialogInitialState BuildInitialState(");
        plannerSource.Should().NotContain("public enum FindReplaceStatusKind");
        plannerSource.Should().NotContain("public sealed record FindReplaceNavigationPlan");
        plannerSource.Should().NotContain("public sealed record FindReplaceReplacementStatus");
        plannerSource.Should().NotContain("ToLocalStatusKind");
        plannerSource.Should().NotContain("FindReplaceStatusKind.");
    }

    private static string ReadSource(string repositoryRoot, string projectName) =>
        File.ReadAllText(Path.Combine(
            repositoryRoot,
            "freep",
            projectName,
            "FindReplaceDialog.cs"));
}
