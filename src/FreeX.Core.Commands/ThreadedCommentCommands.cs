using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Set or replace a cell threaded comment with undo support.</summary>
public sealed class SetThreadedCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly ThreadedComment _comment;
    private bool _hadPrevious;
    private ThreadedComment? _previousComment;

    public string Label => "Set Threaded Comment";

    public SetThreadedCommentCommand(
        SheetId sheetId,
        CellAddress address,
        string text,
        string author = "FreeX",
        DateTimeOffset? timestampUtc = null)
    {
        var timestamp = ThreadedCommentTimestamps.Normalize(timestampUtc);
        _sheetId = sheetId;
        _address = address;
        _comment = ThreadedCommentTimestamps.StampNew(new ThreadedComment(text, author), timestamp);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _hadPrevious = sheet.ThreadedComments.TryGetValue(_address, out _previousComment);
        sheet.ThreadedComments[_address] = _comment;
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_hadPrevious && _previousComment is not null)
            sheet.ThreadedComments[_address] = _previousComment;
        else
            sheet.ThreadedComments.Remove(_address);
    }
}

/// <summary>
/// Append a reply to an existing threaded comment with undo support.
/// </summary>
/// <remarks>
/// This is the reply-only counterpart to <see cref="ApplyThreadedCommentChangesCommand"/> (which
/// additionally supports editing the root comment text and toggling resolved state in one
/// undoable step). Production reply-adding UI (WPF and Avalonia) currently goes through
/// <see cref="ApplyThreadedCommentChangesCommand"/> with only <c>replyText</c> set, not through
/// this class directly — so before changing reply-authoring/timestamp-stamping behavior here,
/// check whether <see cref="ApplyThreadedCommentChangesCommand"/>'s reply branch needs the same
/// fix, since that is the path actually exercised by the app today. Kept as a standalone,
/// separately undoable command for callers (e.g. future reply-only UI/automation entry points)
/// that don't want to touch the root comment or resolved state at all.
/// </remarks>
public sealed class AddThreadedCommentReplyCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly CommentReply _reply;
    private readonly DateTimeOffset _timestampUtc;
    private ThreadedComment? _previous;

    public string Label => "Reply to Comment";

    public AddThreadedCommentReplyCommand(
        SheetId sheetId,
        CellAddress address,
        string text,
        string author = "FreeX",
        DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _address = address;
        _timestampUtc = ThreadedCommentTimestamps.Normalize(timestampUtc);
        _reply = ThreadedCommentTimestamps.StampNew(new CommentReply(text, author), _timestampUtc);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return CommentCommandGuards.ThreadedCommentNotFound();
        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.Touch(
            _previous with { Replies = [.._previous.Replies, _reply] },
            _timestampUtc);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }
}

/// <summary>Edit the root text of an existing threaded comment with undo support.</summary>
public sealed class UpdateThreadedCommentTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly string _text;
    private readonly DateTimeOffset _timestampUtc;
    private ThreadedComment? _previous;

    public string Label => "Edit Comment";

    public UpdateThreadedCommentTextCommand(
        SheetId sheetId,
        CellAddress address,
        string text,
        DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _address = address;
        _text = text;
        _timestampUtc = ThreadedCommentTimestamps.Normalize(timestampUtc);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return CommentCommandGuards.ThreadedCommentNotFound();

        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.Touch(_previous with { Text = _text }, _timestampUtc);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }
}

/// <summary>Edit an existing threaded comment reply with undo support.</summary>
public sealed class UpdateThreadedCommentReplyCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly int _replyIndex;
    private readonly string _text;
    private readonly bool? _isResolved;
    private readonly DateTimeOffset _timestampUtc;
    private ThreadedComment? _previous;

    public string Label => "Edit Comment Reply";

    public UpdateThreadedCommentReplyCommand(
        SheetId sheetId,
        CellAddress address,
        int replyIndex,
        string text,
        bool? isResolved = null,
        DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _address = address;
        _replyIndex = replyIndex;
        _text = text;
        _isResolved = isResolved;
        _timestampUtc = ThreadedCommentTimestamps.Normalize(timestampUtc);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return CommentCommandGuards.ThreadedCommentNotFound();
        if (!IsValidReplyIndex(_previous, _replyIndex))
            return CommentCommandGuards.ThreadedCommentReplyNotFound();

        var replies = _previous.Replies.ToList();
        replies[_replyIndex] = ThreadedCommentTimestamps.Touch(replies[_replyIndex] with { Text = _text }, _timestampUtc);
        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.Touch(
            _previous with
            {
                Replies = replies,
                IsResolved = _isResolved ?? _previous.IsResolved
            },
            _timestampUtc);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }

    private static bool IsValidReplyIndex(ThreadedComment comment, int replyIndex) =>
        replyIndex >= 0 && replyIndex < comment.Replies.Count;
}

/// <summary>Delete an existing threaded comment reply with undo support.</summary>
public sealed class DeleteThreadedCommentReplyCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly int _replyIndex;
    private readonly bool? _isResolved;
    private readonly DateTimeOffset _timestampUtc;
    private ThreadedComment? _previous;

    public string Label => "Delete Comment Reply";

    public DeleteThreadedCommentReplyCommand(
        SheetId sheetId,
        CellAddress address,
        int replyIndex,
        bool? isResolved = null,
        DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _address = address;
        _replyIndex = replyIndex;
        _isResolved = isResolved;
        _timestampUtc = ThreadedCommentTimestamps.Normalize(timestampUtc);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return CommentCommandGuards.ThreadedCommentNotFound();
        if (!IsValidReplyIndex(_previous, _replyIndex))
            return CommentCommandGuards.ThreadedCommentReplyNotFound();

        var replies = _previous.Replies.ToList();
        replies.RemoveAt(_replyIndex);
        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.Touch(
            _previous with
            {
                Replies = replies,
                IsResolved = _isResolved ?? _previous.IsResolved
            },
            _timestampUtc);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }

    private static bool IsValidReplyIndex(ThreadedComment comment, int replyIndex) =>
        replyIndex >= 0 && replyIndex < comment.Replies.Count;
}

/// <summary>Toggle the resolved state of a threaded comment with undo support.</summary>
public sealed class ResolveThreadedCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly bool _resolved;
    private readonly DateTimeOffset _timestampUtc;
    private ThreadedComment? _previous;

    public string Label => _resolved ? "Resolve Comment" : "Unresolve Comment";

    public ResolveThreadedCommentCommand(
        SheetId sheetId,
        CellAddress address,
        bool resolved,
        DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _address = address;
        _resolved = resolved;
        _timestampUtc = ThreadedCommentTimestamps.Normalize(timestampUtc);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return CommentCommandGuards.ThreadedCommentNotFound();
        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.Touch(_previous with { IsResolved = _resolved }, _timestampUtc);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }
}

/// <summary>Apply an existing threaded comment edit, optional reply, and resolved state as one undoable operation.</summary>
public sealed class ApplyThreadedCommentChangesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly string? _rootText;
    private readonly string? _replyText;
    private readonly string _replyAuthor;
    private readonly bool _isResolved;
    private readonly DateTimeOffset _timestampUtc;
    private ThreadedComment? _previous;

    public string Label => "Edit Comment";

    public ApplyThreadedCommentChangesCommand(
        SheetId sheetId,
        CellAddress address,
        string? rootText,
        string? replyText,
        bool isResolved,
        string replyAuthor = "FreeX",
        DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _address = address;
        _rootText = rootText;
        _replyText = replyText;
        _isResolved = isResolved;
        _replyAuthor = replyAuthor;
        _timestampUtc = ThreadedCommentTimestamps.Normalize(timestampUtc);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return CommentCommandGuards.ThreadedCommentNotFound();

        var updated = _previous;
        var hasChange = false;

        if (_rootText is not null && !string.Equals(_rootText, updated.Text, StringComparison.Ordinal))
        {
            updated = updated with { Text = _rootText };
            hasChange = true;
        }

        if (!string.IsNullOrWhiteSpace(_replyText))
        {
            var reply = ThreadedCommentTimestamps.StampNew(new CommentReply(_replyText, _replyAuthor), _timestampUtc);
            updated = updated with { Replies = [..updated.Replies, reply] };
            hasChange = true;
        }

        if (updated.IsResolved != _isResolved)
        {
            updated = updated with { IsResolved = _isResolved };
            hasChange = true;
        }

        if (!hasChange)
            return new CommandOutcome(false, "No threaded comment changes were specified.");

        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.Touch(updated, _timestampUtc);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }
}

/// <summary>Delete a cell threaded comment with undo support.</summary>
public sealed class DeleteThreadedCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private ThreadedComment? _previousComment;

    public string Label => "Delete Threaded Comment";

    public DeleteThreadedCommentCommand(SheetId sheetId, CellAddress address)
    {
        _sheetId = sheetId;
        _address = address;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!sheet.ThreadedComments.TryGetValue(_address, out _previousComment))
            return CommentCommandGuards.ThreadedCommentNotFound();

        sheet.ThreadedComments.Remove(_address);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousComment is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previousComment;
    }
}

internal static class ThreadedCommentTimestamps
{
    public static DateTimeOffset Normalize(DateTimeOffset? timestampUtc) =>
        (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

    public static ThreadedComment StampNew(ThreadedComment comment, DateTimeOffset timestampUtc) =>
        comment with
        {
            CreatedAtUtc = comment.CreatedAtUtc ?? timestampUtc,
            ModifiedAtUtc = timestampUtc
        };

    public static CommentReply StampNew(CommentReply reply, DateTimeOffset timestampUtc) =>
        reply with
        {
            CreatedAtUtc = reply.CreatedAtUtc ?? timestampUtc,
            ModifiedAtUtc = timestampUtc
        };

    public static ThreadedComment Touch(ThreadedComment comment, DateTimeOffset timestampUtc) =>
        comment with
        {
            ModifiedAtUtc = timestampUtc
        };

    public static CommentReply Touch(CommentReply reply, DateTimeOffset timestampUtc) =>
        reply with
        {
            ModifiedAtUtc = timestampUtc
        };
}
