using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal PresentationCommentNavigationPlan? LastCommentNavigationPlan =>
        _reviewWorkflowSession.LastCommentNavigationPlan;
    internal PresentationCommentMentionPickerPlan? LastCommentMentionPickerPlan =>
        _reviewWorkflowSession.LastCommentMentionPickerPlan;
    internal PresentationCommentMentionInsertionPlan? LastCommentMentionInsertionPlan =>
        _reviewWorkflowSession.LastCommentMentionInsertionPlan;
    internal PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan =>
        _reviewWorkflowSession.LastAccessibilitySummaryPlan;
    internal PresentationSlideTitleMutationPlan? LastSlideTitleMutationPlan =>
        _reviewWorkflowSession.LastSlideTitleMutationPlan;
    internal PresentationChartTitleMutationPlan? LastChartTitleMutationPlan =>
        _reviewWorkflowSession.LastChartTitleMutationPlan;
    internal PresentationTableHeaderRowMutationPlan? LastTableHeaderRowMutationPlan =>
        _reviewWorkflowSession.LastTableHeaderRowMutationPlan;
    internal PresentationTableStructureReviewPlan? LastTableStructureReviewPlan =>
        _reviewWorkflowSession.LastTableStructureReviewPlan;
    internal PresentationAltTextRequestPlan? LastAltTextRequestPlan =>
        _reviewWorkflowSession.LastAltTextRequestPlan;
    internal PresentationAltTextPanePlan? LastAltTextPanePlan =>
        _reviewWorkflowSession.LastAltTextPanePlan;
    internal PresentationProofingRequestPlan? LastProofingRequestPlan =>
        _reviewWorkflowSession.LastProofingRequestPlan;
    internal PresentationProofingExecutionPlan? LastProofingExecutionPlan =>
        _reviewWorkflowSession.LastProofingExecutionPlan;
    internal PresentationMediaTranscriptPlan? LastMediaTranscriptPlan =>
        _reviewWorkflowSession.LastMediaTranscriptPlan;
    internal PresentationMediaCaptionAuthoringMutationPlan? LastMediaCaptionAuthoringMutationPlan =>
        _mediaPaneHostCoordinator.LastCaptionAuthoringMutationPlan;
    internal PresentationMediaCaptionTrackMutationResult? LastMediaCaptionTrackMutationResult =>
        _mediaPaneHostCoordinator.LastCaptionTrackMutationResult;
    internal SmartArtTextPaneApplyResult? LastSmartArtTextPaneApplyResult =>
        _smartArtTextPaneSession.LastTextPaneApplyResult;
    internal SmartArtNodeEditResult? LastSmartArtTextPaneEditResult =>
        _smartArtTextPaneSession.LastTextPaneEditResult;
    internal SmartArtTextPaneKeyboardRoute? LastSmartArtTextPaneKeyboardRoute =>
        _smartArtTextPaneSession.LastKeyboardRoute;
    internal SmartArtColorApplyResult? LastSmartArtColorApplyResult =>
        _smartArtTextPaneSession.LastColorApplyResult;
    internal SmartArtDataPartRewriteResult? LastSmartArtDataPartRewriteResult =>
        _smartArtTextPaneSession.LastDataPartRewriteResult;
    internal SmartArtDrawingCacheRegenerationResult? LastSmartArtDrawingCacheRegenerationResult =>
        _smartArtTextPaneSession.LastDrawingCacheRegenerationResult;

    internal bool IsLayoutPickerVisible => _layoutPickerHost?.Visibility == Visibility.Visible;
    internal bool IsTablePickerVisible => _tablePickerHost?.Visibility == Visibility.Visible;
    internal int TablePickerChoiceButtonCount => LastTablePickerPlan?.Choices.Count ?? 0;
    internal int TablePickerDefaultChoiceCount =>
        LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0;
    internal int LayoutPickerChoiceButtonCount => LastLayoutPickerPlan?.Choices.Count ?? 0;
    internal int LayoutPickerGroupHeaderCount => LastLayoutPickerPlan?.Groups.Count ?? 0;
    internal int LayoutPickerThumbnailPlaceholderCount =>
        LastLayoutPickerPlan?.Choices.Sum(choice => choice.ThumbnailPlaceholders.Count) ?? 0;
    internal int LayoutPickerCurrentChoiceCount =>
        LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0;

    internal bool IsAltTextPaneVisible => _altTextPaneHostCoordinator.IsPaneVisible;
    internal bool IsAltTextPaneApplyEnabled => _altTextApplyButton?.IsEnabled == true;
    internal string AltTextPaneTitleLabel => _altTextTitleLabel?.Text ?? string.Empty;
    internal string AltTextPaneTitleText => _altTextTitleBox?.Text ?? string.Empty;
    internal string AltTextPaneTitlePlaceholder => LastAltTextPanePlan?.Title.Placeholder ?? string.Empty;
    internal string AltTextPaneDescriptionLabel => _altTextDescriptionLabel?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionText => _altTextDescriptionBox?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionPlaceholder =>
        LastAltTextPanePlan?.Description.Placeholder ?? string.Empty;
    internal bool IsAltTextPaneDecorativeChecked => _altTextDecorativeCheck?.IsChecked == true;
    internal string AltTextPaneMessage => _altTextPaneMessage?.Text ?? string.Empty;

    internal int SmartArtTextPaneRowCount =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>().Count() ?? 0;
    internal int SmartArtTextPaneSelectedRowCount =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>().Count(box =>
            box.Tag is SmartArtNodeOutlineItem item &&
            StringComparer.Ordinal.Equals(item.ModelId, _smartArtTextPaneSession.SelectedModelId)) ?? 0;
    internal int SmartArtTextPaneActionButtonCount => _smartArtTextPaneActionButtons.Count;
    internal int SmartArtTextPaneEnabledActionButtonCount =>
        _smartArtTextPaneActionButtons.Count(button => button.IsEnabled);
    internal string SmartArtTextPaneMessage => _smartArtTextPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> SmartArtTextPaneRenderedRows =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>()
            .Select(box => box.Tag is SmartArtNodeOutlineItem item
                ? $"{item.ModelId}|{item.Level}|{item.IsAssistant}|{box.Text}"
                : box.Text)
            .ToArray() ?? [];

    internal int AccessibilityCheckerPaneRowCount =>
        LastAccessibilityCheckerPanePlan?.Rows.Count ?? 0;
    internal int AccessibilityCheckerPaneSelectedRowCount =>
        LastAccessibilityCheckerPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal string AccessibilityCheckerPaneHeading =>
        _accessibilityCheckerPaneHeading?.Text ?? string.Empty;
    internal string AccessibilityCheckerPaneMessage =>
        _accessibilityCheckerPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> AccessibilityCheckerTableStructureReviewRenderedLines =>
        _accessibilityCheckerTableStructureReviewRenderedLines.ToArray();

    internal int ReviewCommentSelectedCount =>
        LastCommentPanePlan?.Comments.Count(comment => comment.IsSelected) ?? 0;
    internal bool IsReviewCommentsPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.ReviewComments);
    internal string ReviewCommentPaneSummary => LastCommentPanePlan?.DeckSummaryLabel ?? string.Empty;
    internal IReadOnlyList<string> ReviewCommentPaneFilterStates =>
        LastCommentPanePlan?.Filters.Select(filter =>
            $"{filter.Kind}|{filter.Label}|{filter.Count}|{filter.IsSelected}|{filter.HasMatches}").ToArray() ?? [];
    internal IReadOnlyList<string> ReviewCommentPaneRenderedMentionLines =>
        EnumerateCommentPaneText(_commentListPanel)
            .Where(PresentationSemanticIdentityCatalog.IsCommentMentionSummary)
            .ToArray();
    internal IReadOnlyList<string> ReviewCommentPaneRenderedMentionActions =>
        EnumerateCommentPaneButtons(_commentListPanel)
            .Where(button => button.Tag is string tag &&
                PresentationSemanticIdentityCatalog.IsCommentMentionTag(tag))
            .Select(button => $"{button.Tag}:{button.Content}:{button.IsEnabled}")
            .ToArray();

    internal bool IsReadingOrderPaneVisible => _readingOrderPaneHostCoordinator.IsPaneVisible;
    internal int ReadingOrderPaneItemCount => LastReadingOrderPlan?.Items.Count ?? 0;
    internal string ReadingOrderPaneHeading => _readingOrderPaneHeading?.Text ?? string.Empty;
    internal string ReadingOrderPaneMessage => _readingOrderPaneMessage?.Text ?? string.Empty;
    internal bool IsReadingOrderMoveEarlierEnabled => _readingOrderMoveEarlierButton?.IsEnabled == true;
    internal bool IsReadingOrderMoveLaterEnabled => _readingOrderMoveLaterButton?.IsEnabled == true;
    internal string? ReadingOrderMoveEarlierDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)?.DisabledReason;
    internal string? ReadingOrderMoveLaterDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)?.DisabledReason;

    internal int ProofingPaneIssueRowCount => LastProofingPanePlan?.Rows.Count ?? 0;
    internal int ProofingPaneSelectedIssueCount =>
        LastProofingPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal bool IsProofingPaneCorrectionEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingApplyCorrectionCommandId)?.IsEnabled == true;
    internal bool IsProofingPaneIgnoreEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreCommandId)?.IsEnabled == true;
    internal bool IsProofingPaneIgnoreAllEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreAllCommandId)?.IsEnabled == true;
    internal bool IsProofingPaneAddToDictionaryEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId)?.IsEnabled == true;
    internal string ProofingPaneHeading => _proofingPaneHeading?.Text ?? string.Empty;
    internal string ProofingPaneMessage => _proofingPaneMessage?.Text ?? string.Empty;

    internal string MediaCaptionPaneHeading => _mediaCaptionPaneHeading?.Text ?? string.Empty;
    internal string MediaCaptionPaneMessage => _mediaCaptionPaneMessage?.Text ?? string.Empty;
    internal int MediaCaptionPaneTrackCount => LastMediaCaptionAuthoringPanePlan?.Tracks.Count ?? 0;
    internal bool IsMediaCaptionCreateEnabled => _mediaCaptionCreateButton?.IsEnabled == true;
    internal bool IsMediaCaptionReplaceEnabled => _mediaCaptionReplaceButton?.IsEnabled == true;
    internal bool IsMediaCaptionDeleteEnabled => _mediaCaptionDeleteButton?.IsEnabled == true;
    internal string MediaCaptionPaneTranscriptText => _mediaCaptionTranscriptBox?.Text ?? string.Empty;
    internal int MediaVolumePercent => CaptureMediaVolumeHostSnapshot().NormalizedVolumePercent;
    internal bool IsMediaVolumeApplyEnabled => _mediaVolumeApplyButton?.IsEnabled == true;
    internal MediaPlaybackStartMode MediaPlaybackStartMode => CaptureMediaPlaybackHostSnapshot().StartMode;
    internal bool MediaLoop => _mediaLoopCheckBox?.IsChecked == true;
    internal bool MediaShowWhenStopped => _mediaShowWhenStoppedCheckBox?.IsChecked != false;
    internal bool MediaRewindAfterPlaying => _mediaRewindAfterPlayingCheckBox?.IsChecked == true;
    internal bool MediaPlayFullScreen => _mediaPlayFullScreenCheckBox?.IsChecked == true;
    internal int MediaStopAfterSlides => CaptureMediaPlaybackHostSnapshot().StopAfterSlides;
}
