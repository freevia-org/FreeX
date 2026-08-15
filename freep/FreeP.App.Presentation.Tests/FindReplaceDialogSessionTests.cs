using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class FindReplaceDialogSessionTests
{
    [Fact]
    public void Constructor_ProjectsCanonicalInitialState()
    {
        var (editor, _) = MakeSession("text");

        var session = new FindReplaceDialogSession(editor, showReplace: true);

        session.InitialState.Should().Be(new FindReplaceDialogInitialState(
            true,
            string.Empty,
            string.Empty,
            MatchCase: false,
            WholeWord: false));
        session.LastWorkflowPlan.ShowReplace.Should().BeTrue();
        session.LastWorkflowPlan.Query.Should().BeEmpty();
        session.LastWorkflowPlan.Replacement.Should().BeEmpty();
        session.Surface.Should().BeSameAs(FindReplaceDialogSurfaceCatalog.Surface);
    }

    [Fact]
    public void InputAndOptionChanges_OwnNormalizedStateAndInvalidateMatchPosition()
    {
        var (editor, _) = MakeSession("Cat catalog cat");
        var session = new FindReplaceDialogSession(editor);

        var input = session.SetInput(
            "Cat",
            "dog",
            matchCase: true,
            wholeWord: true);

        input.Query.Should().Be("Cat");
        input.Replacement.Should().Be("dog");
        input.MatchCase.Should().BeTrue();
        input.WholeWord.Should().BeTrue();
        input.MatchCount.Should().Be(0);
        input.CurrentMatchIndex.Should().Be(-1);
        input.CanSearch.Should().BeTrue();

        session.Navigate(+1).MatchCount.Should().Be(1);

        var changed = session.SetMatchCase(false);
        changed.MatchCount.Should().Be(0);
        changed.CurrentMatchIndex.Should().Be(-1);
        changed.StatusText.Should().BeEmpty();
        session.Navigate(+1).MatchCount.Should().Be(2);

        var replaceMode = session.SetShowReplace(true);
        replaceMode.ShowReplace.Should().BeTrue();
        replaceMode.Title.Should().Be(FindReplaceDialogPlanner.FindAndReplaceTitle);
        replaceMode.CanReplace.Should().BeTrue();
    }

    [Fact]
    public void Navigate_OwnsCurrentIndexWraparoundAndSuccessfulNavigationCallback()
    {
        var (editor, shape) = MakeSession("cat cat cat");
        var callbackCount = 0;
        var session = new FindReplaceDialogSession(editor, onNavigationOrMutation: () => callbackCount++);
        session.SetQuery("cat");

        var previous = session.Navigate(-1);
        previous.CurrentMatchIndex.Should().Be(1);
        previous.StatusText.Should().Be("Match 2 of 3");
        editor.SelectedShapeIds.Should().ContainSingle().Which.Should().Be(shape.Id);

        var next = session.Navigate(+1);
        next.CurrentMatchIndex.Should().Be(2);
        next.StatusText.Should().Be("Match 3 of 3");
        callbackCount.Should().Be(2);

        session.SetQuery("missing");
        session.Navigate(+1).StatusKind.Should().Be(FindReplacePolicyStatusKind.NoMatches);
        callbackCount.Should().Be(2);
    }

    [Fact]
    public void Dispatch_MapsCanonicalActionsToNavigationAndMutationPolicy()
    {
        var (editor, shape) = MakeSession("cat cat");
        var session = new FindReplaceDialogSession(editor, showReplace: true);
        session.SetInput("cat", "dog");

        session.Dispatch(FindReplaceDialogAction.FindNext).CurrentMatchIndex.Should().Be(0);
        session.Dispatch(FindReplaceDialogAction.FindPrevious).CurrentMatchIndex.Should().Be(1);
        session.Dispatch(FindReplaceDialogAction.ReplaceCurrent);
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("cat dog");

        var replaceAll = session.Dispatch(FindReplaceDialogAction.ReplaceAll);
        replaceAll.StatusText.Should().Be("1 replacement(s) made.");
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("dog dog");
    }

    [Fact]
    public void ReplaceCurrent_UsesFirstMatchThenRefreshesAndAdvances()
    {
        var (editor, shape) = MakeSession("cat cat");
        var callbackCount = 0;
        var session = new FindReplaceDialogSession(editor, showReplace: true, () => callbackCount++);
        session.SetInput("cat", "dog");

        var first = session.ReplaceCurrent();

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("dog cat");
        first.MatchCount.Should().Be(1);
        first.CurrentMatchIndex.Should().Be(0);
        first.StatusText.Should().Be("Match 1 of 1");
        callbackCount.Should().Be(2);

        var second = session.ReplaceCurrent();
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("dog dog");
        second.StatusKind.Should().Be(FindReplacePolicyStatusKind.NoMatches);
        callbackCount.Should().Be(3);
    }

    [Fact]
    public void ReplaceAll_OwnsValidationMutationAndReplacementStatusTransitions()
    {
        var (editor, shape) = MakeSession("cat cat");
        var callbackCount = 0;
        var session = new FindReplaceDialogSession(editor, showReplace: true, () => callbackCount++);
        session.SetInput(null, "dog");

        var missingQuery = session.ReplaceAll();
        missingQuery.StatusText.Should().Be(FindReplaceDialogPolicy.SearchTermRequiredMessage);
        callbackCount.Should().Be(0);

        session.SetInput("cat", "dog");
        var replaced = session.ReplaceAll();

        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("dog dog");
        replaced.StatusText.Should().Be("2 replacement(s) made.");
        replaced.StatusKind.Should().Be(FindReplacePolicyStatusKind.Replacements);
        replaced.MatchCount.Should().Be(0);
        callbackCount.Should().Be(1);

        editor.Undo();
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("cat cat");

        session.SetQuery("missing");
        var noReplacements = session.ReplaceAll();
        noReplacements.StatusText.Should().Be(FindReplaceDialogPolicy.NoReplacementsStatus);
        noReplacements.StatusKind.Should().Be(FindReplacePolicyStatusKind.NoReplacements);
        callbackCount.Should().Be(2);
    }

    [Fact]
    public void Session_UsesInjectedPolicyTextAcrossSearchAndReplacementTransitions()
    {
        var (editor, _) = MakeSession("cat cat");
        var text = new FindReplacePolicyTextSpec(
            "query required",
            "nothing found",
            "nothing replaced",
            "missing {0}",
            "result {0}/{1}",
            "changed {0}{1}",
            "changed total {0}");
        var session = new FindReplaceDialogSession(
            editor,
            showReplace: true,
            policyText: text);

        session.ReplaceAll().StatusText.Should().Be("query required");

        session.SetQuery("missing");
        session.Navigate(+1).StatusText.Should().Be("nothing found");
        session.ReplaceAll().StatusText.Should().Be("nothing replaced");

        session.SetInput("cat", "dog");
        session.Navigate(+1).StatusText.Should().Be("result 1/2");
        session.ReplaceAll().StatusText.Should().Be("changed total 2");
    }

    private static (EditingSession Editor, SlideShape Shape) MakeSession(string text)
    {
        var presentation = new Presentation
        {
            SlideSizeCxEmu = 12_192_000L,
            SlideSizeCyEmu = 6_858_000L,
        };
        presentation.Slides.Add(new Slide());
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        return (editor, editor.InsertTextBox(text));
    }

}
