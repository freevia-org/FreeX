using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationReviewWorkflowIntentKind
{
    ShowCommentsPane,
    AddComment,
    EditComment,
    DeleteComment,
    PreviousComment,
    NextComment,
    ResolveComment,
    CheckAccessibility,
    OpenAltText,
    RunProofing
}

public enum PresentationWorkflowCapabilityStatus
{
    Available,
    RequiresHost,
    Deferred
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
    bool CanDelete,
    bool CanResolve);

public sealed record PresentationCommentPanePlan(
    int SlideIndex,
    int SlideCount,
    int SlideCommentCount,
    int TotalCommentCount,
    int SelectedCommentIndex,
    IReadOnlyList<PresentationCommentDescriptor> Comments,
    IReadOnlyList<PresentationReviewWorkflowActionPlan> Actions);

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

public sealed record PresentationProofingRequestPlan(
    bool CanStart,
    PresentationWorkflowCapabilityStatus Status,
    int TextShapeCount,
    int NotesSlideCount,
    int ReadOnlyCommentCount,
    string Message);

public static class PresentationReviewWorkflowPlanner
{
    public const string CommentsPaneCommandId = "freep.review.comments.pane";
    public const string AddCommentCommandId = "freep.review.comments.add";
    public const string EditCommentCommandId = "freep.review.comments.edit";
    public const string DeleteCommentCommandId = "freep.review.comments.delete";
    public const string PreviousCommentCommandId = "freep.review.comments.previous";
    public const string NextCommentCommandId = "freep.review.comments.next";
    public const string ResolveCommentCommandId = "freep.review.comments.resolve";
    public const string AccessibilityCommandId = "freep.review.accessibility.check";
    public const string AltTextCommandId = "freep.review.alt-text";
    public const string ProofingCommandId = "freep.review.proofing.spelling";
    public const string InsertLinkCommandId = "freep.insert-link";

    public const string MissingSlideMessage = "Select a slide before adding a comment.";
    public const string MissingCommentMessage = "Select an existing comment first.";
    public const string EmptyCommentMessage = "Comment text cannot be empty.";
    public const string ModernCommentStateDeferredMessage =
        "Modern resolved-thread state is not modeled yet.";
    public const string MissingShapeMessage = "Select a shape before editing alt text.";
    public const string ProofingRequiresHostMessage =
        "Proofing needs a host spelling engine; this shared plan owns the searchable FreeP scopes.";
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
            .Select((comment, index) => DescribeComment(slideIndex, index, comment))
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
            Xemu = current.Xemu,
            Yemu = current.Yemu,
            Idx = current.Idx,
            AuthorId = current.AuthorId
        };

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

    public static PresentationCommentMutationPlan BuildResolveCommentPlan(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int commentIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);

        return GetComment(slides, slideIndex, commentIndex) is null
            ? InvalidMutation(PresentationReviewWorkflowIntentKind.ResolveComment, slideIndex, commentIndex, MissingCommentMessage)
            : InvalidMutation(PresentationReviewWorkflowIntentKind.ResolveComment, slideIndex, commentIndex, ModernCommentStateDeferredMessage);
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

    public static PresentationProofingRequestPlan BuildProofingRequestPlan(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        int textShapes = 0;
        int notesSlides = 0;
        int comments = 0;

        foreach (var slide in presentation.Slides)
        {
            comments += slide.Comments.Count;
            if (slide.Notes is not null && !string.IsNullOrWhiteSpace(TextBodyToPlainText(slide.Notes)))
            {
                notesSlides++;
            }

            textShapes += EnumerateShapes(slide.Shapes)
                .Count(shape => shape.TextBody is not null && !string.IsNullOrWhiteSpace(shape.PlainText));
        }

        return new PresentationProofingRequestPlan(
            textShapes > 0 || notesSlides > 0,
            PresentationWorkflowCapabilityStatus.RequiresHost,
            textShapes,
            notesSlides,
            comments,
            ProofingRequiresHostMessage);
    }

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildCommentActions(
        IReadOnlyList<Slide> slides,
        int slideIndex,
        int? selectedCommentIndex,
        int totalCommentCount)
    {
        var hasSlide = GetSlide(slides, slideIndex) is not null;
        var hasSelectedComment = selectedCommentIndex.HasValue;
        var hasPrevious = TryGetAdjacentComment(slides, slideIndex, selectedCommentIndex, -1, out _);
        var hasNext = TryGetAdjacentComment(slides, slideIndex, selectedCommentIndex, 1, out _);

        return
        [
            new(CommentsPaneCommandId, "Show Comments", PresentationReviewWorkflowIntentKind.ShowCommentsPane, true, PresentationWorkflowCapabilityStatus.Available),
            new(AddCommentCommandId, "New Comment", PresentationReviewWorkflowIntentKind.AddComment, hasSlide, PresentationWorkflowCapabilityStatus.Available, hasSlide ? null : MissingSlideMessage),
            new(EditCommentCommandId, "Edit Comment", PresentationReviewWorkflowIntentKind.EditComment, hasSelectedComment, PresentationWorkflowCapabilityStatus.Available, hasSelectedComment ? null : MissingCommentMessage),
            new(DeleteCommentCommandId, "Delete Comment", PresentationReviewWorkflowIntentKind.DeleteComment, hasSelectedComment, PresentationWorkflowCapabilityStatus.Available, hasSelectedComment ? null : MissingCommentMessage),
            new(PreviousCommentCommandId, "Previous Comment", PresentationReviewWorkflowIntentKind.PreviousComment, hasPrevious, PresentationWorkflowCapabilityStatus.Available, hasPrevious ? null : "No previous comment."),
            new(NextCommentCommandId, "Next Comment", PresentationReviewWorkflowIntentKind.NextComment, hasNext, PresentationWorkflowCapabilityStatus.Available, hasNext ? null : "No next comment."),
            new(ResolveCommentCommandId, "Resolve Comment", PresentationReviewWorkflowIntentKind.ResolveComment, false, PresentationWorkflowCapabilityStatus.Deferred, totalCommentCount == 0 ? MissingCommentMessage : ModernCommentStateDeferredMessage),
        ];
    }

    private static IReadOnlyList<PresentationReviewWorkflowActionPlan> BuildAccessibilityActions()
        =>
        [
            new(AccessibilityCommandId, "Check Accessibility", PresentationReviewWorkflowIntentKind.CheckAccessibility, true, PresentationWorkflowCapabilityStatus.RequiresHost),
            new(AltTextCommandId, "Alt Text", PresentationReviewWorkflowIntentKind.OpenAltText, true, PresentationWorkflowCapabilityStatus.Available),
            new(ProofingCommandId, "Spelling", PresentationReviewWorkflowIntentKind.RunProofing, true, PresentationWorkflowCapabilityStatus.RequiresHost, ProofingRequiresHostMessage),
        ];

    private static PresentationCommentDescriptor DescribeComment(
        int slideIndex,
        int commentIndex,
        SlideComment comment)
        => new(
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
            true,
            false);

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
        if (comments is null || selectedCommentIndex is not { } index)
        {
            return null;
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
