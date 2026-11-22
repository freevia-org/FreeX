using Free.Shared.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

/// <summary>One message (the root or a reply) shown on a threaded-comment card.</summary>
/// <param name="ReplyIndex">-1 for the root comment, otherwise the zero-based reply index.</param>
public sealed record ThreadedCommentCardMessage(
    int ReplyIndex,
    string Author,
    string Heading,
    string Text,
    bool CanEdit,
    bool CanDelete)
{
    public bool IsRoot => ReplyIndex < 0;
}

/// <summary>
/// Everything a host needs to render Excel's threaded-comment card for one cell: the messages,
/// the resolved state, which actions are available (sheet protection aware) and their labels.
/// </summary>
public sealed record ThreadedCommentCardPlan(
    CellAddress Address,
    string CellReference,
    bool IsResolved,
    IReadOnlyList<ThreadedCommentCardMessage> Messages,
    bool CanReply,
    bool CanEditRoot,
    bool CanDeleteThread,
    bool CanToggleResolved,
    LocalizedTextDescriptor ToggleResolvedLabel,
    LocalizedTextDescriptor? StatusText,
    LocalizedTextDescriptor? ReadOnlyNotice)
{
    public ThreadedCommentCardAction ToggleResolvedAction =>
        IsResolved ? ThreadedCommentCardAction.Reopen : ThreadedCommentCardAction.Resolve;
}

public enum ThreadedCommentCardAction
{
    AddReply,
    EditRoot,
    DeleteThread,
    Resolve,
    Reopen,
    EditReply,
    DeleteReply
}

public enum ThreadedCommentPaneFilter
{
    All,
    Active,
    Resolved
}

public sealed record ThreadedCommentPaneRow(
    CellAddress Address,
    string CellReference,
    string Author,
    string Heading,
    string Preview,
    int ReplyCount,
    bool IsResolved,
    bool CanToggleResolved,
    bool CanDelete,
    LocalizedTextDescriptor ToggleResolvedLabel);

public sealed record ThreadedCommentPanePlan(
    ThreadedCommentPaneFilter Filter,
    IReadOnlyList<ThreadedCommentPaneRow> Rows,
    int ActiveCount,
    int ResolvedCount)
{
    public int TotalCount => ActiveCount + ResolvedCount;
}

/// <summary>
/// Shared (both hosts) view-model planner for the threaded-comment card and the Comments pane.
/// Pure: reads the model, returns descriptors; the hosts render them and route user intent through
/// <see cref="PresentationCommentMutationService"/> so every change stays an undoable command.
/// </summary>
public static class ThreadedCommentCardPlanner
{
    public const int PreviewLength = 90;

    /// <summary>True when the sheet's protection leaves comment authoring open (Excel: "Edit objects").</summary>
    public static bool CanAuthorComments(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return !sheet.IsProtected || sheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditObjects);
    }

    public static ThreadedCommentCardPlan? BuildCard(
        Sheet sheet,
        CellAddress address,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (!sheet.ThreadedComments.TryGetValue(address, out var thread))
            return null;

        var allowed = CanAuthorComments(sheet);
        var messages = new List<ThreadedCommentCardMessage>(thread.Replies.Count + 1)
        {
            new(-1, thread.Author, Heading(thread.Author, thread.CreatedAtUtc, now), thread.Text, allowed, allowed)
        };
        for (var i = 0; i < thread.Replies.Count; i++)
        {
            var reply = thread.Replies[i];
            messages.Add(new(i, reply.Author, Heading(reply.Author, reply.CreatedAtUtc, now), reply.Text, allowed, allowed));
        }

        return new ThreadedCommentCardPlan(
            address,
            address.ToA1(),
            thread.IsResolved,
            messages,
            CanReply: allowed && !thread.IsResolved,
            CanEditRoot: allowed,
            CanDeleteThread: allowed,
            CanToggleResolved: allowed,
            ToggleResolvedLabel: ToggleLabel(thread.IsResolved),
            StatusText: thread.IsResolved ? LocalizedTextDescriptor.Resource("ThreadedCommentCard_ResolvedStatus") : null,
            ReadOnlyNotice: allowed ? null : LocalizedTextDescriptor.Resource("ThreadedCommentCard_ProtectedNotice"));
    }

    public static ThreadedCommentPanePlan BuildPane(
        Sheet sheet,
        ThreadedCommentPaneFilter filter = ThreadedCommentPaneFilter.All,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var allowed = CanAuthorComments(sheet);
        var rows = new List<ThreadedCommentPaneRow>();
        var active = 0;
        var resolved = 0;
        foreach (var address in CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments))
        {
            var thread = sheet.ThreadedComments[address];
            if (thread.IsResolved)
                resolved++;
            else
                active++;

            var include = filter switch
            {
                ThreadedCommentPaneFilter.Active => !thread.IsResolved,
                ThreadedCommentPaneFilter.Resolved => thread.IsResolved,
                _ => true
            };
            if (!include)
                continue;

            rows.Add(new ThreadedCommentPaneRow(
                address,
                address.ToA1(),
                thread.Author,
                Heading(thread.Author, thread.CreatedAtUtc, now),
                ThreadedCommentDialogPlanner.SummarizeReplyText(thread.Text, PreviewLength),
                thread.Replies.Count,
                thread.IsResolved,
                allowed,
                allowed,
                ToggleLabel(thread.IsResolved)));
        }

        return new ThreadedCommentPanePlan(filter, rows, active, resolved);
    }

    /// <summary>
    /// Maps a card action to the undoable mutation plan (null when the action is not applicable, e.g.
    /// an empty reply). <paramref name="text"/> carries the reply / edited text for text actions.
    /// </summary>
    public static PresentationCommentMutationPlan? PlanAction(
        PresentationCommentMutationService service,
        SheetId sheetId,
        ThreadedComment existing,
        ThreadedCommentCardAction action,
        string author,
        string? text = null,
        int? replyIndex = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(existing);

        switch (action)
        {
            case ThreadedCommentCardAction.Resolve:
                return existing.IsResolved ? null : service.PlanResolveThreadedComment(sheetId, true);
            case ThreadedCommentCardAction.Reopen:
                return existing.IsResolved ? service.PlanResolveThreadedComment(sheetId, false) : null;
            case ThreadedCommentCardAction.DeleteThread:
                return service.PlanDeleteThreadedComment(sheetId);
            case ThreadedCommentCardAction.AddReply:
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : service.PlanThreadedComment(
                        sheetId,
                        existing,
                        ThreadedCommentDialogPlanner.CreateResult(existing, null, text, existing.IsResolved),
                        author);
            case ThreadedCommentCardAction.EditRoot:
                return string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), existing.Text, StringComparison.Ordinal)
                    ? null
                    : service.PlanThreadedComment(
                        sheetId,
                        existing,
                        ThreadedCommentDialogPlanner.CreateResult(existing, text, null, existing.IsResolved),
                        author);
            case ThreadedCommentCardAction.EditReply:
                return replyIndex is { } editIndex &&
                       ThreadedCommentDialogPlanner.TryCreateReplyEditResult(
                           existing, editIndex, text, existing.IsResolved, out var editResult, out _)
                    ? service.PlanThreadedComment(sheetId, existing, editResult, author)
                    : null;
            case ThreadedCommentCardAction.DeleteReply:
                return replyIndex is { } deleteIndex &&
                       ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(
                           existing, deleteIndex, existing.IsResolved, out var deleteResult, out _)
                    ? service.PlanThreadedComment(sheetId, existing, deleteResult, author)
                    : null;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static LocalizedTextDescriptor ToggleLabel(bool isResolved) =>
        LocalizedTextDescriptor.Resource(
            isResolved ? "ThreadedCommentCard_ReopenThread" : "ThreadedCommentCard_ResolveThread");

    private static string Heading(string author, DateTimeOffset? createdAtUtc, DateTimeOffset? now) =>
        ThreadedCommentDialogPlanner.FormatMessageHeading(
            author,
            createdAtUtc,
            ThreadedCommentTimestampProfile.InlineRelativeLocal,
            now);
}
