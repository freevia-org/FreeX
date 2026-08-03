using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Copies a range's cell payload (values, formulas, styles, comments, hyperlinks and rich text)
/// to a destination, leaving the source untouched. This is the non-destructive counterpart to
/// <see cref="MoveRangeCommand"/> -- it backs Excel's Ctrl+drag-to-copy gesture, where dragging a
/// selection's border normally moves it but Ctrl+drag copies it instead.
///
/// Formula references in the copied cells are adjusted exactly like a normal copy/paste (relative
/// references shift by the row/col delta, absolute ($) references do not) via
/// <see cref="FormulaRewriter"/>'s <see cref="PasteOffsetOp"/>. Unlike a move, no other cell's
/// formulas need to be rewritten to keep pointing at the source, because the source range is left
/// in place -- so undo only needs to restore the destination cells.
/// </summary>
public sealed class CopyRangeCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: CaptureCellSnapshots below captures a full CellSnapshot
    // (Cell clone + style + comment + author + shown-flag + threaded comment + hyperlink/metadata +
    // rich text + phonetic guide -- even richer than PasteCellsCommand's shape, see the CellSnapshot
    // record) for EVERY destination cell, so its footprint scales with _sourceRange.CellCount, not a
    // flat per-command constant. Without this, CommandBus's 50 MB undo byte-budget bills every copy
    // at the 200-byte IEstimatesMemory default regardless of size, so a large copy/paste-drag never
    // trips the budget and only the 100-entry depth cap bounds the undo stack.
    private const int BytesPerCell = 400;

    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private List<CellSnapshot>? _snapshot;
    // R32-commands-clipboard-deep-2: merges cloned at the destination (translated copies of any
    // merge fully contained in _sourceRange), so Revert can remove exactly the ones this Apply
    // added without disturbing any pre-existing merge that happened to already sit there.
    private List<GridRange>? _addedMergedRegions;

    public string Label => "Copy Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_sourceRange.CellCount * BytesPerCell, int.MaxValue);

    public CopyRangeCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sheetId ||
            _sourceRange.End.Sheet != _sheetId ||
            _destination.Sheet != _sheetId)
        {
            return new CommandOutcome(false, "Copy source and destination must be on the target sheet.");
        }

        if (!WorksheetBounds.IsValidAddress(_sourceRange.Start) ||
            !WorksheetBounds.IsValidAddress(_sourceRange.End) ||
            !WorksheetBounds.IsValidAddress(_destination))
        {
            return new CommandOutcome(false, "Copy range is outside the worksheet bounds.");
        }

        if (!WorksheetBounds.TryGetRectangleEnd(
                _destination,
                _sourceRange.RowCount,
                _sourceRange.ColCount,
                out var targetEnd))
        {
            return new CommandOutcome(false, "Copy destination range is outside the worksheet bounds.");
        }

        var targetRange = new GridRange(_destination, targetEnd);
        if (targetRange == _sourceRange)
        {
            _affectedCells = [];
            _snapshot = [];
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        var sheet = ctx.GetSheet(_sheetId);
        // R32-commands-clipboard-deep-2: a merge fully contained in _sourceRange is the merge being
        // copied (a translated clone of it is added at the destination below), not a collision --
        // excluding it here is what lets Excel's ordinary Ctrl+drag-copy of a merged cell onto an
        // empty destination succeed instead of being rejected against its own source-range
        // self-overlap. Any OTHER merge (only partially overlapping the source, or already sitting
        // anywhere in the target) is still a real collision and remains rejected.
        var copiedMerges = sheet.MergedRegions.Where(range => _sourceRange.Contains(range)).ToList();
        if (sheet.MergedRegions.Any(range => !copiedMerges.Contains(range) && (_sourceRange.Overlaps(range) || targetRange.Overlaps(range))))
            return new CommandOutcome(false, "Cannot copy a range that intersects merged cells.");

        var destinationCells = targetRange.AllCells().ToList();
        if (sheet.IsProtected)
        {
            foreach (var address in destinationCells)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return CommandGuards.RejectSheetProtected();
            }

            // O47 (meta of N56): a destination cell's pre-existing comment is unconditionally
            // removed/overwritten by WritePayload even when the corresponding source cell has no
            // comment, so the guard must cover destination cells too, not just the source range.
            if ((HasComments(sheet, _sourceRange.AllCells()) || HasComments(sheet, destinationCells)) &&
                !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditObjects))
            {
                return CommandGuards.RejectSheetProtected();
            }
        }

        if (CommandGuards.RejectIfSplitsArray(sheet, destinationCells) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = CaptureCellSnapshots(sheet, destinationCells);

        var activeSheetName = sheet.Name;
        var rowDelta = checked((int)((long)_destination.Row - _sourceRange.Start.Row));
        var colDelta = checked((int)((long)_destination.Col - _sourceRange.Start.Col));
        var pasteOp = new PasteOffsetOp(rowDelta, colDelta);

        var payloads = CaptureSourcePayloads(sheet, _sourceRange, _destination, pasteOp, activeSheetName);
        foreach (var payload in payloads)
            WritePayload(sheet, payload);

        if (copiedMerges.Count > 0)
        {
            _addedMergedRegions = copiedMerges
                .Select(range => TranslateRange(range, rowDelta, colDelta))
                .ToList();
            sheet.ReplaceMergedRegions(sheet.MergedRegions.Concat(_addedMergedRegions));
        }

        _affectedCells = destinationCells;
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (_addedMergedRegions is { Count: > 0 })
        {
            var added = _addedMergedRegions;
            sheet.ReplaceMergedRegions(sheet.MergedRegions.Where(range => !added.Contains(range)));
        }

        foreach (var snapshot in _snapshot)
            RestoreCellSnapshot(sheet, snapshot);
    }

    private static List<CopyPayload> CaptureSourcePayloads(
        Sheet sheet,
        GridRange sourceRange,
        CellAddress destination,
        PasteOffsetOp pasteOp,
        string activeSheetName)
    {
        var payloads = new List<CopyPayload>(GetSafeListCapacity(sourceRange.CellCount));
        var rowDelta = (long)destination.Row - sourceRange.Start.Row;
        var colDelta = (long)destination.Col - sourceRange.Start.Col;

        foreach (var source in sourceRange.AllCells())
        {
            var target = new CellAddress(
                destination.Sheet,
                checked((uint)(source.Row + rowDelta)),
                checked((uint)(source.Col + colDelta)));

            var cell = sheet.GetCell(source)?.Clone();
            if (cell?.FormulaText is { } formulaText)
            {
                cell.FormulaText = FormulaRewriter.Rewrite(formulaText, pasteOp, activeSheetName)
                    ?? formulaText;
            }

            payloads.Add(new CopyPayload(
                target,
                cell,
                sheet.GetStyleOnly(source.Row, source.Col),
                sheet.Comments.TryGetValue(source, out var comment) ? comment : null,
                sheet.CommentAuthors.TryGetValue(source, out var commentAuthor) ? commentAuthor : null,
                sheet.ShownComments.Contains(source),
                sheet.ThreadedComments.TryGetValue(source, out var threadedComment)
                    ? ClonedThreadedCommentForNewAddress(threadedComment)
                    : null,
                sheet.Hyperlinks.TryGetValue(source, out var hyperlink) ? hyperlink : null,
                sheet.HyperlinkMetadata.TryGetValue(source, out var metadata) ? metadata : null,
                sheet.RichTextRuns.TryGetValue(source, out var richRuns) ? richRuns : null,
                sheet.CellPhoneticGuides.TryGetValue(source, out var phoneticGuide) ? phoneticGuide : null));
        }

        return payloads;
    }

    private static List<CellSnapshot> CaptureCellSnapshots(Sheet sheet, IReadOnlyList<CellAddress> addresses)
    {
        var snapshots = new List<CellSnapshot>(addresses.Count);
        foreach (var address in addresses)
        {
            snapshots.Add(new CellSnapshot(
                address,
                sheet.GetCell(address)?.Clone(),
                sheet.GetStyleOnly(address.Row, address.Col),
                sheet.Comments.TryGetValue(address, out var comment) ? comment : null,
                sheet.CommentAuthors.TryGetValue(address, out var commentAuthor) ? commentAuthor : null,
                sheet.ShownComments.Contains(address),
                sheet.ThreadedComments.TryGetValue(address, out var threadedComment)
                    ? CloneThreadedComment(threadedComment)
                    : null,
                sheet.Hyperlinks.TryGetValue(address, out var hyperlink) ? hyperlink : null,
                sheet.HyperlinkMetadata.TryGetValue(address, out var metadata) ? metadata : null,
                sheet.RichTextRuns.TryGetValue(address, out var richRuns) ? richRuns : null,
                sheet.CellPhoneticGuides.TryGetValue(address, out var phoneticGuide) ? phoneticGuide : null));
        }

        return snapshots;
    }

    private static void WritePayload(Sheet sheet, CopyPayload payload)
    {
        if (payload.Cell is not null)
        {
            sheet.SetCell(payload.Target, payload.Cell.Clone());
        }
        else
        {
            sheet.ClearCell(payload.Target);
            if (payload.StyleOnly.HasValue)
                sheet.SetStyleOnly(payload.Target.Row, payload.Target.Col, payload.StyleOnly.Value);
            else
                sheet.ClearStyleOnly(payload.Target.Row, payload.Target.Col);
        }

        if (payload.Comment is not null)
            sheet.Comments[payload.Target] = payload.Comment;
        else
            sheet.Comments.Remove(payload.Target);

        if (payload.CommentAuthor is not null)
            sheet.CommentAuthors[payload.Target] = payload.CommentAuthor;
        else
            sheet.CommentAuthors.Remove(payload.Target);

        if (payload.CommentShown)
            sheet.ShownComments.Add(payload.Target);
        else
            sheet.ShownComments.Remove(payload.Target);

        if (payload.ThreadedComment is not null)
            sheet.ThreadedComments[payload.Target] = CloneThreadedComment(payload.ThreadedComment);
        else
            sheet.ThreadedComments.Remove(payload.Target);

        if (payload.Hyperlink is not null)
            sheet.Hyperlinks[payload.Target] = payload.Hyperlink;
        else
            sheet.Hyperlinks.Remove(payload.Target);

        if (payload.HyperlinkMetadata is not null)
            sheet.HyperlinkMetadata[payload.Target] = payload.HyperlinkMetadata;
        else
            sheet.HyperlinkMetadata.Remove(payload.Target);

        if (payload.RichTextRuns is not null)
            sheet.RichTextRuns[payload.Target] = payload.RichTextRuns;
        else
            sheet.RichTextRuns.Remove(payload.Target);

        // R78-selfreg-twin-sweep-5: a phonetic guide (furigana) is RichTextRuns' companion for the
        // same cell and must be carried/cleared alongside it at the paste target, or a copied
        // furigana cell arrives with correct run formatting but a silently dropped guide.
        if (payload.PhoneticGuide is not null)
            sheet.CellPhoneticGuides[payload.Target] = payload.PhoneticGuide;
        else
            sheet.CellPhoneticGuides.Remove(payload.Target);
    }

    private static void RestoreCellSnapshot(Sheet sheet, CellSnapshot snapshot)
    {
        if (snapshot.Cell is null)
        {
            sheet.ClearCell(snapshot.Address);
            if (snapshot.StyleOnly.HasValue)
                sheet.SetStyleOnly(snapshot.Address.Row, snapshot.Address.Col, snapshot.StyleOnly.Value);
            else
                sheet.ClearStyleOnly(snapshot.Address.Row, snapshot.Address.Col);
        }
        else
        {
            sheet.SetCell(snapshot.Address, snapshot.Cell.Clone());
        }

        if (snapshot.Comment is not null)
            sheet.Comments[snapshot.Address] = snapshot.Comment;
        else
            sheet.Comments.Remove(snapshot.Address);

        if (snapshot.CommentAuthor is not null)
            sheet.CommentAuthors[snapshot.Address] = snapshot.CommentAuthor;
        else
            sheet.CommentAuthors.Remove(snapshot.Address);

        if (snapshot.CommentShown)
            sheet.ShownComments.Add(snapshot.Address);
        else
            sheet.ShownComments.Remove(snapshot.Address);

        if (snapshot.ThreadedComment is not null)
            sheet.ThreadedComments[snapshot.Address] = CloneThreadedComment(snapshot.ThreadedComment);
        else
            sheet.ThreadedComments.Remove(snapshot.Address);

        if (snapshot.Hyperlink is not null)
            sheet.Hyperlinks[snapshot.Address] = snapshot.Hyperlink;
        else
            sheet.Hyperlinks.Remove(snapshot.Address);

        if (snapshot.HyperlinkMetadata is not null)
            sheet.HyperlinkMetadata[snapshot.Address] = snapshot.HyperlinkMetadata;
        else
            sheet.HyperlinkMetadata.Remove(snapshot.Address);

        if (snapshot.RichTextRuns is not null)
            sheet.RichTextRuns[snapshot.Address] = snapshot.RichTextRuns;
        else
            sheet.RichTextRuns.Remove(snapshot.Address);

        if (snapshot.PhoneticGuide is not null)
            sheet.CellPhoneticGuides[snapshot.Address] = snapshot.PhoneticGuide;
        else
            sheet.CellPhoneticGuides.Remove(snapshot.Address);
    }

    private static ThreadedComment CloneThreadedComment(ThreadedComment comment) =>
        comment with { Replies = comment.Replies.Select(reply => reply with { }).ToList() };

    // A copy/paste creates a brand-new, independent comment thread at the destination address --
    // unlike CloneThreadedComment above (used only to snapshot/restore a cell's own pre-existing
    // comment in place for undo), this must NOT carry over the source's persisted Id or reply
    // Ids. Otherwise the pasted thread's root serializes with the identical
    // <threadedComment id="..."> as the source (XlsxWorksheetThreadedCommentMapper.
    // ToThreadedCommentElements reuses comment.Id verbatim), and reply lookup on reload (grouped
    // globally by parentId string, not scoped per cell) attaches the source's replies to the
    // pasted thread too. Clearing Id (and each reply's Id) forces the writer to mint a fresh,
    // address-derived stable guid for the pasted thread instead.
    private static ThreadedComment ClonedThreadedCommentForNewAddress(ThreadedComment comment) =>
        comment with
        {
            Id = null,
            Replies = comment.Replies.Select(reply => reply with { Id = null }).ToList(),
        };

    private static bool HasComments(Sheet sheet, IEnumerable<CellAddress> addresses)
    {
        foreach (var address in addresses)
        {
            if (sheet.Comments.ContainsKey(address) || sheet.ThreadedComments.ContainsKey(address))
                return true;
        }

        return false;
    }

    private static int GetSafeListCapacity(long cellCount) =>
        cellCount is > 0 and <= 1_000_000 ? (int)cellCount : 0;

    private static GridRange TranslateRange(GridRange range, long rowDelta, long colDelta) =>
        new GridRange(
            new CellAddress(range.Start.Sheet, (uint)(range.Start.Row + rowDelta), (uint)(range.Start.Col + colDelta)),
            new CellAddress(range.End.Sheet,   (uint)(range.End.Row   + rowDelta), (uint)(range.End.Col   + colDelta)));

    private sealed record CellSnapshot(
        CellAddress Address,
        Cell? Cell,
        StyleId? StyleOnly,
        string? Comment,
        string? CommentAuthor,
        bool CommentShown,
        ThreadedComment? ThreadedComment,
        string? Hyperlink,
        HyperlinkMetadata? HyperlinkMetadata,
        IReadOnlyList<CellTextRun>? RichTextRuns,
        CellPhoneticGuide? PhoneticGuide = null);

    private sealed record CopyPayload(
        CellAddress Target,
        Cell? Cell,
        StyleId? StyleOnly,
        string? Comment,
        string? CommentAuthor,
        bool CommentShown,
        ThreadedComment? ThreadedComment,
        string? Hyperlink,
        HyperlinkMetadata? HyperlinkMetadata,
        IReadOnlyList<CellTextRun>? RichTextRuns,
        CellPhoneticGuide? PhoneticGuide = null);
}
