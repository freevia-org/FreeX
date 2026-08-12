using Avalonia.Automation;
using Avalonia.Controls;
using Free.Shared.Shell;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool HasToolbar => _ribbonControl is not null;
    internal int SlideCount => _presentation.Slides.Count;
    internal int SlidePaneSlideItemCount => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is int);
    internal int SlidePaneSectionHeaderCount => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is SlidePaneSectionHeaderTag);
    internal bool IsSlidePaneInsertionIndicatorVisible => _slidePaneInsertionIndicator.IsVisible;
    internal bool IsSlidePaneNewSlideButtonVisible => _slidePaneNewSlideButton.IsVisible;
    internal string? SlidePaneNewSlideButtonText => _slidePaneNewSlideButton.Content?.ToString();
    internal string? SlidePaneNewSlideButtonAutomationName =>
        AutomationProperties.GetName(_slidePaneNewSlideButton);
    internal IReadOnlyList<SlidePaneThumbnailVisualPlan> SlidePaneRenderedThumbnailPlans =>
        _workareaSession.SlidePaneSession.Projection.Items
            .Select(item => item.Thumbnail)
            .OfType<SlidePaneThumbnailVisualPlan>()
            .ToArray();
    internal IReadOnlyList<SlidePaneSectionHeaderVisualPlan> SlidePaneRenderedSectionHeaderPlans =>
        _workareaSession.SlidePaneSession.Projection.Items
            .Select(item => item.SectionHeader)
            .OfType<SlidePaneSectionHeaderVisualPlan>()
            .ToArray();
    internal int DirtyGeneration => _fileWorkflow.DirtyGeneration;

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
    internal AnimationPaneTimelinePlan? LastAnimationPaneTimelinePlan => _animationPaneSession.Timeline;
    internal AnimationPaneWorkflowEvidencePlan? LastAnimationPaneWorkflowEvidencePlan =>
        _animationPaneSession.WorkflowEvidence;
    internal AnimationPanePlaybackSessionPlan? LastAnimationPanePlaybackSessionPlan =>
        _animationPaneSession.Playback;
    internal AnimationPanePlaybackWorkflowEvidencePlan? LastAnimationPanePlaybackWorkflowEvidencePlan =>
        _animationPaneSession.PlaybackWorkflowEvidence;
    internal IReadOnlyList<string> LastVideoFrameImageDiagnostics =>
        _fileSession.LastVideoFrameImageDiagnostics;

    internal bool IsLayoutPickerVisible => _layoutPickerHost?.IsVisible == true;
    internal bool IsTablePickerVisible => _tablePickerHost?.IsVisible == true;
    internal SlideSizeDialog? ActiveSlideSizeDialog => _slideSizeDialog;
    internal HeaderFooterDialog? ActiveHeaderFooterDialog => _headerFooterDialog;
    internal SlideShowSettingsDialog? ActiveSlideShowSettingsDialog => _slideShowSettingsDialog;
    internal int TablePickerChoiceButtonCount => LastTablePickerPlan?.Choices.Count ?? 0;
    internal int TablePickerDefaultChoiceCount =>
        LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0;
    internal int LayoutPickerChoiceButtonCount => LastLayoutPickerPlan?.Choices.Count ?? 0;
    internal int LayoutPickerGroupHeaderCount => LastLayoutPickerPlan?.Groups.Count ?? 0;
    internal int LayoutPickerThumbnailPlaceholderCount =>
        LastLayoutPickerPlan?.Choices.Sum(choice => choice.ThumbnailPlaceholders.Count) ?? 0;
    internal int LayoutPickerCurrentChoiceCount =>
        LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0;

    internal bool IsReviewCommentsPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.ReviewComments);
    internal int ReviewCommentsPaneCommentCount => LastCommentPanePlan?.Comments.Count ?? 0;
    internal int ReviewCommentsPaneActionButtonCount => LastCommentPanePlan?.Actions.Count ?? 0;
    internal int ReviewCommentsPaneSelectedCommentCount =>
        LastCommentPanePlan?.Comments.Count(comment => comment.IsSelected) ?? 0;
    internal string ReviewCommentsPaneSummary => LastCommentPanePlan?.DeckSummaryLabel ?? string.Empty;
    internal IReadOnlyList<string> ReviewCommentsPaneFilterStates =>
        LastCommentPanePlan?.Filters.Select(filter =>
            $"{filter.Kind}|{filter.Label}|{filter.Count}|{filter.IsSelected}|{filter.HasMatches}").ToArray() ?? [];
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedActionStates =>
        EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .Where(button => button.Tag is string commandId &&
                commandId.StartsWith("freep.review.comments.", StringComparison.Ordinal))
            .Select(button => $"{button.Tag}|{button.Content}|{button.IsEnabled}")
            .ToArray();
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedMentionLines =>
        EnumerateReviewPaneText(_reviewCommentsPanePanel)
            .Where(PresentationSemanticIdentityCatalog.IsCommentMentionSummary)
            .ToArray();
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedMentionActions =>
        EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .Where(button => button.Tag is string tag &&
                PresentationSemanticIdentityCatalog.IsCommentMentionTag(tag))
            .Select(button => $"{button.Tag}|{button.Content}|{button.IsEnabled}")
            .ToArray();

    internal bool IsAltTextPaneVisible => _altTextPaneHostCoordinator.IsPaneVisible;
    internal bool IsAltTextPaneApplyEnabled => _altTextApplyButton?.IsEnabled == true;
    internal string AltTextPaneTitleLabel => _altTextTitleLabel?.Text ?? string.Empty;
    internal string AltTextPaneTitleText => _altTextTitleBox?.Text ?? string.Empty;
    internal string AltTextPaneTitlePlaceholder => _altTextTitleBox?.PlaceholderText ?? string.Empty;
    internal string AltTextPaneDescriptionLabel => _altTextDescriptionLabel?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionText => _altTextDescriptionBox?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionPlaceholder =>
        _altTextDescriptionBox?.PlaceholderText ?? string.Empty;
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
    internal int SmartArtTextPaneCommandActionCount =>
        _smartArtTextPaneCommandActions?.Children.OfType<Button>().Count() ?? 0;
    internal bool SmartArtTextPaneCommandActionsWrap => _smartArtTextPaneCommandActions is not null;
    internal string SmartArtTextPaneMessage => _smartArtTextPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> SmartArtTextPaneRenderedRows =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>()
            .Select(box => box.Tag is SmartArtNodeOutlineItem item
                ? $"{item.ModelId}|{item.Level}|{item.IsAssistant}|{box.Text}"
                : box.Text ?? string.Empty)
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

    internal int AnimationPaneItemCount => LastAnimationPaneTimelinePlan?.Items.Count ?? 0;
    internal int AnimationPaneRenderedItemCount => _animationPaneItemsPanel?.Children.Count ?? 0;
    internal string AnimationPaneHeading => LastAnimationPaneWorkflowEvidencePlan?.View.Heading
        ?? _animationPaneHeading?.Text
        ?? string.Empty;
    internal string AnimationPaneMessage => _animationPaneMessage?.Text ?? string.Empty;
    internal bool IsAnimationPanePreviewEnabled => _animationPanePreviewButton?.IsEnabled == true;
    internal IReadOnlyList<string> AnimationPanePlaybackControls => _animationPaneRenderedPlaybackControls;
    internal IReadOnlyList<string> AnimationPaneRenderedRows => _animationPaneRenderedRows;
    internal IReadOnlyList<string> AnimationPaneWorkflowEvidenceLines =>
        LastAnimationPaneWorkflowEvidencePlan?.EvidenceLines ?? Array.Empty<string>();
    internal int AnimationPaneEffectOptionControlCount => _animationPaneEffectOptionControlCount;
    internal int AnimationPaneTriggerControlCount => _animationPaneTriggerControlCount;
    internal int AnimationPaneDurationControlCount => _animationPaneDurationControlCount;
    internal int AnimationPaneDelayControlCount => _animationPaneDelayControlCount;

    internal FindReplaceDialog? ActiveFindReplaceDialog => _findReplaceDialog;
    internal bool IsFindReplaceDialogVisible => _findReplaceDialog?.IsVisible == true;
    internal bool IsFindReplaceReplaceInputVisible => _findReplaceDialog?.ShowReplace == true;
    internal bool IsPrintOptionsPaneVisible => _printOptionsPaneHost?.IsVisible == true;
    internal string PrintOptionsPaneHeading => _printOptionsPaneHeading?.Text ?? string.Empty;
    internal string PrintOptionsPaneMessage => _printOptionsPaneMessage?.Text ?? string.Empty;
    internal int PrintOptionsPaneRenderedRowCount => _printOptionsPaneRowsPanel?.Children.Count ?? 0;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedOptionLines => _printOptionsPaneRenderedOptionLines;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedPreviewRows => _printOptionsPaneRenderedPreviewRows;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedLayoutRows => _printOptionsPaneRenderedLayoutRows;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedRangeRows => _printOptionsPaneRenderedRangeRows;
    internal bool IsBackstageOpen => _backstage.IsOpen;
    internal string? CurrentBackstagePaneLabel => _backstage.CurrentPaneLabel;
    internal IReadOnlyList<SisterBackstageEntryPlan<Control>> BackstageEntries => _backstage.Entries;
}
