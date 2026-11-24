using FreeP.App.Compositor;
using FreeP.Core.Model;

#if FREEP_WPF_RENDERER
namespace FreeP.App.Host;
#elif FREEP_AVALONIA_RENDERER
namespace FreeP.App.Avalonia;
#else
#error A FreeP renderer symbol is required.
#endif

/// <summary>
/// The review / comment / alt-text / reading-order / proofing delegate facade. Both hosts exposed
/// these members as identical one-line forwards onto the shared review session and pane
/// coordinators; they live here once and compile into each shell.
/// </summary>
public sealed partial class MainWindow
{
    internal void RefreshReviewWorkflowPlans()
    {
        _reviewWorkflowSession.RefreshReviewWorkflowPlans();
        RefreshPaneAccessibilityMetadata();
    }

    internal PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        _workareaSession.Panes.Show(PresentationWorkareaPane.ReviewComments);
        return _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    private void SelectReviewComment(int commentIndex) => _reviewWorkflowSession.SelectReviewComment(commentIndex);
    internal PresentationCommentNavigationPlan NavigateReviewComment(PresentationReviewWorkflowIntentKind intent) => _reviewWorkflowSession.NavigateReviewComment(intent);
    internal PresentationCommentMutationPlan DeleteSelectedComment() => _reviewWorkflowSession.DeleteSelectedComment();
    internal PresentationCommentMutationPlan AddComment(string? text, DateTime? timestamp = null, string? author = null, string? initials = null, long xemu = 0, long yemu = 0) => _reviewWorkflowSession.AddComment(text, timestamp, author, initials, xemu, yemu);
    internal PresentationCommentMutationPlan EditSelectedComment(string? text, string? author = null, string? initials = null) => _reviewWorkflowSession.EditSelectedComment(text, author, initials);
    internal PresentationCommentMutationPlan ResolveSelectedComment(DateTime? resolvedAt = null, string? resolvedBy = null) => _reviewWorkflowSession.ResolveSelectedComment(resolvedAt, resolvedBy);
    internal PresentationCommentMutationPlan ReopenSelectedComment() => _reviewWorkflowSession.ReopenSelectedComment();
    internal PresentationCommentMutationPlan ReplyToSelectedComment(string? text, DateTime? timestamp = null, string? author = null, string? initials = null) => _reviewWorkflowSession.ReplyToSelectedComment(text, timestamp, author, initials);

    /// <summary>
    /// Resolves the real author identity to stamp on a new comment, reply, or resolution.
    /// Delegates to the shared session so WPF and Avalonia resolve identity through the one
    /// place instead of each host carrying its own copy of the resolution chain (document
    /// author, then OS account name, mirroring FreeW's ReviewAuthorIdentityPlanner.ResolveAuthor
    /// minus the revision-author override FreeW has and FreeP does not).
    /// </summary>
    internal string? ResolveCommentAuthor() => _reviewWorkflowSession.ResolveCommentAuthor();

    private string? GetSelectedCommentText() => _reviewWorkflowSession.GetSelectedCommentText();

    private string? GetCommentText(int commentIndex) => _reviewWorkflowSession.GetCommentText(commentIndex);

    internal PresentationAccessibilityCheckerPanePlan ShowAccessibilityCheckerPane() { var plan = _reviewWorkflowSession.ShowAccessibilityCheckerPane(); RefreshPaneAccessibilityMetadata(); return plan; }

    internal PresentationAccessibilityCheckerPanePlan SelectAccessibilityCheckerRow(int rowIndex)
        => _reviewWorkflowSession.SelectAccessibilityCheckerRow(rowIndex);

    internal PresentationAccessibilityCheckerPanePlan ApplyAccessibilityCheckerRowAction(int rowIndex)
        => _reviewWorkflowSession.ApplyAccessibilityCheckerRowAction(rowIndex);

    private void RefreshAltTextRequestPlan()
        => _altTextPaneHostCoordinator.RefreshSelection();

    internal void ShowAltTextPane() => _altTextPaneHostCoordinator.Show();

    internal void HideAltTextPane() => _altTextPaneHostCoordinator.Hide();

    internal PresentationReadingOrderPlan ShowReadingOrderPane()
        => _reviewWorkflowSession.ShowReadingOrderPane();

    internal PresentationReadingOrderMutationPlan ApplyReadingOrderMoveEarlier()
        => ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier);

    internal PresentationReadingOrderMutationPlan ApplyReadingOrderMoveLater()
        => ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);

    internal PresentationReadingOrderSelectionPlan ApplyReadingOrderSelectItem(uint shapeId)
        => _reviewWorkflowSession.SelectReadingOrderItem(shapeId);

    private PresentationReadingOrderMutationPlan ApplyReadingOrderMove(
        PresentationReviewWorkflowIntentKind intent)
        => _reviewWorkflowSession.ApplyReadingOrderMove(intent);

    internal void SetAltTextPaneInput(string title, string description, bool isDecorative)
        => _altTextPaneHostCoordinator.SetInput(new(title, description, isDecorative));

    internal PresentationAltTextMutationPlan ApplyAltTextPane() =>
        _altTextPaneHostCoordinator.Apply();

    private void RefreshVisibleAltTextPaneFromFields() => _altTextPaneHostCoordinator.Refresh();

    private void RenderAltTextPaneIfVisible(PresentationAltTextPanePlan plan) =>
        _altTextPaneHostCoordinator.RenderIfVisible(plan);

    internal PresentationAltTextMutationPlan ApplySelectedShapeAlternativeText(string? description, string? title = null, bool isDecorative = false) => _reviewWorkflowSession.ApplySelectedShapeAlternativeText(description, title, isDecorative);
    internal PresentationProofingCorrectionMutationPlan ApplyProofingCorrection(PresentationProofingScopeDescriptor scope, int start, int length, string? replacement) => _reviewWorkflowSession.ApplyProofingCorrection(scope, start, length, replacement);
    private void RefreshProofingRequestPlan() => _reviewWorkflowSession.RefreshProofingRequestPlan();
    internal PresentationProofingPanePlan ShowProofingPane() => _reviewPaneHostCoordinator.ShowProofingPane();
    internal PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex) => _reviewWorkflowSession.SelectProofingIssueRow(rowIndex);
    internal PresentationProofingCorrectionMutationPlan ApplySelectedProofingCorrection() => _reviewWorkflowSession.ApplySelectedProofingCorrection();
    internal PresentationProofingPanePlan IgnoreSelectedProofingIssue() => _reviewWorkflowSession.IgnoreSelectedProofingIssue();
    internal PresentationProofingPanePlan IgnoreAllSelectedProofingIssues() => _reviewWorkflowSession.IgnoreAllSelectedProofingIssues();
    internal PresentationProofingPanePlan AddSelectedProofingWordToDictionary() => _reviewWorkflowSession.AddSelectedProofingWordToDictionary();
}
