using System.Globalization;
using FreeX.App.Presentation.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

public enum ThreadedCommentDialogAction
{
    ApplyThread,
    EditReply,
    DeleteReply
}

public enum ThreadedCommentDialogValidationError
{
    None,
    EnterComment,
    NoThreadedCommentAvailable,
    SelectReply,
    EnterReply
}

public enum ThreadedCommentDialogFocusTarget
{
    RootComment,
    Reply,
    ReplySelection
}

public enum ThreadedCommentTimestampProfile
{
    AbsoluteUtc,
    InlineRelativeLocal
}

public sealed record ThreadedCommentDialogResult(
    string? RootText,
    string? ReplyText,
    bool IsResolved,
    ThreadedCommentDialogAction Action = ThreadedCommentDialogAction.ApplyThread,
    int? ReplyIndex = null,
    string? ReplyEditText = null);

public sealed record ThreadedCommentReplyPresentationDescriptor(
    string ChoiceText,
    LocalizedTextDescriptor AutomationName);

public static class ThreadedCommentDialogPlanner
{
    public const string ReplySelectorAutomationId = "ThreadedCommentReplySelector";
    public const string SelectedReplyEditorAutomationId = "ThreadedCommentSelectedReplyBox";
    public const string UpdateReplyAutomationId = "ThreadedCommentUpdateReplyButton";
    public const string DeleteReplyAutomationId = "ThreadedCommentDeleteReplyButton";

    public static ValidationPresentationDescriptor<ThreadedCommentDialogFocusTarget>? DescribeValidationError(
        ThreadedCommentDialogValidationError error) =>
        error switch
        {
            ThreadedCommentDialogValidationError.None => null,
            ThreadedCommentDialogValidationError.EnterComment => new(
                LocalizedTextDescriptor.Resource("ThreadedComment_EnterCommentMessage"),
                ThreadedCommentDialogFocusTarget.RootComment),
            ThreadedCommentDialogValidationError.NoThreadedCommentAvailable => new(
                LocalizedTextDescriptor.Resource("ThreadedComment_NoThreadedCommentAvailableMessage"),
                ThreadedCommentDialogFocusTarget.ReplySelection),
            ThreadedCommentDialogValidationError.SelectReply => new(
                LocalizedTextDescriptor.Resource("ThreadedComment_SelectReplyMessage"),
                ThreadedCommentDialogFocusTarget.ReplySelection),
            ThreadedCommentDialogValidationError.EnterReply => new(
                LocalizedTextDescriptor.Resource("ThreadedComment_EnterReplyMessage"),
                ThreadedCommentDialogFocusTarget.Reply),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null)
        };

    public static bool TryCreateResult(
        ThreadedComment? existing,
        string? rootText,
        string? replyText,
        bool isResolved,
        out ThreadedCommentDialogResult result,
        out ThreadedCommentDialogValidationError error)
    {
        result = CreateResult(existing, rootText, replyText, isResolved);
        if (existing is not null && string.IsNullOrWhiteSpace(rootText))
        {
            error = ThreadedCommentDialogValidationError.EnterComment;
            return false;
        }

        if (existing is null && string.IsNullOrWhiteSpace(result.ReplyText))
        {
            error = ThreadedCommentDialogValidationError.EnterComment;
            return false;
        }

        error = ThreadedCommentDialogValidationError.None;
        return true;
    }

    public static bool TryCreateReplyEditResult(
        ThreadedComment? existing,
        int replyIndex,
        string? replyText,
        out ThreadedCommentDialogResult result,
        out ThreadedCommentDialogValidationError error) =>
        TryCreateReplyEditResult(existing, replyIndex, replyText, existing?.IsResolved ?? false, out result, out error);

    public static bool TryCreateReplyEditResult(
        ThreadedComment? existing,
        int replyIndex,
        string? replyText,
        bool isResolved,
        out ThreadedCommentDialogResult result,
        out ThreadedCommentDialogValidationError error)
    {
        result = new ThreadedCommentDialogResult(
            null,
            null,
            isResolved,
            ThreadedCommentDialogAction.EditReply,
            replyIndex,
            (replyText ?? "").Trim());
        if (existing is null)
        {
            error = ThreadedCommentDialogValidationError.NoThreadedCommentAvailable;
            return false;
        }

        if (!IsValidReplyIndex(existing, replyIndex))
        {
            error = ThreadedCommentDialogValidationError.SelectReply;
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.ReplyEditText))
        {
            error = ThreadedCommentDialogValidationError.EnterReply;
            return false;
        }

        error = ThreadedCommentDialogValidationError.None;
        return true;
    }

    public static bool TryCreateReplyDeleteResult(
        ThreadedComment? existing,
        int replyIndex,
        out ThreadedCommentDialogResult result,
        out ThreadedCommentDialogValidationError error) =>
        TryCreateReplyDeleteResult(existing, replyIndex, existing?.IsResolved ?? false, out result, out error);

    public static bool TryCreateReplyDeleteResult(
        ThreadedComment? existing,
        int replyIndex,
        bool isResolved,
        out ThreadedCommentDialogResult result,
        out ThreadedCommentDialogValidationError error)
    {
        result = new ThreadedCommentDialogResult(
            null,
            null,
            isResolved,
            ThreadedCommentDialogAction.DeleteReply,
            replyIndex);
        if (existing is null)
        {
            error = ThreadedCommentDialogValidationError.NoThreadedCommentAvailable;
            return false;
        }

        if (!IsValidReplyIndex(existing, replyIndex))
        {
            error = ThreadedCommentDialogValidationError.SelectReply;
            return false;
        }

        error = ThreadedCommentDialogValidationError.None;
        return true;
    }

    public static ThreadedCommentDialogResult CreateResult(
        ThreadedComment? existing,
        string? rootText,
        string? replyText,
        bool isResolved)
    {
        var trimmedRoot = (rootText ?? "").Trim();
        var trimmedReply = (replyText ?? "").Trim();
        if (existing is null)
        {
            return new ThreadedCommentDialogResult(
                null,
                string.IsNullOrWhiteSpace(trimmedRoot) ? null : trimmedRoot,
                isResolved);
        }

        var rootEdit = !string.IsNullOrWhiteSpace(trimmedRoot)
            && !string.Equals(trimmedRoot, existing.Text, StringComparison.Ordinal)
                ? trimmedRoot
                : null;
        return new ThreadedCommentDialogResult(
            rootEdit,
            string.IsNullOrWhiteSpace(trimmedReply) ? null : trimmedReply,
            isResolved);
    }

    public static bool IsValidReplyIndex(ThreadedComment comment, int replyIndex) =>
        replyIndex >= 0 && replyIndex < comment.Replies.Count;

    public static ThreadedCommentReplyPresentationDescriptor DescribeReply(int index, CommentReply reply) =>
        DescribeReply(index, reply, ThreadedCommentTimestampProfile.AbsoluteUtc);

    public static ThreadedCommentReplyPresentationDescriptor DescribeReply(
        int index,
        CommentReply reply,
        ThreadedCommentTimestampProfile timestampProfile,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var heading = FormatMessageHeading(reply.Author, reply.CreatedAtUtc, timestampProfile, now);
        var summary = SummarizeReplyText(reply.Text);
        var automationText = $"Reply {index + 1} by {heading}: {summary}";
        return new ThreadedCommentReplyPresentationDescriptor(
            $"{index + 1}. {heading}: {summary}",
            timestampProfile == ThreadedCommentTimestampProfile.InlineRelativeLocal
                ? LocalizedTextDescriptor.Literal(automationText)
                : LocalizedTextDescriptor.Resource(
                    "ThreadedComment_ReplyAutomationNameFormat",
                    index + 1,
                    heading,
                    summary));
    }

    public static string FormatReplyChoice(int index, CommentReply reply) =>
        DescribeReply(index, reply).ChoiceText;

    public static string SummarizeReplyText(string text)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 60 ? normalized : normalized[..57] + "...";
    }

    public static string FormatMessageHeading(string author, DateTimeOffset? createdAtUtc) =>
        FormatMessageHeading(
            author,
            createdAtUtc,
            ThreadedCommentTimestampProfile.AbsoluteUtc);

    public static string FormatMessageHeading(
        string author,
        DateTimeOffset? createdAtUtc,
        ThreadedCommentTimestampProfile timestampProfile,
        DateTimeOffset? now = null)
    {
        var label = author.Trim();
        if (createdAtUtc is null)
            return label;

        var formatted = timestampProfile switch
        {
            ThreadedCommentTimestampProfile.AbsoluteUtc => createdAtUtc.Value
                .ToUniversalTime()
                .ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            ThreadedCommentTimestampProfile.InlineRelativeLocal => FormatRelativeLocalTimestamp(
                createdAtUtc.Value,
                now ?? DateTimeOffset.Now),
            _ => throw new ArgumentOutOfRangeException(nameof(timestampProfile), timestampProfile, null)
        };
        return string.IsNullOrWhiteSpace(label)
            ? formatted
            : $"{label} - {formatted}";
    }

    private static string FormatRelativeLocalTimestamp(DateTimeOffset createdAtUtc, DateTimeOffset now)
    {
        var local = createdAtUtc.ToLocalTime();
        var nowLocal = now.ToLocalTime();
        var age = nowLocal - local;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes}m";

        var timeOfDay = local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (local.Date == nowLocal.Date)
            return $"Today, {timeOfDay}";
        if (local.Date == nowLocal.Date.AddDays(-1))
            return $"Yesterday, {timeOfDay}";

        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
