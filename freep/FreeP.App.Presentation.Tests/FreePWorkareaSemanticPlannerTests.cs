using Free.Shared.AppServices;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePWorkareaSemanticPlannerTests
{
    [Fact]
    public void Review_plans_project_final_row_text_visibility_and_accessibility_keys()
    {
        var emptyComments = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            [new Slide()],
            slideIndex: 0);
        var reply = new PresentationCommentReplyDescriptor(
            ReplyIndex: 0,
            Author: "Ada",
            Initials: "AL",
            TextPreview: "Looks good.",
            Timestamp: null,
            MentionCount: 0);
        var accessibility = new PresentationAccessibilityCheckerRowPlan(
            RowIndex: 0,
            Severity: PresentationAccessibilityIssueSeverity.Warning,
            Category: "Alternative text",
            SlideIndex: 1,
            SlideDisplay: "Slide 2",
            ShapeId: 42,
            ShapeName: "Picture 1",
            Title: "Missing alternative text",
            Detail: "Add a concise description.",
            IsSelected: true,
            ActionLabel: "Select object",
            CommandHint: null,
            ShouldNavigateToSlide: true,
            ShouldSelectShape: true);
        var action = new PresentationReviewWorkflowActionPlan(
            "proofing.change",
            "Change",
            PresentationReviewWorkflowIntentKind.ApplyProofingCorrection,
            true,
            PresentationWorkflowCapabilityStatus.Available);
        var scope = new PresentationProofingScopeDescriptor(
            PresentationProofingScopeKind.ShapeText,
            SlideIndex: 2,
            ShapeId: 7,
            TableRowIndex: null,
            TableColumnIndex: null,
            CommentIndex: null,
            ReplyIndex: null,
            SourceName: "Title",
            Text: "Teh",
            Snippet: "Teh plan");
        var proofing = new PresentationProofingIssueRowPlan(
            RowIndex: 0,
            Scope: scope,
            Start: 0,
            Length: 3,
            Text: "Teh",
            Message: "Possible spelling error.",
            SourceName: "Title",
            SlideDisplay: "Slide 3",
            Snippet: "Teh plan",
            SuggestedReplacement: "The",
            IsSelected: true,
            CorrectionAction: action,
            IgnoreAction: action,
            IgnoreAllAction: action,
            AddToDictionaryAction: action);

        emptyComments.HasComments.Should().BeFalse();
        emptyComments.ShouldShowEmptyState.Should().BeTrue();
        emptyComments.EmptyStateMessage.Should().Be("No comments on this slide.");
        reply.DisplayText.Should().Be("Ada: Looks good.");
        reply.ShouldShowMentionDetail.Should().BeFalse();
        accessibility.DisplayTitle.Should().Be("Slide 2 - Missing alternative text");
        accessibility.DisplayMetadata.Should().Be("Warning - Alternative text - Picture 1");
        accessibility.ShouldShowSelectionIndicator.Should().BeTrue();
        accessibility.AccessibilityKey.Should().Be("Slide2Shape42Issue1");
        proofing.DisplayTitle.Should().Be("Slide 3 - Title");
        proofing.ReplacementDisplayText.Should().Be("Teh -> The");
        proofing.AccessibilityKey.Should().Be("Slide3ProofingShapeText1");
    }

    [Fact]
    public void SmartArt_media_and_frame_plans_project_renderer_neutral_semantics()
    {
        var assistant = new SmartArtNodeOutlineItem("node-1", "Advisor", 2, 0, true);
        var level = new SmartArtNodeOutlineItem("node-2", "Detail", 2, 0, false);
        var validField = new PresentationMediaCaptionAuthoringFieldPlan(
            "Language",
            "en-US",
            "Language tag",
            true,
            null);
        var invalidField = validField with { ValidationMessage = "Enter a valid language tag." };
        var title = FreePApplicationFrameDescriptor.Title;

        assistant.RoleDisplayText.Should().Be("Assistant row");
        level.RoleDisplayText.Should().Be("Level 3 row");
        validField.ShouldShowValidationMessage.Should().BeFalse();
        validField.DisplayLabel.Should().Be("Language");
        validField.ToolTip.Should().Be("Language tag");
        invalidField.ShouldShowValidationMessage.Should().BeTrue();
        invalidField.DisplayLabel.Should().Be("Language - Enter a valid language tag.");
        invalidField.ToolTip.Should().Be("Enter a valid language tag.");
        title.Should().Be(new FreePApplicationFrameTitleSpec(
            "FreeP",
            " \u2014 ",
            " *",
            WindowTitleApplicationPlacement.DocumentThenApplication));
    }
}
