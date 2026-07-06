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
            var liveTable = FindStructuredTableByName(workbook, cache.SourceTableName);
            if (liveTable is not null)
            {
                sourceSheet = workbook.GetSheet(liveTable.Range.Start.Sheet) ?? sourceSheet;
                pivotTable.SourceRange = liveTable.Range;
                cache.SourceReference = liveTable.Range.ToString();
                cache.SourceSheetName = sourceSheet.Name;
            }
        }

        ClearRefreshRanges(targetSheet, pivotTable);

        var headers = ReadHeaders(sourceSheet, pivotTable.SourceRange);
        var columnFields = pivotTable.ColumnFields.ToList();
        if (!pivotTable.RowFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !pivotTable.PageFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !columnFields.All(field => IsValidField(field.SourceFieldIndex, headers.Count)) ||
            !pivotTable.DataFields.All(field => IsValidDataField(field, pivotTable, headers.Count)))
        {
            pivotTable.LastRenderedRange = null;
            return;
        }

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
        sheet.SetCell(address, value);
        CurrentRenderFootprint.Value?.Include(address);
    }

    private static void SetPivotCell(Sheet sheet, CellAddress address, Cell cell)
    {
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

    private static bool IsValidField(int index, int fieldCount) => index >= 0 && index < fieldCount;

    private static bool IsValidDataField(PivotDataFieldModel field, PivotTableModel pivotTable, int fieldCount) =>
        IsValidField(field.SourceFieldIndex, fieldCount) ||
        (!string.IsNullOrWhiteSpace(field.CalculatedFieldName) &&
         pivotTable.CalculatedFields.Any(calculated =>
             string.Equals(calculated.Name, field.CalculatedFieldName, StringComparison.OrdinalIgnoreCase)));
}
