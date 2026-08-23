using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RenamePivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly string _newName;
    private readonly List<(SheetId SheetId, Guid ChartId)> _updatedCharts = [];
    private readonly List<SlicerModel> _updatedSlicers = [];
    private readonly List<TimelineModel> _updatedTimelines = [];
    private string? _oldName;

    public RenamePivotTableCommand(SheetId sheetId, string pivotTableName, string newName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _newName = newName.Trim();
    }

    public string Label => "Rename PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_newName))
            return CommandGuards.RejectPivotTableNameRequired();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();
        if (PivotTableNameExists(ctx.Workbook, pivotTable, _newName))
            return new CommandOutcome(false, "PivotTable name is already in use.");

        _oldName = pivotTable.Name;
        if (string.Equals(_oldName, _newName, StringComparison.Ordinal))
            return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);

        _updatedCharts.Clear();
        _updatedSlicers.Clear();
        _updatedTimelines.Clear();
        foreach (var workbookSheet in ctx.Workbook.Sheets)
        {
            foreach (var chart in workbookSheet.Charts.Where(chart =>
                         chart.IsPivotChart &&
                         string.Equals(chart.PivotTableName, _oldName, StringComparison.OrdinalIgnoreCase)))
            {
                _updatedCharts.Add((workbookSheet.Id, chart.Id));
            }
        }

        // slicer-timeline-wiring F1: match on EITHER the primary connection (SourcePivotTableName) OR a
        // SECONDARY "Report Connection" entry in ConnectedPivotTableNames -- a slicer/timeline can drive
        // several pivot tables at once, and the renamed pivot table need not be the primary one. Matching
        // primary-only left every secondary connection's stale old name in ConnectedPivotTableNames
        // forever, which SetSlicerSelectionCommand/SetTimelineRangeCommand's connected-pivot lookup then
        // silently treats the same as a deleted pivot table (skipped, no error) -- the renamed table just
        // stops being filtered.
        _updatedSlicers.AddRange(ctx.Workbook.Slicers.Where(slicer =>
            string.Equals(slicer.SourcePivotTableName, _oldName, StringComparison.OrdinalIgnoreCase) ||
            slicer.ConnectedPivotTableNames.Any(name => string.Equals(name, _oldName, StringComparison.OrdinalIgnoreCase))));
        _updatedTimelines.AddRange(ctx.Workbook.Timelines.Where(timeline =>
            string.Equals(timeline.SourcePivotTableName, _oldName, StringComparison.OrdinalIgnoreCase) ||
            timeline.ConnectedPivotTableNames.Any(name => string.Equals(name, _oldName, StringComparison.OrdinalIgnoreCase))));

        pivotTable.Name = _newName;
        foreach (var (chartSheetId, chartId) in _updatedCharts)
        {
            if (ChartCommandGuards.TryFindChart(ctx.GetSheet(chartSheetId), chartId, out var chart))
                chart.PivotTableName = _newName;
        }

        foreach (var slicer in _updatedSlicers)
        {
            // Only rewrite the primary name when this slicer's primary connection is the one being
            // renamed -- a slicer picked up here solely because the renamed table is a SECONDARY
            // connection must keep its existing (different) primary untouched.
            if (string.Equals(slicer.SourcePivotTableName, _oldName, StringComparison.OrdinalIgnoreCase))
                slicer.SourcePivotTableName = _newName;
            // R133-io-slicer-timeline-multipivot: keep the full connections list in agreement with the
            // primary name -- a slicer bound to several pivot tables lists every one of them here (see
            // SlicerModel.ConnectedPivotTableNames), and XlsxSlicerTimelineStateRewriter reconciles the
            // saved cache POSITIONALLY against this list. Leaving it stale would re-save the pre-rename
            // name for this entry while every OTHER connection stays correctly untouched.
            RenamePivotTableConnection(slicer.ConnectedPivotTableNames, _oldName, _newName);
        }
        foreach (var timeline in _updatedTimelines)
        {
            if (string.Equals(timeline.SourcePivotTableName, _oldName, StringComparison.OrdinalIgnoreCase))
                timeline.SourcePivotTableName = _newName;
            RenamePivotTableConnection(timeline.ConnectedPivotTableNames, _oldName, _newName);
        }

        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldName is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _newName, out var pivotTable))
            pivotTable.Name = _oldName;

        foreach (var (chartSheetId, chartId) in _updatedCharts)
        {
            if (ChartCommandGuards.TryFindChart(ctx.GetSheet(chartSheetId), chartId, out var chart))
                chart.PivotTableName = _oldName;
        }

        foreach (var slicer in _updatedSlicers)
        {
            // Mirror the Apply-side condition: only a slicer whose primary connection was actually
            // renamed (now equal to _newName) had its primary changed and needs it reverted -- a
            // secondary-only match never touched SourcePivotTableName in the first place.
            if (string.Equals(slicer.SourcePivotTableName, _newName, StringComparison.OrdinalIgnoreCase))
                slicer.SourcePivotTableName = _oldName;
            RenamePivotTableConnection(slicer.ConnectedPivotTableNames, _newName, _oldName);
        }
        foreach (var timeline in _updatedTimelines)
        {
            if (string.Equals(timeline.SourcePivotTableName, _newName, StringComparison.OrdinalIgnoreCase))
                timeline.SourcePivotTableName = _oldName;
            RenamePivotTableConnection(timeline.ConnectedPivotTableNames, _newName, _oldName);
        }

        _updatedCharts.Clear();
        _updatedSlicers.Clear();
        _updatedTimelines.Clear();
        _oldName = null;
    }

    private static bool PivotTableNameExists(Workbook workbook, PivotTableModel target, string name) =>
        workbook.Sheets
            .SelectMany(sheet => sheet.PivotTables)
            .Any(pivot => !ReferenceEquals(pivot, target) &&
                          string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// R133-io-slicer-timeline-multipivot: replaces every entry equal to <paramref name="fromName"/> with
    /// <paramref name="toName"/> in place, in-order -- a slicer/timeline's
    /// <see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>
    /// list must stay in agreement with <see cref="SlicerModel.SourcePivotTableName"/>/
    /// <see cref="TimelineModel.SourcePivotTableName"/> across a rename, or the OTHER (unrenamed)
    /// connections it also carries would be silently overwritten too by
    /// <see cref="FreeX.Core.IO.XlsxSlicerTimelineStateRewriter"/>'s positional cache reconciliation on the
    /// next save.
    /// </summary>
    private static void RenamePivotTableConnection(List<string> connectedPivotTableNames, string? fromName, string? toName)
    {
        if (fromName is null || toName is null)
            return;

        for (var i = 0; i < connectedPivotTableNames.Count; i++)
        {
            if (string.Equals(connectedPivotTableNames[i], fromName, StringComparison.OrdinalIgnoreCase))
                connectedPivotTableNames[i] = toName;
        }
    }
}

public sealed class ClearPivotTableViewCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private PivotViewClearSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ClearPivotTableViewCommand(SheetId sheetId, string pivotTableName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
    }

    public string Label => "Clear PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        _snapshot = PivotViewClearSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        PivotTableCommandCollections.Replace(pivotTable.RowFields, ClearSelections(pivotTable.RowFields));
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ClearSelections(pivotTable.ColumnFields));
        PivotTableCommandCollections.Replace(pivotTable.PageFields, ClearSelections(pivotTable.PageFields));
        pivotTable.LabelFilters.Clear();
        pivotTable.ValueFilters.Clear();
        pivotTable.Sorts.Clear();

        // R140-remediation-pivot-refresh-growth-guard-completeness: clearing a filter/selection can
        // grow the pivot's footprint (previously-hidden items reappear) past its previous render -- see
        // PivotTableRefreshService.GrowthGuard.cs.
        var snapshot = _snapshot;
        if (PivotTableCommandRefreshTransaction.RefreshGuarded(
                ctx.Workbook, sheet, pivotTable, () => snapshot!.Restore(pivotTable)) is { } failure)
        {
            _snapshot = null;
            _targetSnapshot = null;
            return failure;
        }
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable);
        var snapshot = _snapshot;
        PivotTableCommandRefreshTransaction.Revert(
            ctx.Workbook,
            sheet,
            pivotTable,
            _targetSnapshot,
            snapshot is null ? null : table => snapshot.Restore(table));
        _snapshot = null;
        _targetSnapshot = null;
    }

    private static IReadOnlyList<PivotFieldModel> ClearSelections(IEnumerable<PivotFieldModel> fields) =>
        fields
            .Select(field => field with { SelectedItem = null, SelectedItems = null })
            .ToList();

    private sealed record PivotViewClearSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotLabelFilterModel> LabelFilters,
        IReadOnlyList<PivotValueFilterModel> ValueFilters,
        IReadOnlyList<PivotSortModel> Sorts,
        GridRange? LastRenderedRange)
    {
        public static PivotViewClearSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.LabelFilters.ToList(),
                pivotTable.ValueFilters.ToList(),
                pivotTable.Sorts.ToList(),
                pivotTable.LastRenderedRange);

        public void Restore(PivotTableModel pivotTable)
        {
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            PivotTableCommandCollections.Replace(pivotTable.LabelFilters, LabelFilters);
            PivotTableCommandCollections.Replace(pivotTable.ValueFilters, ValueFilters);
            PivotTableCommandCollections.Replace(pivotTable.Sorts, Sorts);
            pivotTable.LastRenderedRange = LastRenderedRange;
        }
    }

}

public sealed class MovePivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly CellAddress _targetStart;
    private GridRange? _oldTargetRange;
    private GridRange? _newTargetRange;
    private GridRange? _oldLastRenderedRange;
    private List<(CellAddress Address, Cell? Cell)>? _rangeSnapshot;
    // sweep92-F1: merged regions (e.g. from MergeAndCenterLabels) that overlapped the pivot's OLD
    // footprint, captured immediately before the clear below deletes them via
    // sheet.ReplaceMergedRegions(...Where(!Overlaps)). _rangeSnapshot only carries (CellAddress,
    // Cell?) pairs -- AddPivotTableCommand.Restore never touches MergedRegions -- so without this,
    // Revert put the old location's cell VALUES back but left it permanently un-merged.
    private List<GridRange>? _oldMergedRegions;

    public MovePivotTableCommand(SheetId sheetId, string pivotTableName, CellAddress targetStart)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _targetStart = targetStart;
    }

    public string Label => "Move PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_targetStart.Sheet != _sheetId)
            return CommandGuards.RejectPivotTableTargetRangeOnTargetSheet();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();
        if (!TryCreateMovedTargetRange(pivotTable.TargetRange, _targetStart, out var movedRange))
            return new CommandOutcome(false, "PivotTable target range is outside the worksheet bounds.");

        _oldTargetRange = pivotTable.TargetRange;
        _newTargetRange = movedRange;
        _oldLastRenderedRange = pivotTable.LastRenderedRange;
        _rangeSnapshot = SnapshotRanges(sheet, _oldTargetRange.Value, _oldLastRenderedRange ?? _oldTargetRange.Value, _newTargetRange.Value);

        if (_oldTargetRange.Value != _newTargetRange.Value)
        {
            // R140-remediation-pivot-refresh-growth-guard-completeness: the baseline MUST be captured
            // before the manual clear below, or the very cells this guard exists to protect (the
            // pivot's OWN old-location output, which the clear is about to erase) are already gone by
            // the time a growth conflict elsewhere would need to restore them. Moving a pivot onto a
            // new location is just as capable of landing on unrelated user content as any other growth
            // -- the guard's oldFootprint/newFootprint (old location vs. new location) are disjoint
            // here, so a conflict fires for ANY pre-existing content at the destination, not only actual
            // growth -- which matches Excel refusing to move a PivotTable on top of existing data. See
            // PivotTableRefreshService.GrowthGuard.cs.
            var oldTargetRange = _oldTargetRange.Value;
            var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(sheet, pivotTable);

            // sweep92-F1: capture the old footprint's merged regions before the clear below strips
            // them, so Revert can put them back. Scoped to the old TargetRange/LastRenderedRange --
            // exactly what ClearRenderedRange+ClearRange are about to remove -- not the whole sheet,
            // so an unrelated merge elsewhere is never touched.
            _oldMergedRegions = sheet.MergedRegions
                .Where(region => region.Overlaps(oldTargetRange) ||
                                  (_oldLastRenderedRange is { } renderedRange && region.Overlaps(renderedRange)))
                .ToList();

            PivotTableRefreshService.ClearRenderedRange(sheet, _oldLastRenderedRange);
            ClearRange(sheet, _oldTargetRange.Value);
            pivotTable.TargetRange = _newTargetRange.Value;

            if (PivotTableRefreshService.RefreshGuarded(
                    ctx.Workbook, sheet, pivotTable, baseline,
                    () => pivotTable.TargetRange = oldTargetRange) is { } failure)
            {
                _oldTargetRange = null;
                _newTargetRange = null;
                _oldLastRenderedRange = null;
                _rangeSnapshot = null;
                _oldMergedRegions = null;
                return failure;
            }

            PivotTableRefreshService.UpdateBoundPivotCharts(ctx.Workbook, sheet, pivotTable);
        }

        return new CommandOutcome(true, AffectedCells: [_newTargetRange.Value.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldTargetRange is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            pivotTable.TargetRange = _oldTargetRange.Value;
            pivotTable.LastRenderedRange = _oldLastRenderedRange;
        }

        AddPivotTableCommand.Restore(sheet, _rangeSnapshot);
        // sweep92-F1: put back the old footprint's merged regions Apply's clear step removed --
        // AddPivotTableCommand.Restore above only replays cell values, never MergedRegions. The old
        // location was left untouched since Apply cleared it (ClearRenderedRange above only touches
        // the NEW location), so there is nothing here to overlap or clobber.
        if (_oldMergedRegions is { Count: > 0 })
        {
            foreach (var region in _oldMergedRegions)
                sheet.AddMergedRegion(region);
        }
        if (pivotTable is not null)
            PivotTableRefreshService.UpdateBoundPivotCharts(ctx.Workbook, sheet, pivotTable);
        _oldTargetRange = null;
        _newTargetRange = null;
        _oldLastRenderedRange = null;
        _rangeSnapshot = null;
        _oldMergedRegions = null;
    }

    private static bool TryCreateMovedTargetRange(GridRange currentRange, CellAddress targetStart, out GridRange targetRange)
    {
        var rowCount = currentRange.RowCount;
        var colCount = currentRange.ColCount;
        if (targetStart.Row > CellAddress.MaxRow - rowCount + 1 ||
            targetStart.Col > CellAddress.MaxCol - colCount + 1)
        {
            targetRange = default;
            return false;
        }

        targetRange = new GridRange(
            targetStart,
            new CellAddress(
                targetStart.Sheet,
                targetStart.Row + rowCount - 1,
                targetStart.Col + colCount - 1));
        return true;
    }

    private static List<(CellAddress Address, Cell? Cell)> SnapshotRanges(Sheet sheet, params GridRange[] ranges)
    {
        var snapshot = new List<(CellAddress Address, Cell? Cell)>();
        var seen = new HashSet<CellAddress>();
        foreach (var range in ranges)
        {
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (seen.Add(address))
                    snapshot.Add((address, sheet.GetCell(address)?.Clone()));
            }
        }

        return snapshot;
    }

    private static void ClearRange(Sheet sheet, GridRange range)
    {
        sheet.ReplaceMergedRegions(sheet.MergedRegions.Where(region => !region.Overlaps(range)));
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
            sheet.ClearCell(row, col);
    }

}
