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

    public static void Refresh(Workbook workbook, Sheet targetSheet, PivotTableModel pivotTable)
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
                // or misreads on open.
                ReconcileCacheFields(cache, ReadHeaders(sourceSheet, liveTable.Range), sourceSheet, liveTable.Range);
            }
        }

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);

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

        uint? minRow = null;
        uint? minCol = null;
        uint? maxRow = null;
        uint? maxCol = null;

        for (var row = pivotTable.TargetRange.Start.Row; row <= pivotTable.TargetRange.End.Row; row++)
        for (var col = pivotTable.TargetRange.Start.Col; col <= pivotTable.TargetRange.End.Col; col++)
        {
            if (sheet.GetCell(row, col) is null)
                continue;

            minRow = minRow is null ? row : Math.Min(minRow.Value, row);
            minCol = minCol is null ? col : Math.Min(minCol.Value, col);
            maxRow = maxRow is null ? row : Math.Max(maxRow.Value, row);
            maxCol = maxCol is null ? col : Math.Max(maxCol.Value, col);
        }

        if (minRow is null || minCol is null || maxRow is null || maxCol is null)
            return new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);

        return new GridRange(
            new CellAddress(sheet.Id, minRow.Value, minCol.Value),
            new CellAddress(sheet.Id, maxRow.Value, maxCol.Value));
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
    /// R94-app-pivot-cache-5-1: reconciles <paramref name="cache"/>'s cacheFields list to match the
    /// live table's current header set, matching ChangePivotTableSourceCommand's explicit
    /// "Change Data Source" reconciliation. Fields whose name still matches at the same position keep
    /// their existing metadata (grouping/sharedItems/number-format) rather than being rebuilt from
    /// scratch, so a table resize that doesn't touch a surviving column's header doesn't discard that
    /// column's grouping. A no-op fast path (headers already match by name and position) avoids
    /// churning cache.Fields identity on every ordinary refresh.
    /// </summary>
    private static void ReconcileCacheFields(PivotCacheModel cache, IReadOnlyList<string> liveHeaders, Sheet sourceSheet, GridRange sourceRange)
    {
        if (cache.Fields.Count == liveHeaders.Count)
        {
            var alreadyInSync = true;
            for (var i = 0; i < liveHeaders.Count; i++)
            {
                if (!string.Equals(cache.Fields[i].Name, liveHeaders[i], StringComparison.Ordinal))
                {
                    alreadyInSync = false;
                    break;
                }
            }

            if (alreadyInSync)
                return;
        }

        var existingByName = new Dictionary<string, PivotCacheFieldModel>(StringComparer.Ordinal);
        foreach (var field in cache.Fields)
            existingByName.TryAdd(field.Name, field);

        var reconciled = new List<PivotCacheFieldModel>(liveHeaders.Count);
        for (var index = 0; index < liveHeaders.Count; index++)
        {
            var header = liveHeaders[index];
            // R114-commands-pivot-sharedItems: a header that has no existing same-named field (a truly
            // new column the table grew into) must get its SharedItems populated from the live source
            // data the same way a brand-new pivot's cache does -- otherwise a slicer added against this
            // newly-appeared field would have no filter items, exactly like the brand-new-pivot case.
            reconciled.Add(existingByName.TryGetValue(header, out var existing)
                ? existing
                : PivotCacheFieldFactory.BuildFromSourceData(header, sourceSheet, sourceRange, index));
        }

        cache.Fields.Clear();
        cache.Fields.AddRange(reconciled);
    }
}
