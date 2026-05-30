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

    public SetThreadedCommentCommand(SheetId sheetId, CellAddress address, string text, string author = "FreeX")
    {
        _sheetId = sheetId;
        _address = address;
        _comment = new ThreadedComment(text, author);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
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

/// <summary>Append a reply to an existing threaded comment with undo support.</summary>
public sealed class AddThreadedCommentReplyCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly CommentReply _reply;
    private ThreadedComment? _previous;

    public string Label => "Reply to Comment";

    public AddThreadedCommentReplyCommand(SheetId sheetId, CellAddress address, string text, string author = "FreeX")
    {
        _sheetId = sheetId;
        _address = address;
        _reply = new CommentReply(text, author);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return new CommandOutcome(false, "No threaded comment exists at the selected cell.");
        sheet.ThreadedComments[_address] = _previous with { Replies = [.._previous.Replies, _reply] };
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
    private ThreadedComment? _previous;

    public string Label => "Edit Comment";

    public UpdateThreadedCommentTextCommand(SheetId sheetId, CellAddress address, string text)
    {
        _sheetId = sheetId;
        _address = address;
        _text = text;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return new CommandOutcome(false, "No threaded comment exists at the selected cell.");

        sheet.ThreadedComments[_address] = _previous with { Text = _text };
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ThreadedComments[_address] = _previous;
    }
}

/// <summary>Toggle the resolved state of a threaded comment with undo support.</summary>
public sealed class ResolveThreadedCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly bool _resolved;
    private ThreadedComment? _previous;

    public string Label => _resolved ? "Resolve Comment" : "Unresolve Comment";

    public ResolveThreadedCommentCommand(SheetId sheetId, CellAddress address, bool resolved)
    {
        _sheetId = sheetId;
        _address = address;
        _resolved = resolved;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return new CommandOutcome(false, "No threaded comment exists at the selected cell.");
        sheet.ThreadedComments[_address] = _previous with { IsResolved = _resolved };
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
    private ThreadedComment? _previous;

    public string Label => "Edit Comment";

    public ApplyThreadedCommentChangesCommand(
        SheetId sheetId,
        CellAddress address,
        string? rootText,
        string? replyText,
        bool isResolved,
        string replyAuthor = "FreeX")
    {
        _sheetId = sheetId;
        _address = address;
        _rootText = rootText;
        _replyText = replyText;
        _isResolved = isResolved;
        _replyAuthor = replyAuthor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;
        if (!sheet.ThreadedComments.TryGetValue(_address, out _previous))
            return new CommandOutcome(false, "No threaded comment exists at the selected cell.");

        var updated = _previous;
        var hasChange = false;

        if (_rootText is not null && !string.Equals(_rootText, updated.Text, StringComparison.Ordinal))
        {
            updated = updated with { Text = _rootText };
            hasChange = true;
        }

        if (!string.IsNullOrWhiteSpace(_replyText))
        {
            updated = updated with { Replies = [..updated.Replies, new CommentReply(_replyText, _replyAuthor)] };
            hasChange = true;
        }

        if (updated.IsResolved != _isResolved)
        {
            updated = updated with { IsResolved = _isResolved };
            hasChange = true;
        }

        if (!hasChange)
            return new CommandOutcome(false, "No threaded comment changes were specified.");

        sheet.ThreadedComments[_address] = updated;
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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        if (!sheet.ThreadedComments.TryGetValue(_address, out _previousComment))
            return new CommandOutcome(false, "No threaded comment exists at the selected cell.");

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
