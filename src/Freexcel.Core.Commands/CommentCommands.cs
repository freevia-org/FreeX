using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Set or replace a cell comment with undo support.</summary>
public sealed class SetCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly string _comment;
    private bool _hadPrevious;
    private string? _previousComment;

    public string Label => "Set Comment";

    public SetCommentCommand(SheetId sheetId, CellAddress address, string comment)
    {
        _sheetId = sheetId;
        _address = address;
        _comment = comment;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        _hadPrevious = sheet.Comments.TryGetValue(_address, out _previousComment);
        sheet.Comments[_address] = _comment;
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_hadPrevious && _previousComment is not null)
            sheet.Comments[_address] = _previousComment;
        else
            sheet.Comments.Remove(_address);
    }
}

/// <summary>Set or replace a cell threaded comment with undo support.</summary>
public sealed class SetThreadedCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly ThreadedComment _comment;
    private bool _hadPrevious;
    private ThreadedComment? _previousComment;

    public string Label => "Set Threaded Comment";

    public SetThreadedCommentCommand(SheetId sheetId, CellAddress address, string text, string author = "Freexcel")
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

/// <summary>Delete a cell comment with undo support.</summary>
public sealed class DeleteCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private string? _previousComment;

    public string Label => "Delete Comment";

    public DeleteCommentCommand(SheetId sheetId, CellAddress address)
    {
        _sheetId = sheetId;
        _address = address;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        if (!sheet.Comments.TryGetValue(_address, out _previousComment))
            return new CommandOutcome(false, "No comment exists at the selected cell.");

        sheet.Comments.Remove(_address);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousComment is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.Comments[_address] = _previousComment;
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

/// <summary>Clear all comments in a range with undo support.</summary>
public sealed class ClearCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private Dictionary<CellAddress, string>? _snapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedSnapshot;

    public string Label => "Clear Comments";

    public ClearCommentsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = [];
        _threadedSnapshot = [];
        foreach (var addr in _range.AllCells())
        {
            if (sheet.Comments.TryGetValue(addr, out var comment))
            {
                _snapshot[addr] = comment;
                sheet.Comments.Remove(addr);
            }

            if (sheet.ThreadedComments.TryGetValue(addr, out var threadedComment))
            {
                _threadedSnapshot[addr] = threadedComment;
                sheet.ThreadedComments.Remove(addr);
            }
        }

        return new CommandOutcome(true, AffectedCells: _snapshot.Keys.Concat(_threadedSnapshot.Keys).Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null || _threadedSnapshot is null) return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, comment) in _snapshot)
            sheet.Comments[addr] = comment;
        foreach (var (addr, threadedComment) in _threadedSnapshot)
            sheet.ThreadedComments[addr] = threadedComment;
    }
}
