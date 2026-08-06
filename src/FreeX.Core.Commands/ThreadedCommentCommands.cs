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

        // R124: symmetric to SetCommentCommand's guard -- a cell that already carries a legacy
        // Note must never also get a threaded comment. Editing an EXISTING thread (the cell
        // already has a ThreadedComments entry) is still allowed even if the invariant was
        // somehow already violated by older data, since that path does not create the
        // double-annotation state.
        if (!sheet.ThreadedComments.ContainsKey(_address)
            && CommentCommandGuards.RejectIfCellHasNote(sheet, _address) is { } noteOutcome)
            return noteOutcome;

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

        // Editing the text invalidates any preserved raw @mention metadata: its startIndex/length
        // offsets anchor into the OLD text, so keeping MentionsXml verbatim would point the
        // mention at the wrong (or out-of-range) substring of the new text. Excel itself drops a
        // mention whose text was edited away, so clear it rather than carry stale offsets.
        var updated = _previous with { Text = _text };
        if (!string.Equals(_previous.Text, _text, StringComparison.Ordinal))
            updated = updated with { MentionsXml = null };

        sheet.ThreadedComments[_address] = ThreadedCommentTimestamps.TouchRootTextEdit(updated, _timestampUtc);
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
        var priorReply = replies[_replyIndex];
        var updatedReply = priorReply with { Text = _text };
        // Same rationale as UpdateThreadedCommentTextCommand: a reply's preserved raw @mention
        // metadata anchors into the reply's OLD text, so it must be dropped when the text changes
        // rather than carried forward pointing at the wrong/out-of-range substring.
        if (!string.Equals(priorReply.Text, _text, StringComparison.Ordinal))
            updatedReply = updatedReply with { MentionsXml = null };
        replies[_replyIndex] = ThreadedCommentTimestamps.Touch(updatedReply, _timestampUtc);
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
        var rootTextChanged = false;

        if (_rootText is not null && !string.Equals(_rootText, updated.Text, StringComparison.Ordinal))
        {
            // The root text is changing, so any preserved raw @mention metadata (anchored via
            // startIndex/length into the OLD text) would now point at the wrong or out-of-range
            // substring of the new text. Drop it, matching Excel's own behavior of dropping a
            // mention whose text was edited away.
            updated = updated with { Text = _rootText, MentionsXml = null };
            hasChange = true;
            rootTextChanged = true;
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

        // Only a genuine root-text edit should stamp RootTextEditedAtUtc: a reply being added in
        // this same call is thread activity, not a rewrite of the root's own text, and must not
        // be able to (later) masquerade as one -- see ThreadedCommentTimestamps.TouchRootTextEdit
        // and R35-deferred-comment-edit-timestamp-1.
        sheet.ThreadedComments[_address] = rootTextChanged
            ? ThreadedCommentTimestamps.TouchRootTextEdit(updated, _timestampUtc)
            : ThreadedCommentTimestamps.Touch(updated, _timestampUtc);
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

    /// <summary>
    /// Stamps a genuine edit to the ROOT comment's own text (or a resolved-state change applied
    /// alongside it in the same undoable step), as opposed to <see cref="Touch(ThreadedComment,DateTimeOffset)"/>
    /// which only tracks thread-wide "last activity" (e.g. a reply being added/edited/removed
    /// elsewhere in the thread). This gives the IO mapper an unambiguous
    /// <see cref="ThreadedComment.RootTextEditedAtUtc"/> signal for the persisted root
    /// &lt;threadedComment&gt; dT that cannot later be overwritten by unrelated reply activity
    /// bumping <see cref="ThreadedComment.ModifiedAtUtc"/> (see
    /// XlsxWorksheetThreadedCommentMapper.ResolveRootThreadedCommentTimestamp and
    /// R35-deferred-comment-edit-timestamp-1).
    /// </summary>
    public static ThreadedComment TouchRootTextEdit(ThreadedComment comment, DateTimeOffset timestampUtc) =>
        comment with
        {
            ModifiedAtUtc = timestampUtc,
            RootTextEditedAtUtc = timestampUtc
        };

    public static CommentReply Touch(CommentReply reply, DateTimeOffset timestampUtc) =>
        reply with
        {
            ModifiedAtUtc = timestampUtc
        };
}
