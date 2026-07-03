using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record ReviewMarkupDescriptor(
    string Id,
    string Label,
    bool IsChecked,
    int ItemCount,
    string StatusText);

public sealed record ReviewWorkflowStatus(
    bool TrackChangesEnabled,
    ReviewDisplayMode DisplayMode,
    string DisplayModeLabel,
    string DisplayModeDescription,
    int RevisionCount,
    int InsertionCount,
    int DeletionCount,
    int FormattingCount,
    int CommentThreadCount,
    int OpenCommentThreadCount,
    int ResolvedCommentThreadCount,
    int VisibleReviewItemCount,
    bool CanNavigateChanges,
    bool CanAcceptOrRejectChanges,
    bool HasHiddenMarkup,
    string StatusText,
    IReadOnlyList<ReviewMarkupDescriptor> MarkupDescriptors);

public static class ReviewWorkflowStatusPlanner
{
    public static ReviewWorkflowStatus Build(
        TextDocument document,
        ReviewDisplayPolicy policy,
        bool trackChangesEnabled)
    {
        ArgumentNullException.ThrowIfNull(document);

        var revisions = RevisionList.Enumerate(document);
        var comments = CommentListPlanner.Build(document);

        var insertionCount = revisions.Count(entry => entry.Kind == RevisionEntryKind.Insertion);
        var deletionCount = revisions.Count(entry => entry.Kind == RevisionEntryKind.Deletion);
        var formattingCount = revisions.Count(entry => entry.Kind == RevisionEntryKind.Formatting);
        var revisionCount = revisions.Count;
        var commentThreadCount = comments.Count;
        var resolvedCommentThreadCount = comments.Count(item => item.Resolved);
        var openCommentThreadCount = commentThreadCount - resolvedCommentThreadCount;

        var visibleRevisionCount = revisions.Count(entry => IsVisibleByShowMarkup(entry, policy));
        var visibleCommentCount = policy.ShowComments ? commentThreadCount : 0;
        var visibleReviewItemCount = visibleRevisionCount + visibleCommentCount;
        var hasHiddenMarkup = visibleReviewItemCount < revisionCount + commentThreadCount;

        return new ReviewWorkflowStatus(
            trackChangesEnabled,
            policy.DisplayMode,
            DisplayModeLabel(policy.DisplayMode),
            DisplayModeDescription(policy.DisplayMode),
            revisionCount,
            insertionCount,
            deletionCount,
            formattingCount,
            commentThreadCount,
            openCommentThreadCount,
            resolvedCommentThreadCount,
            visibleReviewItemCount,
            CanNavigateChanges: revisionCount > 0,
            CanAcceptOrRejectChanges: revisionCount > 0,
            hasHiddenMarkup,
            BuildStatusText(trackChangesEnabled, revisionCount, commentThreadCount, hasHiddenMarkup),
            BuildMarkupDescriptors(policy, insertionCount + deletionCount, commentThreadCount, formattingCount));
    }

    private static IReadOnlyList<ReviewMarkupDescriptor> BuildMarkupDescriptors(
        ReviewDisplayPolicy policy,
        int insertionDeletionCount,
        int commentThreadCount,
        int formattingCount) =>
        [
            new(
                "insertions-deletions",
                "Insertions and Deletions",
                policy.ShowInsertionsAndDeletions,
                insertionDeletionCount,
                BuildCheckedStatus(policy.ShowInsertionsAndDeletions, insertionDeletionCount)),
            new(
                "comments",
                "Comments",
                policy.ShowComments,
                commentThreadCount,
                BuildCheckedStatus(policy.ShowComments, commentThreadCount)),
            new(
                "formatting",
                "Formatting",
                policy.ShowFormatting,
                formattingCount,
                BuildCheckedStatus(policy.ShowFormatting, formattingCount)),
        ];

    private static bool IsVisibleByShowMarkup(RevisionEntry entry, ReviewDisplayPolicy policy) =>
        entry.Kind switch
        {
            RevisionEntryKind.Insertion or RevisionEntryKind.Deletion => policy.ShowInsertionsAndDeletions,
            RevisionEntryKind.Formatting => policy.ShowFormatting,
            _ => true,
        };

    private static string DisplayModeLabel(ReviewDisplayMode mode) =>
        mode switch
        {
            ReviewDisplayMode.AllMarkup => "All Markup",
            ReviewDisplayMode.SimpleMarkup => "Simple Markup",
            ReviewDisplayMode.NoMarkup => "No Markup",
            ReviewDisplayMode.Original => "Original",
            _ => mode.ToString()
        };

    private static string DisplayModeDescription(ReviewDisplayMode mode) =>
        mode switch
        {
            ReviewDisplayMode.AllMarkup => "Shows all tracked changes inline.",
            ReviewDisplayMode.SimpleMarkup => "Shows final text with change bars.",
            ReviewDisplayMode.NoMarkup => "Shows final text without revision markup.",
            ReviewDisplayMode.Original => "Shows original text before tracked changes.",
            _ => "Shows the selected review display."
        };

    private static string BuildStatusText(
        bool trackChangesEnabled,
        int revisionCount,
        int commentThreadCount,
        bool hasHiddenMarkup)
    {
        var parts = new List<string>
        {
            $"Track Changes: {(trackChangesEnabled ? "On" : "Off")}",
            $"{revisionCount} {Pluralize(revisionCount, "change", "changes")}",
            $"{commentThreadCount} {Pluralize(commentThreadCount, "comment", "comments")}"
        };

        if (hasHiddenMarkup)
            parts.Add("some markup hidden");

        return string.Join(" - ", parts);
    }

    private static string BuildCheckedStatus(bool isChecked, int itemCount) =>
        $"{(isChecked ? "Shown" : "Hidden")} - {itemCount} {Pluralize(itemCount, "item", "items")}";

    private static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;
}
