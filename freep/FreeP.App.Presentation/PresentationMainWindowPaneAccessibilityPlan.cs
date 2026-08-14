namespace FreeP.App.Compositor;

public sealed record PresentationMainWindowPaneAccessibilityState(
    bool IsVisible,
    int ItemCount = 0,
    int SelectedIndex = -1);

public sealed record PresentationMainWindowPaneAccessibilitySnapshot(
    int SlideCount,
    int SelectedSlideIndex,
    PresentationMainWindowPaneAccessibilityState Comments,
    PresentationMainWindowPaneAccessibilityState Accessibility,
    PresentationMainWindowPaneAccessibilityState AltText,
    PresentationMainWindowPaneAccessibilityState ReadingOrder,
    PresentationMainWindowPaneAccessibilityState Proofing,
    PresentationMainWindowPaneAccessibilityState MediaCaptions,
    PresentationMainWindowPaneAccessibilityState SmartArtText,
    PresentationMainWindowPaneAccessibilityState Selection,
    PresentationMainWindowPaneAccessibilityState Animation);

public sealed record PresentationMainWindowPaneAccessibilityNativeSnapshot(
    int AccessibilityItemCount,
    int ReadingOrderItemCount,
    int ProofingItemCount,
    int MediaCaptionItemCount,
    int MediaCaptionSelectedIndex,
    int SmartArtTextItemCount,
    int SmartArtTextSelectedIndex,
    int SelectionItemCount,
    int SelectionSelectedIndex,
    bool IsAnimationVisible,
    int AnimationItemCount,
    int AnimationSelectedIndex);

/// <summary>
/// Owns the stable pane order and accessibility state projection for the two FreeP main windows.
/// Native hosts retain their control lookup and attached-property writes.
/// </summary>
public static class PresentationMainWindowPaneAccessibilityPlan
{
    public static IReadOnlyList<PresentationPaneAccessibilityState> Build(
        PresentationReviewWorkflowSession reviewWorkflow,
        PresentationMediaPaneHostCoordinator mediaPane,
        PresentationWorkareaPaneSession panes,
        int slideCount,
        int selectedSlideIndex,
        PresentationMainWindowPaneAccessibilityNativeSnapshot native)
    {
        ArgumentNullException.ThrowIfNull(reviewWorkflow);
        ArgumentNullException.ThrowIfNull(mediaPane);
        ArgumentNullException.ThrowIfNull(panes);
        ArgumentNullException.ThrowIfNull(native);

        var comments = reviewWorkflow.LastCommentPanePlan;
        var accessibility = reviewWorkflow.LastAccessibilityCheckerPanePlan;
        var readingOrder = reviewWorkflow.LastReadingOrderPlan;
        var proofing = reviewWorkflow.LastProofingPanePlan;
        var captions = mediaPane.LastCaptionAuthoringPanePlan;
        return Build(new(
            slideCount,
            selectedSlideIndex,
            new(panes.IsVisible(PresentationWorkareaPane.ReviewComments),
                comments?.Comments.Count ?? 0, comments?.SelectedCommentIndex ?? -1),
            new(panes.IsVisible(PresentationWorkareaPane.AccessibilityChecker),
                accessibility?.Rows.Count ?? native.AccessibilityItemCount,
                accessibility?.SelectedRowIndex ?? -1),
            new(panes.IsVisible(PresentationWorkareaPane.AltText), 3),
            new(panes.IsVisible(PresentationWorkareaPane.ReadingOrder),
                readingOrder?.Items.Count ?? native.ReadingOrderItemCount,
                readingOrder?.SelectedItemIndex ?? -1),
            new(panes.IsVisible(PresentationWorkareaPane.Proofing),
                proofing?.Rows.Count ?? native.ProofingItemCount,
                proofing?.SelectedRowIndex ?? -1),
            new(panes.IsVisible(PresentationWorkareaPane.MediaCaption),
                captions?.Tracks.Count ?? native.MediaCaptionItemCount,
                captions?.SelectedTrackIndex ?? native.MediaCaptionSelectedIndex),
            new(panes.IsVisible(PresentationWorkareaPane.SmartArtText),
                native.SmartArtTextItemCount, native.SmartArtTextSelectedIndex),
            new(panes.IsVisible(PresentationWorkareaPane.Selection),
                native.SelectionItemCount, native.SelectionSelectedIndex),
            new(native.IsAnimationVisible, native.AnimationItemCount, native.AnimationSelectedIndex)));
    }

    public static IReadOnlyList<PresentationPaneAccessibilityState> Build(
        PresentationMainWindowPaneAccessibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return
        [
            new(PresentationPaneAccessibilityPlanner.SlidePaneId, true, snapshot.SlideCount, snapshot.SelectedSlideIndex),
            new(PresentationPaneAccessibilityPlanner.NotesPaneId, true, 1),
            From(PresentationPaneAccessibilityPlanner.CommentsPaneId, snapshot.Comments),
            From(PresentationPaneAccessibilityPlanner.AccessibilityPaneId, snapshot.Accessibility),
            From(PresentationPaneAccessibilityPlanner.AltTextPaneId, snapshot.AltText),
            From(PresentationPaneAccessibilityPlanner.ReadingOrderPaneId, snapshot.ReadingOrder),
            From(PresentationPaneAccessibilityPlanner.ProofingPaneId, snapshot.Proofing),
            From(PresentationPaneAccessibilityPlanner.MediaCaptionPaneId, snapshot.MediaCaptions),
            From(PresentationPaneAccessibilityPlanner.SmartArtTextPaneId, snapshot.SmartArtText),
            From(PresentationPaneAccessibilityPlanner.SelectionPaneId, snapshot.Selection),
            From(PresentationPaneAccessibilityPlanner.AnimationPaneId, snapshot.Animation),
        ];
    }

    private static PresentationPaneAccessibilityState From(
        string paneId,
        PresentationMainWindowPaneAccessibilityState state) =>
        new(paneId, state.IsVisible, state.ItemCount, state.SelectedIndex);
}
