using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationCommentMutationRequest(
    PresentationReviewWorkflowIntentKind Intent,
    int SlideIndex,
    int? CommentIndex,
    string? Text = null,
    DateTime? Timestamp = null,
    string? Author = null,
    string? Initials = null,
    long Xemu = 0,
    long Yemu = 0,
    DateTime? ResolvedAt = null,
    string? ResolvedBy = null);

public sealed record PresentationCommentMutationResult(
    PresentationCommentMutationPlan Plan,
    bool Applied,
    int? SelectedCommentIndex);

public static class PresentationCommentMutationService
{
    private const string DefaultAuthor = "FreeP User";

    public static PresentationCommentMutationResult Apply(
        IReadOnlyList<Slide> slides,
        PresentationCommentMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(request);

        var plan = BuildPlan(slides, request);
        var applied = PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, plan);
        var selectedCommentIndex = applied
            ? PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(
                slides,
                plan,
                request.CommentIndex)
            : request.CommentIndex;

        return new PresentationCommentMutationResult(plan, applied, selectedCommentIndex);
    }

    /// <summary>
    /// Builds (and validates) the mutation plan for <paramref name="request"/> without applying it.
    /// Callers that need the mutation to be undoable (e.g. <c>PresentationReviewWorkflowSession</c>)
    /// use this to get the before/after comment state, then apply it through the presentation's
    /// undo/redo command bus instead of <see cref="Apply"/>.
    /// </summary>
    public static PresentationCommentMutationPlan BuildPlan(
        IReadOnlyList<Slide> slides,
        PresentationCommentMutationRequest request)
    {
        if (request.Intent == PresentationReviewWorkflowIntentKind.AddComment)
        {
            return PresentationReviewWorkflowPlanner.BuildAddCommentPlan(
                slides,
                request.SlideIndex,
                request.Text,
                request.Author ?? DefaultAuthor,
                request.Initials,
                request.Xemu,
                request.Yemu,
                request.Timestamp ?? DateTime.UtcNow);
        }

        if (request.CommentIndex is not { } commentIndex)
        {
            return InvalidPlan(request, PresentationReviewWorkflowPlanner.MissingCommentMessage);
        }

        return request.Intent switch
        {
            PresentationReviewWorkflowIntentKind.EditComment =>
                PresentationReviewWorkflowPlanner.BuildEditCommentPlan(
                    slides,
                    request.SlideIndex,
                    commentIndex,
                    request.Text,
                    request.Author,
                    request.Initials),
            PresentationReviewWorkflowIntentKind.DeleteComment =>
                PresentationReviewWorkflowPlanner.BuildDeleteCommentPlan(
                    slides,
                    request.SlideIndex,
                    commentIndex),
            PresentationReviewWorkflowIntentKind.ResolveComment =>
                PresentationReviewWorkflowPlanner.BuildResolveCommentPlan(
                    slides,
                    request.SlideIndex,
                    commentIndex,
                    request.ResolvedAt ?? DateTime.UtcNow,
                    request.ResolvedBy ?? DefaultAuthor),
            PresentationReviewWorkflowIntentKind.ReplyComment =>
                PresentationReviewWorkflowPlanner.BuildReplyCommentPlan(
                    slides,
                    request.SlideIndex,
                    commentIndex,
                    request.Text,
                    request.Author ?? DefaultAuthor,
                    request.Initials,
                    request.Timestamp ?? DateTime.UtcNow),
            PresentationReviewWorkflowIntentKind.ReopenComment =>
                PresentationReviewWorkflowPlanner.BuildReopenCommentPlan(
                    slides,
                    request.SlideIndex,
                    commentIndex),
            _ => InvalidPlan(request, PresentationReviewWorkflowPlanner.MissingCommentMessage)
        };
    }

    private static PresentationCommentMutationPlan InvalidPlan(
        PresentationCommentMutationRequest request,
        string message) =>
        new(
            request.Intent,
            false,
            request.SlideIndex,
            request.CommentIndex,
            null,
            message);
}
