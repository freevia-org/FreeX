using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AddPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _targetRange;
    private readonly string _name;
    private readonly IReadOnlyList<int> _rowFieldIndexes;
    private readonly IReadOnlyList<int> _dataFieldIndexes;
    private PivotCacheModel? _addedCache;
    private PivotTableModel? _addedPivotTable;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public string Label => "Insert PivotTable";

    public AddPivotTableCommand(
        SheetId sheetId,
        GridRange sourceRange,
        GridRange targetRange,
        string name,
        IReadOnlyList<int> rowFieldIndexes,
        IReadOnlyList<int> dataFieldIndexes)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _targetRange = targetRange;
        _name = name;
        _rowFieldIndexes = rowFieldIndexes;
        _dataFieldIndexes = dataFieldIndexes;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_targetRange.Start.Sheet != _sheetId || _targetRange.End.Sheet != _sheetId)
            return CommandGuards.RejectPivotTableTargetRangeOnTargetSheet();
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return CommandGuards.RejectPivotTableSourceRangeRequiresHeaders();
        if (string.IsNullOrWhiteSpace(_name))
            return CommandGuards.RejectPivotTableNameRequired();

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (!_rowFieldIndexes.Concat(_dataFieldIndexes).All(index => index >= 0 && index < fieldCount))
            return CommandGuards.RejectPivotTableFieldIndexOutsideSourceRange();
        if (_dataFieldIndexes.Count == 0)
            return CommandGuards.RejectPivotTableRequiresDataField();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        _targetSnapshot = Snapshot(sheet, _targetRange);
        var headers = ReadHeaders(sourceSheet, fieldCount);
        var cacheId = NextCacheId(ctx.Workbook);
        var cache = new PivotCacheModel
        {
            CacheId = cacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sourceSheet.Name,
            SourceReference = _sourceRange.ToString()
        };
        for (var index = 0; index < headers.Count; index++)
            cache.Fields.Add(BuildPivotCacheFieldFromSourceData(headers[index], sourceSheet, _sourceRange, index));

        var pivotTable = new PivotTableModel
        {
            Name = _name,
            CacheId = cacheId,
            SourceRange = _sourceRange,
            TargetRange = _targetRange
        };
        pivotTable.RowFields.AddRange(_rowFieldIndexes.Select(index => new PivotFieldModel(index)));
        pivotTable.DataFields.AddRange(_dataFieldIndexes.Select(index =>
            new PivotDataFieldModel(index, $"Sum of {headers[index]}", "sum")));

        ctx.Workbook.PivotCaches.Add(cache);
        sheet.PivotTables.Add(pivotTable);

        // R140-remediation-pivot-refresh-growth-guard-completeness: the initial render of a brand new
        // pivot is just as capable of "growing" past the TargetRange the user drew as any later
        // refresh -- the user's initial rectangle is only ever a size ESTIMATE (Excel's own Insert
        // PivotTable dialog defaults to a tiny placeholder range too), and the source can easily have
        // more distinct row/column items than that estimate accounted for. Route the very first
        // render through the same growth-conflict guard every other pivot-mutating command now uses,
        // so this creation path can't silently clobber unrelated content sitting just past the chosen
        // target range either.
        var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(sheet, pivotTable);
        if (PivotTableRefreshService.RefreshGuarded(
                ctx.Workbook, sheet, pivotTable, baseline,
                () =>
                {
                    sheet.PivotTables.Remove(pivotTable);
                    ctx.Workbook.PivotCaches.Remove(cache);
                }) is { } failure)
        {
            _targetSnapshot = null;
            return failure;
        }

        _addedCache = cache;
        _addedPivotTable = pivotTable;
        return new CommandOutcome(true, AffectedCells: [_targetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_addedPivotTable is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, _addedPivotTable.LastRenderedRange);
            sheet.PivotTables.Remove(_addedPivotTable);
        }
        if (_addedCache is not null)
            ctx.Workbook.PivotCaches.Remove(_addedCache);
        Restore(sheet, _targetSnapshot);
        _addedPivotTable = null;
        _addedCache = null;
        _targetSnapshot = null;
    }

    private List<string> ReadHeaders(Sheet sheet, int fieldCount)
    {
        var headers = new List<string>(fieldCount);
        for (var index = 0; index < fieldCount; index++)
        {
            var value = sheet.GetValue(_sourceRange.Start.Row, _sourceRange.Start.Col + (uint)index);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{index + 1}");
        }

        return headers;
    }

    // R114-commands-pivot-sharedItems: delegates to the shared choke point (PivotCacheFieldFactory) so
    // every cache field built from LIVE source data -- here, in PivotTableRefreshService.ReconcileCacheFields,
    // and in ChangePivotTableSourceCommand/BuildRedirectedCache -- gets the same type-flag metadata AND
    // the same distinct-value SharedItems/SharedItemKinds list a pivot-bound slicer needs to have any
    // filter items at all (see PivotCacheFieldFactory's own doc comment for the full story).
    private static PivotCacheFieldModel BuildPivotCacheFieldFromSourceData(
        string header,
        Sheet sourceSheet,
        GridRange sourceRange,
        int columnIndex) =>
        PivotCacheFieldFactory.BuildFromSourceData(header, sourceSheet, sourceRange, columnIndex);

    private static int NextCacheId(Workbook workbook) =>
        workbook.PivotCaches.Count == 0
            ? 1
            : workbook.PivotCaches.Max(cache => cache.CacheId) + 1;

    internal static List<(CellAddress Address, Cell? Cell)> Snapshot(Sheet sheet, GridRange range)
    {
        var snapshot = new List<(CellAddress Address, Cell? Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var address = new CellAddress(sheet.Id, row, col);
            snapshot.Add((address, sheet.GetCell(address)?.Clone()));
        }

        return snapshot;
    }

    internal static void Restore(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell? Cell)>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (address, cell) in snapshot)
        {
            if (cell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, cell.Clone());
        }
    }
}

public sealed class AddPivotTableToNewWorksheetCommand : IWorkbookCommand
{
    public const uint InitialTargetRow = 3;
    public const uint InitialTargetColumn = 1;

    private readonly GridRange _sourceRange;
    private readonly string _name;
    private readonly IReadOnlyList<int> _rowFieldIndexes;
    private readonly IReadOnlyList<int> _dataFieldIndexes;
    private SheetId? _createdSheetId;
    private AddPivotTableCommand? _innerCommand;

    public string Label => "Insert PivotTable";
    public SheetId? CreatedSheetId => _createdSheetId;

    public AddPivotTableToNewWorksheetCommand(
        GridRange sourceRange,
        string name,
        IReadOnlyList<int> rowFieldIndexes,
        IReadOnlyList<int> dataFieldIndexes)
    {
        _sourceRange = sourceRange;
        _name = name;
        _rowFieldIndexes = rowFieldIndexes;
        _dataFieldIndexes = dataFieldIndexes;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.Workbook.AddSheet(GetUniquePivotSheetName(ctx.Workbook));
        sheet.ResetViewStateToA1();
        _createdSheetId = sheet.Id;
        var targetRange = CreateInitialTargetRange(sheet.Id, _sourceRange, _rowFieldIndexes.Count, _dataFieldIndexes.Count);
        _innerCommand = new AddPivotTableCommand(
            sheet.Id,
            _sourceRange,
            targetRange,
            _name,
            _rowFieldIndexes,
            _dataFieldIndexes);

        var outcome = _innerCommand.Apply(ctx);
        if (outcome.Success)
            return outcome;

        ctx.Workbook.RemoveSheet(sheet.Id);
        _createdSheetId = null;
        _innerCommand = null;
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdSheetId is null)
            return;

        _innerCommand?.Revert(ctx);
        ctx.Workbook.RemoveSheet(_createdSheetId.Value);
        _createdSheetId = null;
        _innerCommand = null;
    }

    private static GridRange CreateInitialTargetRange(SheetId sheetId, GridRange sourceRange, int rowFieldCount, int dataFieldCount)
    {
        var start = new CellAddress(sheetId, InitialTargetRow, InitialTargetColumn);
        var outputColumns = Math.Max(1, rowFieldCount) + Math.Max(1, dataFieldCount);
        var outputRows = Math.Max(3u, sourceRange.RowCount + 2);
        var endRow = Math.Min(CellAddress.MaxRow, (uint)Math.Min(uint.MaxValue, (ulong)start.Row + outputRows - 1));
        var endCol = Math.Min(CellAddress.MaxCol, (uint)Math.Min(uint.MaxValue, (ulong)start.Col + (uint)outputColumns - 1));
        var end = new CellAddress(
            sheetId,
            endRow,
            endCol);
        return new GridRange(start, end);
    }

    private static string GetUniquePivotSheetName(Workbook workbook)
    {
        const string baseName = "PivotTable";
        if (workbook.ValidateSheetName(baseName) is null)
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }
    }
}

public sealed class RefreshPivotTableCommand : IWorkbookCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _targetSnapshot below captures a (Cell?) per cell in the
    // pivot table's previously-rendered range, the same shape PasteCellsCommand/FillCellsCommand
    // use 300 bytes/cell for. Refreshing a large pivot table (many rows/columns of rendered
    // output) should count proportionally, not the flat 200-byte default.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;
    private GridRange? _lastRenderedRangeSnapshot;
    private RefreshFieldSnapshot? _fieldSnapshot;
    // meta-F2 (round154): merged regions (e.g. from MergeAndCenterLabels) that overlapped the pivot's
    // OLD footprint before RefreshGuarded (below) re-renders it, stripping them via
    // PivotTableRefreshService.ClearTargetRange's unconditional sheet.ReplaceMergedRegions(...Where(
    // !Overlaps)). _targetSnapshot only carries (CellAddress, Cell?) pairs -- AddPivotTableCommand.
    // Restore never touches MergedRegions -- so without this, Revert put the old footprint's cell
    // VALUES back but left it permanently un-merged. Mirrors MovePivotTableCommand's sweep92-F1 fix.
    private List<GridRange>? _oldMergedRegions;

    public int EstimatedBytes => (int)Math.Min((long)(_targetSnapshot?.Count ?? 0) * BytesPerCell, int.MaxValue);

    public RefreshPivotTableCommand(SheetId sheetId, string pivotTableName)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
    }

    public string Label => "Refresh PivotTable";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        var oldFootprint = pivotTable.LastRenderedRange ?? pivotTable.TargetRange;
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, oldFootprint);
        _lastRenderedRangeSnapshot = pivotTable.LastRenderedRange;
        // meta-F2 (round154): capture before RefreshGuarded (below) re-renders the pivot and strips any
        // merges overlapping the old footprint. Scoped to exactly what that re-render can clear -- not
        // the whole sheet -- so an unrelated merge elsewhere is never touched.
        _oldMergedRegions = sheet.MergedRegions.Where(region => region.Overlaps(oldFootprint)).ToList();
        // R116-commands-pivot-refresh-revert: Refresh (below) prunes pivotTable.RowFields/ColumnFields/
        // PageFields/DataFields (RemoveAll) and rebuilds cache.Fields (Clear+AddRange) in place on the
        // SAME live PivotTableModel/PivotCacheModel objects whenever a field's source column has
        // disappeared since the pivot was last refreshed -- neither list is ever swapped for a new
        // instance, so nothing but an explicit capture/restore here can undo that pruning on Undo. This
        // is the identical mutation ChangePivotTableSourceCommand's own call to Refresh triggers, which
        // is why its PivotSourceSnapshot restores cache.Fields the same way; this command additionally
        // has no equivalent of that command's pre-check that every field already fits the (unchanged)
        // source range, so its own RemoveAll calls are frequently NOT no-ops and the field lists must be
        // captured here too.
        var cache = CommandGuards.FindPivotCache(ctx.Workbook, pivotTable);
        _fieldSnapshot = RefreshFieldSnapshot.Capture(ctx.Workbook, pivotTable, cache);

        // R140-commands-pivot-refresh-growth-dataloss (now shared, see
        // PivotTableRefreshService.GrowthGuard.cs): a refresh whose source gained a new distinct
        // row/column item can need MORE rows/columns than the pivot's previous render occupied.
        // RefreshGuarded only discovers the actual new footprint by writing it -- there is no way to
        // know the growth area up front, so it can't be included in the `_targetSnapshot` capture
        // above. The baseline below snapshots every currently-occupied cell on the whole sheet (plus
        // the current merged regions) before Refresh touches anything, so that if the post-refresh
        // footprint DID grow into a cell holding unrelated user content -- a cell `_targetSnapshot`
        // never covered, because it sat outside the old footprint -- RefreshGuarded puts the sheet back
        // to exactly its pre-refresh state and refuses the refresh, the same way real Excel refuses
        // this refresh with a warning instead of silently overwriting adjacent data. Without this, Undo
        // could never repair the loss either: the destroyed cell was never part of any undo snapshot in
        // the first place (see Revert below, which only ever knew about the OLD footprint).
        var baseline = PivotTableRefreshService.CaptureGrowthGuardBaseline(sheet, pivotTable);
        var fieldSnapshot = _fieldSnapshot;

        // R116-commands-pivot-refresh-scope: this command IS the F5 / "Refresh PivotTable" action --
        // the one genuine "source data may have changed" entry point -- so it is the only caller that
        // asks Refresh to re-derive cache.Fields' SharedItems from the live source (see
        // PivotTableRefreshService.Refresh's rescanCacheSharedItems parameter doc).
        if (PivotTableRefreshService.RefreshGuarded(
                ctx.Workbook, sheet, pivotTable, baseline,
                () => fieldSnapshot!.Restore(pivotTable, ctx.Workbook),
                rescanCacheSharedItems: true) is { } failure)
        {
            _targetSnapshot = null;
            _lastRenderedRangeSnapshot = null;
            _fieldSnapshot = null;
            _oldMergedRegions = null;
            return failure;
        }

        UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_targetSnapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            pivotTable.LastRenderedRange = _lastRenderedRangeSnapshot;
            _fieldSnapshot?.Restore(pivotTable, ctx.Workbook);
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        // meta-F2 (round154): put back the old footprint's merged regions the re-render in Apply
        // stripped -- AddPivotTableCommand.Restore above only replays cell values, never MergedRegions.
        // ClearRenderedRange above only touched the CURRENT (post-Apply) rendered range, so the old
        // footprint is untouched by this Revert by the time we get here -- nothing to overlap or clobber.
        if (_oldMergedRegions is { Count: > 0 })
        {
            foreach (var region in _oldMergedRegions)
                sheet.AddMergedRegion(region);
        }
        if (pivotTable is not null)
            UpdateBoundPivotChartRanges(ctx.Workbook, sheet, pivotTable);
        _targetSnapshot = null;
        _lastRenderedRangeSnapshot = null;
        _fieldSnapshot = null;
        _oldMergedRegions = null;
    }

    private static void UpdateBoundPivotChartRanges(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        var outputRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chartSheet in workbook.Sheets)
        foreach (var chart in chartSheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }
    }

    /// <summary>
    /// Captures exactly the state <see cref="PivotTableRefreshService.Refresh"/> may prune or rebuild in
    /// place -- the pivot's four field lists and its cache's field list -- so Revert can restore it even
    /// though Refresh mutates the live <see cref="PivotTableModel"/>/<see cref="PivotCacheModel"/>
    /// objects rather than replacing them. Mirrors <c>ChangePivotTableSourceCommand.PivotSourceSnapshot</c>
    /// for the cache.Fields half of this same mutation.
    /// </summary>
    private sealed record RefreshFieldSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotDataFieldModel> DataFields,
        int? CacheId,
        IReadOnlyList<PivotCacheFieldModel> CacheFields,
        IReadOnlyList<(string SlicerName, List<SlicerCacheItem> CacheItems)> SlicerCacheItems)
    {
        public static RefreshFieldSnapshot Capture(Workbook workbook, PivotTableModel pivotTable, PivotCacheModel? cache) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.DataFields.ToList(),
                cache?.CacheId,
                cache?.Fields.ToList() ?? [],
                // R117-commands-pivot-slicer-growth: Refresh (rescanCacheSharedItems: true, this
                // command's own call below) can now APPEND new entries to a bound slicer's CacheItems
                // (see PivotTableRefreshService.ExtendBoundSlicerCacheItems) the same way it already
                // rebuilds cache.Fields in place -- capture every such slicer's CacheItems here too, so
                // Undo reverts that append exactly like it already reverts the cache.Fields rebuild
                // above, instead of leaving a refresh's slicer-side growth permanently un-undoable.
                workbook.Slicers
                    .Where(slicer => string.Equals(slicer.SourcePivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(slicer => (slicer.Name, slicer.CacheItems.ToList()))
                    .ToList());

        public void Restore(PivotTableModel pivotTable, Workbook workbook)
        {
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            PivotTableCommandCollections.Replace(pivotTable.DataFields, DataFields);

            foreach (var (slicerName, cacheItems) in SlicerCacheItems)
            {
                var slicer = workbook.Slicers.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, slicerName, StringComparison.OrdinalIgnoreCase));
                if (slicer is null)
                    continue;

                slicer.CacheItems.Clear();
                slicer.CacheItems.AddRange(cacheItems);
            }

            if (CacheId is not { } cacheId)
                return;

            var cache = workbook.PivotCaches.FirstOrDefault(existing => existing.CacheId == cacheId);
            if (cache is null)
                return;

            cache.Fields.Clear();
            cache.Fields.AddRange(CacheFields);
        }
    }
}

