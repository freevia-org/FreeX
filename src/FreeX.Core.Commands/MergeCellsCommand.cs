using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Merges a rectangular range into a single cell region.</summary>
public sealed class MergeCellsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;
    private List<GridRange>? _absorbedRegions;
    private IReadOnlyList<CellAddress> _affectedCells = [];

    public string Label => "Merge Cells";

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
        foreach (var addr in _range.AllCells())
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));

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
