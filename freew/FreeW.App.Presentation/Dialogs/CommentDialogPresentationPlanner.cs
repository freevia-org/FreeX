using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Dialogs;

public sealed record CommentDialogTextSpec(
    string ListTitle,
    string ReplyTitle,
    string ReplyFieldLabel,
    string ReplyActionLabel,
    string CancelActionLabel,
    string CloseActionLabel,
    string ReplyRequiredMessage,
    string MissingReplyTargetMessage,
    string NewCommentTitle,
    string CommentFieldLabel,
    string ResolveTitle,
    string MissingResolveTargetMessage,
    string DeleteTitle,
    string MissingDeleteTargetMessage,
    string PreviousTitle,
    string NextTitle,
    string NoCommentsMessage,
    string EmptyListMessage,
    string OpenStateLabel,
    string ResolvedStateLabel,
    string BlankCommentLabel);

public sealed record CommentReplyAcceptance(
    bool IsAccepted,
    string Text,
    string? ValidationMessage = null);

public sealed record CommentListRowPresentation(
    int DisplayNumber,
    string StateLabel,
    string ReplyCountLabel,
    string Author,
    string Body,
    string HeadingText,
    string CompactText);

public sealed record CommentListPresentation(
    string Title,
    string SummaryText,
    string EmptyMessage,
    IReadOnlyList<CommentListRowPresentation> Rows);

/// <summary>
/// Owns comment-dialog validation and semantic text projection. Native hosts retain modal lifetime,
/// controls, layout, focus, and message/status realization.
/// </summary>
public static class CommentDialogPresentationPlanner
{
    public const int MaximumBodyLength = 180;

    public static CommentDialogTextSpec Text { get; } = new(
        ListTitle: "Comments",
        ReplyTitle: "Reply",
        ReplyFieldLabel: "Reply:",
        ReplyActionLabel: "Reply",
        CancelActionLabel: "Cancel",
        CloseActionLabel: "Close",
        ReplyRequiredMessage: "Enter reply text.",
        MissingReplyTargetMessage: "Place the cursor inside a comment, then choose Reply.",
        NewCommentTitle: "New Comment",
        CommentFieldLabel: "Comment:",
        ResolveTitle: "Resolve",
        MissingResolveTargetMessage: "Place the cursor inside a comment, then choose Resolve.",
        DeleteTitle: "Delete Comment",
        MissingDeleteTargetMessage: "Place the cursor inside a comment, then choose Delete.",
        PreviousTitle: "Previous Comment",
        NextTitle: "Next Comment",
        NoCommentsMessage: "This document does not contain any comments.",
        EmptyListMessage: "No comments in this document.",
        OpenStateLabel: "Open",
        ResolvedStateLabel: "Resolved",
        BlankCommentLabel: "(blank)");

    public static CommentReplyAcceptance PlanReplyAcceptance(string? text)
    {
        var normalized = text?.Trim() ?? string.Empty;
        return normalized.Length == 0
            ? new CommentReplyAcceptance(false, string.Empty, Text.ReplyRequiredMessage)
            : new CommentReplyAcceptance(true, normalized);
    }

    public static CommentListPresentation BuildList(IReadOnlyList<CommentListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new CommentListPresentation(
            Text.ListTitle,
            FormatThreadCount(items.Count),
            Text.EmptyListMessage,
            items.Select(BuildRow).ToArray());
    }

    public static CommentListRowPresentation BuildRow(CommentListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var displayNumber = item.Id + 1;
        var state = item.Resolved ? Text.ResolvedStateLabel : Text.OpenStateLabel;
        var replies = FormatReplyCount(item.ReplyCount);
        var author = string.IsNullOrWhiteSpace(item.Author) ? "Unknown" : item.Author.Trim();
        var body = NormalizeBody(item.Text);

        return new CommentListRowPresentation(
            displayNumber,
            state,
            replies,
            author,
            body,
            $"#{displayNumber}  {author}  {state} - {replies}",
            $"#{displayNumber} {state} - {author} - {body} ({replies})");
    }

    public static string FormatThreadCount(int count) =>
        $"{count} comment thread{(count == 1 ? string.Empty : "s")}";

    public static string FormatReplyCount(int count) =>
        count == 1 ? "1 reply" : $"{count} replies";

    public static string NormalizeBody(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? Text.BlankCommentLabel
            : text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= MaximumBodyLength
            ? normalized
            : normalized[..(MaximumBodyLength - 3)] + "...";
    }
}
