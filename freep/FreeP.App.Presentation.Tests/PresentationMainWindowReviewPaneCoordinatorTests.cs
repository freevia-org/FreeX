using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r154 remediation (M2): PresentationMainWindowReviewPaneCoordinator.ExecuteCommentCommand's
/// ResolveCommentCommandId branch must stamp the resolved comment with the real author identity
/// (PresentationReviewWorkflowSession.ResolveCommentAuthor()) instead of leaving resolvedBy null
/// and falling through to the planner's hard-coded "FreeP User" default. This drives the
/// coordinator's own dispatch -- the exact call site both WPF's and Avalonia's toolbar "Resolve"
/// button route through (freep/FreeP.App.Host/MainWindow.cs and
/// freep/FreeP.App.Avalonia/MainWindow.cs, both via
/// "_reviewPaneHostCoordinator.ExecuteCommentCommand(action.CommandId)") -- not just the
/// underlying session method, so a regression in the coordinator's argument wiring is caught
/// even if PresentationReviewWorkflowSession.ResolveSelectedComment itself stays correct.
/// </summary>
public sealed class PresentationMainWindowReviewPaneCoordinatorTests
{
    [Fact]
    public void ExecuteCommentCommand_Resolve_StampsRealDocumentAuthor_NotTheFreePUserDefault()
    {
        var (coordinator, session, editor) = CreateCoordinator(documentAuthor: "Dana Reviewer");
        var slide = editor.Presentation.Slides[0];
        slide.Comments.Add(new SlideComment
        {
            Author = "Original Reviewer",
            Initials = "OR",
            Text = "Needs a look.",
            Idx = 1,
        });
        session.SelectedCommentIndex = 0;

        coordinator.ExecuteCommentCommand(PresentationReviewWorkflowPlanner.ResolveCommentCommandId);

        var comment = slide.Comments.Single();
        comment.IsResolved.Should().BeTrue();
        comment.ResolvedBy.Should().Be("Dana Reviewer");
        comment.ResolvedBy.Should().NotBe("FreeP User");
    }

    [Fact]
    public void ExecuteCommentCommand_Resolve_FallsBackToOsAccountName_WhenDocumentAuthorIsBlank()
    {
        var (coordinator, session, editor) = CreateCoordinator(documentAuthor: "   ");
        var slide = editor.Presentation.Slides[0];
        slide.Comments.Add(new SlideComment
        {
            Author = "Original Reviewer",
            Initials = "OR",
            Text = "Needs a look.",
            Idx = 1,
        });
        session.SelectedCommentIndex = 0;

        coordinator.ExecuteCommentCommand(PresentationReviewWorkflowPlanner.ResolveCommentCommandId);

        slide.Comments.Single().ResolvedBy.Should().Be(Environment.UserName);
    }

    /// <summary>
    /// Sibling/no-regression case: Reopen must keep clearing resolution state (including
    /// ResolvedBy) without picking up any author stamping of its own -- reopening is not an
    /// authorship event, so this fix must not touch it.
    /// </summary>
    [Fact]
    public void ExecuteCommentCommand_Reopen_KeepsClearingResolvedByUnaffectedByThisFix()
    {
        var (coordinator, session, editor) = CreateCoordinator(documentAuthor: "Dana Reviewer");
        var slide = editor.Presentation.Slides[0];
        slide.Comments.Add(new SlideComment
        {
            Author = "Original Reviewer",
            Initials = "OR",
            Text = "Needs a look.",
            Idx = 1,
        });
        session.SelectedCommentIndex = 0;
        coordinator.ExecuteCommentCommand(PresentationReviewWorkflowPlanner.ResolveCommentCommandId);
        slide.Comments.Single().ResolvedBy.Should().Be("Dana Reviewer");

        coordinator.ExecuteCommentCommand(PresentationReviewWorkflowPlanner.ReopenCommentCommandId);

        var comment = slide.Comments.Single();
        comment.IsResolved.Should().BeFalse();
        comment.ResolvedBy.Should().BeNullOrEmpty();
    }

    private static (
        PresentationMainWindowReviewPaneCoordinator Coordinator,
        PresentationReviewWorkflowSession Session,
        EditingSession Editor) CreateCoordinator(string? documentAuthor)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Author = documentAuthor;
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = new PresentationReviewWorkflowSession(() => editor, NoOpCallbacks());
        var panes = new PresentationWorkareaPaneSession();
        var view = new DelegatingPresentationMainWindowReviewPaneView(NoOpViewBindings());
        var coordinator = new PresentationMainWindowReviewPaneCoordinator(session, panes, view);
        return (coordinator, session, editor);
    }

    private static PresentationReviewWorkflowSessionCallbacks NoOpCallbacks() => new(
        MarkDirty: () => { },
        RefreshCanvas: () => { },
        RefreshNotesPane: () => { },
        RenderAccessibilityCheckerPaneIfVisible: _ => { },
        PresentAccessibilityCheckerPane: _ => { },
        OpenAltTextPane: () => { },
        OpenHyperlinkDialog: () => { },
        OpenMediaCaptionPane: () => { },
        RenderCommentPane: _ => { },
        RenderAltTextPaneIfVisible: _ => { },
        RenderReadingOrderPaneIfVisible: _ => { },
        PresentReadingOrderPane: _ => { },
        RenderProofingPaneIfVisible: _ => { },
        PresentProofingPane: _ => { },
        UpdateAfterCommentMutation: () => { },
        UpdateAfterCommentNavigation: () => { },
        UpdateAfterProofingCorrection: () => { });

    private static PresentationMainWindowReviewPaneViewBindings NoOpViewBindings() => new(
        IsAccessibilityPaneVisible: () => false,
        IsProofingPaneVisible: () => false,
        SetAccessibilityPaneVisible: _ => { },
        SetProofingPaneVisible: _ => { },
        RenderAccessibilityPane: _ => { },
        RenderProofingPane: _ => { },
        RefreshPaneAccessibilityMetadata: () => { });
}
