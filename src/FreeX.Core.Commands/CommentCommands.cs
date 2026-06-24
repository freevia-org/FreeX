using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
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
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
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

/// <summary>
/// Toggle the "Show Comment" pinned-box state for a single cell's legacy note.
/// Has no effect if the cell does not have a legacy note.
/// </summary>
public sealed class ShowHideCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private bool _wasShown;

    public string Label => "Show/Hide Comment";

    public ShowHideCommentCommand(SheetId sheetId, CellAddress address)
    {
        _sheetId = sheetId;
        _address = address;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!sheet.Comments.ContainsKey(_address))
            return new CommandOutcome(false, "No note exists at the selected cell.");

        _wasShown = sheet.ShownComments.Contains(_address);
        if (_wasShown)
            sheet.ShownComments.Remove(_address);
        else
            sheet.ShownComments.Add(_address);

        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_wasShown)
            sheet.ShownComments.Add(_address);
        else
            sheet.ShownComments.Remove(_address);
    }
}

/// <summary>
/// Show all legacy notes on the sheet (adds every note address to <see cref="Sheet.ShownComments"/>),
/// or hide all if every note is already pinned (toggles all off).
/// </summary>
public sealed class ShowAllNotesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private HashSet<CellAddress>? _snapshot;

    public string Label => "Show All Notes";

    public ShowAllNotesCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        // Snapshot current state for undo.
        _snapshot = new HashSet<CellAddress>(sheet.ShownComments);

        var allAddresses = sheet.Comments.Keys.ToList();
        if (allAddresses.Count == 0)
            return new CommandOutcome(false, "There are no notes on this sheet.");

        // If every note is already shown, hide all; otherwise show all.
        var allShown = allAddresses.All(a => sheet.ShownComments.Contains(a));
        sheet.ShownComments.Clear();
        if (!allShown)
        {
            foreach (var addr in allAddresses)
                sheet.ShownComments.Add(addr);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.ShownComments.Clear();
        foreach (var addr in _snapshot)
            sheet.ShownComments.Add(addr);
    }
}

/// <summary>Clear all comments in a range with undo support.</summary>
public sealed class ClearCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private Dictionary<CellAddress, string>? _snapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedSnapshot;

    public string Label => "Clear Comments and Notes";

    public ClearCommentsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
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
