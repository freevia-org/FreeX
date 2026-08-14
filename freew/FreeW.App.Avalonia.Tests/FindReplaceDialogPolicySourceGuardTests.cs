using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class FindReplaceDialogPolicySourceGuardTests
{
    [Fact]
    public void FindReplaceDialog_DelegatesOptionPolicyAndWorkflowToPresentationSession()
    {
        var source = ReadAvaloniaSource("FindReplaceDialog.cs");
        var commandHost = ReadAvaloniaSource("Editing", "AvaloniaFindReplaceCommandHost.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("FindReplaceDialogPlanner.Surface");
        source.Should().Contain("Surface.Actions");
        source.Should().Contain("Surface.Option(");
        source.Should().Contain("new FindReplaceDialogSession(");
        source.Should().Contain("SyncSessionInput()");
        source.Should().Contain("private FindReplaceDialogInput ReadInput()");
        source.Should().Contain("_session.Execute(action, ReadInput())");
        source.Should().NotContain("_session.FindNext()");
        source.Should().NotContain("_session.ReplaceNext()");
        source.Should().NotContain("_session.ReplaceAll()");
        source.Should().Contain("ApplyCompactCheckBox(_matchCase");
        source.Should().Contain("ApplyCompactCheckBox(_wholeWord");
        source.Should().Contain("ApplyCompactCheckBox(_useWildcards");
        source.Should().Contain("new AvaloniaFindReplaceCommandHost(_editor)");
        commandHost.Should().Contain("editor.FindNext(request.Term, request.Options)");
        commandHost.Should().Contain("editor.ReplaceNext(request.Term, request.Replacement, request.Options)");
        commandHost.Should().Contain("editor.ReplaceAll(request.Term, request.Replacement, request.Options)");
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
    public void MainWindowInlineFindBar_UsesSharedSessionAndCommandHost()
    {
        var source = ReadAvaloniaSource("MainWindow.cs");

        source.Should().Contain("FindReplaceDialogSession _inlineFindReplaceSession");
        source.Should().Contain("new AvaloniaFindReplaceCommandHost(_editor)");
        source.Should().Contain("_inlineFindReplaceSession.Execute(");
        source.Should().Contain("FindReplaceDialogActionKind.FindNext");
        source.Should().Contain("FindReplaceDialogActionKind.Replace");
        source.Should().Contain("FindReplaceDialogActionKind.ReplaceAll");
        source.Should().NotContain("$\"No match for \\\"{query}\\\".\"");
        source.Should().NotContain("$\"Replaced {n} occurrence");
    }

    [Fact]
    public void FindReplaceDialog_MatchesWpfChromeAndReactivationContract()
    {
        var avalonia = ReadAvaloniaSource("FindReplaceDialog.cs");
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FindReplaceDialog.cs"));

        avalonia.Should().Contain("Width = Surface.Metrics.WindowWidth");
        avalonia.Should().Contain("Surface.Metrics.OuterMargin");
        avalonia.Should().Contain("Surface.Metrics.ActionTopMargin");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.FocusAndSelect(");
        avalonia.Should().NotContain("PlaceholderText =");

        wpf.Should().Contain("Width = Surface.Metrics.WindowWidth");
        wpf.Should().Contain("DialogFocus.FocusAndSelect(");
        wpf.Should().Contain("new Thickness(Surface.Metrics.OuterMargin)");
    }

    [Fact]
    public void FindReplaceRenderersDelegateSpecialInsertionAndGoToProjectionToSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FindReplaceDialog.cs")),
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FindReplaceDialog.cs")),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("_session.PlanSpecialInsertion(");
            source.Should().Contain("_session.BuildGoToTargets(");
            source.Should().Contain("_session.PlanGoTo(");
            source.Should().Contain("Surface.Option(FindReplaceOptionKind.MatchCase).AutomationId");
            source.Should().Contain("Surface.GoToButtonAutomationId");
            source.Should().NotContain("FindReplaceDialogPlanner.BuildGoToTargets(");
            source.Should().NotContain("FindReplaceDialogPlanner.PlanGoTo(");
            source.Should().NotContain(".Insert(caret, text)");
            source.Should().NotContain("\"FindReplaceGoToButton\"");
            source.Should().NotContain("\"FindReplaceMatchCaseCheckBox\"");
        }
    }

    [Fact]
    public void DocumentView_UsesPresentationFindPlannerDirectly()
    {
        var source = ReadAvaloniaSource("Editing", "DocumentView.cs");

        source.Should().Contain("FindReplaceDialogPlanner.FindNextMatch(");
        source.Should().Contain("new FindReplaceSearchOptions()");
        source.Should().NotContain("DocumentSearch");
    }

    /// <summary>
    /// Guards the cross-app Find &amp; Replace open mode: FreeW must consume
    /// <c>Free.Shared.AppServices.FindReplaceOpenMode</c> (shared with FreeX and FreeP) and must
    /// not reintroduce the app-local <c>FindReplaceDialogOpenMode</c> enum that used to live in
    /// FreeW.App.Presentation.
    /// </summary>
    [Fact]
    public void FindReplaceOpenMode_IsOwnedByTheSharedPolicyNotFreeWPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var planner = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogPlanner.cs"));
        var session = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Presentation", "Dialogs", "FindReplaceDialogSession.cs"));
        var sharedPolicy = File.ReadAllText(Path.Combine(
            root, "shared", "Free.Shared.AppServices", "FindReplaceDialogPolicy.cs"));

        sharedPolicy.Should().Contain("public enum FindReplaceOpenMode");
        planner.Should().NotContain("enum FindReplaceOpenMode");
        planner.Should().NotContain("FindReplaceDialogOpenMode");
        session.Should().NotContain("enum FindReplaceOpenMode");
        session.Should().NotContain("FindReplaceDialogOpenMode");
        session.Should().Contain("using Free.Shared.AppServices;");
        session.Should().Contain("FindReplaceOpenMode openMode");

        foreach (var renderer in new[]
                 {
                     File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FindReplaceDialog.cs")),
                     File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FindReplaceDialog.cs")),
                 })
        {
            renderer.Should().Contain("FindReplaceOpenMode.Replace");
            renderer.Should().NotContain("FindReplaceDialogOpenMode");
        }
    }

    private static string ReadAvaloniaSource(params string[] relativeParts)
    {
        var path = Path.Combine(
            [TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew",
                "FreeW.App.Avalonia",
                .. relativeParts]);
        return File.ReadAllText(path);
    }

}
