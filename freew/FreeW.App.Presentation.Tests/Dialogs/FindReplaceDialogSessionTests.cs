using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FindReplaceDialogSessionTests
{
    [Fact]
    public void SetInput_NormalizesWildcardOptionsAndPlansOptionEnablement()
    {
        var session = new FindReplaceDialogSession(new RecordingCommandHost());

        var state = session.SetInput(
            "f*x",
            "wolf",
            matchCase: true,
            wholeWord: true,
            useWildcards: true);

        state.Query.Should().Be("f*x");
        state.Replacement.Should().Be("wolf");
        state.Options.Should().Be(new FindReplaceSearchOptions(
            MatchCase: true,
            WholeWord: false,
            UseWildcards: true));
        state.WholeWordEnabled.Should().BeFalse();
    }

    [Fact]
    public void MissingQuery_ReportsValidationWithoutInvokingCommandHost()
    {
        var host = new RecordingCommandHost();
        var session = new FindReplaceDialogSession(host);
        session.SetInput(null, "wolf", false, false, false);

        session.FindNext().StatusText.Should().Be(FindReplaceDialogPlanner.SearchTermRequiredMessage);
        session.ReplaceNext().StatusText.Should().Be(FindReplaceDialogPlanner.SearchTermRequiredMessage);
        session.ReplaceAll().StatusText.Should().Be(FindReplaceDialogPlanner.SearchTermRequiredMessage);

        host.FindRequest.Should().BeNull();
        host.ReplaceRequest.Should().BeNull();
        host.ReplaceAllRequest.Should().BeNull();
    }

    [Fact]
    public void FindAndReplace_RouteNormalizedRequestsAndOwnStatusTransitions()
    {
        var host = new RecordingCommandHost
        {
            FindResult = false,
            ReplaceResult = true,
        };
        var session = new FindReplaceDialogSession(host);
        session.SetInput("fox", "wolf", true, true, false);

        session.FindNext().StatusText.Should().Be("\"fox\" not found.");
        host.FindRequest.Should().Be(new FindReplaceSearchRequest(
            "fox",
            new FindReplaceSearchOptions(MatchCase: true, WholeWord: true, UseWildcards: false)));

        session.ReplaceNext().StatusText.Should().BeEmpty();
        host.ReplaceRequest.Should().Be(new FindReplaceReplaceRequest(
            "fox",
            "wolf",
            new FindReplaceSearchOptions(MatchCase: true, WholeWord: true, UseWildcards: false)));
    }

    [Fact]
    public void Execute_AppliesTheInputSchemaAndDispatchesTheSharedAction()
    {
        var host = new RecordingCommandHost { ReplaceResult = true };
        var session = new FindReplaceDialogSession(host);

        var state = session.Execute(
            FindReplaceDialogActionKind.Replace,
            new FindReplaceDialogInput(
                "f*x",
                "wolf",
                MatchCase: true,
                WholeWord: true,
                UseWildcards: true));

        state.Options.WholeWord.Should().BeFalse();
        host.ReplaceRequest.Should().Be(new FindReplaceReplaceRequest(
            "f*x",
            "wolf",
            new FindReplaceSearchOptions(MatchCase: true, WholeWord: false, UseWildcards: true)));
    }

    [Fact]
    public void ReplaceAll_ComposesSelectionAwareStatusFromHostResult()
    {
        var host = new RecordingCommandHost
        {
            ReplaceAllResult = new FindReplaceAllExecutionResult(2, InSelection: true),
        };
        var session = new FindReplaceDialogSession(host, FindReplaceDialogOpenMode.Replace);
        session.SetInput("fox", "wolf", false, false, false);

        var state = session.ReplaceAll();

        state.OpenMode.Should().Be(FindReplaceDialogOpenMode.Replace);
        state.StatusText.Should().Be("Replaced 2 occurrences in selection.");
        host.ReplaceAllRequest.Should().Be(new FindReplaceReplaceRequest(
            "fox",
            "wolf",
            new FindReplaceSearchOptions()));
    }

    [Fact]
    public void ActivationAndExternalStatus_RemainPartOfSessionState()
    {
        var session = new FindReplaceDialogSession(new RecordingCommandHost());

        session.ActivateFor(FindReplaceDialogOpenMode.Replace).OpenMode
            .Should().Be(FindReplaceDialogOpenMode.Replace);
        session.SetStatus("Jumped to Document end.").StatusText
            .Should().Be("Jumped to Document end.");
    }

    [Fact]
    public void SpecialInsertionClampsTheCaretAndReturnsTheProjectedTextAndCaret()
    {
        var session = new FindReplaceDialogSession(new RecordingCommandHost());

        session.PlanSpecialInsertion("abc", caretIndex: 99, "^p")
            .Should().Be(new FindReplaceTextInsertionPlan("abc^p", 5));
        session.PlanSpecialInsertion(null, caretIndex: -4, "?")
            .Should().Be(new FindReplaceTextInsertionPlan("?", 1));
    }

    [Fact]
    public void GoToProjectionOwnsSelectionFallbackAndStatusTransition()
    {
        var document = TextDocument.CreateEmpty();
        var session = new FindReplaceDialogSession(new RecordingCommandHost());

        var targets = session.BuildGoToTargets(document, previousSelectedIndex: 99);
        var execution = session.PlanGoTo(targets.Targets[1], document.Blocks.Count);

        targets.SelectedIndex.Should().Be(0);
        targets.Targets.Select(target => target.Kind).Take(2).Should().Equal(
            FindReplaceGoToTargetKind.DocumentStart,
            FindReplaceGoToTargetKind.DocumentEnd);
        execution.Should().NotBeNull();
        execution!.Kind.Should().Be(FindReplaceGoToTargetKind.DocumentEnd);
        session.State.StatusText.Should().Be(execution.StatusText);
    }

    private sealed class RecordingCommandHost : IFindReplaceDialogCommandHost
    {
        public bool FindResult { get; init; }

        public bool ReplaceResult { get; init; }

        public FindReplaceAllExecutionResult ReplaceAllResult { get; init; }

        public FindReplaceSearchRequest? FindRequest { get; private set; }

        public FindReplaceReplaceRequest? ReplaceRequest { get; private set; }

        public FindReplaceReplaceRequest? ReplaceAllRequest { get; private set; }

        public bool FindNext(FindReplaceSearchRequest request)
        {
            FindRequest = request;
            return FindResult;
        }

        public bool ReplaceNext(FindReplaceReplaceRequest request)
        {
            ReplaceRequest = request;
            return ReplaceResult;
        }

        public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request)
        {
            ReplaceAllRequest = request;
            return ReplaceAllResult;
        }
    }
}
