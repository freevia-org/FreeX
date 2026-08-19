using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
    private readonly bool _transpose;
    private readonly IReadOnlyList<GridRange>? _sourceAreas;
    private Dictionary<CellAddress, string?>? _previous;
    private Dictionary<CellAddress, ThreadedComment?>? _previousThreaded;
    // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
    // author + pinned/"Show Comment" state) and must be captured/copied/restored in lockstep
    // with it, or a pasted note's author/pinned box is left missing at the destination.
    private Dictionary<CellAddress, string?>? _previousAuthors;
    private Dictionary<CellAddress, bool>? _previousShown;

    public string Label => "Paste Comments";

    // R78-commands-paste-special-5-3: `sourceAreas`, when supplied with more than one area,
    // records every individually Ctrl+clicked area of a multi-area source selection (mirroring
    // InternalClipboard.SourceAreas in MainWindow.ClipboardCommands.cs). `sourceRange` remains
    // only the BOUNDING BOX of those areas, so without this, a comment sitting in the gap between
    // disjoint areas (never part of the selection) would still be treated as "copied" and leaked
    // onto the destination.
    public PasteCommentsCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose, IReadOnlyList<GridRange>? sourceAreas = null)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
        _sourceAreas = sourceAreas is { Count: > 1 } ? sourceAreas : null;
    }

    // R34-commands-paste-special-3-2: when the caller knows the full destination selection (not
    // just its top-left anchor), this overload lets the paste tile the copied comment(s) across
    // every whole repeat of the source range that fits the selection -- mirroring how
    // PasteCommandFactory.CreateInternalPasteCommand tiles Values/Formulas/Formats/All onto a
    // destination selection that is a whole multiple of the copied range, instead of only ever
    // filling the selection's first (top-left) cell.
    public PasteCommentsCommand(SheetId sheetId, GridRange sourceRange, GridRange destinationRange, bool transpose, IReadOnlyList<GridRange>? sourceAreas = null)
        : this(sheetId, sourceRange, destinationRange.Start, transpose, sourceAreas)
    {
        _destinationRange = destinationRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste comments source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommentCommandGuards.RejectIfEditObjectsBlocked(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        // P114: pre-materialize author/shown state (like the comment text below) BEFORE the
        // mutation loop runs. When source == target (same-sheet paste) with an overlapping
        // source/destination range, reading sourceSheet.CommentAuthors/ShownComments LIVE inside
        // the loop would observe values already overwritten by an earlier iteration instead of
        // each note's own original author/pinned state.
        var sourceComments = EnumerateSourceCells()
            .Where(sourceSheet.Comments.ContainsKey)
            .Select(address => (
                Address: address,
                Comment: sourceSheet.Comments[address],
                Author: sourceSheet.CommentAuthors.TryGetValue(address, out var author) ? author : null,
                Shown: sourceSheet.ShownComments.Contains(address)))
            .ToList();
        var sourceThreadedComments = EnumerateSourceCells()
            .Where(sourceSheet.ThreadedComments.ContainsKey)
            .Select(address => (Address: address, Comment: sourceSheet.ThreadedComments[address]))
            .ToList();
        _previous = [];
        _previousThreaded = [];
        _previousAuthors = [];
        _previousShown = [];
        var affected = new List<CellAddress>();
        foreach (var tileAnchor in EnumerateTileAnchors())
        {
            foreach (var (source, comment, author, shown) in sourceComments)
            {
                var destination = MapDestination(source, _sourceRange, tileAnchor, _transpose);

                // R124-paste-note-vs-thread-parity: real Excel (and every direct-authoring path
                // here -- SetCommentCommand/SetThreadedCommentCommand via CommentCommandGuards)
                // never lets a cell carry both a legacy Note and a threaded Comment. A paste is
                // not "add another annotation kind", it is "make the destination match the
                // copied source", so pasting a Note here must first clear whatever threaded
                // comment thread already sits at the destination rather than unioning with it.
                if (targetSheet.ThreadedComments.TryGetValue(destination, out var oldThreaded))
                {
                    _previousThreaded[destination] = CloneThreadedComment(oldThreaded);
                    targetSheet.ThreadedComments.Remove(destination);
                }

                _previous[destination] = targetSheet.Comments.TryGetValue(destination, out var oldComment)
                    ? oldComment
                    : null;
                targetSheet.Comments[destination] = comment;

                _previousAuthors[destination] = targetSheet.CommentAuthors.TryGetValue(destination, out var oldAuthor)
                    ? oldAuthor
                    : null;
                if (author is not null)
                    targetSheet.CommentAuthors[destination] = author;
                else
                    targetSheet.CommentAuthors.Remove(destination);

                _previousShown[destination] = targetSheet.ShownComments.Contains(destination);
                if (shown)
                    targetSheet.ShownComments.Add(destination);
                else
                    targetSheet.ShownComments.Remove(destination);

                affected.Add(destination);
            }

            foreach (var (source, comment) in sourceThreadedComments)
            {
                var destination = MapDestination(source, _sourceRange, tileAnchor, _transpose);

                // R124-paste-note-vs-thread-parity: symmetric to the Note loop above -- pasting a
                // threaded comment must first clear any legacy Note (and its author/pinned state)
                // already at the destination, so the two annotation kinds never coexist here.
                if (targetSheet.Comments.TryGetValue(destination, out var oldNote))
                {
                    _previous[destination] = oldNote;
                    targetSheet.Comments.Remove(destination);

                    _previousAuthors[destination] = targetSheet.CommentAuthors.TryGetValue(destination, out var oldAuthor)
                        ? oldAuthor
                        : null;
                    targetSheet.CommentAuthors.Remove(destination);

                    _previousShown[destination] = targetSheet.ShownComments.Contains(destination);
                    targetSheet.ShownComments.Remove(destination);
                }

                _previousThreaded[destination] = targetSheet.ThreadedComments.TryGetValue(destination, out var oldComment)
                    ? CloneThreadedComment(oldComment)
                    : null;
                targetSheet.ThreadedComments[destination] = ClonedThreadedCommentForNewAddress(comment);
                affected.Add(destination);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected.Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null || _previousThreaded is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, comment) in _previous)
        {
            if (comment is null)
                sheet.Comments.Remove(address);
            else
                sheet.Comments[address] = comment;
        }

        foreach (var (address, comment) in _previousThreaded)
        {
            if (comment is null)
                sheet.ThreadedComments.Remove(address);
            else
                sheet.ThreadedComments[address] = CloneThreadedComment(comment);
        }

        if (_previousAuthors is not null)
        {
            foreach (var (address, author) in _previousAuthors)
            {
                if (author is null)
                    sheet.CommentAuthors.Remove(address);
                else
                    sheet.CommentAuthors[address] = author;
            }
        }

        if (_previousShown is not null)
        {
            foreach (var (address, wasShown) in _previousShown)
            {
                if (wasShown)
                    sheet.ShownComments.Add(address);
                else
                    sheet.ShownComments.Remove(address);
            }
        }
    }

    private static ThreadedComment CloneThreadedComment(ThreadedComment comment) =>
        comment with { Replies = comment.Replies.ToList() };

    // R80-io-comments-threaded-5-1: pasting a threaded comment onto a new destination cell must
    // NOT carry over the source's persisted Id or reply Ids -- unlike CloneThreadedComment above
    // (used only to snapshot/restore a cell's own pre-existing comment in place for undo), this
    // creates a brand-new, independent thread at the destination. Otherwise the pasted thread's
    // root serializes with the identical <threadedComment id="..."> as the source
    // (XlsxWorksheetThreadedCommentMapper.ToThreadedCommentElements reuses comment.Id verbatim),
    // and reply lookup on reload (grouped globally by parentId string, not scoped per cell)
    // attaches the source's replies to the pasted thread too. Clearing Id (and each reply's Id)
    // forces the writer to mint a fresh, address-derived stable guid for the pasted thread
    // instead. Mirrors CopyRangeCommand.ClonedThreadedCommentForNewAddress.
    private static ThreadedComment ClonedThreadedCommentForNewAddress(ThreadedComment comment) =>
        comment with
        {
            Id = null,
            Replies = comment.Replies.Select(reply => reply with { Id = null }).ToList(),
        };

    // R78-commands-paste-special-5-3: when _sourceAreas records a multi-area (Ctrl+click) source,
    // only cells that fall inside one of the ACTUAL copied areas count as "copied" -- a comment
    // living in the gap between disjoint areas must never be picked up. With no (or a single) area
    // recorded, this is unchanged from iterating the whole bounding box.
    private IEnumerable<CellAddress> EnumerateSourceCells() =>
        _sourceAreas is { } areas
            ? areas.SelectMany(area => area.AllCells()).Distinct()
            : _sourceRange.AllCells();

    // R34-commands-paste-special-3-2: when the constructor was given the full destination
    // selection (not just its top-left anchor) and that selection is larger than the copied
    // source range in either dimension, repeat the paste at every whole tile of the source range
    // that fits -- exactly mirroring PasteCommandFactory.CreateInternalPasteCommand's
    // shouldTileDestinationRange/CreateTiledInternalPasteCommand period-based tiling. A trailing
    // partial tile (selection size not an exact multiple of the source range) is left untouched,
    // matching that same tiling behavior. When no destination range was supplied, or the
    // selection is no larger than the source range, this yields just the single anchor cell so
    // the original (non-tiled) behavior is unchanged.
    private IEnumerable<CellAddress> EnumerateTileAnchors()
    {
        if (_destinationRange is not { } destinationRange)
        {
            yield return _destination;
            yield break;
        }

        var pasteRows = _transpose ? _sourceRange.ColCount : _sourceRange.RowCount;
        var pasteCols = _transpose ? _sourceRange.RowCount : _sourceRange.ColCount;
        var targetRows = destinationRange.RowCount;
        var targetCols = destinationRange.ColCount;

        if (targetRows <= pasteRows && targetCols <= pasteCols)
        {
            yield return destinationRange.Start;
            yield break;
        }

        for (var rowOffset = 0U; rowOffset + pasteRows <= targetRows; rowOffset += pasteRows)
        {
            for (var colOffset = 0U; colOffset + pasteCols <= targetCols; colOffset += pasteCols)
            {
                yield return new CellAddress(
                    destinationRange.Start.Sheet,
                    destinationRange.Start.Row + rowOffset,
                    destinationRange.Start.Col + colOffset);
            }
        }
    }

    private static CellAddress MapDestination(
        CellAddress source,
        GridRange sourceRange,
        CellAddress destination,
        bool transpose)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return transpose
            ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
            : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
    }
}
