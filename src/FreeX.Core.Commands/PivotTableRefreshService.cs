using System.Globalization;
using System.Threading;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static readonly AsyncLocal<PivotRenderFootprint?> CurrentRenderFootprint = new();

    /// <summary>
    /// Materializes each loaded pivot table's built-in style (e.g. PivotStyleLight16) onto the cells
    /// already present for it, WITHOUT recomputing the pivot output.  Excel applies pivot styles
    /// dynamically and does not bake them into per-cell styles, so a pivot read from xlsx arrives with
    /// correct values but no header/group/subtotal/grand-total/banding formatting; this applies that
    /// formatting the same way a refresh would, so a loaded pivot looks like it does in Excel.
    /// Returns true if any pivot was styled.  Best-effort per pivot — a malformed pivot is skipped
    /// rather than failing the whole open.
    /// </summary>
    public static bool ApplyLoadedPivotStyles(Workbook workbook)
    {
        var styledAny = false;
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var pivotTable in sheet.PivotTables)
            {
                if (string.IsNullOrEmpty(pivotTable.StyleName))
                    continue;

                try
                {
                    ApplyPivotTableStyle(
                        workbook,
                        sheet,
                        pivotTable,
                        preserveExistingVisualStyles: true,
                        styleNameOverride: pivotTable.StyleName);
                    styledAny = true;
                }
                catch
                {
                    // A single malformed pivot must not break opening the workbook.
                }
            }
        }

        return styledAny;
    }

    /// <param name="rescanCacheSharedItems">
    /// R116-commands-pivot-refresh-scope: R115 made <see cref="ReconcileCacheFields"/> re-derive every
    /// SURVIVING cache field's SharedItems from a full row-by-row scan of that field's entire source
    /// column (<see cref="PivotCacheFieldFactory.MergeFromSourceData"/> -&gt; BuildFromSourceData),
    /// unconditionally, on every call to this method -- but <see cref="Refresh"/> is not only the F5 /
    /// "Refresh PivotTable" entry point (<see cref="RefreshPivotTableCommand"/>, the only caller that
    /// passes <see langword="true"/> here): it is also the choke point every OTHER pivot-mutating
    /// command funnels through, including a single slicer/timeline selection click
    /// (<c>PivotTableSlicerCommands</c>/<c>PivotTableSlicerTimelineCommands</c>), page/label/value filter
    /// changes, layout/view/options changes, calculated-item edits, rename/move/clear-view, and inserting
    /// a pivot chart. None of those mutate a single source cell, so re-scanning every field's entire
    /// source column on each of them added an O(fieldCount * rowCount) cost -- on top of the pivot's own
    /// O(rowCount) recompute this method already does -- to what is meant to be an instant, frequent,
    /// interactive UI action; Excel does not re-derive shared items for a slicer click or a filter change
    /// on an already-refreshed pivot. Defaulting to <see langword="false"/> means every one of those
    /// non-refresh callers automatically gets the cheap path without having to remember an argument (the
    /// new default IS the fix); only a genuine "the source data may have changed" refresh opts in.
    /// <see cref="ChangePivotTableSourceCommand"/> also leaves this <see langword="false"/> -- it already
    /// reconciles cache.Fields itself against the NEW source before calling <see cref="Refresh"/>, so
    /// asking this method to redo it here would just be the exact same O(fieldCount * rowCount) scan
    /// twice for one "Change Data Source" action. This flag only governs the expensive
    /// <c>ReconcileCacheFields</c> rescan -- the cheap, always-safe <c>ExtendBoundSlicerCacheItems</c>
    /// step (R118) runs regardless of this flag, so a slicer stays in sync however cache.Fields' shared
    /// items grew.
    /// </param>
    public static void Refresh(Workbook workbook, Sheet targetSheet, PivotTableModel pivotTable, bool rescanCacheSharedItems = false)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null || pivotTable.DataFields.Count == 0)
            return;

        // N32: a table-backed pivot must track its source Table's current extent on every refresh,
        // not just at creation / explicit "Change Data Source" time — Excel always re-resolves a
        // table-backed pivot cache against the live ListObject range before recomputing. Re-derive
        // SourceRange from the live structured table (if it still exists) so rows/columns the table
        // grew into since the pivot was last refreshed are included.
        var cache = CommandGuards.FindPivotCache(workbook, pivotTable);
        if (cache is { SourceType: PivotCacheSourceType.Table } && !string.IsNullOrWhiteSpace(cache.SourceTableName))
        {
            // R104: once this cache has a stable SourceTableId, that id — not the current name
            // string — is what identifies "the same table". A name-only lookup here would silently
            // re-bind the pivot to a completely unrelated table that later happens to reuse a freed
            // name (e.g. after the original table was "Converted to Range" and a different table was
            // renamed onto the now-free name). Resolve by id when we have one; only fall back to the
            // name-based lookup while no id has been established yet (a cache just loaded from a file,
            // whose OOXML/JSON source carries only the name) — and lock in the id the first time that
            // succeeds, exactly like SlicerModel.SourceTableId is established for table slicers.
            var liveTable = cache.SourceTableId is { } sourceTableId
                ? FindStructuredTableById(workbook, sourceTableId)
                : FindStructuredTableByName(workbook, cache.SourceTableName);
            if (liveTable is not null)
            {
                cache.SourceTableId ??= liveTable.Id;
                cache.SourceTableName = liveTable.Name;
                sourceSheet = workbook.GetSheet(liveTable.Range.Start.Sheet) ?? sourceSheet;
                pivotTable.SourceRange = liveTable.Range;
                cache.SourceReference = liveTable.Range.ToString();
                cache.SourceSheetName = sourceSheet.Name;

                // R94-app-pivot-cache-5-1: the table's column count may have changed (e.g. a
                // structured-table resize that narrows it) since the cache's cacheFields were last
                // built. cache.Fields must track the live header set exactly the same way
                // ChangePivotTableSourceCommand reconciles it on an explicit "Change Data Source" --
                // otherwise cache.Fields keeps its old (possibly wider) count forever, and
                // XlsxPivotTableWriter emits a <cacheFields count="N"> that no longer matches the
                // narrower field-count it writes into each <pivotCacheRecords><r> (which re-resolves
                // against the current, live source range), producing a corrupt cache Excel repairs
                // or misreads on open. (The reconciliation call itself now lives below, after
                // `headers` is computed against the just-updated SourceRange, so it also runs for a
                // plain worksheet-range cache -- see R115-commands-pivot-sharedItems-refresh.)
            }
        }

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);

        // R115-commands-pivot-sharedItems-refresh: R114 only recomputed a cache field's SharedItems
        // at pivot creation, "Change Data Source", and (via the table-growth branch above) when a
        // table-backed source's column set changed. An ORDINARY refresh -- the F5 / Refresh-All path
        // every other Apply() that mutates source data funnels through -- reused the existing field
        // object verbatim for any header that still matched by name, so a field's SharedItems (and
        // hence SlicerItemResolver.ResolveAvailableItems, the live-UI/render entry point) stayed
        // frozen at whatever it was when the pivot was created or last had its source redirected,
        // even though the underlying cell values kept changing. Worse, this reconciliation was only
        // ever invoked for a table-backed cache (SourceType.Table): a plain worksheet-range pivot
        // cache never called it at all, so its SharedItems were frozen at creation-time forever.
        // Excel re-derives shared items on every refresh (independent of the "number of items to
        // retain" stale-item-retention setting, which controls whether OLD items linger, not whether
        // NEW ones appear).
        //
        // R116-commands-pivot-refresh-scope: "on every refresh" above means every call where the
        // SOURCE DATA may genuinely have changed since the cache was last reconciled -- NOT every call
        // to this method. This method is the choke point for every pivot-mutating command (slicer/
        // timeline clicks, page/label/value filters, layout/view/options, calculated items, rename/
        // move/clear-view, pivot-chart insert), none of which touch a source cell, and each of those
        // was paying the SAME full per-field source-column rescan a genuine refresh does -- an
        // O(fieldCount * rowCount) cost on what must be an instant, frequent, interactive action (a
        // single slicer button click). Real Excel does not rescan the underlying range for a slicer
        // click or a filter change on an already-refreshed pivot, so this only runs when the caller
        // affirmatively asks for it via rescanCacheSharedItems (see that parameter's doc comment for
        // exactly which callers do and don't).
        if (cache is not null && rescanCacheSharedItems)
        {
            ReconcileCacheFields(cache, headers, sourceSheet, pivotTable.SourceRange);
        }

        // R118-commands-pivot-slicer-changesource: R117 put ExtendBoundSlicerCacheItems behind the
        // SAME rescanCacheSharedItems gate as the expensive ReconcileCacheFields rescan above, on the
        // assumption that "cache.Fields' SharedItems changed" only ever happens on that gated path.
        // That is false: ChangePivotTableSourceCommand (an ordinary "Change Data Source" action, not a
        // rare edge case) reconciles cache.Fields itself via PivotCacheFieldFactory.ReconcileFields
        // BEFORE calling this method -- deliberately passing rescanCacheSharedItems: false to avoid
        // redoing that same O(fieldCount * rowCount) scan a second time here -- so a field's
        // SharedItems can grow with a genuinely new distinct value from the new/wider source range
        // while this method never learns about it, leaving any slicer bound to that field unable to
        // ever surface the new item (the exact bug R117 fixed for the F5/Refresh path, reopened on
        // this second entry point). Unlike ReconcileCacheFields, ExtendBoundSlicerCacheItems does NOT
        // rescan the source column -- it only walks cache.Fields (already in memory) and each bound
        // slicer's existing CacheItems, so it costs nothing extra to run unconditionally: when
        // SharedItems genuinely didn't change (the common slicer-click/filter/layout callers), every
        // index it would add already exists and the loop is a same-length no-op. So this always runs
        // whenever a cache is present, regardless of which path grew cache.Fields' SharedItems.
        if (cache is not null)
        {
            ExtendBoundSlicerCacheItems(workbook, pivotTable, cache);
        }

        // R92-app-pivot-drilldown-5-3: a source shrink (columns deleted, or an entire field's
        // backing column gone) can leave some fields' SourceFieldIndex pointing past the new
        // header count. Excel drops those now-invalid fields from the layout and recomputes
        // cleanly rather than leaving the previous stale render in place or erroring -- so prune
        // them from the *live* field lists (mirroring a field falling out of the layout) before
        // anything on the sheet is touched. This must happen before ClearRefreshRanges: computing
        // validity first (rather than clearing unconditionally and only then checking) is what
        // stops a still-renderable pivot from ending up as a permanently blank hole when nothing
        // actually needed to be dropped, or from being cleared and abandoned when everything did.
        pivotTable.RowFields.RemoveAll(field => !IsValidField(field.SourceFieldIndex, headers.Count));
        pivotTable.PageFields.RemoveAll(field => !IsValidField(field.SourceFieldIndex, headers.Count));
        pivotTable.ColumnFields.RemoveAll(field => !IsValidField(field.SourceFieldIndex, headers.Count));
        pivotTable.DataFields.RemoveAll(field => !IsValidDataField(field, pivotTable, headers.Count));

        if (pivotTable.DataFields.Count == 0)
        {
            // Nothing left to compute a values grid from (every data field's source column is
            // gone). Matches the "no data fields configured" guard at the top of this method:
            // leave whatever is already rendered on the sheet untouched rather than blanking it.
            return;
        }

        var columnFields = pivotTable.ColumnFields.ToList();

        ClearRefreshRanges(targetSheet, pivotTable);

        var rows = ReadSourceRows(sourceSheet, pivotTable.SourceRange, headers.Count)
            .Where(row => MatchesFieldSelections(row, pivotTable.PageFields))
            .Where(row => MatchesFieldSelections(row, pivotTable.RowFields))
            .Where(row => MatchesFieldSelections(row, columnFields))
            .ToList();

        var previousFootprint = CurrentRenderFootprint.Value;
        var footprint = new PivotRenderFootprint(targetSheet.Id);
        CurrentRenderFootprint.Value = footprint;
        try
        {
            WritePageFields(targetSheet, pivotTable, headers);

            if (pivotTable.RowFields.Count == 0 && columnFields.Count == 0)
                WriteValuesOnlyPivot(workbook, targetSheet, pivotTable, headers, rows);
            else if (pivotTable.RowFields.Count == 0)
                WriteColumnOnlyPivot(workbook, targetSheet, pivotTable, headers, rows, columnFields);
            else if (columnFields.Count > 0)
                WriteMatrixPivot(workbook, targetSheet, pivotTable, headers, rows, columnFields);
            else
                WriteRowPivot(workbook, targetSheet, pivotTable, headers, rows);

            ApplyPivotTableStyle(workbook, targetSheet, pivotTable);
            ApplyMergedRowLabels(workbook, targetSheet, pivotTable);
            pivotTable.LastRenderedRange = footprint.ToGridRange() ??
                new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);
        }
        finally
        {
            CurrentRenderFootprint.Value = previousFootprint;
        }
    }

    public static GridRange GetMaterializedOutputRange(Sheet sheet, PivotTableModel pivotTable)
    {
        if (CurrentRenderFootprint.Value is { } footprint &&
            footprint.TryGetRange(sheet.Id, out var trackedRange))
        {
            return trackedRange;
        }

        if (pivotTable.LastRenderedRange is { } lastRenderedRange &&
            lastRenderedRange.Start.Sheet == sheet.Id)
        {
            return lastRenderedRange;
        }

        return PivotTableTargetRangeResolver.GetOccupiedRange(sheet, pivotTable);
    }

    /// <summary>
    /// R134-commands-pivotchart-stale-datarange: a bound PivotChart's <see cref="ChartModel.DataRange"/>/
    /// <see cref="ChartModel.PivotCacheId"/> must track the pivot's CURRENT materialized output range
    /// after every mutation that can move/resize/re-source that output (field add/remove/move, grouping,
    /// filters/sorts, options, calculated items, Change Data Source, rename, move, clear-view, refresh --
    /// and, this fix's own target, a slicer/timeline selection change), or the chart keeps rendering
    /// whatever cells the pivot happened to occupy at some earlier point -- stale and silently
    /// inconsistent with the pivot right next to it. This is the single shared implementation of the
    /// per-command "sync bound charts" step. Pivot mutation commands route through this method, either
    /// directly or through <see cref="PivotTableCommandRefreshTransaction"/>, so new callers do not
    /// need to reproduce the chart scan and binding updates.
    /// A PivotChart can only ever live on the SAME sheet as its source pivot table (both
    /// <c>MoveChartCommand</c> and <c>MoveChartToNewSheetCommand</c> reject <c>IsPivotChart</c> charts
    /// outright), but this still scans every sheet -- matching the majority of the existing per-command
    /// copies -- so it stays correct even if that invariant is ever relaxed.
    /// </summary>
    public static void UpdateBoundPivotCharts(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        var outputRange = GetMaterializedOutputRange(sheet, pivotTable);
        foreach (var chartSheet in workbook.Sheets)
        foreach (var chart in chartSheet.Charts.Where(chart =>
                     chart.IsPivotChart &&
                     string.Equals(chart.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase)))
        {
            chart.DataRange = outputRange;
            chart.PivotCacheId = pivotTable.CacheId;
        }
    }

    private static int RowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static CellAddress GetPivotBodyStart(PivotTableModel pivotTable)
    {
        var start = pivotTable.TargetRange.Start;
        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        return pageFieldRows == 0
            ? start
            : new CellAddress(start.Sheet, start.Row + pageFieldRows + 1, start.Col);
    }

    private static uint GetPageFieldRowSpan(PivotTableModel pivotTable)
    {
        // H10: fields added only to carry a slicer/timeline filter for an unplaced field don't get a
        // visible Filters-area row (see PivotFieldModel.IsUnplacedFilterField / WritePageFields), so
        // they must not reserve rows for the pivot body either.
        var count = pivotTable.PageFields.Count(field => !field.IsUnplacedFilterField);
        if (count == 0)
            return 0;

        var wrap = Math.Max(0, pivotTable.PageWrap);
        if (pivotTable.PageOverThenDown)
            return (uint)(wrap <= 0 ? 1 : (int)Math.Ceiling(count / (double)wrap));

        return (uint)(wrap <= 0 ? count : Math.Min(count, wrap));
    }

    private static IReadOnlyList<string> ReadHeaders(Sheet sheet, GridRange range)
    {
        var headers = new List<string>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var value = sheet.GetCell(range.Start.Row, col)?.Value;
            headers.Add(value is TextValue text && !string.IsNullOrWhiteSpace(text.Value)
                ? text.Value
                : $"Field{headers.Count + 1}");
        }

        return headers;
    }

    private static IEnumerable<IReadOnlyList<ScalarValue>> ReadSourceRows(Sheet sheet, GridRange range, int fieldCount)
    {
        for (var row = range.Start.Row + 1; row <= range.End.Row; row++)
        {
            var values = new ScalarValue[fieldCount];
            for (var index = 0; index < fieldCount; index++)
            {
                var col = range.Start.Col + (uint)index;
                values[index] = sheet.GetCell(row, col)?.Value ?? BlankValue.Instance;
            }

            yield return values;
        }
    }

    private static void ClearTargetRange(Sheet sheet, GridRange targetRange)
    {
        sheet.ReplaceMergedRegions(sheet.MergedRegions.Where(region => !region.Overlaps(targetRange)));

        for (var row = targetRange.Start.Row; row <= targetRange.End.Row; row++)
        for (var col = targetRange.Start.Col; col <= targetRange.End.Col; col++)
            sheet.ClearCell(row, col);
    }

    private static void ClearRefreshRanges(Sheet sheet, PivotTableModel pivotTable)
    {
        var previousRange = pivotTable.LastRenderedRange is { } previous &&
            previous.Start.Sheet == sheet.Id
            ? previous
            : (GridRange?)null;

        if (previousRange is { } previousOnSheet)
            ClearRenderedRange(sheet, previousOnSheet);

        if (previousRange is null ||
            previousRange.Value != pivotTable.TargetRange &&
            previousRange.Value.Start != pivotTable.TargetRange.Start)
        {
            ClearRenderedRange(sheet, pivotTable.TargetRange);
        }
    }

    internal static void ClearRenderedRange(Sheet sheet, GridRange? range)
    {
        if (range is { } rangeOnSheet && rangeOnSheet.Start.Sheet == sheet.Id)
            ClearTargetRange(sheet, rangeOnSheet);
    }

    private static void SetPivotCell(Sheet sheet, CellAddress address, ScalarValue value)
    {
        // R106: a destination cell that is a non-anchor (hidden/covered) member of a pre-existing
        // merged region must stay empty -- only a merge's top-left anchor cell ever carries a
        // value, exactly like PasteCellsCommand's identical guard. ClearRefreshRanges only ever
        // un-merges inside the pivot's PREVIOUSLY known footprint (LastRenderedRange/TargetRange);
        // it cannot know the new render's actual extent up front, so a pivot that grows (more row
        // groups, more columns, a move, etc.) could otherwise silently plant a hidden value into
        // someone else's merged cell that was never part of the old footprint. Every pivot body/
        // header write funnels through this pair of methods, so guarding here protects all of them
        // without every writer having to remember its own check.
        if (sheet.GetMergeRegion(address) is { } mergeRegion && !mergeRegion.Start.Equals(address))
            return;

        sheet.SetCell(address, value);
        CurrentRenderFootprint.Value?.Include(address);
    }

    private static void SetPivotCell(Sheet sheet, CellAddress address, Cell cell)
    {
        if (sheet.GetMergeRegion(address) is { } mergeRegion && !mergeRegion.Start.Equals(address))
            return;

        sheet.SetCell(address, cell);
        CurrentRenderFootprint.Value?.Include(address);
    }

    private sealed class PivotRenderFootprint
    {
        private readonly SheetId _sheetId;
        private uint? _minRow;
        private uint? _minCol;
        private uint? _maxRow;
        private uint? _maxCol;

        // Per-row compact indent levels: row → indent. Populated by WriteRowPivot
        // for compact layout with >1 row field. Null means use legacy flat-indent path.
        public Dictionary<uint, int>? CompactRowIndentLevels { get; set; }

        public PivotRenderFootprint(SheetId sheetId)
        {
            _sheetId = sheetId;
        }

        public void Include(CellAddress address)
        {
            if (address.Sheet != _sheetId)
                return;

            _minRow = _minRow is null ? address.Row : Math.Min(_minRow.Value, address.Row);
            _minCol = _minCol is null ? address.Col : Math.Min(_minCol.Value, address.Col);
            _maxRow = _maxRow is null ? address.Row : Math.Max(_maxRow.Value, address.Row);
            _maxCol = _maxCol is null ? address.Col : Math.Max(_maxCol.Value, address.Col);
        }

        public bool TryGetRange(SheetId sheetId, out GridRange range)
        {
            if (sheetId == _sheetId && ToGridRange() is { } gridRange)
            {
                range = gridRange;
                return true;
            }

            range = default;
            return false;
        }

        public GridRange? ToGridRange()
        {
            if (_minRow is null || _minCol is null || _maxRow is null || _maxCol is null)
                return null;

            return new GridRange(
                new CellAddress(_sheetId, _minRow.Value, _minCol.Value),
                new CellAddress(_sheetId, _maxRow.Value, _maxCol.Value));
        }
    }

    private static StructuredTableModel? FindStructuredTableByName(Workbook workbook, string tableName)
    {
        foreach (var sheet in workbook.Sheets)
        foreach (var table in sheet.StructuredTables)
        {
            if (string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(table.DisplayName, tableName, StringComparison.OrdinalIgnoreCase))
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>
    /// R104: resolves a table-backed pivot cache's live source by its stable
    /// <see cref="StructuredTableModel.Id"/>, never by name. Deliberately has no name-based fallback:
    /// once a cache has an id, a table elsewhere in the workbook that merely shares its old name is NOT
    /// the same table, and must not be treated as one (see <see cref="PivotCacheModel.SourceTableId"/>).
    /// </summary>
    private static StructuredTableModel? FindStructuredTableById(Workbook workbook, int tableId)
    {
        foreach (var sheet in workbook.Sheets)
        foreach (var table in sheet.StructuredTables)
        {
            if (table.Id == tableId)
                return table;
        }

        return null;
    }

    private static bool IsValidField(int index, int fieldCount) => index >= 0 && index < fieldCount;

    private static bool IsValidDataField(PivotDataFieldModel field, PivotTableModel pivotTable, int fieldCount) =>
        IsValidField(field.SourceFieldIndex, fieldCount) ||
        (!string.IsNullOrWhiteSpace(field.CalculatedFieldName) &&
         pivotTable.CalculatedFields.Any(calculated =>
             string.Equals(calculated.Name, field.CalculatedFieldName, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// R94-app-pivot-cache-5-1 / R115-commands-pivot-sharedItems-refresh: reconciles
    /// <paramref name="cache"/>'s cacheFields list to match the live source's current header set,
    /// matching ChangePivotTableSourceCommand's explicit "Change Data Source" reconciliation, AND
    /// re-derives every SURVIVING field's <see cref="PivotCacheFieldModel.SharedItems"/> from the live
    /// column data on every call (not only when the header set itself changed) -- see
    /// <see cref="PivotCacheFieldFactory.MergeFromSourceData"/> for why this is a merge rather than a
    /// full rebuild. Deliberately has NO "headers already match" fast-path early return any more: that
    /// used to skip this entirely on the overwhelmingly common ordinary-refresh case (same header
    /// names, only the underlying values changed), which is exactly the staleness this method now
    /// exists to prevent. A field whose header has no existing same-named match (a truly new column)
    /// still gets a brand-new field built from scratch, same as before.
    /// </summary>
    private static void ReconcileCacheFields(PivotCacheModel cache, IReadOnlyList<string> liveHeaders, Sheet sourceSheet, GridRange sourceRange)
    {
        // R116-commands-pivot-slicer-changesource: delegates to the shared choke point in
        // PivotCacheFieldFactory so this ordinary-refresh path and ChangePivotTableSourceCommand's
        // explicit "Change Data Source" path can never drift apart on how a surviving field's
        // SharedItems are reconciled (see PivotCacheFieldFactory.ReconcileFields for the full
        // rationale -- merge-and-preserve-order via MergeFromSourceData, not a full rebuild).
        var reconciled = PivotCacheFieldFactory.ReconcileFields(cache.Fields, liveHeaders, sourceSheet, sourceRange);
        cache.Fields.Clear();
        cache.Fields.AddRange(reconciled);
    }

    /// <summary>
    /// R117-commands-pivot-slicer-growth: appends a <see cref="SlicerCacheItem"/> to every slicer bound
    /// to <paramref name="pivotTable"/> (by <see cref="SlicerModel.SourcePivotTableName"/> +
    /// <see cref="SlicerModel.SourceFieldName"/>) for each index in the field's
    /// <see cref="PivotCacheFieldModel.SharedItems"/> that the slicer's existing
    /// <see cref="SlicerModel.CacheItems"/> does not yet represent. SharedItems is append-only
    /// (<see cref="PivotCacheFieldFactory.MergeFromSourceData"/>), so a brand-new distinct value always
    /// lands at a brand-new index at the END of that list -- this only ever APPENDS, in ascending index
    /// order, and never touches an existing entry's <see cref="SlicerCacheItem.Index"/>/
    /// <see cref="SlicerCacheItem.IsSelected"/>, so an untouched refresh (no new distinct values) is a
    /// complete no-op and a user's prior per-item selection/deselection survives unchanged.
    /// <para>
    /// R118-commands-pivot-slicer-includeNewItemsInFilter: a brand-new item does NOT unconditionally
    /// default to <c>IsSelected: true</c>. Excel's <c>pivotField/@includeNewItemsInFilter</c>
    /// (ECMA-376 §18.10.1.65; <see cref="PivotFieldModel.IncludeNewItemsInFilter"/>, default
    /// <see langword="false"/> when absent) governs exactly this situation for a field with a MANUAL
    /// FILTER already applied (at least one existing item deselected): a newly-appeared item is
    /// automatically SELECTED only when that flag is explicitly true; otherwise it is added deselected,
    /// so the user's deliberately-narrowed filter is not silently widened by data they never asked to
    /// see reappear. When the slicer currently has NO filter at all (every existing item selected),
    /// there is nothing to preserve, so the new item is still selected by default -- exactly like the
    /// pre-existing behavior and <c>AddSlicerCommand.BuildInitialCacheItems</c>'s own "all items
    /// selected" seed for a fresh slicer -- otherwise the slicer would spontaneously start filtering
    /// data it never filtered before. This never touches <see cref="SlicerModel.SelectedItems"/> itself
    /// -- only the resolver's projection of CacheItems into SelectedItems, which already only fires when
    /// SelectedItems is still empty -- so a user's existing explicit filter (captured via
    /// <c>SetSlicerSelectionCommand</c>) is completely unaffected.
    /// </para>
    /// <para>
    /// Deliberately skips a slicer whose CacheItems is currently empty: an empty CacheItems means either
    /// a table slicer (no pivot cache binding at all -- SourceFieldName wouldn't resolve a field here
    /// anyway) or a pivot slicer that was never seeded with cache items in the first place (a distinct,
    /// pre-existing gap this fix does not attempt to change the shape of).
    /// </para>
    /// </summary>
    private static void ExtendBoundSlicerCacheItems(Workbook workbook, PivotTableModel pivotTable, PivotCacheModel cache)
    {
        if (workbook.Slicers.Count == 0 || string.IsNullOrWhiteSpace(pivotTable.Name))
            return;

        foreach (var slicer in workbook.Slicers)
        {
            if (slicer.CacheItems.Count == 0)
                continue;
            if (!string.Equals(slicer.SourcePivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(slicer.SourceFieldName))
                continue;

            var fieldIndex = -1;
            PivotCacheFieldModel? field = null;
            for (var candidateIndex = 0; candidateIndex < cache.Fields.Count; candidateIndex++)
            {
                if (string.Equals(cache.Fields[candidateIndex].Name, slicer.SourceFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    fieldIndex = candidateIndex;
                    field = cache.Fields[candidateIndex];
                    break;
                }
            }
            if (field?.SharedItems is not { Count: > 0 } sharedItems)
                continue;

            var existingIndices = new HashSet<int>(slicer.CacheItems.Count);
            var hasManualFilter = false;
            foreach (var item in slicer.CacheItems)
            {
                existingIndices.Add(item.Index);
                if (!item.IsSelected)
                    hasManualFilter = true;
            }

            // No existing deselection => nothing to preserve => new items are selected by default, same
            // as before. A manual filter is present => only widen it when the field's own
            // includeNewItemsInFilter explicitly says to (Excel default is false/absent => preserve).
            var includeNewItems = !hasManualFilter ||
                (FindPivotField(pivotTable, fieldIndex)?.IncludeNewItemsInFilter ?? false);

            for (var index = 0; index < sharedItems.Count; index++)
            {
                if (existingIndices.Add(index))
                    slicer.CacheItems.Add(new SlicerCacheItem(index, IsSelected: includeNewItems));
            }
        }
    }

    /// <summary>
    /// Finds the <see cref="PivotFieldModel"/> for <paramref name="sourceFieldIndex"/> across whichever
    /// axis list (Row/Column/Page) it is currently placed in -- <c>IncludeNewItemsInFilter</c> and other
    /// per-field settings are carried on that axis-placement record, not on the pivot cache field itself
    /// (mirroring <c>XlsxPivotTableWriter.FindPivotField</c>'s identical lookup for the same reason: the
    /// OOXML <c>pivotField</c> element these settings round-trip through is independent of which axis,
    /// if any, currently hosts the field). Returns <see langword="null"/> for a field that is bound to a
    /// slicer but not currently placed on any axis (its filter settings, if it never was placed, do not
    /// exist to read).
    /// </summary>
    private static PivotFieldModel? FindPivotField(PivotTableModel pivotTable, int sourceFieldIndex) =>
        pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .LastOrDefault(field => field.SourceFieldIndex == sourceFieldIndex);
}
