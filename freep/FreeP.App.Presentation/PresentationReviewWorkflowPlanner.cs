using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationReviewWorkflowIntentKind
{
    ShowCommentsPane,
    AddComment,
    EditComment,
    ReplyComment,
    DeleteComment,
    PreviousComment,
    NextComment,
    ResolveComment,
    ReopenComment,
    CheckAccessibility,
    OpenAltText,
    ApplyAltText,
    ToggleAltTextDecorative,
    CloseAltTextPane,
    OpenReadingOrderPane,
    MoveReadingOrderEarlier,
    MoveReadingOrderLater,
    SelectReadingOrderItem,
    RunProofing
}

public enum PresentationWorkflowCapabilityStatus
{
    Available,
    RequiresHost,
    Deferred
}

public enum PresentationCommentThreadStatus
{
    Open,
    Resolved
}

public enum PresentationProofingScopeKind
{
    SlideTitle,
    ShapeText,
    TableCellText,
    SpeakerNotes,
    Comment,
    CommentReply
}

public enum PresentationAccessibilityIssueSeverity
{
    Info,
    Warning
}

public sealed record PresentationReviewWorkflowActionPlan(
    string CommandId,
    string Label,
    PresentationReviewWorkflowIntentKind Intent,
    bool IsEnabled,
    PresentationWorkflowCapabilityStatus Status,
    string? DisabledReason = null);

public sealed record PresentationCommentReplyDescriptor(
    int ReplyIndex,
    string Author,
    string Initials,
    string TextPreview,
    DateTime? Timestamp,
    int MentionCount);

public sealed record PresentationCommentDescriptor(
    int SlideIndex,
    int CommentIndex,
    int Idx,
    string Author,
    string Initials,
    string TextPreview,
    DateTime? Timestamp,
    long Xemu,
    long Yemu,
    bool CanEdit,
    bool CanReply,
    bool CanDelete,
    bool CanResolve,
    bool CanReopen,
    int ReplyCount,
    int MentionCount,
    IReadOnlyList<PresentationCommentReplyDescriptor> Replies,
    PresentationCommentThreadStatus ThreadStatus,
    bool IsSelected);

public sealed record PresentationCommentPanePlan(
    int SlideIndex,
    int SlideCount,
    int SlideCommentCount,
    int TotalCommentCount,
    int SelectedCommentIndex,
    IReadOnlyList<PresentationCommentDescriptor> Comments,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions)
{
    public PresentationCommentDescriptor? SelectedComment =>
        SelectedCommentIndex >= 0 && SelectedCommentIndex < Comments.Count
            ? Comments[SelectedCommentIndex]
            : null;
}

public sealed record PresentationCommentMutationPlan(
    PresentationReviewWorkflowIntentKind Intent,
    bool ShouldApply,
    int SlideIndex,
    int? CommentIndex,
    SlideComment? Comment,
    string? ValidationMessage);

public sealed record PresentationAltTextRequestPlan(
    bool HasSelection,
    uint? ShapeId,
    string ShapeName,
    string SuggestedTitle,
    string CurrentTitle,
    string ProposedTitle,
    string CurrentDescription,
    string ProposedDescription,
    bool IsDecorative,
    bool CanApply,
    PresentationWorkflowCapabilityStatus Status,
    string Message);

public sealed record PresentationAltTextPaneFieldPlan(
    string FieldId,
    string Label,
    string Value,
    string Placeholder,
    bool IsEnabled,
    bool IsRequired,
    string? ValidationMessage);

public sealed record PresentationAltTextPanePlan(
    bool HasSelection,
    uint? ShapeId,
    string ShapeName,
    string SuggestedTitle,
    PresentationAltTextPaneFieldPlan Title,
    PresentationAltTextPaneFieldPlan Description,
    bool IsDecorative,
    bool CanApply,
    PresentationWorkflowCapabilityStatus Status,
    string Message,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions);

public sealed record PresentationAltTextMutationPlan(
    bool ShouldApply,
    int SlideIndex,
    uint? ShapeId,
    string Title,
    string Description,
    bool IsDecorative,
    string? ValidationMessage);

public sealed record PresentationAccessibilityIssueDescriptor(
    PresentationAccessibilityIssueSeverity Severity,
    int SlideIndex,
    uint? ShapeId,
    string Title,
    string Detail,
    PresentationAccessibilityIssueActionSummary Action);

public sealed record PresentationAccessibilityIssueActionSummary(
    string Summary,
    string? CommandId,
    bool RequiresShapeSelection);

public sealed record PresentationAccessibilitySummaryPlan(
    int SlideCount,
    int ShapeCount,
    int CommentCount,
    int NotesSlideCount,
    IReadOnlyList<PresentationAccessibilityIssueDescriptor> Issues,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions);

public sealed record PresentationReadingOrderItemPlan(
    int ReadingOrderIndex,
    int NestingDepth,
    uint ShapeId,
    string ShapeName,
    SlideShapeKind ShapeType,
    string ShapeTypeLabel,
    string AlternativeTextTitle,
    string AlternativeTextDescription,
    bool IsDecorative,
    string AccessibilitySummary,
    bool IsSelected);

public sealed record PresentationReadingOrderPlan(
    int SlideIndex,
    bool HasSlide,
    bool HasSingleSelectedItem,
    uint? SelectedShapeId,
    int SelectedItemIndex,
    IReadOnlyList<PresentationReadingOrderItemPlan> Items,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions)
{
    public PresentationReadingOrderItemPlan? SelectedItem =>
        SelectedItemIndex >= 0 && SelectedItemIndex < Items.Count
            ? Items[SelectedItemIndex]
            : null;
}

public sealed record PresentationReadingOrderMutationPlan(
    PresentationReviewWorkflowIntentKind Intent,
    bool ShouldApply,
    int SlideIndex,
    uint? ShapeId,
    int SourceIndex,
    int TargetIndex,
    string? ValidationMessage);

public sealed record PresentationProofingRequestPlan(
    bool CanStart,
    PresentationWorkflowCapabilityStatus Status,
    int TextShapeCount,
    int NotesSlideCount,
    int ReadOnlyCommentCount,
    string Message);

public sealed record PresentationProofingScopeDescriptor(
    PresentationProofingScopeKind Kind,
    int SlideIndex,
    uint? ShapeId,
    int? TableRowIndex,
    int? TableColumnIndex,
    int? CommentIndex,
    int? ReplyIndex,
    string SourceName,
    string Text,
    string Snippet);

public sealed record PresentationProofingIssueMatch(
    int Start,
    int Length,
    string Text,
    string Message);

public sealed record PresentationProofingIssueDescriptor(
    PresentationProofingScopeDescriptor Scope,
    int Start,
    int Length,
    string Text,
    string Message);

public sealed record PresentationProofingExecutionPlan(
    bool CanRun,
    PresentationWorkflowCapabilityStatus Status,
    int ScopeCount,
    int IssueCount,
    IReadOnlyList<PresentationProofingScopeDescriptor> Scopes,
    IReadOnlyList<PresentationProofingIssueDescriptor> Issues,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions,
    string Message);

public sealed record PresentationProofingCorrectionMutationPlan(
    bool ShouldApply,
    PresentationProofingScopeDescriptor Scope,
    int Start,
    int Length,
    string Replacement,
    string? UpdatedText,
    string? ValidationMessage);

public static class PresentationReviewWorkflowPlanner
{
    private sealed record ReadingOrderMoveTarget(
        int SourceIndex,
        int TargetIndex,
        int SiblingCount);

    public const string CommentsPaneCommandId = "freep.review.comments.pane";
    public const string AddCommentCommandId = "freep.review.comments.add";
    public const string EditCommentCommandId = "freep.review.comments.edit";
    public const string ReplyCommentCommandId = "freep.review.comments.reply";
    public const string DeleteCommentCommandId = "freep.review.comments.delete";
    public const string PreviousCommentCommandId = "freep.review.comments.previous";
    public const string NextCommentCommandId = "freep.review.comments.next";
    public const string ResolveCommentCommandId = "freep.review.comments.resolve";
    public const string ReopenCommentCommandId = "freep.review.comments.reopen";
    public const string AccessibilityCommandId = "freep.review.accessibility.check";
    public const string AltTextCommandId = "freep.review.alt-text";
    public const string AltTextPaneApplyCommandId = "freep.review.alt-text.apply";
    public const string AltTextPaneDecorativeCommandId = "freep.review.alt-text.decorative";
    public const string AltTextPaneCloseCommandId = "freep.review.alt-text.close";
    public const string AltTextTitleFieldId = "title";
    public const string AltTextDescriptionFieldId = "description";
    public const string ReadingOrderPaneCommandId = "freep.review.reading-order.pane";
    public const string ReadingOrderMoveEarlierCommandId = "freep.review.reading-order.move-earlier";
    public const string ReadingOrderMoveLaterCommandId = "freep.review.reading-order.move-later";
    public const string ReadingOrderSelectItemCommandId = "freep.review.reading-order.select";
    public const string ProofingCommandId = "freep.review.proofing.spelling";
    public const string InsertLinkCommandId = "freep.insert-link";

    public const string MissingSlideMessage = "Select a slide before adding a comment.";
    public const string MissingCommentMessage = "Select an existing comment first.";
    public const string EmptyCommentMessage = "Comment text cannot be empty.";
    public const string EmptyCommentReplyMessage = "Reply text cannot be empty.";
    public const string CannotReplyToResolvedCommentMessage =
        "Reopen the thread before adding a reply.";
    public const string CommentAlreadyResolvedMessage =
        "Selected comment thread is already resolved.";
    public const string CommentAlreadyOpenMessage =
        "Selected comment thread is already open.";
    public const string MissingShapeMessage = "Select a shape before editing alt text.";
    public const string MissingAltTextDescriptionMessage =
        "Alt text description is required unless the object is marked decorative.";
    public const string MissingReadingOrderSelectionMessage =
        "Select one shape before changing reading order.";
    public const string EmptyReadingOrderMessage =
        "Current slide has no shapes in the reading order.";
    public const string ReadingOrderReorderDeferredMessage =
        "Reading order mutation is deferred; this shared plan exposes stable shape order for a visible pane follow-up.";
    public const string NestedReadingOrderReorderDeferredMessage =
        "Nested/group child reading-order moves are deferred; select a top-level shape first.";
    public const string ReadingOrderAlreadyEarliestMessage =
        "Selected shape is already earliest in the reading order.";
    public const string ReadingOrderAlreadyLatestMessage =
        "Selected shape is already latest in the reading order.";
    public const string ProofingRequiresHostMessage =
        "Proofing needs a host spelling engine; this shared plan owns the searchable FreeP scopes.";
    public const string ProofingReadyMessage =
        "Proofing shared scan prepared searchable FreeP text scopes for a host spelling engine.";
    public const string ProofingNoTextMessage =
        "No slide text, notes, or comments are available for proofing.";
    public const string ProofingCorrectionMissingSlideMessage =
        "Proofing correction target slide was not found.";
    public const string ProofingCorrectionMissingScopeMessage =
        "Proofing correction target scope was not found.";
    public const string ProofingCorrectionInvalidRangeMessage =
        "Proofing correction range is outside the target text.";
    public const string ProofingCorrectionEmptyReplacementMessage =
        "Enter a replacement before applying the proofing correction.";
    public const string MissingSlideTitleActionSummary =
        "Add a concise slide title so screen-reader users can navigate the deck.";
    public const string MissingAltTextActionSummary =
        "Select the object and add alt text that describes the informative content.";
    public const string MissingHyperlinkScreenTipActionSummary =
        "Edit the hyperlink and add ScreenTip text that explains the destination.";

    public static PresentationCommentPanePlan BuildCommentPanePlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex = null)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var selected = NormalizeSelectedCommentIndex(slides, slideIndex, selectedCommentIndex);
        var comments = GetSlide(slides, slideIndex)?.Comments ?? [];
        var descriptors = comments
            .Select((comment, index) => DescribeComment(slideIndex, index, comment, selected == index))
            .ToArray();
        var total = slides.Sum(slide => slide.Comments.Count);

        return new PresentationCommentPanePlan(
            slideIndex,
            slides.Count,
            comments.Count,
            total,
            selected ?? -1,
            descriptors,
            BuildCommentActions(slides, slideIndex, selected, total));
    }

    public static PresentationCommentMutationPlan BuildAddCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        string? text,
        string? author,
        string? initials,
        long xemu,
        long yemu,
        DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var slide = GetSlide(slides, slideIndex);
        if (slide is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.AddComment, slideIndex, null, MissingSlideMessage);
        }

        var normalizedText = NormalizeText(text);
        if (normalizedText is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.AddComment, slideIndex, null, EmptyCommentMessage);
        }

        var comment = new SlideComment
        {
            Author = NormalizeText(author) ?? "FreeP User",
            Initials = NormalizeInitials(initials, author),
            Text = normalizedText,
            DateTime = timestamp,
            IsResolved = false,
            ResolvedDateTime = null,
            ResolvedBy = string.Empty,
            Xemu = Math.Max(0, xemu),
            Yemu = Math.Max(0, yemu),
            Idx = slide.Comments.Count + 1
        };

        return new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.AddComment,
            true,
            slideIndex,
            null,
            comment,
            null);
    }

    public static PresentationCommentMutationPlan BuildEditCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex,
        string? text,
        string? author = null,
        string? initials = null)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var current = GetComment(slides, slideIndex, commentIndex);
        if (current is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.EditComment, slideIndex, commentIndex, MissingCommentMessage);
        }

        var normalizedText = NormalizeText(text);
        if (normalizedText is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.EditComment, slideIndex, commentIndex, EmptyCommentMessage);
        }

        var effectiveAuthor = NormalizeText(author) ?? current.Author;
        var comment = new SlideComment
        {
            Author = effectiveAuthor,
            Initials = NormalizeText(initials) ?? current.Initials,
            Text = normalizedText,
            DateTime = current.DateTime,
            IsResolved = current.IsResolved,
            ResolvedDateTime = current.ResolvedDateTime,
            ResolvedBy = current.ResolvedBy,
            Xemu = current.Xemu,
            Yemu = current.Yemu,
            Idx = current.Idx,
            AuthorId = current.AuthorId
        };
        CopyReplies(current, comment);

        return new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.EditComment,
            true,
            slideIndex,
            commentIndex,
            comment,
            null);
    }

    public static PresentationCommentMutationPlan BuildDeleteCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);

        return GetComment(slides, slideIndex, commentIndex) is null
            ? InvalidMutation(PresentationReviewWorkflowIntentKind.DeleteComment, slideIndex, commentIndex, MissingCommentMessage)
            : new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.DeleteComment,
                true,
                slideIndex,
                commentIndex,
                null,
                null);
    }

    public static PresentationCommentMutationPlan BuildReplyCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex,
        string? text,
        string? author,
        string? initials,
        DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var current = GetComment(slides, slideIndex, commentIndex);
        if (current is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ReplyComment, slideIndex, commentIndex, MissingCommentMessage);
        }

        if (current.IsResolved)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ReplyComment, slideIndex, commentIndex, CannotReplyToResolvedCommentMessage);
        }

        var normalizedText = NormalizeText(text);
        if (normalizedText is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ReplyComment, slideIndex, commentIndex, EmptyCommentReplyMessage);
        }

        var comment = CloneComment(current);
        var effectiveAuthor = NormalizeText(author) ?? "FreeP User";
        comment.Replies.Add(new SlideCommentReply
        {
            Author = effectiveAuthor,
            Initials = NormalizeInitials(initials, effectiveAuthor),
            Text = normalizedText,
            DateTime = timestamp
        });

        return new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ReplyComment,
            true,
            slideIndex,
            commentIndex,
            comment,
            null);
    }

    public static PresentationCommentMutationPlan BuildResolveCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex,
        DateTime? resolvedAt = null,
        string? resolvedBy = null)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var current = GetComment(slides, slideIndex, commentIndex);
        if (current is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ResolveComment, slideIndex, commentIndex, MissingCommentMessage);
        }

        if (current.IsResolved)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ResolveComment, slideIndex, commentIndex, CommentAlreadyResolvedMessage);
        }

        var comment = CloneComment(current);
        comment.IsResolved = true;
        comment.ResolvedDateTime = resolvedAt;
        comment.ResolvedBy = NormalizeText(resolvedBy) ?? string.Empty;
        return new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ResolveComment,
            true,
            slideIndex,
            commentIndex,
            comment,
            null);
    }

    public static PresentationCommentMutationPlan BuildReopenCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var current = GetComment(slides, slideIndex, commentIndex);
        if (current is null)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ReopenComment, slideIndex, commentIndex, MissingCommentMessage);
        }

        if (!current.IsResolved)
        {
            return InvalidMutation(PresentationReviewWorkflowIntentKind.ReopenComment, slideIndex, commentIndex, CommentAlreadyOpenMessage);
        }

        var comment = CloneComment(current);
        comment.IsResolved = false;
        comment.ResolvedDateTime = null;
        comment.ResolvedBy = string.Empty;
        return new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ReopenComment,
            true,
            slideIndex,
            commentIndex,
            comment,
            null);
    }

    public static bool TryApplyCommentMutationPlan(
        IReadOnlyList<Slide> slides,
        PresentationCommentMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply)
        {
            return false;
        }

        var slide = GetSlide(slides, plan.SlideIndex);
        if (slide is null)
        {
            return false;
        }

        switch (plan.Intent)
        {
            case PresentationReviewWorkflowIntentKind.AddComment:
                if (plan.Comment is null)
                {
                    return false;
                }

                slide.Comments.Add(CloneComment(plan.Comment));
                return true;

            case PresentationReviewWorkflowIntentKind.EditComment:
            case PresentationReviewWorkflowIntentKind.ReplyComment:
            case PresentationReviewWorkflowIntentKind.ResolveComment:
            case PresentationReviewWorkflowIntentKind.ReopenComment:
                if (plan.CommentIndex is not { } commentIndex ||
                    plan.Comment is null ||
                    commentIndex < 0 ||
                    commentIndex >= slide.Comments.Count)
                {
                    return false;
                }

                slide.Comments[commentIndex] = CloneComment(plan.Comment);
                return true;

            case PresentationReviewWorkflowIntentKind.DeleteComment:
                if (plan.CommentIndex is not { } deleteIndex ||
                    deleteIndex < 0 ||
                    deleteIndex >= slide.Comments.Count)
                {
                    return false;
                }

                slide.Comments.RemoveAt(deleteIndex);
                return true;

            default:
                return false;
        }
    }

    public static int? NormalizeCommentSelectionAfterMutation(
        IReadOnlyList<Slide> slides,
        PresentationCommentMutationPlan plan,
        int? previousSelectedCommentIndex = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(plan);

        var comments = GetSlide(slides, plan.SlideIndex)?.Comments;
        if (comments is null || comments.Count == 0)
        {
            return null;
        }

        return plan.Intent switch
        {
            PresentationReviewWorkflowIntentKind.AddComment => comments.Count - 1,
            PresentationReviewWorkflowIntentKind.DeleteComment when plan.CommentIndex is { } deletedIndex =>
                Math.Min(Math.Max(deletedIndex, 0), comments.Count - 1),
            PresentationReviewWorkflowIntentKind.EditComment
            or PresentationReviewWorkflowIntentKind.ReplyComment
            or PresentationReviewWorkflowIntentKind.ResolveComment
            or PresentationReviewWorkflowIntentKind.ReopenComment =>
                NormalizeSelectedCommentIndex(slides, plan.SlideIndex, plan.CommentIndex ?? previousSelectedCommentIndex),
            _ => NormalizeSelectedCommentIndex(slides, plan.SlideIndex, previousSelectedCommentIndex)
        };
    }

    public static PresentationAltTextRequestPlan BuildAltTextRequestPlan(
        Slide? slide,
        uint? selectedShapeId,
        string? proposedDescription,
        string? proposedTitle = null,
        bool? isDecorative = null)
    {
        var shape = selectedShapeId is { } id ? FindShape(slide?.Shapes, id) : null;
        if (shape is null)
        {
            var missingIsDecorative = isDecorative ?? false;
            return new PresentationAltTextRequestPlan(
                false,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                NormalizeAltTextTitle(proposedTitle),
                string.Empty,
                missingIsDecorative ? string.Empty : NormalizeAltTextDescription(proposedDescription),
                missingIsDecorative,
                false,
                PresentationWorkflowCapabilityStatus.Available,
                MissingShapeMessage);
        }

        var suggestedTitle = BuildAltTextSuggestedTitle(shape);
        var decorative = isDecorative ?? shape.IsDecorative;
        var normalizedTitle = decorative
            ? string.Empty
            : (proposedTitle is null ? shape.AlternativeTextTitle : NormalizeAltTextTitle(proposedTitle));
        var normalizedDescription = decorative
            ? string.Empty
            : (proposedDescription is null ? shape.AlternativeText : NormalizeAltTextDescription(proposedDescription));
        return new PresentationAltTextRequestPlan(
            true,
            shape.Id,
            shape.Name,
            suggestedTitle,
            shape.AlternativeTextTitle,
            normalizedTitle,
            shape.AlternativeText,
            normalizedDescription,
            decorative,
            true,
            PresentationWorkflowCapabilityStatus.Available,
            decorative
                ? "Selected shape is marked decorative and does not require alt text."
                : string.IsNullOrEmpty(shape.AlternativeText)
                ? "Add a persistent alt-text description for the selected shape."
                : "Edit the persistent alt-text description for the selected shape.");
    }

    public static PresentationAltTextPanePlan BuildAltTextPanePlan(
        Slide? slide,
        uint? selectedShapeId,
        string? proposedDescription,
        string? proposedTitle = null,
        bool? isDecorative = null)
    {
        var request = BuildAltTextRequestPlan(
            slide,
            selectedShapeId,
            proposedDescription,
            proposedTitle,
            isDecorative);
        var descriptionValidation = request.HasSelection
            && !request.IsDecorative
            && string.IsNullOrWhiteSpace(request.ProposedDescription)
            ? MissingAltTextDescriptionMessage
            : null;
        var canApply = request.HasSelection && descriptionValidation is null;

        var title = new PresentationAltTextPaneFieldPlan(
            AltTextTitleFieldId,
            "Title",
            request.ProposedTitle,
            request.SuggestedTitle,
            request.HasSelection && !request.IsDecorative,
            false,
            null);
        var description = new PresentationAltTextPaneFieldPlan(
            AltTextDescriptionFieldId,
            "Description",
            request.ProposedDescription,
            "Describe the selected object for people who cannot see it.",
            request.HasSelection && !request.IsDecorative,
            !request.IsDecorative,
            descriptionValidation);

        return new PresentationAltTextPanePlan(
            request.HasSelection,
            request.ShapeId,
            request.ShapeName,
            request.SuggestedTitle,
            title,
            description,
            request.IsDecorative,
            canApply,
            request.Status,
            descriptionValidation ?? request.Message,
            BuildAltTextPaneActions(request.HasSelection, canApply, descriptionValidation));
    }

    public static PresentationAltTextMutationPlan BuildAltTextMutationPlan(
        Slide? slide,
        int slideIndex,
        uint? selectedShapeId,
        string? description,
        string? title = null,
        bool isDecorative = false)
    {
        var normalizedTitle = isDecorative ? string.Empty : NormalizeAltTextTitle(title);
        var normalizedDescription = isDecorative ? string.Empty : NormalizeAltTextDescription(description);
        var shape = selectedShapeId is { } id ? FindShape(slide?.Shapes, id) : null;
        if (shape is null)
        {
            return new PresentationAltTextMutationPlan(
                false,
                slideIndex,
                null,
                normalizedTitle,
                normalizedDescription,
                isDecorative,
                MissingShapeMessage);
        }

        return new PresentationAltTextMutationPlan(
            true,
            slideIndex,
            shape.Id,
            normalizedTitle,
            normalizedDescription,
            isDecorative,
            null);
    }

    public static PresentationAccessibilitySummaryPlan BuildAccessibilitySummaryPlan(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var issues = new List<PresentationAccessibilityIssueDescriptor>();
        int shapeCount = 0;
        int commentCount = 0;
        int notesCount = 0;

        for (int slideIndex = 0; slideIndex < presentation.Slides.Count; slideIndex++)
        {
            var slide = presentation.Slides[slideIndex];
            commentCount += slide.Comments.Count;
            if (slide.Notes is not null && !string.IsNullOrWhiteSpace(TextBodyToPlainText(slide.Notes)))
            {
                notesCount++;
            }

            if (string.IsNullOrWhiteSpace(slide.Title))
            {
                issues.Add(new PresentationAccessibilityIssueDescriptor(
                    PresentationAccessibilityIssueSeverity.Warning,
                    slideIndex,
                    null,
                    "Missing slide title",
                    "PowerPoint accessibility checks expect each slide to have a meaningful title.",
                    new PresentationAccessibilityIssueActionSummary(
                        MissingSlideTitleActionSummary,
                        null,
                        false)));
            }

            foreach (var shape in EnumerateShapes(slide.Shapes))
            {
                shapeCount++;

                if (NeedsAltText(shape)
                    && !shape.IsDecorative
                    && string.IsNullOrWhiteSpace(shape.AlternativeText))
                {
                    issues.Add(new PresentationAccessibilityIssueDescriptor(
                        PresentationAccessibilityIssueSeverity.Warning,
                        slideIndex,
                        shape.Id,
                        "Alt text missing",
                        $"{DescribeShape(shape)} should have persistent alt text.",
                        new PresentationAccessibilityIssueActionSummary(
                            MissingAltTextActionSummary,
                            AltTextCommandId,
                            true)));
                }

                if (shape.Hyperlink is not null && string.IsNullOrWhiteSpace(shape.Hyperlink.Tooltip))
                {
                    issues.Add(new PresentationAccessibilityIssueDescriptor(
                        PresentationAccessibilityIssueSeverity.Info,
                        slideIndex,
                        shape.Id,
                        "Hyperlink ScreenTip missing",
                        $"{DescribeShape(shape)} has a hyperlink without hover/help text.",
                        new PresentationAccessibilityIssueActionSummary(
                            MissingHyperlinkScreenTipActionSummary,
                            InsertLinkCommandId,
                            true)));
                }
            }
        }

        return new PresentationAccessibilitySummaryPlan(
            presentation.Slides.Count,
            shapeCount,
            commentCount,
            notesCount,
            issues,
            BuildAccessibilityActions());
    }

    public static PresentationReadingOrderPlan BuildReadingOrderPlan(
        Slide? slide,
        int slideIndex,
        IReadOnlyList<uint>? selectedShapeIds)
    {
        var singleSelectedShapeId = selectedShapeIds is { Count: 1 }
            ? selectedShapeIds[0]
            : (uint?)null;
        if (slide is null)
        {
            return new PresentationReadingOrderPlan(
                slideIndex,
                false,
                false,
                null,
                -1,
                [],
                BuildReadingOrderActions(hasItems: false, hasSingleSelectedItem: false));
        }

        var items = EnumerateShapesWithDepth(slide.Shapes)
            .Select((entry, index) => DescribeReadingOrderItem(
                entry.Shape,
                index,
                entry.Depth,
                singleSelectedShapeId == entry.Shape.Id))
            .ToArray();
        var selectedIndex = singleSelectedShapeId is { } id
            ? Array.FindIndex(items, item => item.ShapeId == id)
            : -1;
        var hasSingleSelectedItem = selectedIndex >= 0;

        return new PresentationReadingOrderPlan(
            slideIndex,
            true,
            hasSingleSelectedItem,
            hasSingleSelectedItem ? singleSelectedShapeId : null,
            selectedIndex,
            items,
            BuildReadingOrderActions(slide, items, selectedIndex, singleSelectedShapeId));
    }

    public static PresentationReadingOrderMutationPlan BuildReadingOrderMovePlan(
        Slide? slide,
        int slideIndex,
        IReadOnlyList<uint>? selectedShapeIds,
        PresentationReviewWorkflowIntentKind intent)
    {
        if (intent is not PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier
            and not PresentationReviewWorkflowIntentKind.MoveReadingOrderLater)
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unsupported reading-order move intent.");
        }

        var plan = BuildReadingOrderPlan(slide, slideIndex, selectedShapeIds);
        var commandId = intent == PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier
            ? ReadingOrderMoveEarlierCommandId
            : ReadingOrderMoveLaterCommandId;
        var action = plan.Actions.Single(action => action.CommandId == commandId);
        if (!action.IsEnabled || slide is null || plan.SelectedShapeId is not { } shapeId)
        {
            return new PresentationReadingOrderMutationPlan(
                intent,
                false,
                slideIndex,
                plan.SelectedShapeId,
                -1,
                -1,
                action.DisabledReason);
        }

        var offset = intent == PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier ? -1 : 1;
        var moveTarget = FindReadingOrderMoveTarget(slide, shapeId, offset);
        if (moveTarget is null)
        {
            return new PresentationReadingOrderMutationPlan(
                intent,
                false,
                slideIndex,
                shapeId,
                -1,
                -1,
                ReadingOrderReorderDeferredMessage);
        }

        return new PresentationReadingOrderMutationPlan(
            intent,
            true,
            slideIndex,
            shapeId,
            moveTarget.SourceIndex,
            moveTarget.TargetIndex,
            null);
    }

    public static PresentationReadingOrderMutationPlan TryApplyReadingOrderMove(
        EditingSession editor,
        PresentationReviewWorkflowIntentKind intent)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var plan = BuildReadingOrderMovePlan(
            editor.CurrentSlide,
            editor.CurrentSlideIndex,
            editor.SelectedShapeIds,
            intent);
        if (plan.ShouldApply)
        {
            editor.MoveSelectedShapeInReadingOrder(plan.TargetIndex - plan.SourceIndex);
        }

        return plan;
    }

    public static PresentationProofingRequestPlan BuildProofingRequestPlan(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var executionPlan = BuildProofingExecutionPlan(presentation);
        var textShapes = executionPlan.Scopes.Count(scope =>
            scope.Kind is PresentationProofingScopeKind.SlideTitle
                or PresentationProofingScopeKind.ShapeText
                or PresentationProofingScopeKind.TableCellText);
        var notesSlides = executionPlan.Scopes
            .Where(scope => scope.Kind == PresentationProofingScopeKind.SpeakerNotes)
            .Select(scope => scope.SlideIndex)
            .Distinct()
            .Count();
        var comments = executionPlan.Scopes.Count(scope =>
            scope.Kind is PresentationProofingScopeKind.Comment
                or PresentationProofingScopeKind.CommentReply);

        return new PresentationProofingRequestPlan(
            executionPlan.CanRun,
            executionPlan.Status,
            textShapes,
            notesSlides,
            comments,
            executionPlan.Message);
    }

    public static PresentationProofingExecutionPlan BuildProofingExecutionPlan(
        Presentation presentation,
        Func<PresentationProofingScopeDescriptor, IEnumerable<PresentationProofingIssueMatch>>? scanner = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var scopes = EnumerateProofingScopes(presentation).ToArray();
        var issues = new List<PresentationProofingIssueDescriptor>();
        if (scanner is not null)
        {
            foreach (var scope in scopes)
            {
                foreach (var match in scanner(scope))
                {
                    issues.Add(new PresentationProofingIssueDescriptor(
                        scope,
                        Math.Clamp(match.Start, 0, scope.Text.Length),
                        Math.Clamp(match.Length, 0, Math.Max(0, scope.Text.Length - Math.Clamp(match.Start, 0, scope.Text.Length))),
                        match.Text,
                        match.Message));
                }
            }
        }

        var canRun = scopes.Length > 0;
        return new PresentationProofingExecutionPlan(
            canRun,
            canRun ? PresentationWorkflowCapabilityStatus.Available : PresentationWorkflowCapabilityStatus.Deferred,
            scopes.Length,
            issues.Count,
            scopes,
            issues,
            BuildProofingActions(canRun),
            canRun ? ProofingReadyMessage : ProofingNoTextMessage);
    }

    public static PresentationProofingCorrectionMutationPlan TryApplyProofingCorrection(
        Presentation presentation,
        PresentationProofingScopeDescriptor scope,
        int start,
        int length,
        string? replacement)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(scope);

        var normalizedReplacement = replacement ?? string.Empty;
        if (normalizedReplacement.Length == 0)
        {
            return new PresentationProofingCorrectionMutationPlan(
                false,
                scope,
                start,
                length,
                normalizedReplacement,
                null,
                ProofingCorrectionEmptyReplacementMessage);
        }

        if (!TryGetProofingScopeText(presentation, scope, out var text, out var apply, out var validationMessage))
        {
            return new PresentationProofingCorrectionMutationPlan(
                false,
                scope,
                start,
                length,
                normalizedReplacement,
                null,
                validationMessage);
        }

        if (start < 0 || length <= 0 || start > text.Length || length > text.Length - start)
        {
            return new PresentationProofingCorrectionMutationPlan(
                false,
                scope,
                start,
                length,
                normalizedReplacement,
                null,
                ProofingCorrectionInvalidRangeMessage);
        }

        var updatedText = ReplaceTextRange(text, start, length, normalizedReplacement);
        apply(updatedText);
        return new PresentationProofingCorrectionMutationPlan(
            true,
            scope,
            start,
            length,
            normalizedReplacement,
            updatedText,
            null);
    }

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildCommentActions(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex,
        int totalCommentCount)
    {
        var hasSlide = GetSlide(slides, slideIndex) is not null;
        var hasSelectedComment = selectedCommentIndex.HasValue;
        var selectedComment = selectedCommentIndex is { } index
            ? GetComment(slides, slideIndex, index)
            : null;
        var hasPrevious = TryGetAdjacentComment(slides, slideIndex, selectedCommentIndex, -1, out _);
        var hasNext = TryGetAdjacentComment(slides, slideIndex, selectedCommentIndex, 1, out _);
        var canResolve = selectedComment is not null && !selectedComment.IsResolved;
        var canReopen = selectedComment?.IsResolved == true;
        var canReply = selectedComment is not null && !selectedComment.IsResolved;

        return
        [
            new(CommentsPaneCommandId, "Show Comments", PresentationReviewWorkflowIntentKind.ShowCommentsPane, true, PresentationWorkflowCapabilityStatus.Available),
            new(AddCommentCommandId, "New Comment", PresentationReviewWorkflowIntentKind.AddComment, hasSlide, PresentationWorkflowCapabilityStatus.Available, hasSlide ? null : MissingSlideMessage),
            new(EditCommentCommandId, "Edit Comment", PresentationReviewWorkflowIntentKind.EditComment, hasSelectedComment, PresentationWorkflowCapabilityStatus.Available, hasSelectedComment ? null : MissingCommentMessage),
            new(ReplyCommentCommandId, "Reply", PresentationReviewWorkflowIntentKind.ReplyComment, canReply, PresentationWorkflowCapabilityStatus.Available, canReply ? null : selectedComment?.IsResolved == true ? CannotReplyToResolvedCommentMessage : MissingCommentMessage),
            new(DeleteCommentCommandId, "Delete Comment", PresentationReviewWorkflowIntentKind.DeleteComment, hasSelectedComment, PresentationWorkflowCapabilityStatus.Available, hasSelectedComment ? null : MissingCommentMessage),
            new(PreviousCommentCommandId, "Previous Comment", PresentationReviewWorkflowIntentKind.PreviousComment, hasPrevious, PresentationWorkflowCapabilityStatus.Available, hasPrevious ? null : "No previous comment."),
            new(NextCommentCommandId, "Next Comment", PresentationReviewWorkflowIntentKind.NextComment, hasNext, PresentationWorkflowCapabilityStatus.Available, hasNext ? null : "No next comment."),
            new(ResolveCommentCommandId, "Resolve Comment", PresentationReviewWorkflowIntentKind.ResolveComment, canResolve, PresentationWorkflowCapabilityStatus.Available, canResolve ? null : selectedComment?.IsResolved == true ? CommentAlreadyResolvedMessage : MissingCommentMessage),
            new(ReopenCommentCommandId, "Reopen Comment", PresentationReviewWorkflowIntentKind.ReopenComment, canReopen, PresentationWorkflowCapabilityStatus.Available, canReopen ? null : selectedComment is null ? MissingCommentMessage : CommentAlreadyOpenMessage),
        ];
    }

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildAccessibilityActions()
        =>
        [
            new(AccessibilityCommandId, "Check Accessibility", PresentationReviewWorkflowIntentKind.CheckAccessibility, true, PresentationWorkflowCapabilityStatus.RequiresHost),
            new(AltTextCommandId, "Alt Text", PresentationReviewWorkflowIntentKind.OpenAltText, true, PresentationWorkflowCapabilityStatus.Available),
            new(ReadingOrderPaneCommandId, "Reading Order", PresentationReviewWorkflowIntentKind.OpenReadingOrderPane, true, PresentationWorkflowCapabilityStatus.Available),
            new(ProofingCommandId, "Spelling", PresentationReviewWorkflowIntentKind.RunProofing, true, PresentationWorkflowCapabilityStatus.Available),
        ];

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildProofingActions(bool canRun)
        =>
        [
            new(
                ProofingCommandId,
                "Spelling",
                PresentationReviewWorkflowIntentKind.RunProofing,
                canRun,
                canRun ? PresentationWorkflowCapabilityStatus.Available : PresentationWorkflowCapabilityStatus.Deferred,
                canRun ? null : ProofingNoTextMessage),
        ];

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildReadingOrderActions(
        bool hasItems,
        bool hasSingleSelectedItem)
    {
        var reorderReason = !hasItems
            ? EmptyReadingOrderMessage
            : !hasSingleSelectedItem
                ? MissingReadingOrderSelectionMessage
                : ReadingOrderReorderDeferredMessage;

        return
        [
            new(
                ReadingOrderPaneCommandId,
                "Reading Order",
                PresentationReviewWorkflowIntentKind.OpenReadingOrderPane,
                true,
                PresentationWorkflowCapabilityStatus.Available),
            new(
                ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                false,
                PresentationWorkflowCapabilityStatus.Deferred,
                reorderReason),
            new(
                ReadingOrderMoveLaterCommandId,
                "Move Later",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                false,
                PresentationWorkflowCapabilityStatus.Deferred,
                reorderReason),
            new(
                ReadingOrderSelectItemCommandId,
                "Select Item",
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                hasItems,
                PresentationWorkflowCapabilityStatus.Available,
                hasItems ? null : EmptyReadingOrderMessage),
        ];
    }

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildReadingOrderActions(
        Slide slide,
        IReadOnlyList<PresentationReadingOrderItemPlan> items,
        int selectedItemIndex,
        uint? selectedShapeId)
    {
        var hasItems = items.Count > 0;
        var moveEarlier = BuildReadingOrderMoveAction(
            slide,
            items,
            selectedItemIndex,
            selectedShapeId,
            ReadingOrderMoveEarlierCommandId,
            "Move Earlier",
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
            offset: -1);
        var moveLater = BuildReadingOrderMoveAction(
            slide,
            items,
            selectedItemIndex,
            selectedShapeId,
            ReadingOrderMoveLaterCommandId,
            "Move Later",
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            offset: 1);

        return
        [
            new(
                ReadingOrderPaneCommandId,
                "Reading Order",
                PresentationReviewWorkflowIntentKind.OpenReadingOrderPane,
                true,
                PresentationWorkflowCapabilityStatus.Available),
            moveEarlier,
            moveLater,
            new(
                ReadingOrderSelectItemCommandId,
                "Select Item",
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                hasItems,
                PresentationWorkflowCapabilityStatus.Available,
                hasItems ? null : EmptyReadingOrderMessage),
        ];
    }

    private static PresentationReviewWorkflowActionPlan BuildReadingOrderMoveAction(
        Slide slide,
        IReadOnlyList<PresentationReadingOrderItemPlan> items,
        int selectedItemIndex,
        uint? selectedShapeId,
        string commandId,
        string label,
        PresentationReviewWorkflowIntentKind intent,
        int offset)
    {
        var disabledReason = GetReadingOrderMoveDisabledReason(
            slide,
            items,
            selectedItemIndex,
            selectedShapeId,
            offset);

        return new PresentationReviewWorkflowActionPlan(
            commandId,
            label,
            intent,
            disabledReason is null,
            PresentationWorkflowCapabilityStatus.Available,
            disabledReason);
    }

    private static string? GetReadingOrderMoveDisabledReason(
        Slide slide,
        IReadOnlyList<PresentationReadingOrderItemPlan> items,
        int selectedItemIndex,
        uint? selectedShapeId,
        int offset)
    {
        if (items.Count == 0)
        {
            return EmptyReadingOrderMessage;
        }

        if (selectedItemIndex < 0 || selectedShapeId is null)
        {
            return MissingReadingOrderSelectionMessage;
        }

        var moveTarget = FindReadingOrderMoveTarget(slide, selectedShapeId.Value, offset);
        if (moveTarget is null)
        {
            return ReadingOrderReorderDeferredMessage;
        }

        if (moveTarget.TargetIndex < 0)
        {
            return ReadingOrderAlreadyEarliestMessage;
        }

        return moveTarget.TargetIndex >= moveTarget.SiblingCount
            ? ReadingOrderAlreadyLatestMessage
            : null;
    }

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildAltTextPaneActions(
        bool hasSelection,
        bool canApply,
        string? validationMessage)
        =>
        [
            new(
                AltTextPaneApplyCommandId,
                "Apply",
                PresentationReviewWorkflowIntentKind.ApplyAltText,
                canApply,
                PresentationWorkflowCapabilityStatus.Available,
                canApply ? null : validationMessage ?? MissingShapeMessage),
            new(
                AltTextPaneDecorativeCommandId,
                "Mark as Decorative",
                PresentationReviewWorkflowIntentKind.ToggleAltTextDecorative,
                hasSelection,
                PresentationWorkflowCapabilityStatus.Available,
                hasSelection ? null : MissingShapeMessage),
            new(
                AltTextPaneCloseCommandId,
                "Close",
                PresentationReviewWorkflowIntentKind.CloseAltTextPane,
                true,
                PresentationWorkflowCapabilityStatus.Available),
        ];

    private static PresentationCommentDescriptor DescribeComment(
        int slideIndex,
        int commentIndex,
        SlideComment comment,
        bool isSelected)
    {
        var replies = comment.Replies
            .Select((reply, index) => DescribeCommentReply(index, reply))
            .ToArray();
        return new(
            slideIndex,
            commentIndex,
            comment.Idx,
            comment.Author,
            comment.Initials,
            BuildPreview(comment.Text),
            comment.DateTime,
            comment.Xemu,
            comment.Yemu,
            true,
            !comment.IsResolved,
            true,
            !comment.IsResolved,
            comment.IsResolved,
            replies.Length,
            CountMentions(comment.Text) + replies.Sum(reply => reply.MentionCount),
            replies,
            comment.IsResolved ? PresentationCommentThreadStatus.Resolved : PresentationCommentThreadStatus.Open,
            isSelected);
    }

    private static PresentationCommentReplyDescriptor DescribeCommentReply(
        int replyIndex,
        SlideCommentReply reply)
        => new(
            replyIndex,
            reply.Author,
            reply.Initials,
            BuildPreview(reply.Text),
            reply.DateTime,
            CountMentions(reply.Text));

    private static SlideComment CloneComment(SlideComment comment)
    {
        var clone = new SlideComment
        {
            Author = comment.Author,
            Initials = comment.Initials,
            Text = comment.Text,
            DateTime = comment.DateTime,
            IsResolved = comment.IsResolved,
            ResolvedDateTime = comment.ResolvedDateTime,
            ResolvedBy = comment.ResolvedBy,
            Xemu = comment.Xemu,
            Yemu = comment.Yemu,
            Idx = comment.Idx,
            AuthorId = comment.AuthorId,
        };
        CopyReplies(comment, clone);
        return clone;
    }

    private static void CopyReplies(SlideComment source, SlideComment target)
    {
        target.Replies.Clear();
        foreach (var reply in source.Replies)
        {
            target.Replies.Add(new SlideCommentReply
            {
                AuthorId = reply.AuthorId,
                Author = reply.Author,
                Initials = reply.Initials,
                Text = reply.Text,
                DateTime = reply.DateTime,
            });
        }
    }

    private static PresentationCommentMutationPlan InvalidMutation(
        PresentationReviewWorkflowIntentKind intent,
        int slideIndex,
        int? commentIndex,
        string message)
        => new(intent, false, slideIndex, commentIndex, null, message);

    private static int? NormalizeSelectedCommentIndex(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex)
    {
        var comments = GetSlide(slides, slideIndex)?.Comments;
        if (comments is null || comments.Count == 0)
        {
            return null;
        }

        if (selectedCommentIndex is not { } index)
        {
            return 0;
        }

        return index >= 0 && index < comments.Count ? index : null;
    }

    private static bool TryGetAdjacentComment(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex,
        int direction,
        out (int slideIndex, int commentIndex) target)
    {
        var flattened = slides
            .SelectMany((slide, si) => slide.Comments.Select((_, ci) => (slideIndex: si, commentIndex: ci)))
            .ToArray();
        if (flattened.Length == 0)
        {
            target = default;
            return false;
        }

        var current = selectedCommentIndex.HasValue
            ? Array.FindIndex(flattened, item => item.slideIndex == slideIndex && item.commentIndex == selectedCommentIndex.Value)
            : Array.FindIndex(flattened, item => item.slideIndex >= slideIndex);

        if (current < 0)
        {
            target = default;
            return false;
        }

        var candidate = current + Math.Sign(direction);
        if (candidate < 0 || candidate >= flattened.Length)
        {
            target = default;
            return false;
        }

        target = flattened[candidate];
        return true;
    }

    private static Slide? GetSlide(IReadOnlyList<Slide> slides, int slideIndex)
        => slideIndex >= 0 && slideIndex < slides.Count ? slides[slideIndex] : null;

    private static SlideComment? GetComment(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex)
    {
        var comments = GetSlide(slides, slideIndex)?.Comments;
        return comments is not null && commentIndex >= 0 && commentIndex < comments.Count
            ? comments[commentIndex]
            : null;
    }

    private static string? NormalizeText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string NormalizeAltTextTitle(string? value)
        => NormalizeText(value) ?? string.Empty;

    private static string NormalizeAltTextDescription(string? value)
        => NormalizeText(value) ?? string.Empty;

    private static string NormalizeInitials(string? initials, string? author)
    {
        var normalized = NormalizeText(initials);
        if (normalized is not null)
        {
            return normalized.Length <= 3 ? normalized : normalized[..3];
        }

        var name = NormalizeText(author) ?? "FreeP User";
        var parts = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .Select(part => char.ToUpperInvariant(part[0]));
        var derived = string.Concat(parts);
        return string.IsNullOrWhiteSpace(derived) ? "FU" : derived;
    }

    private static string BuildPreview(string? text)
    {
        var normalized = NormalizeText(text) ?? string.Empty;
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }

    private static int CountMentions(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Count(part => part.Length > 1 && part[0] == '@');

    private static string BuildAltTextSuggestedTitle(SlideShape shape)
    {
        var title = NormalizeText(shape.AlternativeTextTitle);
        if (title is not null)
        {
            return title;
        }

        var name = NormalizeText(shape.Name);
        return name is not null ? name : DescribeShape(shape);
    }

    private static bool NeedsAltText(SlideShape shape)
        => shape.Kind is SlideShapeKind.Picture
            or SlideShapeKind.Chart
            or SlideShapeKind.SmartArt
            or SlideShapeKind.Media
            or SlideShapeKind.Ole
            or SlideShapeKind.Model3d
            or SlideShapeKind.Zoom
            or SlideShapeKind.PreservedObject;

    private static string DescribeShape(SlideShape shape)
        => string.IsNullOrWhiteSpace(shape.Name)
            ? $"{shape.Kind} {shape.Id}"
            : shape.Name;

    private static PresentationReadingOrderItemPlan DescribeReadingOrderItem(
        SlideShape shape,
        int readingOrderIndex,
        int nestingDepth,
        bool isSelected)
    {
        var title = NormalizeAltTextTitle(shape.AlternativeTextTitle);
        var description = NormalizeAltTextDescription(shape.AlternativeText);
        return new PresentationReadingOrderItemPlan(
            readingOrderIndex,
            nestingDepth,
            shape.Id,
            DescribeShape(shape),
            shape.Kind,
            shape.Kind.ToString(),
            title,
            description,
            shape.IsDecorative,
            BuildAccessibilitySummary(title, description, shape.IsDecorative),
            isSelected);
    }

    private static string BuildAccessibilitySummary(
        string title,
        string description,
        bool isDecorative)
    {
        if (isDecorative)
        {
            return "Decorative";
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
        {
            return "No alt text";
        }

        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(description))
        {
            return $"{title}: {description}";
        }

        return string.IsNullOrWhiteSpace(description) ? title : description;
    }

    private static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in EnumerateShapes(shape.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<PresentationProofingScopeDescriptor> EnumerateProofingScopes(Presentation presentation)
    {
        for (int slideIndex = 0; slideIndex < presentation.Slides.Count; slideIndex++)
        {
            var slide = presentation.Slides[slideIndex];
            foreach (var shape in EnumerateShapes(slide.Shapes))
            {
                var text = shape.TextBody is null ? null : TextBodyToPlainText(shape.TextBody);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var isTitle = shape.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle;
                    yield return new PresentationProofingScopeDescriptor(
                        isTitle ? PresentationProofingScopeKind.SlideTitle : PresentationProofingScopeKind.ShapeText,
                        slideIndex,
                        shape.Id,
                        null,
                        null,
                        null,
                        null,
                        isTitle ? $"Slide {slideIndex + 1} title" : DescribeShape(shape),
                        text,
                        BuildPreview(text));
                }

                foreach (var tableCell in EnumerateTableCellProofingScopes(slideIndex, shape))
                {
                    yield return tableCell;
                }
            }

            if (slide.Notes is not null)
            {
                var notesText = TextBodyToPlainText(slide.Notes);
                if (!string.IsNullOrWhiteSpace(notesText))
                {
                    yield return new PresentationProofingScopeDescriptor(
                        PresentationProofingScopeKind.SpeakerNotes,
                        slideIndex,
                        null,
                        null,
                        null,
                        null,
                        null,
                        $"Slide {slideIndex + 1} speaker notes",
                        notesText,
                        BuildPreview(notesText));
                }
            }

            for (int commentIndex = 0; commentIndex < slide.Comments.Count; commentIndex++)
            {
                var comment = slide.Comments[commentIndex];
                if (!string.IsNullOrWhiteSpace(comment.Text))
                {
                    yield return new PresentationProofingScopeDescriptor(
                        PresentationProofingScopeKind.Comment,
                        slideIndex,
                        null,
                        null,
                        null,
                        commentIndex,
                        null,
                        $"Slide {slideIndex + 1} comment {commentIndex + 1}",
                        comment.Text,
                        BuildPreview(comment.Text));
                }

                for (int replyIndex = 0; replyIndex < comment.Replies.Count; replyIndex++)
                {
                    var reply = comment.Replies[replyIndex];
                    if (string.IsNullOrWhiteSpace(reply.Text))
                    {
                        continue;
                    }

                    yield return new PresentationProofingScopeDescriptor(
                        PresentationProofingScopeKind.CommentReply,
                        slideIndex,
                        null,
                        null,
                        null,
                        commentIndex,
                        replyIndex,
                        $"Slide {slideIndex + 1} comment {commentIndex + 1} reply {replyIndex + 1}",
                        reply.Text,
                        BuildPreview(reply.Text));
                }
            }
        }
    }

    private static IEnumerable<PresentationProofingScopeDescriptor> EnumerateTableCellProofingScopes(
        int slideIndex,
        SlideShape shape)
    {
        if (shape.Table is null)
        {
            yield break;
        }

        for (int rowIndex = 0; rowIndex < shape.Table.Rows.Count; rowIndex++)
        {
            var row = shape.Table.Rows[rowIndex];
            for (int columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                var cell = row.Cells[columnIndex];
                if (cell.TextBody is null)
                {
                    continue;
                }

                var text = TextBodyToPlainText(cell.TextBody);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                yield return new PresentationProofingScopeDescriptor(
                    PresentationProofingScopeKind.TableCellText,
                    slideIndex,
                    shape.Id,
                    rowIndex,
                    columnIndex,
                    null,
                    null,
                    $"{DescribeShape(shape)} cell {rowIndex + 1},{columnIndex + 1}",
                    text,
                    BuildPreview(text));
            }
        }
    }

    private static bool TryGetProofingScopeText(
        Presentation presentation,
        PresentationProofingScopeDescriptor scope,
        out string text,
        out Action<string> apply,
        out string validationMessage)
    {
        text = string.Empty;
        apply = _ => { };
        validationMessage = ProofingCorrectionMissingScopeMessage;

        var slide = GetSlide(presentation.Slides, scope.SlideIndex);
        if (slide is null)
        {
            validationMessage = ProofingCorrectionMissingSlideMessage;
            return false;
        }

        switch (scope.Kind)
        {
            case PresentationProofingScopeKind.SlideTitle:
            case PresentationProofingScopeKind.ShapeText:
            {
                if (scope.ShapeId is not { } shapeId)
                    return false;

                var shape = FindShape(slide.Shapes, shapeId);
                if (shape?.TextBody is null)
                    return false;

                text = TextBodyToPlainText(shape.TextBody);
                apply = updatedText => shape.TextBody =
                    InCanvasTextEditPlanner.BuildPlainTextBody(shape.TextBody, updatedText);
                return true;
            }

            case PresentationProofingScopeKind.TableCellText:
            {
                if (scope.ShapeId is not { } shapeId ||
                    scope.TableRowIndex is not { } rowIndex ||
                    scope.TableColumnIndex is not { } columnIndex)
                {
                    return false;
                }

                var shape = FindShape(slide.Shapes, shapeId);
                if (shape?.Table is null ||
                    rowIndex < 0 ||
                    rowIndex >= shape.Table.Rows.Count ||
                    columnIndex < 0 ||
                    columnIndex >= shape.Table.Rows[rowIndex].Cells.Count)
                {
                    return false;
                }

                var cell = shape.Table.Rows[rowIndex].Cells[columnIndex];
                if (cell.TextBody is null)
                    return false;

                text = TextBodyToPlainText(cell.TextBody);
                apply = updatedText => cell.TextBody =
                    InCanvasTextEditPlanner.BuildPlainTextBody(cell.TextBody, updatedText);
                return true;
            }

            case PresentationProofingScopeKind.SpeakerNotes:
            {
                if (slide.Notes is null)
                    return false;

                text = TextBodyToPlainText(slide.Notes);
                apply = updatedText => slide.Notes =
                    InCanvasTextEditPlanner.BuildPlainTextBody(slide.Notes, updatedText);
                return true;
            }

            case PresentationProofingScopeKind.Comment:
            {
                if (scope.CommentIndex is not { } commentIndex ||
                    commentIndex < 0 ||
                    commentIndex >= slide.Comments.Count)
                {
                    return false;
                }

                var comment = slide.Comments[commentIndex];
                text = comment.Text;
                apply = updatedText => comment.Text = updatedText;
                return true;
            }

            case PresentationProofingScopeKind.CommentReply:
            {
                if (scope.CommentIndex is not { } commentIndex ||
                    scope.ReplyIndex is not { } replyIndex ||
                    commentIndex < 0 ||
                    commentIndex >= slide.Comments.Count ||
                    replyIndex < 0 ||
                    replyIndex >= slide.Comments[commentIndex].Replies.Count)
                {
                    return false;
                }

                var reply = slide.Comments[commentIndex].Replies[replyIndex];
                text = reply.Text;
                apply = updatedText => reply.Text = updatedText;
                return true;
            }

            default:
                return false;
        }
    }

    private static string ReplaceTextRange(string text, int start, int length, string replacement)
        => string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));

    private static IEnumerable<(SlideShape Shape, int Depth)> EnumerateShapesWithDepth(
        IEnumerable<SlideShape> shapes,
        int depth = 0)
    {
        foreach (var shape in shapes)
        {
            yield return (shape, depth);
            foreach (var child in EnumerateShapesWithDepth(shape.Children, depth + 1))
            {
                yield return child;
            }
        }
    }

    private static ReadingOrderMoveTarget? FindReadingOrderMoveTarget(
        Slide slide,
        uint shapeId,
        int offset)
    {
        var siblings = FindContainingShapeList(slide.Shapes, shapeId);
        if (siblings is null)
        {
            return null;
        }

        var sourceIndex = FindShapeIndex(siblings, shapeId);
        if (sourceIndex < 0)
        {
            return null;
        }

        return new ReadingOrderMoveTarget(
            sourceIndex,
            sourceIndex + offset,
            siblings.Count);
    }

    private static IReadOnlyList<SlideShape>? FindContainingShapeList(
        IReadOnlyList<SlideShape> shapes,
        uint shapeId)
    {
        if (FindShapeIndex(shapes, shapeId) >= 0)
        {
            return shapes;
        }

        foreach (var shape in shapes)
        {
            var childShapes = FindContainingShapeList(shape.Children, shapeId);
            if (childShapes is not null)
            {
                return childShapes;
            }
        }

        return null;
    }

    private static int FindShapeIndex(IReadOnlyList<SlideShape> shapes, uint shapeId)
    {
        for (var index = 0; index < shapes.Count; index++)
        {
            if (shapes[index].Id == shapeId)
            {
                return index;
            }
        }

        return -1;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape>? shapes, uint shapeId)
    {
        if (shapes is null)
        {
            return null;
        }

        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
            {
                return shape;
            }

            var child = FindShape(shape.Children, shapeId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string TextBodyToPlainText(TextBody textBody)
        => string.Join("\n", textBody.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
}
