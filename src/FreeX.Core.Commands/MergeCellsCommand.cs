using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Merges a rectangular range into a single cell region.</summary>
public sealed class MergeCellsCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _snapshot below captures every non-top-left cell that gets
    // blanked by the merge (Cell clone, no style), so a merge over a large range should count
    // proportionally, not the flat 200-byte default. Matches PasteCellsCommand's constant for the
    // same (Address, Cell?) shape.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;
    private List<GridRange>? _absorbedRegions;
    private IReadOnlyList<CellAddress> _affectedCells = [];

    // R158-merge-comment-orphan: legacy Comments/CommentAuthors/ShownComments and ThreadedComments
    // are address-keyed dictionaries independent of Cell, so blanking a non-anchor cell's Cell value
    // (further below) does nothing to any comment that lives at that same address. Every other
    // comment-aware code path (GridView.Rendering's indicator pass, GridView.CommentPreview's hit
    // testing, CommentNavigationPlanner's Next Note/Comment) assumes a merged range's comments only
    // ever live at the anchor address -- see the invariant documented at
    // GridView.CommentPreview.cs's TryGetCommentPreviewForCell ("Comments/notes are only ever keyed
    // on a merged range's anchor cell"). Left alone, a comment surviving at a covered address becomes
    // both mis-rendered and permanently unreachable through every comment UI. These snapshots capture
    // the pre-merge state of all four collections across the whole range so Apply can relocate a
    // covered cell's comment to the anchor (first-covered-cell-wins if more than one covered cell
    // carries one, mirroring the "upper-left value survives" rule already applied to cell values
    // below) and Revert can put everything back exactly where it started.
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, string>? _commentAuthorSnapshot;
    private HashSet<CellAddress>? _shownCommentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;

    public string Label => "Merge Cells";

    public int EstimatedBytes => (int)Math.Min((long)(_snapshot?.Count ?? _range.CellCount) * BytesPerCell, int.MaxValue);

    // R116: every non-top-left cell in _range that Apply actually blanked (ClearCell/SetCell
    // below) must be surfaced here -- without it, WorkbookCellEditService.UpdateFormulaDependencies
    // never re-registers/clears the swallowed cell's own dependency edges, and RecalcEngine.
    // Recalculate short-circuits to an EmptyReport when there is nothing else dirty, leaving any
    // formula elsewhere that referenced the discarded value showing its stale pre-merge result
    // indefinitely. Mirrors the same fix already applied to ResizeStructuredTableCommand.
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public MergeCellsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range   = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _affectedCells = [];

        // Guard the same worksheet ceiling CopyRangeCommand/MoveRangeCommand already enforce for
        // their own destination ranges. Every caller of this command derives _range from a
        // UI selection (already bounded) EXCEPT FormatPainterCommandFactory's merge-tiling path,
        // which rounds a target range's row/column counts UP to a whole multiple of the source
        // merge's own span (ExpandTargetToMergeMultiple) -- a target anchored close enough to the
        // sheet edge can round past CellAddress.MaxRow/MaxCol with no clamp of its own. Rejecting
        // here (the single choke point every merge-creating command ultimately funnels through)
        // stops that out-of-bounds region from ever reaching sheet.AddMergedRegion, instead of
        // requiring every caller to remember its own bounds check.
        if (!WorksheetBounds.IsValidAddress(_range.Start) || !WorksheetBounds.IsValidAddress(_range.End))
            return new CommandOutcome(false, "Merge range is outside the worksheet bounds.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        // Excel refuses outright to merge a selection that touches ANY cell of a live dynamic-array
        // spill (or a legacy CSE / cached-array range loaded from a file) -- there is no exception for
        // selecting the array's exact full extent, unlike CommandGuards.RejectIfSplitsArray's "the
        // whole array may be replaced as a unit" rule for editing/clearing commands. That exception
        // does not apply here: a merge never removes the anchor's formula, it only blanks the other
        // covered cells and adds a merged region -- so even a merge whose footprint matches the spill's
        // full extent leaves the anchor cell itself part of a merged region afterwards, and
        // Sheet.IsSpillBlocked refuses to re-spill into (or through) a merged region on the very next
        // recalculation, silently turning the array into #SPILL!. This is the single choke point every
        // merge-creating command funnels through (see the worksheet-bounds comment above), so checking
        // here -- rather than relying on CellMergePlanner.HasLiveSpillTarget, which only the
        // ribbon-driven Merge & Center / Merge Cells / Merge Across paths call before ever constructing
        // this command -- also protects FormatPainterCommandFactory.AddTiledMerges' tiled-merge path,
        // which constructs MergeCellsCommand directly with no upstream guard of its own.
        if (sheet.HasArrayOrSpillMembers)
        {
            foreach (var addr in _range.AllCells())
            {
                if (sheet.TryGetArrayExtent(addr, out _, out _, out _))
                    return new CommandOutcome(false, "Can't merge cells that overlap a dynamic array's spill range.");
            }
        }

        // Real Excel allows merging a range that fully CONTAINS one or more smaller existing merged
        // regions: the smaller region(s) are silently absorbed (un-merged) and replaced by the single
        // new merge over the full selection. Only a genuinely PARTIAL overlap -- the new range
        // straddles an existing region's boundary without fully containing it -- is a real conflict
        // and still gets rejected, matching Excel's "That would remove merged cells..." refusal.
        var absorbed = new List<GridRange>();
        foreach (var existing in sheet.MergedRegions)
        {
            if (!Overlaps(_range, existing))
                continue;

            if (_range.Contains(existing))
            {
                absorbed.Add(existing);
                continue;
            }

            return new CommandOutcome(false, "Range overlaps an existing merged region.");
        }

        foreach (var table in sheet.StructuredTables)
        {
            if (Overlaps(_range, table.Range))
                return new CommandOutcome(false, "Cannot merge cells that overlap a table.");
        }

        _snapshot = [];
        _commentSnapshot = [];
        _commentAuthorSnapshot = [];
        _shownCommentSnapshot = [];
        _threadedCommentSnapshot = [];
        foreach (var addr in _range.AllCells())
        {
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));
            if (sheet.Comments.TryGetValue(addr, out var existingComment))
                _commentSnapshot[addr] = existingComment;
            if (sheet.CommentAuthors.TryGetValue(addr, out var existingAuthor))
                _commentAuthorSnapshot[addr] = existingAuthor;
            if (sheet.ShownComments.Contains(addr))
                _shownCommentSnapshot.Add(addr);
            if (sheet.ThreadedComments.TryGetValue(addr, out var existingThreaded))
                _threadedCommentSnapshot[addr] = existingThreaded;
        }

        foreach (var region in absorbed)
            sheet.RemoveMergedRegion(region);
        _absorbedRegions = absorbed;

        // Real Excel discards only the VALUE of every non-top-left cell (with the standard
        // content-loss warning) but keeps that cell's own formatting (fill/font/number-format/
        // borders) alive -- an Unmerge later brings back an empty cell that still carries its
        // original look, matching Excel. sheet.ClearCell would hard-delete the whole Cell record
        // (value AND StyleId together), permanently losing the formatting. Only cells that actually
        // carry non-default formatting need to survive as a blank-but-styled Cell (mirroring the
        // preserved-style blanking pattern ClearContentsCommand.Apply already uses); a plain,
        // unstyled value cell has nothing to preserve, so it is still fully removed as before.
        var topLeft = _range.Start;

        // Relocate (or, if the anchor is already occupied, discard) any comment/note left behind on
        // a covered cell -- see the field comments on the snapshot dictionaries above for why this
        // must happen. "First covered cell wins" the anchor slot when more than one covered cell has
        // a comment of the same kind, matching the value-loss rule Excel itself applies when merging
        // a selection with data in more than one cell.
        var anchorHasLegacyComment = _commentSnapshot.ContainsKey(topLeft);
        var anchorHasThreadedComment = _threadedCommentSnapshot.ContainsKey(topLeft);
        var legacyCommentMigrated = false;
        var threadedCommentMigrated = false;
        foreach (var addr in _range.AllCells())
        {
            if (addr == topLeft) continue;

            if (_commentSnapshot.TryGetValue(addr, out var coveredComment))
            {
                if (!anchorHasLegacyComment && !legacyCommentMigrated)
                {
                    sheet.Comments[topLeft] = coveredComment;
                    if (_commentAuthorSnapshot.TryGetValue(addr, out var coveredAuthor))
                        sheet.CommentAuthors[topLeft] = coveredAuthor;
                    if (_shownCommentSnapshot.Contains(addr))
                        sheet.ShownComments.Add(topLeft);
                    legacyCommentMigrated = true;
                }

                sheet.Comments.Remove(addr);
                sheet.CommentAuthors.Remove(addr);
                sheet.ShownComments.Remove(addr);
            }

            if (_threadedCommentSnapshot.TryGetValue(addr, out var coveredThreaded))
            {
                if (!anchorHasThreadedComment && !threadedCommentMigrated)
                {
                    sheet.ThreadedComments[topLeft] = coveredThreaded;
                    threadedCommentMigrated = true;
                }

                sheet.ThreadedComments.Remove(addr);
            }
        }

        var affected = new List<CellAddress>();
        foreach (var addr in _range.AllCells())
        {
            if (addr == topLeft) continue;
            var oldCell = sheet.GetCell(addr);
            if (oldCell is null || oldCell.StyleId == StyleId.Default)
            {
                sheet.ClearCell(addr);
                if (oldCell is not null)
                    affected.Add(addr);
                continue;
            }

            var cleared = Cell.FromValue(BlankValue.Instance);
            cleared.StyleId = oldCell.StyleId;
            sheet.SetCell(addr, cleared);
            affected.Add(addr);
        }
        _affectedCells = affected;

        sheet.AddMergedRegion(_range);
        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        sheet.RemoveMergedRegion(_range);

        if (_absorbedRegions is not null)
        {
            foreach (var region in _absorbedRegions)
                sheet.AddMergedRegion(region);
        }

        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(addr);
            else
                sheet.SetCell(addr, oldCell.Clone());
        }

        // Undo the comment/note relocation performed in Apply -- put every one of the four
        // collections back to exactly its pre-merge, per-address state (including putting a
        // migrated comment back on its original covered cell and clearing it back off the anchor).
        foreach (var addr in _range.AllCells())
        {
            sheet.Comments.Remove(addr);
            if (_commentSnapshot is not null && _commentSnapshot.TryGetValue(addr, out var comment))
                sheet.Comments[addr] = comment;

            sheet.CommentAuthors.Remove(addr);
            if (_commentAuthorSnapshot is not null && _commentAuthorSnapshot.TryGetValue(addr, out var author))
                sheet.CommentAuthors[addr] = author;

            sheet.ShownComments.Remove(addr);
            if (_shownCommentSnapshot is not null && _shownCommentSnapshot.Contains(addr))
                sheet.ShownComments.Add(addr);

            sheet.ThreadedComments.Remove(addr);
            if (_threadedCommentSnapshot is not null && _threadedCommentSnapshot.TryGetValue(addr, out var threaded))
                sheet.ThreadedComments[addr] = threaded;
        }
    }

    private static bool Overlaps(GridRange a, GridRange b) =>
        a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row &&
        a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
}

/// <summary>Removes a merged cell region (makes cells independent again).</summary>
public sealed class UnmergeCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private bool _removed;

    public string Label => "Unmerge Cells";

    public UnmergeCellsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range   = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        // R116: matches CellMergePlanner's NoOpWorkbookCommand convention -- unmerging a range that
        // was never actually merged must report IsNoOp so CommandBus skips pushing an undo entry,
        // rather than leaving a phantom "Unmerge Cells" entry that Revert then correctly no-ops on
        // anyway. Today's production callers (CellMergePlanner.CreateUnmergeCommands and the Merge
        // & Center / Merge Cells toggle-to-unmerge branches) already pre-filter to real merged
        // regions, but this command is public and must not rely on that caller discipline.
        _removed = sheet.RemoveMergedRegion(_range);
        return new CommandOutcome(true, IsNoOp: !_removed);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_removed)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (!sheet.MergedRegions.Contains(_range))
            sheet.AddMergedRegion(_range);
        _removed = false;
    }
}
