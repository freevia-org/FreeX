using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Set or replace a cell comment with undo support.</summary>
public sealed class SetCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly string _comment;
    private readonly string _author;
    private bool _hadPrevious;
    private string? _previousComment;
    private bool _hadPreviousAuthor;
    private string? _previousAuthor;

    public string Label => "Set Comment";

    // R91-render-comment-ui-5-3: default author mirrors SetThreadedCommentCommand's own "FreeX"
    // default for a brand-new threaded comment, so a note created via the same production path
    // (WorkbookSession.SetActiveCellNote) is attributed exactly like its threaded-comment sibling.
    public SetCommentCommand(SheetId sheetId, CellAddress address, string comment, string author = "FreeX")
    {
        _sheetId = sheetId;
        _address = address;
        _comment = comment;
        _author = author;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        // R124: a cell that already carries a threaded comment must never also get a legacy
        // Note -- see CommentCommandGuards.RejectIfCellHasThreadedComment for the full rationale.
        // Editing an EXISTING note's own text (the cell already has a Comments entry) is still
        // allowed even if the invariant was somehow already violated by older data, since that
        // path does not create the double-annotation state.
        if (!sheet.Comments.ContainsKey(_address)
            && CommentCommandGuards.RejectIfCellHasThreadedComment(sheet, _address) is { } threadedOutcome)
            return threadedOutcome;

        _hadPrevious = sheet.Comments.TryGetValue(_address, out _previousComment);
        sheet.Comments[_address] = _comment;

        // A brand-new note (no comment previously existed at this address) is auto-attributed to
        // the author, matching real Excel's auto-filled <authors> entry for a freshly inserted
        // legacy note -- both the Notes list and a printed "at end of sheet" comment summary
        // otherwise show a blank author for every note the user creates inside FreeX. Editing an
        // EXISTING note's text must not touch its already-recorded author.
        if (!_hadPrevious)
        {
            _hadPreviousAuthor = sheet.CommentAuthors.TryGetValue(_address, out _previousAuthor);
            sheet.CommentAuthors[_address] = _author;
        }

        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_hadPrevious && _previousComment is not null)
            sheet.Comments[_address] = _previousComment;
        else
            sheet.Comments.Remove(_address);

        if (!_hadPrevious)
        {
            if (_hadPreviousAuthor && _previousAuthor is not null)
                sheet.CommentAuthors[_address] = _previousAuthor;
            else
                sheet.CommentAuthors.Remove(_address);
        }
    }
}

/// <summary>Delete a cell comment with undo support.</summary>
public sealed class DeleteCommentCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private string? _previousComment;
    private bool _hadAuthor;
    private string? _previousAuthor;
    private bool _wasShown;

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

        _hadAuthor = sheet.CommentAuthors.TryGetValue(_address, out _previousAuthor);
        _wasShown = sheet.ShownComments.Contains(_address);

        sheet.Comments.Remove(_address);
        sheet.CommentAuthors.Remove(_address);
        sheet.ShownComments.Remove(_address);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousComment is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.Comments[_address] = _previousComment;
        if (_hadAuthor && _previousAuthor is not null)
            sheet.CommentAuthors[_address] = _previousAuthor;
        if (_wasShown)
            sheet.ShownComments.Add(_address);
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

/// <summary>
/// Convert all legacy notes on a sheet to threaded comments in a single undoable operation.
/// This mirrors Excel 365's Review → Notes → "Convert to Comments" command.
///
/// <para>
/// <b>Behaviour for cells that already have a threaded comment:</b>
/// Such cells are <em>skipped</em> (the legacy note is left in place). Excel's own implementation
/// also leaves pre-existing threaded comments untouched rather than merging or overwriting them.
/// </para>
///
/// <para>
/// For each remaining note the command:
/// <list type="bullet">
///   <item>Creates a threaded comment whose root text equals the note text and whose author is taken
///         from <see cref="Sheet.CommentAuthors"/> (falling back to <c>"FreeX"</c> when absent).</item>
///   <item>Removes the note from <see cref="Sheet.Comments"/>, <see cref="Sheet.CommentAuthors"/>,
///         and <see cref="Sheet.ShownComments"/>.</item>
/// </list>
/// Revert restores all converted notes (text + author + pinned-open state) and removes the
/// threaded comments that were created during Apply.
/// </para>
/// </summary>
public sealed class ConvertNotesToCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly DateTimeOffset? _timestampUtc;

    // Captured during Apply for Revert.
    private List<ConvertedNote>? _converted;

    public string Label => "Convert to Comments";

    public ConvertNotesToCommentsCommand(SheetId sheetId, DateTimeOffset? timestampUtc = null)
    {
        _sheetId = sheetId;
        _timestampUtc = timestampUtc;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (sheet.Comments.Count == 0)
            return new CommandOutcome(false, "There are no notes on this sheet to convert.");

        var timestamp = ThreadedCommentTimestamps.Normalize(_timestampUtc);
        _converted = [];

        foreach (var (addr, noteText) in sheet.Comments.ToList())
        {
            // Skip cells that already have a threaded comment — leave both untouched.
            if (sheet.ThreadedComments.ContainsKey(addr))
                continue;

            var hadAuthor = sheet.CommentAuthors.TryGetValue(addr, out var storedAuthor) && !string.IsNullOrEmpty(storedAuthor);
            var author = hadAuthor ? storedAuthor! : "FreeX";

            var wasShown = sheet.ShownComments.Contains(addr);

            // Remove the legacy note.
            sheet.Comments.Remove(addr);
            sheet.CommentAuthors.Remove(addr);
            sheet.ShownComments.Remove(addr);

            // Create the threaded comment.
            var threaded = ThreadedCommentTimestamps.StampNew(new ThreadedComment(noteText, author), timestamp);
            sheet.ThreadedComments[addr] = threaded;

            _converted.Add(new ConvertedNote(addr, noteText, author, wasShown, hadAuthor));
        }

        if (_converted.Count == 0)
            return new CommandOutcome(false, "All notes already have threaded comments — nothing to convert.");

        var affected = _converted.Select(c => c.Address).ToList();
        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_converted is null || _converted.Count == 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);

        foreach (var note in _converted)
        {
            // Remove the threaded comment created during Apply.
            sheet.ThreadedComments.Remove(note.Address);

            // Restore the legacy note.
            sheet.Comments[note.Address] = note.Text;
            if (note.HadAuthor)
                sheet.CommentAuthors[note.Address] = note.Author;
            if (note.WasShown)
                sheet.ShownComments.Add(note.Address);
        }
    }

    private readonly record struct ConvertedNote(
        CellAddress Address,
        string Text,
        string Author,
        bool WasShown,
        bool HadAuthor);
}

/// <summary>Clear all comments in a range with undo support.</summary>
public sealed class ClearCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private Dictionary<CellAddress, string>? _snapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedSnapshot;
    private Dictionary<CellAddress, string>? _authorSnapshot;
    private HashSet<CellAddress>? _shownSnapshot;

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
        _authorSnapshot = [];
        _shownSnapshot = [];
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

            if (sheet.CommentAuthors.TryGetValue(addr, out var author))
            {
                _authorSnapshot[addr] = author;
                sheet.CommentAuthors.Remove(addr);
            }

            if (sheet.ShownComments.Remove(addr))
                _shownSnapshot.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: _snapshot.Keys.Concat(_threadedSnapshot.Keys).Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null || _threadedSnapshot is null || _authorSnapshot is null || _shownSnapshot is null) return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, comment) in _snapshot)
            sheet.Comments[addr] = comment;
        foreach (var (addr, threadedComment) in _threadedSnapshot)
            sheet.ThreadedComments[addr] = threadedComment;
        foreach (var (addr, author) in _authorSnapshot)
            sheet.CommentAuthors[addr] = author;
        foreach (var addr in _shownSnapshot)
            sheet.ShownComments.Add(addr);
    }
}
