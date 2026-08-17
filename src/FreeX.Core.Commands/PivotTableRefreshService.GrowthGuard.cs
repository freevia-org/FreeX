using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    // R140-remediation-pivot-refresh-growth-guard-completeness: the R140 fix wave added the
    // growth-conflict guard below to RefreshPivotTableCommand.Apply ONLY -- every other caller of
    // Refresh (12 other command Apply() methods, enumerated at every RefreshGuarded call site) kept
    // calling Refresh directly and so kept the exact data-loss defect the round set out to fix: a
    // refresh whose growth lands on a cell that already held unrelated user content silently
    // overwrites it, and Undo can never recover it (the pre-Apply snapshot every one of those commands
    // already captures is bounded to the pivot's OLD footprint, which never covered the clobbered
    // cell in the first place). This type is the ONE place that logic now lives -- every Refresh call
    // site in this assembly must route through <see cref="CaptureGrowthGuardBaseline"/> +
    // <see cref="RefreshGuarded"/> instead of calling <see cref="Refresh"/> directly, so a future call
    // site can't reintroduce the gap by simply forgetting to copy the guard.
    //
    // Performance: <see cref="CloneOccupiedCells"/> below still clones every occupied cell on the
    // WHOLE sheet before every guarded refresh, even when the refresh doesn't grow at all (the common
    // case). This was already true of the original RefreshPivotTableCommand-only fix and is NOT made
    // worse by fanning it out to the other 12 call sites (each one now pays the same cost
    // RefreshPivotTableCommand already paid, not a new one). A tighter bound was deliberately not
    // attempted here: the only way to know a refresh's actual new footprint is to run Refresh and look
    // at what it wrote (WriteRowPivot/WriteMatrixPivot/WriteColumnOnlyPivot/WriteValuesOnlyPivot
    // compute row/column geometry WHILE writing, with no separate "compute extent only" mode), so any
    // pre-refresh snapshot region narrower than "everything currently occupied" has to be justified by
    // an upper bound on how far the render can grow -- and that bound depends on report layout
    // (compact/outline/tabular), subtotal placement, grand totals, and matrix cross-tabs, all of which
    // affect row/column counts differently. Getting that bound even slightly too small would silently
    // reopen the exact data-loss bug this guard exists to close, for the sake of skipping a
    // Dictionary-clone whose cost is proportional to the sheet's OCCUPIED cell count (a sparse map),
    // not its declared row/column extent -- i.e. it already scales with how much real data is on the
    // sheet, not with the sheet's nominal size. A real fix would need Refresh's writers to expose a
    // non-writing "compute the footprint" pass so the snapshot can be scoped to old-footprint ∪
    // prospective-new-footprint before anything is written; that's a separate, larger change to the
    // Writers.* internals, not a safe bolt-on for a completeness-focused remediation pass.

    /// <summary>
    /// Pre-refresh state <see cref="RefreshGuarded"/> needs to detect and roll back a growth conflict.
    /// Must be captured by the caller (via <see cref="CaptureGrowthGuardBaseline"/>) at the point
    /// immediately before whatever this refresh's caller is about to do that could touch the sheet or
    /// <paramref name="pivotTable"/>'s field lists -- for the ordinary case (a command that only
    /// mutates <see cref="PivotTableModel"/> field/filter lists before calling Refresh) that's right
    /// before the old direct <c>PivotTableRefreshService.Refresh(...)</c> call used to sit. The one
    /// exception is <see cref="MovePivotTableCommand"/>, which relocates <c>TargetRange</c> and clears
    /// the OLD rendered cells itself before refreshing at the new location -- there, the baseline must
    /// be captured before that manual clear, or the very cells this guard exists to protect are already
    /// gone by the time it looks for them.
    /// </summary>
    internal readonly struct GrowthGuardBaseline(
        GridRange oldFootprint,
        GridRange? lastRenderedRangeSnapshot,
        Dictionary<(uint Row, uint Col), Cell> beforeOccupied,
        List<GridRange> mergedRegionsBeforeRefresh)
    {
        internal GridRange OldFootprint { get; } = oldFootprint;
        internal GridRange? LastRenderedRangeSnapshot { get; } = lastRenderedRangeSnapshot;
        internal Dictionary<(uint Row, uint Col), Cell> BeforeOccupied { get; } = beforeOccupied;
        internal List<GridRange> MergedRegionsBeforeRefresh { get; } = mergedRegionsBeforeRefresh;
    }

    /// <summary>Captures the baseline every guarded refresh needs. See <see cref="GrowthGuardBaseline"/> for WHEN to call this.</summary>
    internal static GrowthGuardBaseline CaptureGrowthGuardBaseline(Sheet sheet, PivotTableModel pivotTable) =>
        new(
            pivotTable.LastRenderedRange ?? pivotTable.TargetRange,
            pivotTable.LastRenderedRange,
            CloneOccupiedCells(sheet),
            sheet.MergedRegions.ToList());

    /// <summary>
    /// Runs <see cref="Refresh"/> guarded against the R140 growth-conflict data-loss defect: if the
    /// refresh needed to grow beyond <paramref name="baseline"/>'s old footprint and that growth landed
    /// on a cell <paramref name="baseline"/> shows was already occupied, every sheet-level effect of
    /// this call (written cells, merged regions, <see cref="PivotTableModel.LastRenderedRange"/>) is
    /// rolled back to the baseline, <paramref name="restorePivotState"/> is invoked so the caller can
    /// roll back whatever it mutated on <paramref name="pivotTable"/>/its cache/other model state
    /// BEFORE calling this method, and a rejection <see cref="CommandOutcome"/> is returned. Returns
    /// <see langword="null"/> on success (Refresh ran normally; the caller proceeds exactly as if it
    /// had called <see cref="Refresh"/> directly). <paramref name="restorePivotState"/> must NOT touch
    /// sheet cells, merged regions, or LastRenderedRange itself -- this method already restores all
    /// three before invoking it.
    /// </summary>
    internal static CommandOutcome? RefreshGuarded(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        GrowthGuardBaseline baseline,
        Action restorePivotState,
        bool rescanCacheSharedItems = false)
    {
        Refresh(workbook, sheet, pivotTable, rescanCacheSharedItems);

        var newFootprint = pivotTable.LastRenderedRange ?? baseline.OldFootprint;
        if (FindGrowthConflict(baseline.OldFootprint, newFootprint, baseline.BeforeOccupied) is null)
            return null;

        var affected = Union(Union(baseline.OldFootprint, pivotTable.TargetRange), newFootprint);
        RestoreRegion(sheet, affected, baseline.BeforeOccupied);
        sheet.ReplaceMergedRegions(baseline.MergedRegionsBeforeRefresh);
        pivotTable.LastRenderedRange = baseline.LastRenderedRangeSnapshot;
        restorePivotState();
        return CommandGuards.RejectPivotRefreshWouldOverwriteData();
    }

    /// <summary>
    /// Clones every currently-occupied cell on <paramref name="sheet"/>, keyed by raw (row, col) -- see
    /// this file's top-of-file comment for why a growing refresh needs this whole-sheet capture rather
    /// than a single range snapshot, and why it is not narrowed further. <see
    /// cref="Sheet.GetOccupiedCellMap"/> returns the LIVE backing dictionary, so every value must be
    /// cloned here -- otherwise "restoring" from it would just hand the sheet back the very same <see
    /// cref="Cell"/> instances Refresh may still go on to mutate in place.
    /// </summary>
    private static Dictionary<(uint Row, uint Col), Cell> CloneOccupiedCells(Sheet sheet)
    {
        var occupied = sheet.GetOccupiedCellMap();
        var snapshot = new Dictionary<(uint Row, uint Col), Cell>(occupied.Count);
        foreach (var (key, cell) in occupied)
            snapshot[key] = cell.Clone();
        return snapshot;
    }

    /// <summary>
    /// Returns the address of the first cell inside <paramref name="newFootprint"/> that falls OUTSIDE
    /// <paramref name="oldFootprint"/> (i.e. is part of the refresh's growth, not a re-render of the
    /// pivot's own previous output) and already held content in <paramref name="beforeOccupied"/> before
    /// the refresh ran -- <see langword="null"/> if the refresh didn't grow, or grew only into cells that
    /// were genuinely blank. Growing into previously-blank space is exactly how a pivot is expected to
    /// grow and is never a conflict; only landing on a cell someone else was already using is.
    /// </summary>
    private static CellAddress? FindGrowthConflict(
        GridRange oldFootprint,
        GridRange newFootprint,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> beforeOccupied)
    {
        if (oldFootprint.Contains(newFootprint))
            return null;

        foreach (var address in newFootprint.AllCells())
        {
            if (oldFootprint.Contains(address))
                continue;
            if (beforeOccupied.ContainsKey((address.Row, address.Col)))
                return address;
        }

        return null;
    }

    /// <summary>Smallest range (on the shared sheet) that contains both <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static GridRange Union(GridRange a, GridRange b) =>
        new(
            new CellAddress(a.Start.Sheet, Math.Min(a.Start.Row, b.Start.Row), Math.Min(a.Start.Col, b.Start.Col)),
            new CellAddress(a.Start.Sheet, Math.Max(a.End.Row, b.End.Row), Math.Max(a.End.Col, b.End.Col)));

    /// <summary>
    /// Puts every cell in <paramref name="region"/> back to its <paramref name="beforeOccupied"/> content
    /// (or blank, for a cell absent from that capture) -- the rollback half of the growth-conflict guard
    /// above. <paramref name="region"/> must cover everything Refresh could possibly have touched: the
    /// old footprint, the pivot's TargetRange (ClearRefreshRanges always clears both), and the new
    /// footprint Refresh actually rendered.
    /// </summary>
    private static void RestoreRegion(Sheet sheet, GridRange region, IReadOnlyDictionary<(uint Row, uint Col), Cell> beforeOccupied)
    {
        foreach (var address in region.AllCells())
        {
            if (beforeOccupied.TryGetValue((address.Row, address.Col), out var cell))
                sheet.SetCell(address, cell.Clone());
            else
                sheet.ClearCell(address);
        }
    }
}
