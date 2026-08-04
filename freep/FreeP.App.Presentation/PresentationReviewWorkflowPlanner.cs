using System.Globalization;
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
    SetSlideTitle,
    OpenAltText,
    ApplyAltText,
    ToggleAltTextDecorative,
    CloseAltTextPane,
    OpenReadingOrderPane,
    MoveReadingOrderEarlier,
    MoveReadingOrderLater,
    SelectReadingOrderItem,
    SetTableHeaderRow,
    ReviewTableStructure,
    RunProofing,
    SelectProofingIssue,
    IgnoreProofingIssue,
    IgnoreAllProofingIssues,
    AddProofingWordToDictionary,
    ApplyProofingCorrection
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

public enum PresentationCommentPaneFilterKind
{
    All,
    Open,
    Resolved,
    Mentions
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

public sealed record PresentationCommentMentionDescriptor(
    int MentionIndex,
    int Start,
    int Length,
    string DisplayText,
    string IdentityKey)
{
    public string Label => $"@{DisplayText}";
}

public sealed record PresentationCommentMentionCandidate(
    string DisplayName,
    string Initials,
    string IdentityKey,
    string InsertToken,
    string SourceLabel)
{
    public string Label => $"@{InsertToken}";

    public string Summary => string.IsNullOrWhiteSpace(Initials)
        ? DisplayName
        : $"{DisplayName} ({Initials})";
}

public sealed record PresentationCommentMentionPickerPlan(
    string Query,
    IReadOnlyList<PresentationCommentMentionCandidate> Candidates)
{
    public bool HasCandidates => Candidates.Count > 0;

    public PresentationCommentMentionCandidate? DefaultCandidate => HasCandidates ? Candidates[0] : null;

    public string SummaryLabel => HasCandidates
        ? PresentationCommentMetadataPolicy.BuildCountSummary(Candidates.Count, "mention candidate")
        : "No mention candidates";
}

public sealed record PresentationCommentMentionInsertionPlan(
    bool ShouldApply,
    string OriginalText,
    string UpdatedText,
    int SelectionStart,
    int SelectionLength,
    PresentationCommentMentionCandidate? Candidate,
    string? ValidationMessage);

public sealed record PresentationCommentReplyDescriptor(
    int ReplyIndex,
    string Author,
    string Initials,
    string TextPreview,
    DateTime? Timestamp,
    int MentionCount)
{
    public string ModernAuthorId { get; init; } = string.Empty;

    public string ModernAuthorUserId { get; init; } = string.Empty;

    public string ModernAuthorProviderId { get; init; } = string.Empty;

    public string ModernReplyId { get; init; } = string.Empty;

    public string AuthorDisplayName => PresentationCommentMetadataPolicy.NormalizeAuthorDisplayName(Author);

    public string InitialsBadgeText => PresentationCommentMetadataPolicy.NormalizeInitialsBadge(Initials, AuthorDisplayName);

    public string AuthorIdentityKey => PresentationCommentMetadataPolicy.BuildAuthorIdentityKey(AuthorDisplayName, InitialsBadgeText);

    public string ReplyLabel => $"Reply {ReplyIndex + 1}";

    public string MentionSummary => PresentationCommentMetadataPolicy.BuildCountSummary(MentionCount, "mention");

    public IReadOnlyList<PresentationCommentMentionDescriptor> Mentions { get; init; } = [];

    public string MentionDetailSummary => PresentationCommentMetadataPolicy.BuildMentionDetailSummary(Mentions);
}

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
    string ModernAnchorKind,
    bool CanEdit,
    bool CanReply,
    bool CanDelete,
    bool CanResolve,
    bool CanReopen,
    int ReplyCount,
    int MentionCount,
    IReadOnlyList<PresentationCommentReplyDescriptor> Replies,
    PresentationCommentThreadStatus ThreadStatus,
    bool IsSelected,
    string ResolvedBy = "",
    DateTime? ResolvedTimestamp = null)
{
    public string ModernCommentId { get; init; } = string.Empty;

    public string ModernAuthorId { get; init; } = string.Empty;

    public string ModernAuthorUserId { get; init; } = string.Empty;

    public string ModernAuthorProviderId { get; init; } = string.Empty;

    public string AuthorDisplayName => PresentationCommentMetadataPolicy.NormalizeAuthorDisplayName(Author);

    public string InitialsBadgeText => PresentationCommentMetadataPolicy.NormalizeInitialsBadge(Initials, AuthorDisplayName);

    public string AuthorIdentityKey => PresentationCommentMetadataPolicy.BuildAuthorIdentityKey(AuthorDisplayName, InitialsBadgeText);

    public string ThreadStatusLabel => ThreadStatus == PresentationCommentThreadStatus.Resolved ? "Resolved" : "Open";

    public string ResolvedByDisplayName =>
        ThreadStatus == PresentationCommentThreadStatus.Resolved
            ? PresentationCommentMetadataPolicy.NormalizeAuthorDisplayName(ResolvedBy)
            : string.Empty;

    public string ReplySummary => PresentationCommentMetadataPolicy.BuildCountSummary(ReplyCount, "reply");

    public string MentionSummary => PresentationCommentMetadataPolicy.BuildCountSummary(MentionCount, "mention");

    public IReadOnlyList<PresentationCommentMentionDescriptor> Mentions { get; init; } = [];

    public string MentionDetailSummary => PresentationCommentMetadataPolicy.BuildMentionDetailSummary(Mentions);

    public string AnchorSummary =>
        string.IsNullOrWhiteSpace(ModernAnchorKind)
            ? $"Legacy comment anchor at {Xemu},{Yemu} EMU"
            : $"{PresentationCommentMetadataPolicy.BuildAnchorDisplayName(ModernAnchorKind)} at {Xemu},{Yemu} EMU";

    public string ThreadStatusSummary =>
        ThreadStatus == PresentationCommentThreadStatus.Resolved
            ? string.IsNullOrWhiteSpace(ResolvedByDisplayName)
                ? "Resolved"
                : $"Resolved by {ResolvedByDisplayName}"
            : ReplyCount == 0
                ? "Open"
                : $"Open - {ReplySummary}";
}

public sealed record PresentationCommentPanePlan(
    int SlideIndex,
    int SlideCount,
    int SlideCommentCount,
    int TotalCommentCount,
    int OpenThreadCount,
    int ResolvedThreadCount,
    int TotalReplyCount,
    int TotalMentionCount,
    PresentationCommentPaneFilterKind FilterKind,
    int FilteredCommentCount,
    IReadOnlyList<PresentationCommentPaneFilterPlan> Filters,
    int SelectedCommentIndex,
    IReadOnlyList<PresentationCommentDescriptor> Comments,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions)
{
    public PresentationCommentDescriptor? SelectedComment =>
        SelectedCommentIndex >= 0 && SelectedCommentIndex < Comments.Count
            ? Comments[SelectedCommentIndex]
            : null;

    public string DeckSummaryLabel
    {
        get
        {
            var threadSummary = PresentationCommentMetadataPolicy.BuildCountSummary(TotalCommentCount, "thread");
            var openSummary = PresentationCommentMetadataPolicy.BuildCountSummary(OpenThreadCount, "open thread");
            var resolvedSummary = PresentationCommentMetadataPolicy.BuildCountSummary(ResolvedThreadCount, "resolved thread");
            var replySummary = PresentationCommentMetadataPolicy.BuildCountSummary(TotalReplyCount, "reply");
            var mentionSummary = PresentationCommentMetadataPolicy.BuildCountSummary(TotalMentionCount, "mention");
            return $"{threadSummary}: {openSummary}, {resolvedSummary}, {replySummary}, {mentionSummary}";
        }
    }

    public string CurrentSlideSummaryLabel =>
        $"Slide {SlideIndex + 1}: {PresentationCommentMetadataPolicy.BuildCountSummary(SlideCommentCount, "thread")}";

    public string FilterSummaryLabel =>
        FilterKind == PresentationCommentPaneFilterKind.All
            ? "Showing all threads"
            : $"Showing {PresentationCommentMetadataPolicy.BuildCountSummary(FilteredCommentCount, "thread")}";
}

public sealed record PresentationCommentPaneFilterPlan(
    PresentationCommentPaneFilterKind Kind,
    string Label,
    int Count,
    bool IsSelected)
{
    public bool HasMatches => Count > 0;

    public string Summary => $"{Label}: {PresentationCommentMetadataPolicy.BuildCountSummary(Count, "thread")}";
}

public sealed record PresentationCommentMutationPlan(
    PresentationReviewWorkflowIntentKind Intent,
    bool ShouldApply,
    int SlideIndex,
    int? CommentIndex,
    SlideComment? Comment,
    string? ValidationMessage);

public sealed record PresentationCommentNavigationPlan(
    PresentationReviewWorkflowIntentKind Intent,
    bool ShouldNavigate,
    int SourceSlideIndex,
    int? SourceCommentIndex,
    int TargetSlideIndex,
    int TargetCommentIndex,
    string? DisabledReason);

public sealed record PresentationAltTextRequestPlan(
    bool HasSelection,
    uint? ShapeId,
    string ShapeName,
    string SuggestedTitle,
    string SuggestedDescription,
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

public sealed record PresentationSlideTitleMutationPlan(
    bool ShouldApply,
    int SlideIndex,
    string Title,
    string SuggestedTitle,
    string? ValidationMessage);

public sealed record PresentationChartTitleMutationPlan(
    bool ShouldApply,
    int SlideIndex,
    uint? ShapeId,
    string Title,
    string SuggestedTitle,
    string? ValidationMessage);

public sealed record PresentationTableHeaderRowMutationPlan(
    bool ShouldApply,
    int SlideIndex,
    uint? ShapeId,
    string? ValidationMessage);

public sealed record PresentationTableStructureCellPlan(
    int RowIndex,
    int ColumnIndex,
    string CellReference);

public sealed record PresentationTableStructureSpanPlan(
    int RowIndex,
    int ColumnIndex,
    string CellReference,
    int GridSpan,
    int RowSpan,
    bool IsHorizontalMergeContinuation,
    bool IsVerticalMergeContinuation,
    string Summary);

public sealed record PresentationTableStructureReviewPlan(
    bool CanReview,
    int SlideIndex,
    uint? ShapeId,
    string TableName,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<PresentationTableStructureCellPlan> BlankHeaderCells,
    IReadOnlyList<PresentationTableStructureCellPlan> BlankBodyCells,
    IReadOnlyList<PresentationTableStructureSpanPlan> MergedOrSplitCells,
    string Guidance,
    bool ShouldNavigateToSlide,
    bool ShouldSelectTable,
    string? ValidationMessage);

public sealed record PresentationTableStructureReviewDetailRowPlan(
    string Category,
    string Summary,
    string Detail);

public sealed record PresentationTableStructureReviewDisplayPlan(
    bool CanReview,
    string Heading,
    string Summary,
    string Guidance,
    IReadOnlyList<PresentationTableStructureReviewDetailRowPlan> Details,
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

public sealed record PresentationAccessibilityCheckerRowPlan(
    int RowIndex,
    PresentationAccessibilityIssueSeverity Severity,
    string Category,
    int SlideIndex,
    string SlideDisplay,
    uint? ShapeId,
    string ShapeName,
    string Title,
    string Detail,
    bool IsSelected,
    string ActionLabel,
    string? CommandHint,
    bool ShouldNavigateToSlide,
    bool ShouldSelectShape);

public sealed record PresentationAccessibilityCheckerPanePlan(
    int SlideCount,
    int IssueCount,
    int SelectedRowIndex,
    IReadOnlyList<PresentationAccessibilityCheckerRowPlan> Rows,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions)
{
    public PresentationAccessibilityCheckerRowPlan? SelectedRow =>
        SelectedRowIndex >= 0 && SelectedRowIndex < Rows.Count
            ? Rows[SelectedRowIndex]
            : null;
}

public sealed record PresentationAccessibilityCheckerNavigationPlan(
    bool ShouldNavigate,
    int SelectedRowIndex,
    int TargetSlideIndex,
    uint? TargetShapeId,
    bool ShouldSelectShape,
    string? ValidationMessage);

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

public sealed record PresentationReadingOrderSelectionPlan(
    PresentationReviewWorkflowIntentKind Intent,
    bool ShouldSelect,
    int SlideIndex,
    uint? ShapeId,
    int ItemIndex,
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

public sealed record PresentationProofingIgnoredIssueDescriptor(
    PresentationProofingScopeDescriptor Scope,
    int Start,
    int Length,
    string Text,
    string Message);

public sealed record PresentationProofingIgnoredIssueGroupDescriptor(
    string NormalizedText,
    string Message);

public sealed record PresentationProofingIgnoreState(
    IReadOnlyList<PresentationProofingIgnoredIssueDescriptor> IgnoredIssues,
    IReadOnlyList<PresentationProofingIgnoredIssueGroupDescriptor> IgnoredIssueGroups)
{
    public static PresentationProofingIgnoreState Empty { get; } = new([], []);
}

public sealed record PresentationProofingDictionaryState(
    IReadOnlyList<string> NormalizedWords)
{
    public static PresentationProofingDictionaryState Empty { get; } = new([]);
}

public sealed record PresentationProofingExecutionPlan(
    bool CanRun,
    PresentationWorkflowCapabilityStatus Status,
    int ScopeCount,
    int IssueCount,
    IReadOnlyList<PresentationProofingScopeDescriptor> Scopes,
    IReadOnlyList<PresentationProofingIssueDescriptor> Issues,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions,
    string Message);

public sealed record PresentationProofingIssueRowPlan(
    int RowIndex,
    PresentationProofingScopeDescriptor Scope,
    int Start,
    int Length,
    string Text,
    string Message,
    string SourceName,
    string SlideDisplay,
    string Snippet,
    string SuggestedReplacement,
    bool IsSelected,
    PresentationReviewWorkflowActionPlan CorrectionAction,
    PresentationReviewWorkflowActionPlan IgnoreAction,
    PresentationReviewWorkflowActionPlan IgnoreAllAction,
    PresentationReviewWorkflowActionPlan AddToDictionaryAction);

public sealed record PresentationProofingPanePlan(
    bool CanRun,
    PresentationWorkflowCapabilityStatus Status,
    int ScopeCount,
    int IssueCount,
    int SelectedRowIndex,
    IReadOnlyList<PresentationProofingIssueRowPlan> Rows,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions,
    string Message)
{
    public PresentationProofingIssueRowPlan? SelectedRow =>
        SelectedRowIndex >= 0 && SelectedRowIndex < Rows.Count
            ? Rows[SelectedRowIndex]
            : null;
}

public sealed record PresentationProofingCorrectionMutationPlan(
    bool ShouldApply,
    PresentationProofingScopeDescriptor Scope,
    int Start,
    int Length,
    string Replacement,
    string? UpdatedText,
    string? ValidationMessage);

internal static class PresentationCommentMetadataPolicy
{
    private const string UnknownReviewerDisplayName = "Unknown reviewer";

    public static string NormalizeAuthorDisplayName(string? author)
        => NormalizeText(author) ?? UnknownReviewerDisplayName;

    public static string NormalizeInitialsBadge(string? initials, string authorDisplayName)
    {
        if (NormalizeText(initials) is { } normalizedInitials)
        {
            return normalizedInitials.ToUpperInvariant();
        }

        var letters = NormalizeText(authorDisplayName)?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.FirstOrDefault(char.IsLetterOrDigit))
            .Where(ch => ch != default)
            .Take(3)
            .ToArray();

        return letters is { Length: > 0 }
            ? new string(letters).ToUpperInvariant()
            : "?";
    }

    public static string BuildAuthorIdentityKey(string authorDisplayName, string initialsBadgeText)
        => $"{NormalizeAuthorDisplayName(authorDisplayName).ToUpperInvariant()}|{NormalizeInitialsBadge(initialsBadgeText, authorDisplayName)}";

    public static string BuildCountSummary(int count, string singularNoun)
        => count == 1
            ? $"1 {singularNoun}"
            : singularNoun.EndsWith('y')
                ? $"{Math.Max(0, count)} {singularNoun[..^1]}ies"
                : $"{Math.Max(0, count)} {singularNoun}s";

    public static string BuildAnchorDisplayName(string? anchorKind)
    {
        var normalized = NormalizeText(anchorKind);
        if (normalized is null)
        {
            return "Legacy comment anchor";
        }

        var words = normalized.EndsWith("Anchor", StringComparison.Ordinal)
            ? normalized[..^"Anchor".Length]
            : normalized;
        if (string.IsNullOrWhiteSpace(words))
        {
            return "Modern comment anchor";
        }

        return string.Concat(
            words.SelectMany((ch, index) =>
                index > 0 && char.IsUpper(ch)
                    ? new[] { ' ', char.ToLowerInvariant(ch) }
                    : new[] { char.ToLowerInvariant(ch) })) + " anchor";
    }

    public static string BuildMentionIdentityKey(string displayText)
        => NormalizeText(displayText)?.ToUpperInvariant() ?? string.Empty;

    public static string BuildMentionDetailSummary(IReadOnlyList<PresentationCommentMentionDescriptor> mentions)
    {
        if (mentions.Count == 0)
        {
            return "No mentions";
        }

        var labels = mentions
            .Take(3)
            .Select(mention => mention.Label)
            .ToArray();
        if (mentions.Count <= labels.Length)
        {
            return $"Mentions: {string.Join(", ", labels)}";
        }

        return $"Mentions: {string.Join(", ", labels)}, +{mentions.Count - labels.Length} more";
    }

    private static string? NormalizeText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

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
    public const string SetSlideTitleCommandId = "freep.review.accessibility.set-slide-title";
    public const string ChartTitleCommandId = "freep.review.accessibility.chart-title";
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
    public const string SetTableHeaderRowCommandId = "freep.review.accessibility.set-table-header-row";
    public const string ReviewTableStructureCommandId = "freep.review.accessibility.review-table-structure";
    public const string ProofingCommandId = "freep.review.proofing.spelling";
    public const string ProofingApplyCorrectionCommandId = "freep.review.proofing.apply-correction";
    public const string ProofingIgnoreCommandId = "freep.review.proofing.ignore";
    public const string ProofingIgnoreAllCommandId = "freep.review.proofing.ignore-all";
    public const string ProofingAddToDictionaryCommandId = "freep.review.proofing.add-to-dictionary";
    public const string InsertLinkCommandId = "freep.insert-link";

    public const string MissingSlideMessage = "Select a slide before adding a comment.";
    public const string MissingCommentMessage = "Select an existing comment first.";
    public const string EmptyCommentMessage = "Comment text cannot be empty.";
    public const string EmptyCommentReplyMessage = "Reply text cannot be empty.";
    public const string MissingMentionCandidateMessage = "Select a mention candidate first.";
    public const string CannotReplyToResolvedCommentMessage =
        "Reopen the thread before adding a reply.";
    public const string CommentAlreadyResolvedMessage =
        "Selected comment thread is already resolved.";
    public const string CommentAlreadyOpenMessage =
        "Selected comment thread is already open.";
    public const string MissingShapeMessage = "Select a shape before editing alt text.";
    public const string MissingAltTextDescriptionMessage =
        "Alt text description is required unless the object is marked decorative.";
    private const string GenericAltTextDescriptionPlaceholder =
        "Describe the selected object for people who cannot see it.";
    public const string MissingReadingOrderSelectionMessage =
        "Select one shape before changing reading order.";
    public const string EmptyReadingOrderMessage =
        "Current slide has no shapes in the reading order.";
    public const string ReadingOrderAlreadyEarliestMessage =
        "Selected shape is already earliest in the reading order.";
    public const string ReadingOrderAlreadyLatestMessage =
        "Selected shape is already latest in the reading order.";
    public const string ReadingOrderItemNotFoundMessage =
        "Reading order item is no longer available.";
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
    public const string ProofingNoIssuesMessage =
        "No proofing issues found in slide text, notes, or comments.";
    public const string ProofingMissingIssueMessage =
        "Select a proofing issue before applying a correction.";
    public const string ProofingMissingIgnoreIssueMessage =
        "Select a proofing issue before ignoring it.";
    public const string ProofingMissingDictionaryIssueMessage =
        "Select a spelling issue before adding it to the dictionary.";
    public const string ProofingCannotAddToDictionaryMessage =
        "Only spelling issues backed by a single word can be added to the dictionary.";
    public const string ProofingNoSuggestionMessage =
        "No replacement suggestion is available for the selected proofing issue.";
    public const string ProofingPossibleMisspellingMessage =
        "Possible misspelling.";
    public const string ProofingWhitespaceBeforePunctuationMessage =
        "Remove the space before punctuation.";
    public const string ProofingMissingSpaceAfterSentencePunctuationMessage =
        "Add a space after sentence punctuation.";
    public const string ProofingMissingSpaceAfterListPunctuationMessage =
        "Add a space after list punctuation.";
    public const string ProofingExtraSpacesMessage =
        "Remove extra spaces between words or sentences.";
    public const string ProofingRepeatedTerminalPunctuationMessage =
        "Use a single terminal punctuation mark.";
    public const string ProofingArticleAgreementMessage =
        "Use the article that matches the next word.";
    public const string ProofingSubjectVerbAgreementMessage =
        "Use the verb form that matches the subject.";
    public const string ProofingGrammarConfusionMessage =
        "Check this grammar phrase.";
    public const string SlideTitleMissingSlideMessage =
        "Slide title target slide was not found.";
    public const string SlideTitleEmptyMessage =
        "Enter a slide title before applying the accessibility fix.";
    public const string ChartTitleMissingSlideMessage =
        "Chart title target slide was not found.";
    public const string ChartTitleMissingShapeMessage =
        "Chart title target chart was not found.";
    public const string ChartTitleAlreadySetMessage =
        "The selected chart already has a title.";
    public const string TableHeaderRowMissingSlideMessage =
        "Table header-row target slide was not found.";
    public const string TableHeaderRowMissingShapeMessage =
        "Table header-row target table was not found.";
    public const string TableHeaderRowAlreadySetMessage =
        "The selected table already marks its first row as a header row.";
    public const string TableStructureReviewMissingSlideMessage =
        "Table-structure review target slide was not found.";
    public const string TableStructureReviewMissingShapeMessage =
        "Table-structure review target table was not found.";
    public const string TableStructureReviewGuidance =
        "Review the listed cells in PowerPoint-style table editing. Add concise header/body text, remove unused rows or columns, or split complex merged cells manually; FreeP will not auto-simplify the table.";
    public const string MissingSlideTitleActionSummary =
        "Add a concise slide title so screen-reader users can navigate the deck.";
    public const string DuplicateSlideTitleActionSummary =
        "Give this slide a unique title so screen-reader users can distinguish it in the deck outline.";
    public const string MissingAltTextActionSummary =
        "Select the object and add alt text that describes the informative content.";
    public const string DuplicateAltTextActionSummary =
        "Select the object and replace reused alt text with a description specific to this object.";
    public const string FilenameLikeAltTextActionSummary =
        "Select the object and replace filename-like alt text with a meaningful visual description.";
    public const string MissingChartTitleActionSummary =
        "Add a concise chart title so screen-reader users can understand the chart purpose.";
    public const string MissingVideoCaptionsActionSummary =
        "Add closed captions or subtitles for video content so people who cannot hear the audio can follow along.";
    public const string MissingHyperlinkScreenTipActionSummary =
        "Edit the hyperlink and add ScreenTip text that explains the destination.";
    public const string UnclearHyperlinkTextActionSummary =
        "Edit the hyperlink display text so it describes the destination.";
    public const string MissingTableHeaderRowActionSummary =
        "Select the table and enable the header row option so assistive technology can identify column headings.";
    public const string BlankTableHeaderCellsActionSummary =
        "Add concise text to blank header cells so assistive technology can announce each column heading.";
    public const string BlankTableBodyCellsActionSummary =
        "Add concise text to blank body cells or remove unused rows and columns.";
    public const string MergedTableCellsActionSummary =
        "Review merged or split cells and simplify the table structure or add clear text cues.";
    public const double TextContrastMinimumRatio = 4.5;
    public const string LowTextContrastActionSummary =
        "Select the object and use text or background colors with at least a 4.5:1 contrast ratio.";

    public static PresentationCommentPanePlan BuildCommentPanePlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex = null,
        PresentationCommentPaneFilterKind filterKind = PresentationCommentPaneFilterKind.All)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var comments = GetSlide(slides, slideIndex)?.Comments ?? [];
        var filteredComments = comments
            .Select((comment, index) => (Comment: comment, Index: index))
            .Where(item => MatchesCommentPaneFilter(item.Comment, filterKind))
            .ToArray();
        var selected = NormalizeSelectedCommentIndex(
            filteredComments.Select(item => item.Index).ToArray(),
            selectedCommentIndex,
            useFirstVisibleWhenInvalid: filterKind != PresentationCommentPaneFilterKind.All);
        var descriptors = filteredComments
            .Select(item => DescribeComment(slideIndex, item.Index, item.Comment, selected == item.Index))
            .ToArray();
        var deckComments = slides.SelectMany(slide => slide.Comments).ToArray();
        var total = deckComments.Length;
        var open = deckComments.Count(comment => !comment.IsResolved);
        var resolved = deckComments.Count(comment => comment.IsResolved);
        var replies = deckComments.Sum(comment => comment.Replies.Count);
        var mentions = deckComments.Sum(comment =>
            CountMentions(comment.Text) + comment.Replies.Sum(reply => CountMentions(reply.Text)));
        var filterPlans = BuildCommentPaneFilters(comments, filterKind);
        var selectedDisplayIndex = Array.FindIndex(descriptors, descriptor => descriptor.IsSelected);

        return new PresentationCommentPanePlan(
            slideIndex,
            slides.Count,
            comments.Count,
            total,
            open,
            resolved,
            replies,
            mentions,
            filterKind,
            descriptors.Length,
            filterPlans,
            selectedDisplayIndex,
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
            UsesModernCommentSchema = current.UsesModernCommentSchema,
            ModernCommentId = current.ModernCommentId,
            ModernAuthorId = current.ModernAuthorId,
            ModernAuthorUserId = current.ModernAuthorUserId,
            ModernAuthorProviderId = current.ModernAuthorProviderId,
            ModernAnchorKind = current.ModernAnchorKind,
            ModernAnchorXml = current.ModernAnchorXml,
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
        var effectiveInitials = NormalizeInitials(initials, effectiveAuthor);
        var modernAuthorIdentity = FindMatchingModernAuthorIdentity(current, effectiveAuthor, effectiveInitials);
        comment.Replies.Add(new SlideCommentReply
        {
            Author = effectiveAuthor,
            Initials = effectiveInitials,
            Text = normalizedText,
            DateTime = timestamp,
            ModernAuthorId = modernAuthorIdentity.Id,
            ModernAuthorUserId = modernAuthorIdentity.UserId,
            ModernAuthorProviderId = modernAuthorIdentity.ProviderId
        });

        return new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ReplyComment,
            true,
            slideIndex,
            commentIndex,
            comment,
            null);
    }

    public static PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlan(
        IReadOnlyList<Slide> slides,
        string? query = null,
        string? currentAuthor = null,
        string? currentInitials = null)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var normalizedQuery = NormalizeMentionQuery(query);
        var candidates = new Dictionary<string, PresentationCommentMentionCandidate>(StringComparer.Ordinal);

        AddMentionCandidate(candidates, currentAuthor, currentInitials, "Current author");
        foreach (var comment in slides.SelectMany(slide => slide.Comments))
        {
            AddMentionCandidate(candidates, comment.Author, comment.Initials, "Comment author");
            foreach (var reply in comment.Replies)
            {
                AddMentionCandidate(candidates, reply.Author, reply.Initials, "Reply author");
            }
        }

        var filtered = candidates.Values
            .Where(candidate => MatchesMentionCandidate(candidate, normalizedQuery))
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Initials, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PresentationCommentMentionPickerPlan(normalizedQuery, filtered);
    }

    public static PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlanForInsertionContext(
        IReadOnlyList<Slide> slides,
        string? text,
        int caretIndex,
        string? currentAuthor = null,
        string? currentInitials = null)
    {
        var query = ExtractMentionQueryBeforeCaret(text, caretIndex);
        return BuildCommentMentionPickerPlan(slides, query, currentAuthor, currentInitials);
    }

    public static PresentationCommentMentionInsertionPlan BuildCommentMentionInsertionPlan(
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
    {
        var original = text ?? string.Empty;
        var safeCaret = Math.Clamp(caretIndex, 0, original.Length);
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.InsertToken))
        {
            return new PresentationCommentMentionInsertionPlan(
                false,
                original,
                original,
                safeCaret,
                0,
                candidate,
                MissingMentionCandidateMessage);
        }

        var token = NormalizeMentionToken(candidate.InsertToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PresentationCommentMentionInsertionPlan(
                false,
                original,
                original,
                safeCaret,
                0,
                candidate,
                MissingMentionCandidateMessage);
        }

        var replacement = $"@{token}";
        var replaceStart = safeCaret;
        var replaceEnd = safeCaret;
        var replacingPartialMention = false;

        var tokenStart = safeCaret;
        while (tokenStart > 0 && IsMentionCharacter(original[tokenStart - 1]))
        {
            tokenStart--;
        }

        if (tokenStart > 0 &&
            original[tokenStart - 1] == '@' &&
            HasMentionBoundaryBefore(original, tokenStart - 1))
        {
            replaceStart = tokenStart - 1;
            while (replaceEnd < original.Length && IsMentionCharacter(original[replaceEnd]))
            {
                replaceEnd++;
            }

            replacingPartialMention = true;
        }

        var prefix = !replacingPartialMention &&
            replaceStart > 0 &&
            !char.IsWhiteSpace(original[replaceStart - 1])
                ? " "
                : string.Empty;
        var suffix = replaceEnd < original.Length && char.IsWhiteSpace(original[replaceEnd])
            ? string.Empty
            : " ";
        var inserted = prefix + replacement + suffix;
        var updated = original[..replaceStart] + inserted + original[replaceEnd..];
        return new PresentationCommentMentionInsertionPlan(
            true,
            original,
            updated,
            replaceStart + inserted.Length,
            0,
            candidate,
            null);
    }

    private static string? ExtractMentionQueryBeforeCaret(string? text, int caretIndex)
    {
        var original = text ?? string.Empty;
        var safeCaret = Math.Clamp(caretIndex, 0, original.Length);
        var tokenStart = safeCaret;
        while (tokenStart > 0 && IsMentionCharacter(original[tokenStart - 1]))
        {
            tokenStart--;
        }

        if (tokenStart > 0 &&
            original[tokenStart - 1] == '@' &&
            HasMentionBoundaryBefore(original, tokenStart - 1))
        {
            return original[tokenStart..safeCaret];
        }

        return null;
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

    public static PresentationCommentNavigationPlan BuildCommentNavigationPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex,
        PresentationReviewWorkflowIntentKind intent)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var direction = intent switch
        {
            PresentationReviewWorkflowIntentKind.PreviousComment => -1,
            PresentationReviewWorkflowIntentKind.NextComment => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent, "Use a comment navigation intent.")
        };
        var selected = NormalizeSelectedCommentIndex(slides, slideIndex, selectedCommentIndex);
        if (TryGetAdjacentComment(slides, slideIndex, selected, direction, out var target))
        {
            return new PresentationCommentNavigationPlan(
                intent,
                true,
                slideIndex,
                selected,
                target.slideIndex,
                target.commentIndex,
                null);
        }

        return new PresentationCommentNavigationPlan(
            intent,
            false,
            slideIndex,
            selected,
            slideIndex,
            selected ?? -1,
            direction < 0 ? "No previous comment." : "No next comment.");
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
        var suggestedDescription = decorative ? string.Empty : BuildAltTextSuggestedDescription(slide, shape);
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
            suggestedDescription,
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
        var descriptionPlaceholder = request.IsDecorative
            ? string.Empty
            : string.IsNullOrWhiteSpace(request.SuggestedDescription)
                ? GenericAltTextDescriptionPlaceholder
                : request.SuggestedDescription;
        var description = new PresentationAltTextPaneFieldPlan(
            AltTextDescriptionFieldId,
            "Description",
            request.ProposedDescription,
            descriptionPlaceholder,
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

        var duplicateSlideTitles = presentation.Slides
            .Select((slide, index) => (Title: NormalizeText(slide.Title), SlideIndex: index))
            .Where(entry => entry.Title is not null)
            .GroupBy(entry => entry.Title!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => (DisplayTitle: group.Key, Count: group.Count()),
                StringComparer.OrdinalIgnoreCase);
        var duplicateAltTexts = presentation.Slides
            .SelectMany(slide => EnumerateShapes(slide.Shapes))
            .Where(shape => NeedsAltText(shape) && !shape.IsDecorative)
            .Select(shape => NormalizeAltTextDiagnosticText(shape.AlternativeText))
            .Where(text => text is not null)
            .GroupBy(text => text!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => (DisplayText: group.Key, Count: group.Count()),
                StringComparer.OrdinalIgnoreCase);
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
                        SetSlideTitleCommandId,
                        false)));
            }
            else if (NormalizeText(slide.Title) is { } slideTitle
                && duplicateSlideTitles.TryGetValue(slideTitle, out var duplicate))
            {
                issues.Add(new PresentationAccessibilityIssueDescriptor(
                    PresentationAccessibilityIssueSeverity.Warning,
                    slideIndex,
                    null,
                    "Duplicate slide title",
                    $"Slide title \"{duplicate.DisplayTitle}\" is reused by {duplicate.Count} slides.",
                    new PresentationAccessibilityIssueActionSummary(
                        DuplicateSlideTitleActionSummary,
                        SetSlideTitleCommandId,
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
                else if (NeedsAltText(shape) && !shape.IsDecorative)
                {
                    AddAltTextQualityIssues(issues, slideIndex, shape, duplicateAltTexts);
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

                AddChartAccessibilityIssues(issues, slideIndex, shape);
                AddMediaAccessibilityIssues(issues, slideIndex, shape);
                AddTextHyperlinkAccessibilityIssues(issues, slideIndex, shape);
                AddTextContrastAccessibilityIssues(issues, slideIndex, slide, shape);
                AddTableAccessibilityIssues(issues, slideIndex, shape);
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

    public static string BuildSuggestedSlideTitle(Presentation presentation, int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slide = GetSlide(presentation.Slides, slideIndex);
        if (slide is null)
        {
            return $"Slide {Math.Max(0, slideIndex) + 1}";
        }

        if (NormalizeText(slide.Title) is { } existingTitle)
        {
            return existingTitle;
        }

        var titlePlaceholderText = EnumerateShapes(slide.Shapes)
            .Where(shape => shape.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle)
            .Select(shape => NormalizeText(shape.PlainText))
            .FirstOrDefault(text => text is not null);
        if (titlePlaceholderText is not null)
        {
            return BuildPreview(titlePlaceholderText);
        }

        var firstText = EnumerateShapes(slide.Shapes)
            .Select(shape => NormalizeText(shape.PlainText))
            .FirstOrDefault(text => text is not null);
        return firstText is null ? $"Slide {slideIndex + 1}" : BuildPreview(firstText);
    }

    public static PresentationSlideTitleMutationPlan BuildSlideTitleMutationPlan(
        Presentation presentation,
        int slideIndex,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var suggestedTitle = BuildSuggestedSlideTitle(presentation, slideIndex);
        var normalizedTitle = title is null ? suggestedTitle : NormalizeText(title) ?? string.Empty;
        if (GetSlide(presentation.Slides, slideIndex) is null)
        {
            return new PresentationSlideTitleMutationPlan(
                false,
                slideIndex,
                normalizedTitle,
                suggestedTitle,
                SlideTitleMissingSlideMessage);
        }

        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return new PresentationSlideTitleMutationPlan(
                false,
                slideIndex,
                string.Empty,
                suggestedTitle,
                SlideTitleEmptyMessage);
        }

        return new PresentationSlideTitleMutationPlan(
            true,
            slideIndex,
            normalizedTitle,
            suggestedTitle,
            null);
    }

    public static PresentationSlideTitleMutationPlan TryApplySlideTitleMutation(
        EditingSession editor,
        int slideIndex,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var plan = BuildSlideTitleMutationPlan(editor.Presentation, slideIndex, title);
        if (plan.ShouldApply)
        {
            editor.SetSlideTitle(plan.SlideIndex, plan.Title);
        }

        return plan;
    }

    public static string BuildSuggestedChartTitle(
        Presentation presentation,
        int slideIndex,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slide = GetSlide(presentation.Slides, slideIndex);
        var shape = shapeId is { } id ? slide is null ? null : FindShape(slide.Shapes, id) : null;
        if (shape?.Chart is not { } chart)
            return "Chart";

        if (NormalizeText(chart.Title) is { } existingTitle)
            return existingTitle;

        var altText = NormalizeText(shape.AlternativeText);
        if (altText is not null)
            return BuildPreview(altText.TrimEnd('.', '!', '?'));

        var shapeName = NormalizeText(shape.Name);
        return shapeName is null ? "Chart" : BuildPreview(shapeName);
    }

    public static PresentationChartTitleMutationPlan BuildChartTitleMutationPlan(
        Presentation presentation,
        int slideIndex,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var suggestedTitle = BuildSuggestedChartTitle(presentation, slideIndex, shapeId);
        var slide = GetSlide(presentation.Slides, slideIndex);
        if (slide is null)
        {
            return new PresentationChartTitleMutationPlan(
                false, slideIndex, shapeId, suggestedTitle, suggestedTitle, ChartTitleMissingSlideMessage);
        }

        var shape = shapeId is { } id ? FindShape(slide.Shapes, id) : null;
        if (shape?.Chart is null)
        {
            return new PresentationChartTitleMutationPlan(
                false, slideIndex, shapeId, suggestedTitle, suggestedTitle, ChartTitleMissingShapeMessage);
        }

        if (NormalizeText(shape.Chart.Title) is not null)
        {
            return new PresentationChartTitleMutationPlan(
                false, slideIndex, shape.Id, shape.Chart.Title!, suggestedTitle, ChartTitleAlreadySetMessage);
        }

        return new PresentationChartTitleMutationPlan(
            true, slideIndex, shape.Id, suggestedTitle, suggestedTitle, null);
    }

    public static PresentationChartTitleMutationPlan TryApplyChartTitleMutation(
        EditingSession editor,
        int slideIndex,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var plan = BuildChartTitleMutationPlan(editor.Presentation, slideIndex, shapeId);
        if (plan is { ShouldApply: true, ShapeId: { } targetShapeId })
            editor.SetChartTitle(plan.SlideIndex, targetShapeId, plan.Title);

        return plan;
    }

    public static PresentationTableHeaderRowMutationPlan BuildTableHeaderRowMutationPlan(
        Presentation presentation,
        int slideIndex,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slide = GetSlide(presentation.Slides, slideIndex);
        if (slide is null)
        {
            return new PresentationTableHeaderRowMutationPlan(
                false,
                slideIndex,
                shapeId,
                TableHeaderRowMissingSlideMessage);
        }

        var shape = shapeId is { } id ? FindShape(slide.Shapes, id) : null;
        if (shape?.Table is null)
        {
            return new PresentationTableHeaderRowMutationPlan(
                false,
                slideIndex,
                shapeId,
                TableHeaderRowMissingShapeMessage);
        }

        if (shape.Table.Flags.FirstRow)
        {
            return new PresentationTableHeaderRowMutationPlan(
                false,
                slideIndex,
                shape.Id,
                TableHeaderRowAlreadySetMessage);
        }

        return new PresentationTableHeaderRowMutationPlan(
            true,
            slideIndex,
            shape.Id,
            null);
    }

    public static PresentationTableHeaderRowMutationPlan TryApplyTableHeaderRowMutation(
        EditingSession editor,
        int slideIndex,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var plan = BuildTableHeaderRowMutationPlan(editor.Presentation, slideIndex, shapeId);
        if (plan is { ShouldApply: true, ShapeId: { } targetShapeId })
        {
            editor.SetTableHeaderRow(slideIndex, targetShapeId, isHeaderRow: true);
        }

        return plan;
    }

    public static PresentationTableStructureReviewPlan BuildTableStructureReviewPlan(
        Presentation presentation,
        int slideIndex,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slide = GetSlide(presentation.Slides, slideIndex);
        if (slide is null)
        {
            return new PresentationTableStructureReviewPlan(
                false,
                slideIndex,
                shapeId,
                string.Empty,
                0,
                0,
                [],
                [],
                [],
                TableStructureReviewGuidance,
                false,
                false,
                TableStructureReviewMissingSlideMessage);
        }

        var shape = shapeId is { } id ? FindShape(slide.Shapes, id) : null;
        if (shape?.Table is not { } table)
        {
            return new PresentationTableStructureReviewPlan(
                false,
                slideIndex,
                shapeId,
                string.Empty,
                0,
                0,
                [],
                [],
                [],
                TableStructureReviewGuidance,
                false,
                false,
                TableStructureReviewMissingShapeMessage);
        }

        return new PresentationTableStructureReviewPlan(
            true,
            slideIndex,
            shape.Id,
            DescribeShape(shape),
            table.Rows.Count,
            table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Cells.Count),
            BuildBlankHeaderCellPlans(table),
            BuildBlankBodyCellPlans(table),
            BuildMergedOrSplitCellPlans(table),
            TableStructureReviewGuidance,
            true,
            true,
            null);
    }

    public static PresentationTableStructureReviewDisplayPlan BuildTableStructureReviewDisplayPlan(
        PresentationTableStructureReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanReview)
        {
            return new PresentationTableStructureReviewDisplayPlan(
                false,
                "Review Table Structure",
                plan.ValidationMessage ?? "Select a table to review its structure.",
                plan.Guidance,
                [],
                plan.ValidationMessage);
        }

        var details = new List<PresentationTableStructureReviewDetailRowPlan>();
        details.AddRange(plan.BlankHeaderCells.Select(cell => new PresentationTableStructureReviewDetailRowPlan(
            "Blank header cell",
            $"{cell.CellReference} is blank.",
            "Add descriptive header text or remove the empty header cell.")));
        details.AddRange(plan.BlankBodyCells.Select(cell => new PresentationTableStructureReviewDetailRowPlan(
            "Blank body cell",
            $"{cell.CellReference} is blank.",
            "Confirm the blank data cell is intentional or add visible text.")));
        details.AddRange(plan.MergedOrSplitCells.Select(cell => new PresentationTableStructureReviewDetailRowPlan(
            "Merged or split cell",
            cell.Summary,
            "Verify the table still reads correctly in row and column order.")));

        return new PresentationTableStructureReviewDisplayPlan(
            true,
            "Review Table Structure",
            BuildTableStructureReviewSummary(plan),
            plan.Guidance,
            details,
            null);
    }

    public static PresentationAccessibilityCheckerPanePlan BuildAccessibilityCheckerPanePlan(
        Presentation presentation,
        PresentationAccessibilitySummaryPlan summaryPlan,
        int? selectedRowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(summaryPlan);

        var selected = NormalizeAccessibilityCheckerSelection(summaryPlan.Issues, selectedRowIndex);
        var rows = summaryPlan.Issues
            .Select((issue, rowIndex) => BuildAccessibilityCheckerRow(presentation, issue, rowIndex, rowIndex == selected))
            .ToArray();

        return new PresentationAccessibilityCheckerPanePlan(
            summaryPlan.SlideCount,
            rows.Length,
            selected,
            rows,
            summaryPlan.Actions);
    }

    public static int NormalizeAccessibilityCheckerRowSelection(
        PresentationAccessibilityCheckerPanePlan panePlan,
        int? requestedRowIndex)
    {
        ArgumentNullException.ThrowIfNull(panePlan);

        if (panePlan.Rows.Count == 0)
        {
            return -1;
        }

        if (requestedRowIndex is { } requested &&
            panePlan.Rows.Any(row => row.RowIndex == requested))
        {
            return requested;
        }

        return panePlan.SelectedRowIndex >= 0 ? panePlan.SelectedRowIndex : 0;
    }

    public static PresentationAccessibilityCheckerNavigationPlan BuildAccessibilityCheckerNavigationPlan(
        PresentationAccessibilityCheckerPanePlan panePlan,
        int? requestedRowIndex)
    {
        ArgumentNullException.ThrowIfNull(panePlan);

        var selectedRowIndex = NormalizeAccessibilityCheckerRowSelection(panePlan, requestedRowIndex);
        if (selectedRowIndex < 0)
        {
            return new PresentationAccessibilityCheckerNavigationPlan(
                false,
                -1,
                -1,
                null,
                false,
                "No accessibility issues found.");
        }

        var row = panePlan.Rows.FirstOrDefault(row => row.RowIndex == selectedRowIndex)
            ?? panePlan.SelectedRow;
        if (row is null)
        {
            return new PresentationAccessibilityCheckerNavigationPlan(
                false,
                selectedRowIndex,
                -1,
                null,
                false,
                "Selected accessibility issue is no longer available.");
        }

        return new PresentationAccessibilityCheckerNavigationPlan(
            row.ShouldNavigateToSlide,
            row.RowIndex,
            row.SlideIndex,
            row.ShapeId,
            row.ShouldSelectShape,
            null);
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
                BuildReadingOrderActions(hasItems: false));
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
                ReadingOrderItemNotFoundMessage);
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

    public static PresentationReadingOrderSelectionPlan BuildReadingOrderSelectionPlan(
        Slide? slide,
        int slideIndex,
        uint? shapeId)
    {
        if (shapeId is null)
        {
            return new PresentationReadingOrderSelectionPlan(
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                false,
                slideIndex,
                null,
                -1,
                MissingReadingOrderSelectionMessage);
        }

        var plan = BuildReadingOrderPlan(slide, slideIndex, [shapeId.Value]);
        if (!plan.HasSlide || plan.Items.Count == 0)
        {
            return new PresentationReadingOrderSelectionPlan(
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                false,
                slideIndex,
                shapeId,
                -1,
                EmptyReadingOrderMessage);
        }

        if (plan.SelectedItemIndex < 0)
        {
            return new PresentationReadingOrderSelectionPlan(
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                false,
                slideIndex,
                shapeId,
                -1,
                ReadingOrderItemNotFoundMessage);
        }

        return new PresentationReadingOrderSelectionPlan(
            PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
            true,
            slideIndex,
            shapeId,
            plan.SelectedItemIndex,
            null);
    }

    public static PresentationReadingOrderSelectionPlan TryApplyReadingOrderSelection(
        EditingSession editor,
        uint? shapeId)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var plan = BuildReadingOrderSelectionPlan(editor.CurrentSlide, editor.CurrentSlideIndex, shapeId);
        if (plan.ShouldSelect && plan.ShapeId is { } selectedShapeId)
        {
            editor.Select(selectedShapeId);
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
        scanner ??= ScanBuiltInProofingIssues;
        var issues = new List<PresentationProofingIssueDescriptor>();
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

    public static PresentationProofingPanePlan BuildProofingPanePlan(
        PresentationProofingExecutionPlan executionPlan,
        int? selectedRowIndex = null,
        PresentationProofingIgnoreState? ignoreState = null,
        PresentationProofingDictionaryState? dictionaryState = null)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);

        ignoreState ??= PresentationProofingIgnoreState.Empty;
        dictionaryState ??= PresentationProofingDictionaryState.Empty;
        var visibleIssues = executionPlan.Issues
            .Where(issue => !IsProofingIssueIgnored(issue, ignoreState))
            .Where(issue => !IsProofingIssueInDictionary(issue, dictionaryState))
            .ToArray();
        var normalizedSelection = NormalizeProofingIssueSelection(
            visibleIssues.Length,
            selectedRowIndex);
        var rows = visibleIssues
            .Select((issue, index) =>
            {
                var suggestion = SuggestProofingReplacement(issue.Text);
                var canApply = normalizedSelection == index &&
                    suggestion.Length > 0 &&
                    !string.Equals(issue.Text, suggestion, StringComparison.Ordinal);
                var canIgnore = normalizedSelection == index;
                var canAddToDictionary = canIgnore && TryGetDictionaryWordKey(issue, out _);
                var disabledReason = normalizedSelection == index
                    ? canApply ? null : ProofingNoSuggestionMessage
                    : ProofingMissingIssueMessage;
                var ignoreDisabledReason = canIgnore ? null : ProofingMissingIgnoreIssueMessage;
                var dictionaryDisabledReason = normalizedSelection != index
                    ? ProofingMissingDictionaryIssueMessage
                    : canAddToDictionary
                        ? null
                        : ProofingCannotAddToDictionaryMessage;

                return new PresentationProofingIssueRowPlan(
                    index,
                    issue.Scope,
                    issue.Start,
                    issue.Length,
                    issue.Text,
                    issue.Message,
                    issue.Scope.SourceName,
                    $"Slide {issue.Scope.SlideIndex + 1}",
                    issue.Scope.Snippet,
                    suggestion,
                    normalizedSelection == index,
                    new PresentationReviewWorkflowActionPlan(
                        ProofingApplyCorrectionCommandId,
                        "Change",
                        PresentationReviewWorkflowIntentKind.ApplyProofingCorrection,
                        canApply,
                        executionPlan.Status,
                        disabledReason),
                    new PresentationReviewWorkflowActionPlan(
                        ProofingIgnoreCommandId,
                        "Ignore",
                        PresentationReviewWorkflowIntentKind.IgnoreProofingIssue,
                        canIgnore,
                        executionPlan.Status,
                        ignoreDisabledReason),
                    new PresentationReviewWorkflowActionPlan(
                        ProofingIgnoreAllCommandId,
                        "Ignore All",
                        PresentationReviewWorkflowIntentKind.IgnoreAllProofingIssues,
                        canIgnore,
                        executionPlan.Status,
                        ignoreDisabledReason),
                    new PresentationReviewWorkflowActionPlan(
                        ProofingAddToDictionaryCommandId,
                        "Add to Dictionary",
                        PresentationReviewWorkflowIntentKind.AddProofingWordToDictionary,
                        canAddToDictionary,
                        executionPlan.Status,
                        dictionaryDisabledReason));
            })
            .ToArray();

        var selectedAction = rows.FirstOrDefault(row => row.IsSelected)?.CorrectionAction;
        var selectedIgnoreAction = rows.FirstOrDefault(row => row.IsSelected)?.IgnoreAction;
        var selectedIgnoreAllAction = rows.FirstOrDefault(row => row.IsSelected)?.IgnoreAllAction;
        var selectedAddToDictionaryAction = rows.FirstOrDefault(row => row.IsSelected)?.AddToDictionaryAction;
        var applyAction = selectedAction ?? new PresentationReviewWorkflowActionPlan(
            ProofingApplyCorrectionCommandId,
            "Change",
            PresentationReviewWorkflowIntentKind.ApplyProofingCorrection,
            false,
            executionPlan.Status,
            visibleIssues.Length == 0 ? ProofingNoIssuesMessage : ProofingMissingIssueMessage);
        var ignoreAction = selectedIgnoreAction ?? new PresentationReviewWorkflowActionPlan(
            ProofingIgnoreCommandId,
            "Ignore",
            PresentationReviewWorkflowIntentKind.IgnoreProofingIssue,
            false,
            executionPlan.Status,
            visibleIssues.Length == 0 ? ProofingNoIssuesMessage : ProofingMissingIgnoreIssueMessage);
        var ignoreAllAction = selectedIgnoreAllAction ?? new PresentationReviewWorkflowActionPlan(
            ProofingIgnoreAllCommandId,
            "Ignore All",
            PresentationReviewWorkflowIntentKind.IgnoreAllProofingIssues,
            false,
            executionPlan.Status,
            visibleIssues.Length == 0 ? ProofingNoIssuesMessage : ProofingMissingIgnoreIssueMessage);
        var addToDictionaryAction = selectedAddToDictionaryAction ?? new PresentationReviewWorkflowActionPlan(
            ProofingAddToDictionaryCommandId,
            "Add to Dictionary",
            PresentationReviewWorkflowIntentKind.AddProofingWordToDictionary,
            false,
            executionPlan.Status,
            visibleIssues.Length == 0 ? ProofingNoIssuesMessage : ProofingMissingDictionaryIssueMessage);

        return new PresentationProofingPanePlan(
            executionPlan.CanRun,
            executionPlan.Status,
            executionPlan.ScopeCount,
            visibleIssues.Length,
            normalizedSelection,
            rows,
            [.. executionPlan.Actions, applyAction, ignoreAction, ignoreAllAction, addToDictionaryAction],
            visibleIssues.Length == 0 ? ProofingNoIssuesMessage : executionPlan.Message);
    }

    public static int NormalizeProofingSelectionAfterCorrection(
        int previousSelectedRowIndex,
        PresentationProofingPanePlan refreshedPlan)
    {
        ArgumentNullException.ThrowIfNull(refreshedPlan);

        if (refreshedPlan.Rows.Count == 0)
            return -1;

        return Math.Clamp(previousSelectedRowIndex, 0, refreshedPlan.Rows.Count - 1);
    }

    public static int NormalizeProofingSelectionAfterIgnore(
        int previousSelectedRowIndex,
        PresentationProofingPanePlan refreshedPlan)
        => NormalizeProofingSelectionAfterCorrection(previousSelectedRowIndex, refreshedPlan);

    public static PresentationProofingIgnoreState AddProofingIgnoredIssue(
        PresentationProofingIgnoreState? ignoreState,
        PresentationProofingIssueRowPlan? row)
    {
        ignoreState ??= PresentationProofingIgnoreState.Empty;
        if (row is null)
            return ignoreState;

        var ignoredIssue = new PresentationProofingIgnoredIssueDescriptor(
            row.Scope,
            row.Start,
            row.Length,
            row.Text,
            row.Message);
        if (ignoreState.IgnoredIssues.Any(existing => IsSameProofingIssueOccurrence(ignoredIssue, existing)))
            return ignoreState;

        return ignoreState with { IgnoredIssues = [.. ignoreState.IgnoredIssues, ignoredIssue] };
    }

    public static PresentationProofingIgnoreState AddProofingIgnoredIssueGroup(
        PresentationProofingIgnoreState? ignoreState,
        PresentationProofingIssueRowPlan? row)
    {
        ignoreState ??= PresentationProofingIgnoreState.Empty;
        if (row is null)
            return ignoreState;

        var group = new PresentationProofingIgnoredIssueGroupDescriptor(
            NormalizeProofingIssueTextKey(row.Text),
            row.Message);
        if (ignoreState.IgnoredIssueGroups.Contains(group))
            return ignoreState;

        return ignoreState with { IgnoredIssueGroups = [.. ignoreState.IgnoredIssueGroups, group] };
    }

    public static PresentationProofingDictionaryState AddProofingDictionaryWord(
        PresentationProofingDictionaryState? dictionaryState,
        PresentationProofingIssueRowPlan? row)
    {
        dictionaryState ??= PresentationProofingDictionaryState.Empty;
        if (row is null)
            return dictionaryState;

        var issue = new PresentationProofingIssueDescriptor(
            row.Scope,
            row.Start,
            row.Length,
            row.Text,
            row.Message);
        if (!TryGetDictionaryWordKey(issue, out var normalizedWord))
            return dictionaryState;

        return dictionaryState.NormalizedWords.Contains(normalizedWord, StringComparer.Ordinal)
            ? dictionaryState
            : dictionaryState with { NormalizedWords = [.. dictionaryState.NormalizedWords, normalizedWord] };
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
            new(SetTableHeaderRowCommandId, "Set Header Row", PresentationReviewWorkflowIntentKind.SetTableHeaderRow, true, PresentationWorkflowCapabilityStatus.Available),
            new(ReviewTableStructureCommandId, "Review Table Structure", PresentationReviewWorkflowIntentKind.ReviewTableStructure, true, PresentationWorkflowCapabilityStatus.Available),
            new(ProofingCommandId, "Spelling", PresentationReviewWorkflowIntentKind.RunProofing, true, PresentationWorkflowCapabilityStatus.Available),
        ];

    private static PresentationAccessibilityCheckerRowPlan BuildAccessibilityCheckerRow(
        Presentation presentation,
        PresentationAccessibilityIssueDescriptor issue,
        int rowIndex,
        bool isSelected)
    {
        var shape = issue.ShapeId is { } shapeId
            ? FindShape(GetSlide(presentation.Slides, issue.SlideIndex)?.Shapes, shapeId)
            : null;
        var category = ClassifyAccessibilityIssue(issue);
        var actionLabel = BuildAccessibilityCheckerActionLabel(issue);

        return new PresentationAccessibilityCheckerRowPlan(
            rowIndex,
            issue.Severity,
            category,
            issue.SlideIndex,
            $"Slide {issue.SlideIndex + 1}",
            issue.ShapeId,
            shape?.Name ?? string.Empty,
            issue.Title,
            issue.Detail,
            isSelected,
            actionLabel,
            issue.Action.CommandId,
            true,
            issue.ShapeId is not null);
    }

    private static int NormalizeAccessibilityCheckerSelection(
        IReadOnlyList<PresentationAccessibilityIssueDescriptor> issues,
        int? selectedRowIndex)
    {
        if (issues.Count == 0)
        {
            return -1;
        }

        return selectedRowIndex is { } index && index >= 0 && index < issues.Count
            ? index
            : 0;
    }

    private static string ClassifyAccessibilityIssue(PresentationAccessibilityIssueDescriptor issue)
    {
        if (issue.Title is "Missing slide title" or "Duplicate slide title")
        {
            return "Slide title";
        }

        if (issue.Action.CommandId == AltTextCommandId)
        {
            return "Alt text";
        }

        if (issue.Action.CommandId == InsertLinkCommandId)
        {
            return "Hyperlink";
        }

        if (issue.Title == "Low text contrast")
        {
            return "Text contrast";
        }

        if (issue.Title == "Chart title missing")
        {
            return "Chart";
        }

        if (issue.Action.CommandId == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId
            || issue.Title == "Video captions missing")
        {
            return "Media";
        }

        if (issue.Title == "Table header row missing"
            || issue.Title == "Blank table header cells"
            || issue.Title == "Blank table body cells"
            || issue.Title == "Merged or split table cells")
        {
            return "Table";
        }

        return "Accessibility";
    }

    private static string BuildAccessibilityCheckerActionLabel(PresentationAccessibilityIssueDescriptor issue)
        => issue.Action.CommandId switch
        {
            AltTextCommandId => "Open Alt Text",
            InsertLinkCommandId => "Edit Hyperlink",
            SetSlideTitleCommandId => "Set Slide Title",
            ChartTitleCommandId => "Add Chart Title",
            SetTableHeaderRowCommandId => "Set Header Row",
            ReviewTableStructureCommandId => "Review Table Structure",
            PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId => "Open Captions",
            _ when issue.Title == "Chart title missing" => "Add Chart Title",
            _ when issue.Title == "Video captions missing" => "Open Captions",
            _ when issue.ShapeId is null => "Go to Slide",
            _ => "Select Object"
        };

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

    private static int NormalizeProofingIssueSelection(int issueCount, int? selectedRowIndex)
    {
        if (issueCount <= 0)
            return -1;

        return selectedRowIndex is { } index && index >= 0 && index < issueCount
            ? index
            : 0;
    }

    private static bool IsProofingIssueIgnored(
        PresentationProofingIssueDescriptor issue,
        PresentationProofingIgnoreState ignoreState)
        => ignoreState.IgnoredIssues.Any(ignoredIssue => IsSameProofingIssueOccurrence(issue, ignoredIssue)) ||
            ignoreState.IgnoredIssueGroups.Any(group =>
                string.Equals(group.NormalizedText, NormalizeProofingIssueTextKey(issue.Text), StringComparison.Ordinal) &&
                string.Equals(group.Message, issue.Message, StringComparison.Ordinal));

    private static bool IsProofingIssueInDictionary(
        PresentationProofingIssueDescriptor issue,
        PresentationProofingDictionaryState dictionaryState)
        => TryGetDictionaryWordKey(issue, out var normalizedWord) &&
            dictionaryState.NormalizedWords.Contains(normalizedWord, StringComparer.Ordinal);

    private static bool IsSameProofingIssueOccurrence(
        PresentationProofingIssueDescriptor issue,
        PresentationProofingIgnoredIssueDescriptor ignoredIssue)
        => IsSameProofingScope(issue.Scope, ignoredIssue.Scope) &&
            issue.Start == ignoredIssue.Start &&
            issue.Length == ignoredIssue.Length &&
            string.Equals(NormalizeProofingIssueTextKey(issue.Text), NormalizeProofingIssueTextKey(ignoredIssue.Text), StringComparison.Ordinal) &&
            string.Equals(issue.Message, ignoredIssue.Message, StringComparison.Ordinal);

    private static bool IsSameProofingIssueOccurrence(
        PresentationProofingIgnoredIssueDescriptor issue,
        PresentationProofingIgnoredIssueDescriptor ignoredIssue)
        => IsSameProofingScope(issue.Scope, ignoredIssue.Scope) &&
            issue.Start == ignoredIssue.Start &&
            issue.Length == ignoredIssue.Length &&
            string.Equals(NormalizeProofingIssueTextKey(issue.Text), NormalizeProofingIssueTextKey(ignoredIssue.Text), StringComparison.Ordinal) &&
            string.Equals(issue.Message, ignoredIssue.Message, StringComparison.Ordinal);

    private static bool IsSameProofingScope(
        PresentationProofingScopeDescriptor left,
        PresentationProofingScopeDescriptor right)
        => left.Kind == right.Kind &&
            left.SlideIndex == right.SlideIndex &&
            left.ShapeId == right.ShapeId &&
            left.TableRowIndex == right.TableRowIndex &&
            left.TableColumnIndex == right.TableColumnIndex &&
            left.CommentIndex == right.CommentIndex &&
            left.ReplyIndex == right.ReplyIndex;

    private static string NormalizeProofingIssueTextKey(string text)
        => text.ToUpperInvariant();

    private static bool TryGetDictionaryWordKey(
        PresentationProofingIssueDescriptor issue,
        out string normalizedWord)
    {
        normalizedWord = string.Empty;
        if (!string.Equals(issue.Message, ProofingPossibleMisspellingMessage, StringComparison.Ordinal))
            return false;

        var words = EnumerateProofingWords(issue.Text).ToArray();
        if (words.Length != 1 ||
            words[0].Start != 0 ||
            words[0].Length != issue.Text.Length)
        {
            return false;
        }

        normalizedWord = NormalizeProofingIssueTextKey(words[0].Text);
        return normalizedWord.Length > 0;
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanBuiltInProofingIssues(
        PresentationProofingScopeDescriptor scope)
    {
        foreach (var typo in BuiltInProofingCorrections)
        {
            var start = 0;
            while (start < scope.Text.Length)
            {
                var index = scope.Text.IndexOf(typo.Key, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                yield return new PresentationProofingIssueMatch(
                    index,
                    typo.Key.Length,
                    scope.Text.Substring(index, typo.Key.Length),
                    ProofingPossibleMisspellingMessage);
                start = index + typo.Key.Length;
            }
        }

        foreach (var repeatedWord in ScanRepeatedWords(scope.Text))
            yield return repeatedWord;

        foreach (var repeatedTerminalPunctuation in ScanRepeatedTerminalPunctuation(scope.Text))
            yield return repeatedTerminalPunctuation;

        foreach (var punctuationSpacing in ScanPunctuationSpacing(scope.Text))
            yield return punctuationSpacing;

        foreach (var extraSpacing in ScanExtraInteriorSpaces(scope.Text))
            yield return extraSpacing;

        foreach (var sentenceStart in ScanSentenceStartCapitalization(scope.Text))
            yield return sentenceStart;

        foreach (var articleAgreement in ScanArticleAgreement(scope.Text))
            yield return articleAgreement;

        foreach (var subjectVerbAgreement in ScanSubjectVerbAgreement(scope.Text))
            yield return subjectVerbAgreement;

        foreach (var grammarConfusion in ScanGrammarConfusions(scope.Text))
            yield return grammarConfusion;
    }

    private static string SuggestProofingReplacement(string text)
    {
        if (BuiltInProofingCorrections.TryGetValue(text, out var replacement))
            return MatchReplacementCasing(text, replacement);

        if (BuiltInGrammarCorrections.TryGetValue(text, out replacement))
            return MatchGrammarCasing(text, replacement);

        if (text.Length == 1 && char.IsLower(text[0]))
            return char.ToUpperInvariant(text[0]).ToString();

        if (TryBuildPunctuationSpacingReplacement(text, out var punctuationSpacingReplacement))
            return punctuationSpacingReplacement;

        if (TryBuildRepeatedTerminalPunctuationReplacement(text, out var terminalPunctuationReplacement))
            return terminalPunctuationReplacement;

        if (TryBuildExtraInteriorSpacesReplacement(text, out var extraSpacingReplacement))
            return extraSpacingReplacement;

        if (TryBuildArticleAgreementReplacement(text, out var articleAgreementReplacement))
            return articleAgreementReplacement;

        if (TryBuildSubjectVerbAgreementReplacement(text, out var subjectVerbAgreementReplacement))
            return subjectVerbAgreementReplacement;

        return TryBuildRepeatedWordReplacement(text, out var repeatedWordReplacement)
            ? repeatedWordReplacement
            : string.Empty;
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanSubjectVerbAgreement(string text)
    {
        var words = EnumerateProofingWords(text).ToArray();
        for (var index = 0; index < words.Length - 1; index++)
        {
            var subject = words[index];
            var verb = words[index + 1];
            var determiner = index > 0 ? words[index - 1] : (ProofingWordSpan?)null;

            if (TryBuildSubjectVerbAgreementReplacement(text, determiner, subject, verb, out var start, out var length, out _))
            {
                yield return new PresentationProofingIssueMatch(
                    start,
                    length,
                    text.Substring(start, length),
                    ProofingSubjectVerbAgreementMessage);
            }
        }
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanGrammarConfusions(string text)
    {
        foreach (var correction in BuiltInGrammarCorrections)
        {
            var start = 0;
            while (start < text.Length)
            {
                var index = text.IndexOf(correction.Key, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;

                var end = index + correction.Key.Length;
                var hasWordBefore = index > 0 && IsProofingWordChar(text[index - 1]);
                var hasWordAfter = end < text.Length && IsProofingWordChar(text[end]);
                if (!hasWordBefore && !hasWordAfter)
                {
                    yield return new PresentationProofingIssueMatch(
                        index,
                        correction.Key.Length,
                        text.Substring(index, correction.Key.Length),
                        ProofingGrammarConfusionMessage);
                }

                start = end;
            }
        }
    }

    private static bool TryBuildSubjectVerbAgreementReplacement(string text, out string replacement)
    {
        var words = EnumerateProofingWords(text).ToArray();
        if (words.Length is < 2 or > 3)
        {
            replacement = string.Empty;
            return false;
        }

        var hasDeterminer = words.Length == 3;
        var determiner = hasDeterminer ? words[0] : (ProofingWordSpan?)null;
        var subject = words[hasDeterminer ? 1 : 0];
        var verb = words[hasDeterminer ? 2 : 1];
        if (!TryBuildSubjectVerbAgreementReplacement(text, determiner, subject, verb, out _, out _, out replacement))
        {
            replacement = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryBuildSubjectVerbAgreementReplacement(
        string text,
        ProofingWordSpan? determiner,
        ProofingWordSpan subject,
        ProofingWordSpan verb,
        out int start,
        out int length,
        out string replacement)
    {
        start = 0;
        length = 0;
        replacement = string.Empty;

        if (!HasOnlyHorizontalWhitespaceBetween(text, subject, verb) ||
            !TryResolveProofingVerbAgreement(verb.Text, out var verbNumber, out var singularVerb, out var pluralVerb) ||
            IsGuardedSubjectVerbAgreementToken(text, subject) ||
            IsGuardedSubjectVerbAgreementToken(text, verb) ||
            !TryResolveProofingSubjectNumber(subject.Text, determiner?.Text, out var subjectNumber))
        {
            return false;
        }

        var phraseStart = subject.Start;
        if (determiner is { } determinerValue)
        {
            if (!HasOnlyHorizontalWhitespaceBetween(text, determinerValue, subject) ||
                !IsSubjectVerbAgreementDeterminer(determinerValue.Text) ||
                IsGuardedSubjectVerbAgreementToken(text, determinerValue))
            {
                return false;
            }

            phraseStart = determinerValue.Start;
        }

        var phraseLength = verb.Start + verb.Length - phraseStart;
        if (IsGuardedSubjectVerbAgreementRange(text, phraseStart, phraseLength) ||
            subjectNumber == verbNumber)
        {
            return false;
        }

        var desiredVerb = subjectNumber == ProofingSubjectNumber.Singular ? singularVerb : pluralVerb;
        start = phraseStart;
        length = phraseLength;
        replacement = ReplaceTextRange(
            text.Substring(phraseStart, phraseLength),
            verb.Start - phraseStart,
            verb.Length,
            MatchReplacementCasing(verb.Text, desiredVerb));
        return true;
    }

    private static bool TryResolveProofingVerbAgreement(
        string verb,
        out ProofingSubjectNumber number,
        out string singularVerb,
        out string pluralVerb)
    {
        number = ProofingSubjectNumber.Singular;
        singularVerb = string.Empty;
        pluralVerb = string.Empty;

        switch (verb.ToLowerInvariant())
        {
            case "is":
                singularVerb = "is";
                pluralVerb = "are";
                return true;
            case "are":
                number = ProofingSubjectNumber.Plural;
                singularVerb = "is";
                pluralVerb = "are";
                return true;
            case "was":
                singularVerb = "was";
                pluralVerb = "were";
                return true;
            case "were":
                number = ProofingSubjectNumber.Plural;
                singularVerb = "was";
                pluralVerb = "were";
                return true;
            case "has":
                singularVerb = "has";
                pluralVerb = "have";
                return true;
            case "have":
                number = ProofingSubjectNumber.Plural;
                singularVerb = "has";
                pluralVerb = "have";
                return true;
            default:
                return false;
        }
    }

    private static bool TryResolveProofingSubjectNumber(
        string subject,
        string? determiner,
        out ProofingSubjectNumber number)
    {
        number = ProofingSubjectNumber.Singular;
        var normalizedSubject = subject.ToLowerInvariant();
        var normalizedDeterminer = determiner?.ToLowerInvariant();

        if (normalizedSubject.Any(char.IsDigit) ||
            IsAllCapsProofingTerm(subject) ||
            IsAmbiguousProofingSubject(normalizedSubject))
        {
            return false;
        }

        if (ProofingSingularSubjects.Contains(normalizedSubject))
            return ResolveDeterminerAgreement(normalizedDeterminer, ProofingSubjectNumber.Singular, out number);

        if (ProofingPluralSubjects.Contains(normalizedSubject))
            return ResolveDeterminerAgreement(normalizedDeterminer, ProofingSubjectNumber.Plural, out number);

        if (normalizedSubject.EndsWith('s') &&
            normalizedSubject.Length > 3 &&
            !normalizedSubject.EndsWith("ss", StringComparison.Ordinal) &&
            ProofingSingularSubjects.Contains(normalizedSubject[..^1]))
        {
            return ResolveDeterminerAgreement(normalizedDeterminer, ProofingSubjectNumber.Plural, out number);
        }

        return false;
    }

    private static bool ResolveDeterminerAgreement(
        string? determiner,
        ProofingSubjectNumber subjectNumber,
        out ProofingSubjectNumber number)
    {
        number = subjectNumber;
        return determiner switch
        {
            null or "the" => true,
            "a" or "an" or "this" or "that" => subjectNumber == ProofingSubjectNumber.Singular,
            "these" or "those" => subjectNumber == ProofingSubjectNumber.Plural,
            _ => false
        };
    }

    private static bool IsSubjectVerbAgreementDeterminer(string text)
        => string.Equals(text, "the", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "a", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "an", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "this", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "that", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "these", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "those", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguousProofingSubject(string normalizedSubject)
        => normalizedSubject is "i" or "you" or "we" or "they" or "it" or "he" or "she" or "this" or "that" or "these" or "those" or "series" or "data" or "analysis";

    private static bool IsGuardedSubjectVerbAgreementToken(string text, ProofingWordSpan word)
        => TryGetUrlOrEmailCoreEnd(text, word.Start, out _) ||
            word.Text.Length <= 1 ||
            word.Text.Any(ch => ch is '\'' or '`' or '_' or '-') ||
            IsGuardedSubjectVerbAgreementBoundary(text, word.Start - 1) ||
            IsGuardedSubjectVerbAgreementBoundary(text, word.Start + word.Length);

    private static bool IsGuardedSubjectVerbAgreementRange(string text, int start, int length)
        => IsGuardedSubjectVerbAgreementBoundary(text, start - 1) ||
            IsGuardedSubjectVerbAgreementBoundary(text, start + length);

    private static bool IsGuardedSubjectVerbAgreementBoundary(string text, int index)
    {
        if (index < 0 || index >= text.Length)
            return false;

        return text[index] is '"' or '\'' or '`' or '/' or '\\' or '@' or '#';
    }

    private enum ProofingSubjectNumber
    {
        Singular,
        Plural
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanArticleAgreement(string text)
    {
        ProofingWordSpan? previousWord = null;
        foreach (var word in EnumerateProofingWords(text))
        {
            if (previousWord is { } article &&
                TryBuildArticleAgreementReplacement(text, article, word, out _))
            {
                var length = word.Start + word.Length - article.Start;
                yield return new PresentationProofingIssueMatch(
                    article.Start,
                    length,
                    text.Substring(article.Start, length),
                    ProofingArticleAgreementMessage);
            }

            previousWord = word;
        }
    }

    private static bool TryBuildArticleAgreementReplacement(string text, out string replacement)
    {
        var words = EnumerateProofingWords(text).ToArray();
        if (words.Length == 2 &&
            TryBuildArticleAgreementReplacement(text, words[0], words[1], out replacement))
        {
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    private static bool TryBuildArticleAgreementReplacement(
        string text,
        ProofingWordSpan article,
        ProofingWordSpan followingWord,
        out string replacement)
    {
        replacement = string.Empty;
        if (!IsIndefiniteArticle(article.Text) ||
            !HasOnlyHorizontalWhitespaceBetween(text, article, followingWord) ||
            TryGetUrlOrEmailCoreEnd(text, article.Start, out _) ||
            IsGuardedArticleAgreementFollowingWord(text, followingWord))
        {
            return false;
        }

        var shouldUseAn = ShouldUseAnBefore(followingWord.Text);
        if (!shouldUseAn.HasValue)
            return false;

        var desiredArticle = shouldUseAn.Value ? "an" : "a";
        if (string.Equals(article.Text, desiredArticle, StringComparison.OrdinalIgnoreCase))
            return false;

        replacement = ReplaceTextRange(
            text,
            article.Start,
            article.Length,
            MatchArticleCasing(article.Text, desiredArticle));
        return true;
    }

    private static bool IsIndefiniteArticle(string text)
        => string.Equals(text, "a", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "an", StringComparison.OrdinalIgnoreCase);

    private static bool HasOnlyHorizontalWhitespaceBetween(
        string text,
        ProofingWordSpan first,
        ProofingWordSpan second)
    {
        if (first.Start + first.Length >= second.Start)
            return false;

        for (var index = first.Start + first.Length; index < second.Start; index++)
        {
            if (!IsHorizontalProofingWhitespace(text[index]))
                return false;
        }

        return true;
    }

    private static bool IsGuardedArticleAgreementFollowingWord(string text, ProofingWordSpan word)
    {
        if (TryGetUrlOrEmailCoreEnd(text, word.Start, out _))
            return true;

        return word.Text.Length == 1 ||
            word.Text.Any(char.IsDigit) ||
            IsAllCapsProofingTerm(word.Text);
    }

    private static bool IsAllCapsProofingTerm(string text)
        => text.Length > 1 &&
            text.Any(char.IsLetter) &&
            text.Where(char.IsLetter).All(char.IsUpper);

    private static bool? ShouldUseAnBefore(string word)
    {
        if (word.Length == 0)
            return null;

        var normalized = word.ToLowerInvariant();
        if (StartsWithAny(normalized, "honest", "honor", "honour", "hour", "heir"))
            return true;

        if (StartsWithAny(normalized, "user", "use", "using", "used", "useful", "usual", "unit", "union", "unique", "unicorn", "unified", "unilateral", "universal", "university"))
            return false;

        if (StartsWithAny(normalized, "one", "once"))
            return false;

        if (StartsWithAny(normalized, "euro", "ewe"))
            return false;

        return normalized[0] switch
        {
            'a' or 'e' or 'i' or 'o' or 'u' => true,
            'b' or 'c' or 'd' or 'f' or 'g' or 'j' or 'k' or 'l' or 'm' or 'n' or 'p' or 'q' or 'r' or 's' or 't' or 'v' or 'w' or 'x' or 'y' or 'z' => false,
            _ => null
        };
    }

    private static bool StartsWithAny(string text, params string[] prefixes)
        => prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));

    private static string MatchArticleCasing(string source, string replacement)
    {
        if (source.Length > 1 && source.All(char.IsUpper))
            return replacement.ToUpperInvariant();

        return MatchReplacementCasing(source, replacement);
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanExtraInteriorSpaces(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!IsHorizontalProofingWhitespace(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && IsHorizontalProofingWhitespace(text[index]))
                index++;

            if (index - start >= 2 &&
                start > 0 &&
                index < text.Length &&
                !char.IsWhiteSpace(text[start - 1]) &&
                !char.IsWhiteSpace(text[index]))
            {
                yield return new PresentationProofingIssueMatch(
                    start,
                    index - start,
                    text.Substring(start, index - start),
                    ProofingExtraSpacesMessage);
            }
        }
    }

    private static bool TryBuildExtraInteriorSpacesReplacement(string text, out string replacement)
    {
        if (text.Length >= 2 && text.All(IsHorizontalProofingWhitespace))
        {
            replacement = " ";
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanRepeatedTerminalPunctuation(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!IsSentenceTerminator(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && IsSentenceTerminator(text[index]))
                index++;

            var length = index - start;
            if (length >= 2 &&
                ShouldFlagRepeatedTerminalPunctuation(text, start, length))
            {
                yield return new PresentationProofingIssueMatch(
                    start,
                    length,
                    text.Substring(start, length),
                    ProofingRepeatedTerminalPunctuationMessage);
            }
        }
    }

    private static bool ShouldFlagRepeatedTerminalPunctuation(string text, int start, int length)
    {
        if (IsGuardedTerminalPunctuationRun(text, start, length))
        {
            return false;
        }

        var first = text[start];
        var allSame = true;
        for (var index = start; index < start + length; index++)
        {
            if (text[index] == '.')
                return false;

            allSame &= text[index] == first;
        }

        if (allSame)
            return true;

        return length >= 3;
    }

    private static bool IsGuardedTerminalPunctuationRun(string text, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (TryGetUrlOrEmailCoreEnd(text, index, out _))
                return true;
        }

        return false;
    }

    private static bool TryBuildRepeatedTerminalPunctuationReplacement(string text, out string replacement)
    {
        if (text.Length >= 2 &&
            text.All(IsSentenceTerminator) &&
            ShouldFlagRepeatedTerminalPunctuation(text, 0, text.Length))
        {
            replacement = text[0].ToString();
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanPunctuationSpacing(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (IsProofingPunctuation(ch) &&
                !IsGuardedProofingPunctuation(text, index))
            {
                var whitespaceStart = index;
                while (whitespaceStart > 0 &&
                    char.IsWhiteSpace(text[whitespaceStart - 1]) &&
                    text[whitespaceStart - 1] != '\n' &&
                    text[whitespaceStart - 1] != '\r')
                {
                    whitespaceStart--;
                }

                if (whitespaceStart < index)
                {
                    var length = index - whitespaceStart + 1;
                    yield return new PresentationProofingIssueMatch(
                        whitespaceStart,
                        length,
                        text.Substring(whitespaceStart, length),
                        ProofingWhitespaceBeforePunctuationMessage);
                }
            }

            if (IsListSpacingPunctuation(ch) &&
                index + 1 < text.Length &&
                !char.IsWhiteSpace(text[index + 1]) &&
                !IsListSpacingPunctuationBoundary(text[index + 1]) &&
                !HasWhitespaceBefore(text, index) &&
                !IsGuardedProofingPunctuation(text, index))
            {
                yield return new PresentationProofingIssueMatch(
                    index,
                    1,
                    text[index].ToString(),
                    ProofingMissingSpaceAfterListPunctuationMessage);
            }

            if (IsSentenceTerminator(ch) &&
                index + 1 < text.Length &&
                char.IsLetter(text[index + 1]) &&
                !IsGuardedProofingPunctuation(text, index))
            {
                yield return new PresentationProofingIssueMatch(
                    index,
                    2,
                    text.Substring(index, 2),
                    ProofingMissingSpaceAfterSentencePunctuationMessage);
            }
        }
    }

    private static bool TryBuildPunctuationSpacingReplacement(string text, out string replacement)
    {
        if (text.Length >= 2 &&
            IsProofingPunctuation(text[^1]) &&
            text[..^1].All(char.IsWhiteSpace))
        {
            replacement = text[^1].ToString();
            return true;
        }

        if (text.Length == 2 &&
            IsSentenceTerminator(text[0]) &&
            char.IsLetter(text[1]))
        {
            replacement = $"{text[0]} {text[1]}";
            return true;
        }

        if (text.Length == 1 && IsListSpacingPunctuation(text[0]))
        {
            replacement = $"{text[0]} ";
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanSentenceStartCapitalization(string text)
    {
        var expectsSentenceStart = true;
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (expectsSentenceStart)
            {
                if (char.IsWhiteSpace(ch) || IsSentenceOpeningPunctuation(ch))
                {
                    index++;
                    continue;
                }

                if (char.IsLetter(ch))
                {
                    if (TryGetUrlOrEmailCoreEnd(text, index, out var tokenCoreEnd))
                    {
                        expectsSentenceStart = false;
                        index = tokenCoreEnd;
                        continue;
                    }

                    if (char.IsLower(ch))
                    {
                        yield return new PresentationProofingIssueMatch(
                            index,
                            1,
                            text[index].ToString(),
                            "Sentence should start with a capital letter.");
                    }

                    expectsSentenceStart = false;
                    index++;
                    continue;
                }

                expectsSentenceStart = IsSentenceTerminator(ch) && HasSentenceBoundaryAfter(text, index);
                index++;
                continue;
            }

            if (IsSentenceTerminator(ch) && HasSentenceBoundaryAfter(text, index))
            {
                expectsSentenceStart = true;
            }

            index++;
        }
    }

    private static IEnumerable<PresentationProofingIssueMatch> ScanRepeatedWords(string text)
    {
        var previousWord = string.Empty;
        var previousStart = -1;

        foreach (var word in EnumerateProofingWords(text))
        {
            if (previousStart >= 0 &&
                string.Equals(previousWord, word.Text, StringComparison.OrdinalIgnoreCase))
            {
                var length = word.Start + word.Length - previousStart;
                yield return new PresentationProofingIssueMatch(
                    previousStart,
                    length,
                    text.Substring(previousStart, length),
                    "Repeated word.");
            }

            previousWord = word.Text;
            previousStart = word.Start;
        }
    }

    private static bool TryBuildRepeatedWordReplacement(string text, out string replacement)
    {
        var words = EnumerateProofingWords(text).ToArray();
        if (words.Length == 2 &&
            string.Equals(words[0].Text, words[1].Text, StringComparison.OrdinalIgnoreCase))
        {
            replacement = text.Substring(words[0].Start, words[0].Length);
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    private static IEnumerable<ProofingWordSpan> EnumerateProofingWords(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && !IsProofingWordChar(text[index]))
                index++;

            var start = index;
            while (index < text.Length && IsProofingWordChar(text[index]))
                index++;

            if (index > start)
            {
                yield return new ProofingWordSpan(
                    start,
                    index - start,
                    text.Substring(start, index - start));
            }
        }
    }

    private static bool IsProofingWordChar(char value)
        => char.IsLetterOrDigit(value) || value == '\'';

    private readonly record struct ProofingWordSpan(int Start, int Length, string Text);

    private static bool IsSentenceTerminator(char value)
        => value is '.' or '!' or '?';

    private static bool IsProofingPunctuation(char value)
        => value is ',' or '.' or ';' or ':' or '!' or '?';

    private static bool IsListSpacingPunctuation(char value)
        => value is ',' or ';' or ':';

    private static bool IsListSpacingPunctuationBoundary(char value)
        => IsProofingPunctuation(value) ||
            IsSentenceOpeningPunctuation(value) ||
            IsSentenceClosingPunctuation(value);

    private static bool IsHorizontalProofingWhitespace(char value)
        => value is ' ' or '\t';

    private static bool HasWhitespaceBefore(string text, int index)
        => index > 0 && char.IsWhiteSpace(text[index - 1]);

    private static bool IsSentenceOpeningPunctuation(char value)
        => value is '"' or '\'' or '(' or '[' or '{';

    private static bool IsSentenceClosingPunctuation(char value)
        => value is '"' or '\'' or ')' or ']' or '}';

    private static bool HasSentenceBoundaryAfter(string text, int terminatorIndex)
    {
        var index = terminatorIndex + 1;
        while (index < text.Length && IsSentenceClosingPunctuation(text[index]))
            index++;

        return index >= text.Length || char.IsWhiteSpace(text[index]);
    }

    private static bool IsGuardedProofingPunctuation(string text, int index)
        => IsNumericPunctuation(text, index) || TryGetUrlOrEmailCoreEnd(text, index, out _);

    private static bool IsNumericPunctuation(string text, int index)
        => (text[index] == '.' || IsListSpacingPunctuation(text[index])) &&
            index > 0 &&
            index + 1 < text.Length &&
            char.IsDigit(text[index - 1]) &&
            char.IsDigit(text[index + 1]);

    private static bool TryGetUrlOrEmailCoreEnd(string text, int index, out int coreEnd)
    {
        var tokenStart = index;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
            tokenStart--;

        var tokenEnd = index;
        while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
            tokenEnd++;

        var coreStart = tokenStart;
        while (coreStart < tokenEnd && IsSentenceOpeningPunctuation(text[coreStart]))
            coreStart++;

        coreEnd = tokenEnd;
        while (coreEnd > coreStart && IsSentenceClosingPunctuation(text[coreEnd - 1]))
            coreEnd--;

        if (coreEnd > coreStart && IsSentenceTerminator(text[coreEnd - 1]))
            coreEnd--;

        if (index < coreStart || index >= coreEnd)
            return false;

        var token = text.Substring(coreStart, coreEnd - coreStart);
        return token.Contains("://", StringComparison.Ordinal) ||
            token.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            token.Contains('@', StringComparison.Ordinal);
    }

    private static string MatchReplacementCasing(string source, string replacement)
    {
        if (source.Length == 0 || replacement.Length == 0)
            return replacement;

        return char.IsUpper(source[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }

    private static string MatchGrammarCasing(string source, string replacement)
    {
        if (source.Any(char.IsLetter) && source.Where(char.IsLetter).All(char.IsUpper))
            return replacement.ToUpperInvariant();

        return MatchReplacementCasing(source, replacement);
    }

    private static readonly IReadOnlyDictionary<string, string> BuiltInProofingCorrections =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alot"] = "a lot",
            ["becuase"] = "because",
            ["begining"] = "beginning",
            ["definately"] = "definitely",
            ["enviroment"] = "environment",
            ["eror"] = "error",
            ["maintainance"] = "maintenance",
            ["occassion"] = "occasion",
            ["teh"] = "the",
            ["recieve"] = "receive",
            ["adress"] = "address",
            ["occured"] = "occurred",
            ["seperate"] = "separate",
            ["thier"] = "their",
            ["tommorow"] = "tomorrow",
            ["untill"] = "until",
            ["wich"] = "which",
        };

    private static readonly IReadOnlyDictionary<string, string> BuiltInGrammarCorrections =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["could of"] = "could have",
            ["might of"] = "might have",
            ["must of"] = "must have",
            ["should of"] = "should have",
            ["would of"] = "would have",
            ["its a"] = "it's a",
            ["its been"] = "it's been",
            ["its not"] = "it's not",
            ["their are"] = "there are",
            ["their is"] = "there is",
            ["your going"] = "you're going",
            ["your invited"] = "you're invited",
            ["your right"] = "you're right",
            ["your welcome"] = "you're welcome",
        };

    private static readonly IReadOnlySet<string> ProofingSingularSubjects =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "agenda",
            "bullet",
            "cell",
            "chart",
            "comment",
            "deck",
            "figure",
            "graph",
            "image",
            "item",
            "note",
            "plan",
            "reply",
            "report",
            "result",
            "review",
            "section",
            "shape",
            "slide",
            "table",
            "template",
            "title",
            "topic",
        };

    private static readonly IReadOnlySet<string> ProofingPluralSubjects =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "agendas",
            "bullets",
            "cells",
            "charts",
            "comments",
            "decks",
            "figures",
            "graphs",
            "images",
            "items",
            "notes",
            "plans",
            "replies",
            "reports",
            "results",
            "reviews",
            "sections",
            "shapes",
            "slides",
            "tables",
            "templates",
            "titles",
            "topics",
        };

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildReadingOrderActions(
        bool hasItems)
    {
        var reorderReason = !hasItems
            ? EmptyReadingOrderMessage
            : MissingReadingOrderSelectionMessage;

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
            return ReadingOrderItemNotFoundMessage;
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
        var mentions = ExtractMentions(comment.Text);
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
            comment.ModernAnchorKind,
            true,
            !comment.IsResolved,
            true,
            !comment.IsResolved,
            comment.IsResolved,
            replies.Length,
            mentions.Count + replies.Sum(reply => reply.MentionCount),
            replies,
            comment.IsResolved ? PresentationCommentThreadStatus.Resolved : PresentationCommentThreadStatus.Open,
            isSelected,
            comment.ResolvedBy,
            comment.ResolvedDateTime)
        {
            ModernCommentId = comment.ModernCommentId,
            ModernAuthorId = comment.ModernAuthorId,
            ModernAuthorUserId = comment.ModernAuthorUserId,
            ModernAuthorProviderId = comment.ModernAuthorProviderId,
            Mentions = mentions,
        };
    }

    private static PresentationCommentReplyDescriptor DescribeCommentReply(
        int replyIndex,
        SlideCommentReply reply)
    {
        var mentions = ExtractMentions(reply.Text);
        return new(
            replyIndex,
            reply.Author,
            reply.Initials,
            BuildPreview(reply.Text),
            reply.DateTime,
            mentions.Count)
        {
            ModernReplyId = reply.ModernReplyId,
            ModernAuthorId = reply.ModernAuthorId,
            ModernAuthorUserId = reply.ModernAuthorUserId,
            ModernAuthorProviderId = reply.ModernAuthorProviderId,
            Mentions = mentions,
        };
    }

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
            UsesModernCommentSchema = comment.UsesModernCommentSchema,
            ModernCommentId = comment.ModernCommentId,
            ModernAuthorId = comment.ModernAuthorId,
            ModernAuthorUserId = comment.ModernAuthorUserId,
            ModernAuthorProviderId = comment.ModernAuthorProviderId,
            ModernAnchorKind = comment.ModernAnchorKind,
            ModernAnchorXml = comment.ModernAnchorXml,
            Xemu = comment.Xemu,
            Yemu = comment.Yemu,
            Idx = comment.Idx,
            AuthorId = comment.AuthorId,
        };
        CopyReplies(comment, clone);
        return clone;
    }

    private static (string Id, string UserId, string ProviderId) FindMatchingModernAuthorIdentity(
        SlideComment comment,
        string author,
        string initials)
    {
        if (Matches(comment.Author, comment.Initials, author, initials) &&
            !string.IsNullOrWhiteSpace(comment.ModernAuthorId))
        {
            return (
                comment.ModernAuthorId,
                comment.ModernAuthorUserId,
                comment.ModernAuthorProviderId);
        }

        foreach (var reply in comment.Replies)
        {
            if (Matches(reply.Author, reply.Initials, author, initials) &&
                !string.IsNullOrWhiteSpace(reply.ModernAuthorId))
            {
                return (
                    reply.ModernAuthorId,
                    reply.ModernAuthorUserId,
                    reply.ModernAuthorProviderId);
            }
        }

        return (string.Empty, string.Empty, string.Empty);

        static bool Matches(string candidateAuthor, string candidateInitials, string author, string initials)
        {
            var normalizedAuthor = NormalizeText(candidateAuthor);
            if (!string.Equals(normalizedAuthor, author, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(
                NormalizeInitials(candidateInitials, normalizedAuthor ?? author),
                initials,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void CopyReplies(SlideComment source, SlideComment target)
    {
        target.Replies.Clear();
        foreach (var reply in source.Replies)
        {
            target.Replies.Add(new SlideCommentReply
            {
                AuthorId = reply.AuthorId,
                ModernReplyId = reply.ModernReplyId,
                ModernAuthorId = reply.ModernAuthorId,
                ModernAuthorUserId = reply.ModernAuthorUserId,
                ModernAuthorProviderId = reply.ModernAuthorProviderId,
                Author = reply.Author,
                Initials = reply.Initials,
                Text = reply.Text,
                DateTime = reply.DateTime,
            });
        }
    }

    private static void AddMentionCandidate(
        IDictionary<string, PresentationCommentMentionCandidate> candidates,
        string? author,
        string? initials,
        string sourceLabel)
    {
        if (NormalizeText(author) is not { } normalizedAuthor)
        {
            return;
        }

        var displayName = PresentationCommentMetadataPolicy.NormalizeAuthorDisplayName(normalizedAuthor);
        var initialsBadge = PresentationCommentMetadataPolicy.NormalizeInitialsBadge(initials, displayName);
        var identityKey = PresentationCommentMetadataPolicy.BuildAuthorIdentityKey(displayName, initialsBadge);
        if (string.IsNullOrWhiteSpace(identityKey) || candidates.ContainsKey(identityKey))
        {
            return;
        }

        var insertToken = NormalizeMentionToken(displayName);
        if (string.IsNullOrWhiteSpace(insertToken))
        {
            insertToken = NormalizeMentionToken(initialsBadge);
        }

        if (string.IsNullOrWhiteSpace(insertToken))
        {
            return;
        }

        candidates.Add(identityKey, new PresentationCommentMentionCandidate(
            displayName,
            initialsBadge,
            identityKey,
            insertToken,
            sourceLabel));
    }

    private static bool MatchesMentionCandidate(
        PresentationCommentMentionCandidate candidate,
        string query)
        => string.IsNullOrWhiteSpace(query) ||
            candidate.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            candidate.Initials.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            candidate.InsertToken.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            candidate.IdentityKey.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMentionQuery(string? query)
    {
        var normalized = NormalizeText(query) ?? string.Empty;
        return normalized.StartsWith('@') ? normalized[1..] : normalized;
    }

    private static string NormalizeMentionToken(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            return string.Empty;
        }

        var chars = new List<char>(normalized.Length);
        var lastWasSeparator = false;
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                chars.Add(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && chars.Count > 0)
            {
                chars.Add('.');
                lastWasSeparator = true;
            }
        }

        while (chars.Count > 0 && chars[^1] == '.')
        {
            chars.RemoveAt(chars.Count - 1);
        }

        return new string(chars.ToArray());
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

    private static int? NormalizeSelectedCommentIndex(
        IReadOnlyList<int> visibleCommentIndexes,
        int? selectedCommentIndex,
        bool useFirstVisibleWhenInvalid)
    {
        if (visibleCommentIndexes.Count == 0)
        {
            return null;
        }

        if (selectedCommentIndex is not { } index)
        {
            return visibleCommentIndexes[0];
        }

        return visibleCommentIndexes.Contains(index)
            ? index
            : useFirstVisibleWhenInvalid
                ? visibleCommentIndexes[0]
                : null;
    }

    private static IReadOnlyList<PresentationCommentPaneFilterPlan> BuildCommentPaneFilters(
        IReadOnlyList<SlideComment> comments,
        PresentationCommentPaneFilterKind selectedFilter)
        =>
        [
            BuildCommentPaneFilter(comments, selectedFilter, PresentationCommentPaneFilterKind.All, "All"),
            BuildCommentPaneFilter(comments, selectedFilter, PresentationCommentPaneFilterKind.Open, "Open"),
            BuildCommentPaneFilter(comments, selectedFilter, PresentationCommentPaneFilterKind.Resolved, "Resolved"),
            BuildCommentPaneFilter(comments, selectedFilter, PresentationCommentPaneFilterKind.Mentions, "Mentions"),
        ];

    private static PresentationCommentPaneFilterPlan BuildCommentPaneFilter(
        IReadOnlyList<SlideComment> comments,
        PresentationCommentPaneFilterKind selectedFilter,
        PresentationCommentPaneFilterKind filterKind,
        string label)
        => new(
            filterKind,
            label,
            comments.Count(comment => MatchesCommentPaneFilter(comment, filterKind)),
            selectedFilter == filterKind);

    private static bool MatchesCommentPaneFilter(
        SlideComment comment,
        PresentationCommentPaneFilterKind filterKind)
        => filterKind switch
        {
            PresentationCommentPaneFilterKind.Open => !comment.IsResolved,
            PresentationCommentPaneFilterKind.Resolved => comment.IsResolved,
            PresentationCommentPaneFilterKind.Mentions =>
                CountMentions(comment.Text) > 0 ||
                comment.Replies.Any(reply => CountMentions(reply.Text) > 0),
            _ => true
        };

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

        int candidate;
        if (selectedCommentIndex.HasValue)
        {
            var current = Array.FindIndex(
                flattened,
                item => item.slideIndex == slideIndex && item.commentIndex == selectedCommentIndex.Value);
            if (current < 0)
            {
                target = default;
                return false;
            }

            candidate = current + Math.Sign(direction);
        }
        else if (direction < 0)
        {
            candidate = Array.FindLastIndex(flattened, item => item.slideIndex <= slideIndex);
        }
        else
        {
            candidate = Array.FindIndex(flattened, item => item.slideIndex >= slideIndex);
        }

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
        => ExtractMentions(text).Count;

    private static IReadOnlyList<PresentationCommentMentionDescriptor> ExtractMentions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<PresentationCommentMentionDescriptor>? mentions = null;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '@' || !HasMentionBoundaryBefore(text, index))
            {
                continue;
            }

            var start = index + 1;
            var end = start;
            while (end < text.Length && IsMentionCharacter(text[end]))
            {
                end++;
            }

            while (end > start && IsTrailingMentionPunctuation(text[end - 1]))
            {
                end--;
            }

            if (end <= start)
            {
                continue;
            }

            var displayText = text[start..end];
            mentions ??= [];
            mentions.Add(new PresentationCommentMentionDescriptor(
                mentions.Count,
                index,
                end - index,
                displayText,
                PresentationCommentMetadataPolicy.BuildMentionIdentityKey(displayText)));
            index = end - 1;
        }

        return mentions ?? [];
    }

    private static bool HasMentionBoundaryBefore(string text, int atIndex)
    {
        if (atIndex == 0)
        {
            return true;
        }

        var previous = text[atIndex - 1];
        return !char.IsLetterOrDigit(previous) && previous != '_' && previous != '.';
    }

    private static bool IsMentionCharacter(char ch)
        => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.';

    private static bool IsTrailingMentionPunctuation(char ch)
        => ch is '.' or '-' or '_';

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

    private static string BuildAltTextSuggestedDescription(Slide? slide, SlideShape shape)
    {
        var currentDescription = NormalizeText(shape.AlternativeText);
        if (currentDescription is not null)
        {
            return currentDescription;
        }

        var slideContext = BuildAltTextSlideContext(slide);
        return shape.Kind switch
        {
            SlideShapeKind.Chart => BuildAltTextSentence(
                BuildChartAltTextReference(shape),
                slideContext,
                "Summarize the main trend, comparison, or takeaway."),
            SlideShapeKind.Table => BuildAltTextSentence(
                BuildTableAltTextReference(shape),
                slideContext,
                "Summarize the key headers, values, and takeaway."),
            SlideShapeKind.Picture => BuildAltTextSentence(
                BuildPictureAltTextReference(shape),
                slideContext,
                "Describe the important visual details and context."),
            SlideShapeKind.Media => BuildAltTextSentence(
                BuildAltTextReference(shape.Media?.IsVideo == false ? "Audio" : "Video", NormalizeText(shape.Name)),
                slideContext,
                "Describe the media purpose and the visible poster frame when relevant."),
            SlideShapeKind.SmartArt => BuildAltTextSentence(
                BuildAltTextReference("SmartArt graphic", NormalizeText(shape.Name)),
                slideContext,
                "Summarize the process, relationship, or hierarchy it communicates."),
            SlideShapeKind.Ole => BuildAltTextSentence(
                BuildAltTextReference("Embedded object", NormalizeText(shape.Name)),
                slideContext,
                "Describe the object type and the information it contributes."),
            SlideShapeKind.Zoom => BuildAltTextSentence(
                BuildAltTextReference("Zoom link", NormalizeText(shape.Name)),
                slideContext,
                "Describe the destination slide or section."),
            SlideShapeKind.Model3d => BuildAltTextSentence(
                BuildAltTextReference("3D model", NormalizeText(shape.Name)),
                slideContext,
                "Describe the model and why it is included."),
            SlideShapeKind.PreservedObject => BuildAltTextSentence(
                BuildAltTextReference("Preserved object", NormalizeText(shape.Name)),
                slideContext,
                "Describe the object and the information it contributes."),
            SlideShapeKind.Group => BuildAltTextSentence(
                BuildAltTextReference("Grouped object", NormalizeText(shape.Name)),
                slideContext,
                "Describe the combined meaning of the grouped objects."),
            SlideShapeKind.Connector => BuildAltTextSentence(
                BuildAltTextReference("Connector", NormalizeText(shape.Name)),
                slideContext,
                "Describe the relationship or flow it indicates."),
            _ when NormalizeText(shape.PlainText) is { } text => BuildAltTextSentence(
                BuildAltTextReference("Text shape", BuildPreview(text)),
                slideContext,
                "Describe the visible text or the shape's purpose."),
            _ => BuildAltTextSentence(
                BuildAltTextReference(shape.Kind.ToString(), NormalizeText(shape.Name)),
                slideContext,
                "Describe the object's purpose and important visual details.")
        };
    }

    private static string BuildAltTextSentence(string subject, string? slideContext, string guidance)
    {
        var context = string.IsNullOrWhiteSpace(slideContext) ? string.Empty : $" on {slideContext}";
        return $"{subject}{context}. {guidance}";
    }

    private static string? BuildAltTextSlideContext(Slide? slide)
    {
        var title = NormalizeText(slide?.Title);
        return title is null ? null : $"slide \"{title}\"";
    }

    private static string BuildAltTextReference(string kind, string? name)
        => name is null ? kind : $"{kind} \"{name}\"";

    private static string BuildChartAltTextReference(SlideShape shape)
    {
        var chart = shape.Chart;
        var reference = BuildAltTextReference("Chart", NormalizeText(chart?.Title) ?? NormalizeText(shape.Name));
        if (chart is null)
        {
            return reference;
        }

        var details = BuildChartAltTextDetails(chart).ToArray();
        return details.Length == 0 ? reference : $"{reference} ({string.Join(", ", details)})";
    }

    private static IEnumerable<string> BuildChartAltTextDetails(ChartShape chart)
    {
        if (FormatChartType(chart.ChartType) is { } chartType)
        {
            yield return chartType;
        }

        var seriesNames = chart.Series
            .Select(series => NormalizeText(series.Name))
            .Where(name => name is not null)
            .Select(name => BuildPreview(name!))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        if (seriesNames.Length > 0)
        {
            yield return $"series {FormatAltTextInlineList(seriesNames)}";
        }
        else if (chart.Series.Count > 0)
        {
            yield return $"{chart.Series.Count} {Pluralize(chart.Series.Count, "series", "series")}";
        }

        var categoryLabels = chart.Categories
            .Select(NormalizeText)
            .Where(category => category is not null)
            .Select(category => BuildPreview(category!))
            .Take(3)
            .ToArray();
        if (chart.Categories.Count > 0 && categoryLabels.Length > 0)
        {
            yield return $"{chart.Categories.Count} {Pluralize(chart.Categories.Count, "category", "categories")} including {FormatAltTextInlineList(categoryLabels)}";
        }
        else if (chart.Categories.Count > 0)
        {
            yield return $"{chart.Categories.Count} {Pluralize(chart.Categories.Count, "category", "categories")}";
        }

        var valueCount = chart.Series.Sum(series => series.Values.Count(value => value.HasValue));
        if (valueCount > 0)
        {
            yield return $"{valueCount} {Pluralize(valueCount, "value", "values")}";
        }

        var xValueCount = chart.Series.Sum(series => series.XValues.Count(value => value.HasValue));
        if (xValueCount > 0)
        {
            yield return $"{xValueCount} X {Pluralize(xValueCount, "value", "values")}";
        }

        var bubbleSizeCount = chart.Series.Sum(series => series.BubbleSizes.Count(value => value.HasValue));
        if (bubbleSizeCount > 0)
        {
            yield return $"{bubbleSizeCount} bubble {Pluralize(bubbleSizeCount, "size", "sizes")}";
        }
    }

    private static string? FormatChartType(ChartType chartType)
        => chartType switch
        {
            ChartType.ColumnClustered => "clustered column chart",
            ChartType.ColumnStacked => "stacked column chart",
            ChartType.ColumnStacked100 => "100% stacked column chart",
            ChartType.BarClustered => "clustered bar chart",
            ChartType.BarStacked => "stacked bar chart",
            ChartType.BarStacked100 => "100% stacked bar chart",
            ChartType.Line => "line chart",
            ChartType.LineMarkers => "line chart with markers",
            ChartType.Pie => "pie chart",
            ChartType.Area => "area chart",
            ChartType.AreaStacked => "stacked area chart",
            ChartType.Scatter => "scatter chart",
            ChartType.Doughnut => "doughnut chart",
            ChartType.Radar => "radar chart",
            ChartType.Bubble => "bubble chart",
            ChartType.Stock => "stock chart",
            ChartType.Surface => "surface chart",
            ChartType.Surface3D => "3-D surface chart",
            _ => null
        };

    private static string Pluralize(int count, string singular, string plural)
        => count == 1 ? singular : plural;

    private static string BuildPictureAltTextReference(SlideShape shape)
    {
        var reference = BuildAltTextReference("Picture", NormalizeText(shape.Name));
        var details = BuildPictureAltTextDetails(shape).ToArray();
        return details.Length == 0 ? reference : $"{reference} ({string.Join(", ", details)})";
    }

    private static IEnumerable<string> BuildPictureAltTextDetails(SlideShape shape)
    {
        if (NormalizePictureContentType(shape.Picture?.ContentType) is { } contentType)
        {
            yield return $"{contentType} image";
        }

        if (shape.PictureFormat is { } format)
        {
            if (format.HasCrop)
            {
                yield return "cropped";
            }

            if (format.Grayscale)
            {
                yield return "grayscale effect";
            }

            if (format.BiLevelThreshold.HasValue)
            {
                yield return "black-and-white threshold effect";
            }

            if (format.Brightness.HasValue || format.Contrast.HasValue)
            {
                yield return "brightness or contrast adjustment";
            }

            if (format.AlphaModPct is { } alpha && alpha < 1.0)
            {
                yield return "transparency adjustment";
            }
        }

        if (NormalizePictureFrame(shape.PictureFrameGeometry) is { } frame)
        {
            yield return $"{frame} frame";
        }
    }

    private static string? NormalizePictureContentType(string? contentType)
    {
        var normalized = NormalizeText(contentType)?.ToLowerInvariant();
        return normalized switch
        {
            "image/png" => "PNG",
            "image/jpeg" or "image/jpg" => "JPEG",
            "image/gif" => "GIF",
            "image/svg+xml" => "SVG",
            "image/wmf" or "image/x-wmf" => "WMF",
            "image/emf" or "image/x-emf" => "EMF",
            _ => null
        };
    }

    private static string? NormalizePictureFrame(string? frame)
    {
        var normalized = NormalizeText(frame);
        return normalized switch
        {
            null or "" or "rect" => null,
            "roundRect" => "rounded-rectangle",
            "ellipse" => "oval",
            _ => normalized
        };
    }

    private static string BuildTableAltTextReference(SlideShape shape)
    {
        var table = shape.Table;
        if (table is null)
        {
            return BuildAltTextReference("Table", NormalizeText(shape.Name));
        }

        var rowCount = table.Rows.Count;
        var columnCount = table.ColumnWidthsEmu.Count;
        var dimensions = rowCount > 0 && columnCount > 0
            ? $" with {rowCount} rows and {columnCount} columns"
            : string.Empty;
        var details = BuildTableAltTextDetails(table).ToArray();
        var detailSuffix = details.Length == 0 ? string.Empty : $", {string.Join(", ", details)}";
        return $"{BuildAltTextReference("Table", NormalizeText(shape.Name))}{dimensions}{detailSuffix}";
    }

    private static IEnumerable<string> BuildTableAltTextDetails(TableShape table)
    {
        if (table.Rows.Count == 0)
        {
            yield break;
        }

        var firstRowCells = GetTableRowCellText(table.Rows[0]).Take(3).ToArray();
        if (firstRowCells.Length > 0)
        {
            var firstRowLabel = table.Flags.FirstRow ? "headers" : "first row";
            yield return $"{firstRowLabel} {FormatAltTextInlineList(firstRowCells)}";
        }

        var sampleRow = table.Rows
            .Skip(1)
            .Select(row => GetTableRowCellText(row).Take(3).ToArray())
            .FirstOrDefault(cells => cells.Length > 0);
        if (sampleRow is { Length: > 0 })
        {
            yield return $"sample row {FormatAltTextInlineList(sampleRow)}";
        }
    }

    private static IEnumerable<string> GetTableRowCellText(TableRow row)
        => row.Cells
            .Where(cell => !cell.HMerge && !cell.VMerge)
            .Select(cell => NormalizeText(GetPlainText(cell.TextBody)))
            .Where(text => text is not null)
            .Select(text => BuildPreview(text!));

    private static string GetPlainText(TextBody? body)
        => body is null
            ? string.Empty
            : string.Join(" ", body.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));

    private static string FormatAltTextInlineList(IReadOnlyList<string> values)
        => values.Count switch
        {
            0 => string.Empty,
            1 => $"\"{values[0]}\"",
            2 => $"\"{values[0]}\" and \"{values[1]}\"",
            _ => string.Join(", ", values.Take(values.Count - 1).Select(value => $"\"{value}\""))
                + $", and \"{values[^1]}\""
        };

    private static bool NeedsAltText(SlideShape shape)
        => shape.Kind is SlideShapeKind.Picture
            or SlideShapeKind.Chart
            or SlideShapeKind.SmartArt
            or SlideShapeKind.Media
            or SlideShapeKind.Ole
            or SlideShapeKind.Model3d
            or SlideShapeKind.Zoom
            or SlideShapeKind.PreservedObject;

    private static void AddAltTextQualityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        SlideShape shape,
        IReadOnlyDictionary<string, (string DisplayText, int Count)> duplicateAltTexts)
    {
        var altText = NormalizeAltTextDiagnosticText(shape.AlternativeText);
        if (altText is null)
        {
            return;
        }

        if (IsFilenameLikeAltText(altText))
        {
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Filename-like alt text",
                $"{DescribeShape(shape)} uses filename-like alt text \"{BuildPreview(altText)}\".",
                new PresentationAccessibilityIssueActionSummary(
                    FilenameLikeAltTextActionSummary,
                    AltTextCommandId,
                    true)));
        }

        if (duplicateAltTexts.TryGetValue(altText, out var duplicate))
        {
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Duplicate alt text",
                $"{DescribeShape(shape)} reuses alt text \"{BuildPreview(duplicate.DisplayText)}\" on {duplicate.Count} non-decorative objects.",
                new PresentationAccessibilityIssueActionSummary(
                    DuplicateAltTextActionSummary,
                    AltTextCommandId,
                    true)));
        }
    }

    private static string? NormalizeAltTextDiagnosticText(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized is null
            ? null
            : string.Join(' ', normalized.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsFilenameLikeAltText(string altText)
    {
        var candidate = altText.Trim().Trim('"', '\'').TrimEnd('.', ',', ';', ':');
        if (candidate.Length == 0 || candidate.Length > 120)
        {
            return false;
        }

        var lastSeparator = Math.Max(candidate.LastIndexOf('/'), candidate.LastIndexOf('\\'));
        if (lastSeparator >= 0 && lastSeparator < candidate.Length - 1)
        {
            candidate = candidate[(lastSeparator + 1)..];
        }

        var extensionStart = candidate.LastIndexOf('.');
        if (extensionStart <= 0 || extensionStart == candidate.Length - 1)
        {
            return false;
        }

        var extension = candidate[extensionStart..].ToLowerInvariant();
        return extension is ".jpg"
            or ".jpeg"
            or ".png"
            or ".gif"
            or ".bmp"
            or ".tif"
            or ".tiff"
            or ".webp"
            or ".svg"
            or ".emf"
            or ".wmf"
            or ".heic";
    }

    private static void AddChartAccessibilityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        SlideShape shape)
    {
        if (shape.Kind != SlideShapeKind.Chart
            || shape.Chart is not { } chart
            || NormalizeText(chart.Title) is not null)
        {
            return;
        }

        issues.Add(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            slideIndex,
            shape.Id,
            "Chart title missing",
            $"{DescribeShape(shape)} does not have a chart title.",
                    new PresentationAccessibilityIssueActionSummary(
                        MissingChartTitleActionSummary,
                        ChartTitleCommandId,
                        true)));
    }

    private static void AddMediaAccessibilityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        SlideShape shape)
    {
        if (shape.Kind != SlideShapeKind.Media
            || shape.Media?.IsVideo != true
            || shape.IsDecorative
            || HasModeledCaptionTracks(shape.Media))
        {
            return;
        }

        issues.Add(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            slideIndex,
            shape.Id,
            "Video captions missing",
            $"{DescribeShape(shape)} does not expose closed captions or subtitles.",
            new PresentationAccessibilityIssueActionSummary(
                MissingVideoCaptionsActionSummary,
                PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId,
                true)));
    }

    private static bool HasModeledCaptionTracks(MediaInfo? media)
        => media?.CaptionTracks.Any(track =>
            !string.IsNullOrWhiteSpace(track.Source)
            || !string.IsNullOrWhiteSpace(track.RelationshipId)
            || !string.IsNullOrWhiteSpace(track.ContentType)) == true;

    private static void AddTableAccessibilityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        SlideShape shape)
    {
        if (shape.Table is not { } table || !HasTableCells(table))
        {
            return;
        }

        var hasDeclaredHeaderRow = HasDeclaredTableHeaderRow(table);
        if (!hasDeclaredHeaderRow)
        {
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Table header row missing",
                $"{DescribeShape(shape)} does not mark the first row as a header row.",
                new PresentationAccessibilityIssueActionSummary(
                    MissingTableHeaderRowActionSummary,
                    SetTableHeaderRowCommandId,
                    true)));
        }
        else if (CountBlankHeaderCells(table) is var blankHeaderCellCount && blankHeaderCellCount > 0)
        {
            var noun = blankHeaderCellCount == 1 ? "cell" : "cells";
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Blank table header cells",
                $"{DescribeShape(shape)} has {blankHeaderCellCount} blank header {noun}.",
                new PresentationAccessibilityIssueActionSummary(
                    BlankTableHeaderCellsActionSummary,
                    ReviewTableStructureCommandId,
                    true)));
        }

        if (CountBlankBodyCells(table) is var blankBodyCellCount && blankBodyCellCount > 0)
        {
            var noun = blankBodyCellCount == 1 ? "cell" : "cells";
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Blank table body cells",
                $"{DescribeShape(shape)} has {blankBodyCellCount} blank body {noun}.",
                new PresentationAccessibilityIssueActionSummary(
                    BlankTableBodyCellsActionSummary,
                    ReviewTableStructureCommandId,
                    true)));
        }

        if (HasMergedTableCells(table))
        {
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Merged or split table cells",
                $"{DescribeShape(shape)} contains merged or split cells that can make table reading order ambiguous.",
                new PresentationAccessibilityIssueActionSummary(
                    MergedTableCellsActionSummary,
                    ReviewTableStructureCommandId,
                    true)));
        }
    }

    private static bool HasTableCells(TableShape table)
        => table.Rows.Any(row => row.Cells.Count > 0);

    private static bool HasDeclaredTableHeaderRow(TableShape table)
        => table.Flags.FirstRow;

    private static int CountBlankHeaderCells(TableShape table)
    {
        if (table.Rows.Count == 0)
        {
            return 0;
        }

        return table.Rows[0].Cells.Count(cell =>
            !cell.HMerge &&
            !cell.VMerge &&
            NormalizeText(GetPlainText(cell.TextBody)) is null);
    }

    private static int CountBlankBodyCells(TableShape table)
    {
        var firstBodyRowIndex = 1;
        if (table.Rows.Count <= firstBodyRowIndex)
        {
            return 0;
        }

        return table.Rows
            .Skip(firstBodyRowIndex)
            .SelectMany(row => row.Cells)
            .Count(cell =>
                !cell.HMerge &&
                !cell.VMerge &&
                NormalizeText(GetPlainText(cell.TextBody)) is null);
    }

    private static bool HasMergedTableCells(TableShape table)
        => table.Rows
            .SelectMany(row => row.Cells)
            .Any(cell => cell.GridSpan > 1 || cell.RowSpan > 1 || cell.HMerge || cell.VMerge);

    private static IReadOnlyList<PresentationTableStructureCellPlan> BuildBlankHeaderCellPlans(TableShape table)
    {
        if (table.Rows.Count == 0)
        {
            return [];
        }

        return BuildBlankCellPlans(table.Rows[0], rowIndex: 0);
    }

    private static IReadOnlyList<PresentationTableStructureCellPlan> BuildBlankBodyCellPlans(TableShape table)
    {
        if (table.Rows.Count <= 1)
        {
            return [];
        }

        var cells = new List<PresentationTableStructureCellPlan>();
        for (var rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            cells.AddRange(BuildBlankCellPlans(table.Rows[rowIndex], rowIndex));
        }

        return cells;
    }

    private static IReadOnlyList<PresentationTableStructureCellPlan> BuildBlankCellPlans(TableRow row, int rowIndex)
    {
        var cells = new List<PresentationTableStructureCellPlan>();
        for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
        {
            var cell = row.Cells[columnIndex];
            if (!cell.HMerge &&
                !cell.VMerge &&
                NormalizeText(GetPlainText(cell.TextBody)) is null)
            {
                cells.Add(new PresentationTableStructureCellPlan(
                    rowIndex,
                    columnIndex,
                    BuildTableCellReference(rowIndex, columnIndex)));
            }
        }

        return cells;
    }

    private static IReadOnlyList<PresentationTableStructureSpanPlan> BuildMergedOrSplitCellPlans(TableShape table)
    {
        var cells = new List<PresentationTableStructureSpanPlan>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                var cell = row.Cells[columnIndex];
                if (cell.GridSpan <= 1 && cell.RowSpan <= 1 && !cell.HMerge && !cell.VMerge)
                {
                    continue;
                }

                var reference = BuildTableCellReference(rowIndex, columnIndex);
                cells.Add(new PresentationTableStructureSpanPlan(
                    rowIndex,
                    columnIndex,
                    reference,
                    Math.Max(1, cell.GridSpan),
                    Math.Max(1, cell.RowSpan),
                    cell.HMerge,
                    cell.VMerge,
                    BuildTableCellSpanSummary(reference, cell)));
            }
        }

        return cells;
    }

    private static string BuildTableCellReference(int rowIndex, int columnIndex)
        => $"R{rowIndex + 1}C{columnIndex + 1}";

    private static string BuildTableStructureReviewSummary(PresentationTableStructureReviewPlan plan)
    {
        var tableName = string.IsNullOrWhiteSpace(plan.TableName) ? "Selected table" : plan.TableName;
        var dimensions = $"{PresentationCommentMetadataPolicy.BuildCountSummary(plan.RowCount, "row")}, {PresentationCommentMetadataPolicy.BuildCountSummary(plan.ColumnCount, "column")}";
        var blankHeader = PresentationCommentMetadataPolicy.BuildCountSummary(plan.BlankHeaderCells.Count, "blank header cell");
        var blankBody = PresentationCommentMetadataPolicy.BuildCountSummary(plan.BlankBodyCells.Count, "blank body cell");
        var merged = PresentationCommentMetadataPolicy.BuildCountSummary(plan.MergedOrSplitCells.Count, "merged or split cell");
        return $"{tableName}: {dimensions}. {blankHeader}, {blankBody}, {merged}.";
    }

    private static string BuildTableCellSpanSummary(string cellReference, TableCell cell)
    {
        var details = new List<string>();
        if (cell.GridSpan > 1)
        {
            details.Add($"spans {cell.GridSpan} columns");
        }

        if (cell.RowSpan > 1)
        {
            details.Add($"spans {cell.RowSpan} rows");
        }

        if (cell.HMerge)
        {
            details.Add("continues a horizontal merge");
        }

        if (cell.VMerge)
        {
            details.Add("continues a vertical merge");
        }

        return details.Count == 0
            ? $"{cellReference} has merged or split structure."
            : $"{cellReference} {string.Join(", ", details)}.";
    }

    private static void AddTextHyperlinkAccessibilityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        SlideShape shape)
    {
        var hasMissingScreenTip = HasTextRunHyperlinkWithoutScreenTip(shape);
        var unclearDisplayText = FindUnclearTextRunHyperlinkDisplayText(shape);
        if (!hasMissingScreenTip && unclearDisplayText is null)
        {
            return;
        }

        if (hasMissingScreenTip)
        {
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Info,
                slideIndex,
                shape.Id,
                "Hyperlink ScreenTip missing",
                $"Text link in {DescribeShape(shape)} is missing hover/help text.",
                new PresentationAccessibilityIssueActionSummary(
                    MissingHyperlinkScreenTipActionSummary,
                    InsertLinkCommandId,
                    true)));
        }

        if (unclearDisplayText is not null)
        {
            var reason = IsRawUrlDisplayText(unclearDisplayText)
                ? "raw URL display text"
                : "non-descriptive display text";
            issues.Add(new PresentationAccessibilityIssueDescriptor(
                PresentationAccessibilityIssueSeverity.Warning,
                slideIndex,
                shape.Id,
                "Unclear hyperlink text",
                $"Text link in {DescribeShape(shape)} uses {reason} \"{BuildPreview(unclearDisplayText)}\".",
                new PresentationAccessibilityIssueActionSummary(
                    UnclearHyperlinkTextActionSummary,
                    InsertLinkCommandId,
                true)));
        }
    }

    private sealed record TextContrastMatch(string TextPreview, double ContrastRatio, string? CellReference);

    private static void AddTextContrastAccessibilityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        Slide slide,
        SlideShape shape)
    {
        AddShapeTextContrastAccessibilityIssue(issues, slideIndex, slide, shape);
        AddTableTextContrastAccessibilityIssues(issues, slideIndex, shape);
    }

    private static void AddShapeTextContrastAccessibilityIssue(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        Slide slide,
        SlideShape shape)
    {
        if (shape.TextBody is null
            || !TryGetShapeTextBackgroundColor(slide, shape, out var backgroundColor)
            || !TryFindLowContrastText(shape.TextBody, inheritedTextColor: null, backgroundColor, out var match))
        {
            return;
        }

        issues.Add(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            slideIndex,
            shape.Id,
            "Low text contrast",
            BuildLowTextContrastDetail(DescribeShape(shape), match),
            new PresentationAccessibilityIssueActionSummary(
                LowTextContrastActionSummary,
                null,
                true)));
    }

    private static void AddTableTextContrastAccessibilityIssues(
        List<PresentationAccessibilityIssueDescriptor> issues,
        int slideIndex,
        SlideShape shape)
    {
        if (shape.Table is not { } table)
        {
            return;
        }

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (int columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                var cell = row.Cells[columnIndex];
                if (cell.TextBody is null
                    || !TryGetExplicitOpaqueSolidColor(table.ComputeEffectiveFill(rowIndex, columnIndex, cell), out var backgroundColor))
                {
                    continue;
                }

                var inheritedTextColor = TryGetExplicitOpaqueColor(
                    table.ComputeEffectiveTextColor(rowIndex, columnIndex),
                    out var effectiveTextColor)
                    ? effectiveTextColor
                    : (SrgbColor?)null;
                var cellReference = BuildTableCellReference(rowIndex, columnIndex);
                if (!TryFindLowContrastText(cell.TextBody, inheritedTextColor, backgroundColor, out var match))
                {
                    continue;
                }

                issues.Add(new PresentationAccessibilityIssueDescriptor(
                    PresentationAccessibilityIssueSeverity.Warning,
                    slideIndex,
                    shape.Id,
                    "Low text contrast",
                    BuildLowTextContrastDetail(DescribeShape(shape), match with { CellReference = cellReference }),
                    new PresentationAccessibilityIssueActionSummary(
                        LowTextContrastActionSummary,
                        null,
                        true)));
            }
        }
    }

    private static bool TryFindLowContrastText(
        TextBody textBody,
        SrgbColor? inheritedTextColor,
        SrgbColor backgroundColor,
        out TextContrastMatch match)
    {
        foreach (var run in textBody.Paragraphs.SelectMany(paragraph => paragraph.Runs))
        {
            if (string.IsNullOrWhiteSpace(run.Text)
                || !TryGetRunForegroundColor(run, inheritedTextColor, out var foregroundColor))
            {
                continue;
            }

            var ratio = CalculateContrastRatio(foregroundColor, backgroundColor);
            if (ratio < TextContrastMinimumRatio)
            {
                match = new TextContrastMatch(BuildPreview(run.Text), ratio, null);
                return true;
            }
        }

        match = new TextContrastMatch(string.Empty, 0, null);
        return false;
    }

    private static bool TryGetRunForegroundColor(
        Run run,
        SrgbColor? inheritedTextColor,
        out SrgbColor color)
    {
        if (run.TextFill is not null)
        {
            return TryGetExplicitOpaqueSolidColor(run.TextFill, out color);
        }

        if (TryGetExplicitOpaqueColor(run.Color, out color))
        {
            return true;
        }

        if (inheritedTextColor is { } inherited)
        {
            color = inherited;
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryGetShapeTextBackgroundColor(Slide slide, SlideShape shape, out SrgbColor color)
    {
        if (shape.Fill is null || shape.Fill is ShapeFill.None)
        {
            return TryGetExplicitOpaqueSolidColor(slide.Background, out color);
        }

        return TryGetExplicitOpaqueSolidColor(shape.Fill, out color);
    }

    private static bool TryGetExplicitOpaqueSolidColor(ShapeFill? fill, out SrgbColor color)
    {
        if (fill is ShapeFill.Solid solid)
        {
            return TryGetExplicitOpaqueColor(solid.Color, out color);
        }

        color = default;
        return false;
    }

    private static bool TryGetExplicitOpaqueColor(ThemeAwareColor? color, out SrgbColor resolved)
    {
        if (color is { Alpha: 255, SchemeColor: null })
        {
            resolved = color.Resolved;
            return true;
        }

        resolved = default;
        return false;
    }

    private static string BuildLowTextContrastDetail(string sourceName, TextContrastMatch match)
    {
        var location = match.CellReference is null ? sourceName : $"{sourceName} cell {match.CellReference}";
        return $"{location} text \"{match.TextPreview}\" has {FormatContrastRatio(match.ContrastRatio)}:1 contrast against its solid background; threshold is {TextContrastMinimumRatio.ToString("0.0", CultureInfo.InvariantCulture)}:1.";
    }

    private static string FormatContrastRatio(double ratio)
        => ratio.ToString("0.##", CultureInfo.InvariantCulture);

    private static double CalculateContrastRatio(SrgbColor foreground, SrgbColor background)
    {
        var foregroundLuminance = CalculateRelativeLuminance(foreground);
        var backgroundLuminance = CalculateRelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double CalculateRelativeLuminance(SrgbColor color)
        => 0.2126 * ConvertSrgbChannelToLinear(color.R)
            + 0.7152 * ConvertSrgbChannelToLinear(color.G)
            + 0.0722 * ConvertSrgbChannelToLinear(color.B);

    private static double ConvertSrgbChannelToLinear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static bool HasTextRunHyperlinkWithoutScreenTip(SlideShape shape)
        => EnumerateShapeTextBodies(shape)
            .SelectMany(body => body.Paragraphs)
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.Hyperlink is not null && string.IsNullOrWhiteSpace(run.Hyperlink.Tooltip));

    private static string? FindUnclearTextRunHyperlinkDisplayText(SlideShape shape)
    {
        foreach (var run in EnumerateShapeTextBodies(shape)
            .SelectMany(body => body.Paragraphs)
            .SelectMany(paragraph => paragraph.Runs))
        {
            if (run.Hyperlink is null)
            {
                continue;
            }

            var displayText = NormalizeHyperlinkDisplayText(run.Text);
            if (displayText is not null && IsUnclearHyperlinkDisplayText(displayText))
            {
                return displayText;
            }
        }

        return null;
    }

    private static string? NormalizeHyperlinkDisplayText(string? text)
    {
        var normalized = NormalizeText(text);
        return normalized is null
            ? null
            : string.Join(' ', normalized.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsUnclearHyperlinkDisplayText(string displayText)
        => displayText.ToLowerInvariant() switch
        {
            "click here" or "here" or "learn more" or "more" or "read more" => true,
            _ => IsRawUrlDisplayText(displayText)
        };

    private static bool IsRawUrlDisplayText(string displayText)
    {
        var candidate = displayText.Trim().TrimEnd('.', ',', ';', ':', ')', ']');
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeMailto))
        {
            return true;
        }

        return candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out _);
    }

    private static IEnumerable<TextBody> EnumerateShapeTextBodies(SlideShape shape)
    {
        if (shape.TextBody is not null)
        {
            yield return shape.TextBody;
        }

        if (shape.Table is null)
        {
            yield break;
        }

        foreach (var cell in shape.Table.Rows.SelectMany(row => row.Cells))
        {
            if (cell.TextBody is not null)
            {
                yield return cell.TextBody;
            }
        }
    }

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

    private static List<SlideShape>? FindContainingShapeList(
        List<SlideShape> shapes,
        uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
            {
                return shapes;
            }

            if (shape.Children.Count > 0 &&
                FindContainingShapeList(shape.Children, shapeId) is { } childList)
            {
                return childList;
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
