using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ConfigurePivotTableCalculatedItemsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly IReadOnlyList<PivotFieldModel> _rowFields;
    private readonly IReadOnlyList<PivotFieldModel> _columnFields;
    private readonly IReadOnlyList<PivotFieldModel> _pageFields;
    private readonly IReadOnlyList<PivotCalculatedFieldModel> _calculatedFields;
    private readonly IReadOnlyList<PivotCalculatedItemModel> _calculatedItems;
    private PivotCalculatedItemsSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ConfigurePivotTableCalculatedItemsCommand(
        SheetId sheetId,
        string pivotTableName,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _rowFields = rowFields;
        _columnFields = columnFields;
        _pageFields = pageFields;
        _calculatedFields = calculatedFields;
        _calculatedItems = calculatedItems;
    }

    public string Label => "Configure PivotTable Calculations";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        var fieldCount = checked((int)pivotTable.SourceRange.ColCount);
        if (_rowFields.Concat(_columnFields).Concat(_pageFields)
                .Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount) ||
            _calculatedItems.Any(item => item.SourceFieldIndex < 0 || item.SourceFieldIndex >= fieldCount))
        {
            return CommandGuards.RejectPivotTableFieldIndexOutsideSourceRange();
        }

        if (_calculatedFields.Any(field => string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Formula)) ||
            _calculatedItems.Any(item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Formula)))
        {
            return new CommandOutcome(false, "Calculated field and item names and formulas are required.");
        }

        _snapshot = PivotCalculatedItemsSnapshot.Capture(pivotTable);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        PivotTableCommandCollections.Replace(pivotTable.RowFields, _rowFields);
        PivotTableCommandCollections.Replace(pivotTable.ColumnFields, _columnFields);
        PivotTableCommandCollections.Replace(pivotTable.PageFields, _pageFields);
        PivotTableCommandCollections.Replace(pivotTable.CalculatedFields, _calculatedFields);
        PivotTableCommandCollections.Replace(pivotTable.CalculatedItems, _calculatedItems);

        // R140-remediation-pivot-refresh-growth-guard-completeness: a calculated item/field can add a
        // new row/column item, which can grow the pivot's footprint past its previous render -- see
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

    private sealed record PivotCalculatedItemsSnapshot(
        IReadOnlyList<PivotFieldModel> RowFields,
        IReadOnlyList<PivotFieldModel> ColumnFields,
        IReadOnlyList<PivotFieldModel> PageFields,
        IReadOnlyList<PivotCalculatedFieldModel> CalculatedFields,
        IReadOnlyList<PivotCalculatedItemModel> CalculatedItems,
        GridRange? LastRenderedRange)
    {
        public static PivotCalculatedItemsSnapshot Capture(PivotTableModel pivotTable) =>
            new(
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                pivotTable.CalculatedFields.ToList(),
                pivotTable.CalculatedItems.ToList(),
                pivotTable.LastRenderedRange);

        public void Restore(PivotTableModel pivotTable)
        {
            PivotTableCommandCollections.Replace(pivotTable.RowFields, RowFields);
            PivotTableCommandCollections.Replace(pivotTable.ColumnFields, ColumnFields);
            PivotTableCommandCollections.Replace(pivotTable.PageFields, PageFields);
            PivotTableCommandCollections.Replace(pivotTable.CalculatedFields, CalculatedFields);
            PivotTableCommandCollections.Replace(pivotTable.CalculatedItems, CalculatedItems);
            pivotTable.LastRenderedRange = LastRenderedRange;
        }
    }
}

public sealed class ChangePivotTableSourceCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly GridRange _sourceRange;
    private PivotSourceSnapshot? _snapshot;
    private List<(CellAddress Address, Cell? Cell)>? _targetSnapshot;

    public ChangePivotTableSourceCommand(SheetId sheetId, string pivotTableName, GridRange sourceRange)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _sourceRange = sourceRange;
    }

    public string Label => "Change PivotTable Data Source";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.ColCount == 0 || _sourceRange.RowCount < 2)
            return CommandGuards.RejectPivotTableSourceRangeRequiresHeaders();

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UsePivotTableReports) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        if (!CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable))
            return CommandGuards.RejectPivotTableNotFound();

        var fieldCount = checked((int)_sourceRange.ColCount);
        if (pivotTable.RowFields.Concat(pivotTable.ColumnFields).Concat(pivotTable.PageFields)
                .Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount) ||
            pivotTable.DataFields.Any(field => field.SourceFieldIndex < 0 || field.SourceFieldIndex >= fieldCount))
        {
            return new CommandOutcome(false, "Existing PivotTable fields are outside the new source range.");
        }

        var cache = CommandGuards.FindPivotCache(ctx.Workbook, pivotTable);
        _snapshot = PivotSourceSnapshot.Capture(pivotTable, cache);
        _targetSnapshot = AddPivotTableCommand.Snapshot(sheet, pivotTable.LastRenderedRange ?? pivotTable.TargetRange);

        pivotTable.SourceRange = _sourceRange;
        if (cache is not null)
        {
            var headers = ReadHeaders(sourceSheet, _sourceRange);

            // R104-sibling: an explicit "Change Data Source" must always win over whatever the cache
            // was previously bound to -- including its table binding. Whether the NEW range happens to
            // be a live structured table's exact extent decides the new binding: a resolved table
            // reference always resolves to that table's exact range (see
            // PivotDataSourcePlanner/TryResolveReferenceRange), so this exact match is how the command
            // tells "redirected onto a table" apart from "redirected onto a plain range" -- and it fires
            // identically whether the OLD binding was a table or a plain range, and whether the NEW
            // table is the same table, a different table, or no table at all.
            var matchedTable = FindLiveTableByExactRange(sourceSheet, _sourceRange);
            var desiredType = matchedTable is not null ? PivotCacheSourceType.Table : PivotCacheSourceType.WorksheetRange;

            if (desiredType == cache.SourceType)
            {
                // No SourceType crossing needed -- mutate the existing cache in place, same as the
                // pre-fix command always did (so any external reference captured before Apply still
                // observes the update), but the fix always reconciles the table binding to the NEW
                // source too, instead of leaving a stale SourceTableName/SourceTableId behind.
                cache.SourceSheetName = sourceSheet.Name;
                cache.SourceReference = _sourceRange.ToString();
                cache.SourceTableName = matchedTable?.Name;
                cache.SourceTableId = matchedTable?.Id;
                // R116-commands-pivot-slicer-changesource: an explicit "Change Data Source" must
                // RECONCILE cache.Fields against the new headers the same way an ordinary refresh does
                // (PivotTableRefreshService.ReconcileCacheFields), not unconditionally rebuild every
                // field from scratch. A field whose name survives the source change keeps its existing
                // SharedItems order/index via PivotCacheFieldFactory.MergeFromSourceData -- a full
                // rebuild would renumber SharedItems purely based on the NEW source's row order, silently
                // corrupting a pivot-bound slicer's SlicerModel.CacheItems[].Index (a positional index
                // into SharedItems that Change Data Source never touches) even though the user's
                // selection didn't change. A header with no existing same-named field (a genuinely new
                // column) still gets a brand-new field built from scratch, same as before.
                var reconciledFields = PivotCacheFieldFactory.ReconcileFields(cache.Fields, headers, sourceSheet, _sourceRange);
                cache.Fields.Clear();
                cache.Fields.AddRange(reconciledFields);
            }
            else
            {
                // PivotCacheModel.SourceType is init-only (mirrors the OOXML cacheSource's fixed
                // shape), so crossing the table/range boundary can't be expressed by mutating the
                // existing cache -- build a replacement carrying the same CacheId and swap it into the
                // workbook's PivotCaches list.
                var redirectedCache = BuildRedirectedCache(cache, sourceSheet, _sourceRange, matchedTable, desiredType, headers);
                var cacheIndex = ctx.Workbook.PivotCaches.FindIndex(existing => existing.CacheId == cache.CacheId);
                if (cacheIndex >= 0)
                    ctx.Workbook.PivotCaches[cacheIndex] = redirectedCache;
            }
        }

        // R140-remediation-pivot-refresh-growth-guard-completeness: an explicit "Change Data Source" is
        // the sibling this file's own RefreshPivotTableCommand comment (above, R116-commands-pivot-
        // refresh-revert) already calls out as triggering the identical field-pruning mutation -- it is
        // just as capable of needing more rows/columns than the pivot's previous render occupied (a
        // wider/taller source range typically has MORE distinct row/column items, not fewer), so it
        // gets the same growth-conflict guard -- see PivotTableRefreshService.GrowthGuard.cs.
        var snapshot = _snapshot;
        var workbook = ctx.Workbook;
        if (PivotTableCommandRefreshTransaction.RefreshGuarded(
                workbook, sheet, pivotTable, () => snapshot!.Restore(pivotTable, workbook)) is { } failure)
        {
            _snapshot = null;
            _targetSnapshot = null;
            return failure;
        }
        // R134-commands-pivotchart-stale-datarange: "Change Data Source" can grow/shrink/relocate the
        // pivot's materialized output just like every other refresh-triggering mutation -- without this,
        // a PivotChart bound to this pivot table keeps rendering the cells the pivot occupied against the
        // OLD source, silently inconsistent with the pivot right next to it.
        return new CommandOutcome(true, AffectedCells: [pivotTable.TargetRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.TryFindPivotTable(sheet, _pivotTableName, out var pivotTable) && _snapshot is not null)
        {
            PivotTableRefreshService.ClearRenderedRange(sheet, pivotTable.LastRenderedRange);
            _snapshot.Restore(pivotTable, ctx.Workbook);
        }
        AddPivotTableCommand.Restore(sheet, _targetSnapshot);
        if (pivotTable is not null)
            PivotTableRefreshService.UpdateBoundPivotCharts(ctx.Workbook, sheet, pivotTable);
        _snapshot = null;
        _targetSnapshot = null;
    }

    /// <summary>
    /// A resolved table reference always yields that table's exact <see cref="StructuredTableModel.Range"/>
    /// (the dialogs/planner never carry a separate "this was a table" flag through to this command), so an
    /// exact range match against a live table on the source sheet is how a table-backed redirect is detected.
    /// </summary>
    private static StructuredTableModel? FindLiveTableByExactRange(Sheet sourceSheet, GridRange sourceRange)
    {
        foreach (var table in sourceSheet.StructuredTables)
        {
            if (table.Range.Equals(sourceRange))
                return table;
        }

        return null;
    }

    /// <summary>
    /// Builds the cache that should replace <paramref name="original"/> when an explicit "Change Data
    /// Source" crosses the table/range SourceType boundary (the only case that needs a whole new cache
    /// object, since SourceType is init-only). The table binding (SourceTableName/SourceTableId) always
    /// reflects the NEW source -- cleared entirely when <paramref name="matchedTable"/> is null
    /// (redirected to a plain range) and re-established from scratch when it is not (newly table-backed).
    /// Never carries the old table binding forward by accident.
    /// </summary>
    private static PivotCacheModel BuildRedirectedCache(
        PivotCacheModel original,
        Sheet sourceSheet,
        GridRange sourceRange,
        StructuredTableModel? matchedTable,
        PivotCacheSourceType desiredType,
        List<string> headers)
    {
        var redirected = new PivotCacheModel
        {
            CacheId = original.CacheId,
            SourceType = desiredType,
            SourceSheetName = sourceSheet.Name,
            SourceReference = sourceRange.ToString(),
            SourceTableName = matchedTable?.Name,
            SourceTableId = matchedTable?.Id,
            PackagePart = original.PackagePart,
            ConnectionId = original.ConnectionId,
            IsOlap = original.IsOlap,
            RefreshOnLoad = original.RefreshOnLoad,
            SaveData = original.SaveData,
            EnableRefresh = original.EnableRefresh,
            PreserveSourceSortFilter = original.PreserveSourceSortFilter,
            MissingItemsLimit = original.MissingItemsLimit,
            RecordCount = original.RecordCount,
            CreatedVersion = original.CreatedVersion,
            MinRefreshableVersion = original.MinRefreshableVersion,
            RefreshedVersion = original.RefreshedVersion,
            RefreshedBy = original.RefreshedBy,
            RefreshedDateIso = original.RefreshedDateIso,
            RawRecordsXml = original.RawRecordsXml,
        };

        // R116-commands-pivot-slicer-changesource: same as the same-SourceType branch above --
        // RECONCILE against the ORIGINAL cache's fields (by name) via PivotCacheFieldFactory.
        // ReconcileFields rather than unconditionally rebuilding every field from scratch, so a field
        // that survives the SourceType crossing (e.g. redirecting a range-backed pivot onto a table
        // covering the same columns) keeps its existing SharedItems order/index -- a pivot-bound
        // slicer's SlicerModel.CacheItems[].Index must keep meaning what it always meant even when the
        // crossing forced a whole new PivotCacheModel object.
        redirected.Fields.AddRange(PivotCacheFieldFactory.ReconcileFields(original.Fields, headers, sourceSheet, sourceRange));

        return redirected;
    }

    private static List<string> ReadHeaders(Sheet sheet, GridRange sourceRange)
    {
        var headers = new List<string>();
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            var value = sheet.GetValue(sourceRange.Start.Row, col);
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private sealed record PivotSourceSnapshot(
        GridRange SourceRange,
        GridRange? LastRenderedRange,
        PivotCacheModel? OriginalCache,
        PivotCacheSourceType? OriginalCacheSourceType,
        string? OriginalCacheSourceSheetName,
        string? OriginalCacheSourceReference,
        string? OriginalCacheSourceTableName,
        int? OriginalCacheSourceTableId,
        IReadOnlyList<PivotCacheFieldModel> OriginalCacheFields)
    {
        public static PivotSourceSnapshot Capture(PivotTableModel pivotTable, PivotCacheModel? cache) =>
            new(
                pivotTable.SourceRange,
                pivotTable.LastRenderedRange,
                cache,
                cache?.SourceType,
                cache?.SourceSheetName,
                cache?.SourceReference,
                cache?.SourceTableName,
                cache?.SourceTableId,
                cache?.Fields.ToList() ?? []);

        /// <summary>
        /// Restores the exact prior cache state -- including SourceType (init-only, so it can only ever
        /// be restored by putting the untouched original object back, never by field assignment) and
        /// whether SourceTableId had been established yet (null) or was already pinned to a stable id (an
        /// int), which are two different states a naive field-by-field restore could conflate.
        ///
        /// If Apply mutated the cache in place (no SourceType crossing), <see cref="OriginalCache"/> is
        /// still the very same object currently sitting in <paramref name="workbook"/>'s PivotCaches --
        /// its SourceType is unchanged, so restoring its mutable fields in place here matches the
        /// pre-fix command's behavior (any external reference captured before Apply observes the revert
        /// too). If Apply instead swapped in a replacement cache to cross the table/range boundary, the
        /// CURRENT cache's SourceType differs from the captured one, and the only correct undo is putting
        /// the exact original object back.
        /// </summary>
        public void Restore(PivotTableModel pivotTable, Workbook workbook)
        {
            pivotTable.SourceRange = SourceRange;
            pivotTable.LastRenderedRange = LastRenderedRange;
            if (OriginalCache is null)
                return;

            var index = workbook.PivotCaches.FindIndex(existing => existing.CacheId == OriginalCache.CacheId);
            if (index < 0)
                return;

            if (workbook.PivotCaches[index].SourceType == OriginalCacheSourceType)
            {
                var current = workbook.PivotCaches[index];
                current.SourceSheetName = OriginalCacheSourceSheetName;
                current.SourceReference = OriginalCacheSourceReference;
                current.SourceTableName = OriginalCacheSourceTableName;
                current.SourceTableId = OriginalCacheSourceTableId;
                current.Fields.Clear();
                current.Fields.AddRange(OriginalCacheFields);
            }
            else
            {
                workbook.PivotCaches[index] = OriginalCache;
            }
        }
    }
}

