using FreeX.Core.Model;
using System.Globalization;
using System.Text;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies or clears a value filter on a range by toggling Sheet.FilterHiddenRows.
/// Rows whose filter-column value is not in <c>allowedValues</c> are hidden.
/// Passing an empty/null <c>allowedValues</c> clears all hidden rows.
/// </summary>
public sealed class FilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;   // 0 = first column of the range
    private readonly IReadOnlyList<string> _allowedValues;

    private FilterUndoSnapshot _undoSnapshot;
    // R95-commands-filter-table-reband-undo-1: see StructuredTableBandingReflow.ReflowIfMatched --
    // RebandTable always repaints the table's ENTIRE data body with forceFill:true (MergeStyleOntoCell's
    // keepExistingFill is unconditionally false under forceFill), unconditionally overwriting any
    // explicit FillColor a user set on a body cell. Mirrors InsertRowsCommand/DeleteRowsCommand/
    // SortCommand's own _tableRebandSnapshot fields, which exist for the exact same reason.
    private List<(CellAddress Address, Cell? OldCell)>? _tableRebandSnapshot;
    // H18: when _range is a structured table's range, table.FilterColumns (the model
    // XlsxStructuredTableWriter actually serializes into the table's <autoFilter>/<filterColumn> XML)
    // must be kept in sync with the interactive filter, otherwise the filter is visibly applied but
    // silently lost the moment the workbook is saved and reopened. -1 = no table matched this range.
    private int _tableId = -1;
    private List<StructuredTableFilterColumnModel>? _previousTableFilterColumns;
    // R33-commands-autofilter-slicer-1: when _range is a plain worksheet-level AutoFilter range
    // (sheet.AutoFilter.Reference, not a structured table), sheet.AutoFilter.FilterColumns (the
    // model XlsxWorksheetAutoFilterXmlMapper.Save serializes into the worksheet's own
    // <autoFilter>/<filterColumn> XML) must likewise be kept in sync, otherwise value-list filters
    // vanish from the saved .xlsx.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;

    public string Label => _allowedValues.Count == 0 ? "Clear Filter" : "Apply Filter";

    public FilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        IReadOnlyList<string> allowedValues)
    {
        _sheetId = sheetId;
        _range   = range;
        _filterColOffset = filterColOffset;
        _allowedValues   = allowedValues ?? [];
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet    = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Reset();
        _undoSnapshot.CaptureIfNeeded(sheet);

        uint filterCol  = _range.Start.Col + _filterColOffset;

        // R38-commands-autofilter-advanced-2-1: Excel allows only one active AutoFilter criterion
        // per column, so applying (or clearing) a plain value-list filter here must relinquish any
        // rows still owned by a DIFFERENT filter mechanism (Top10/Average/custom-criterion/color) on
        // this SAME column — otherwise that mechanism's ColumnFilterOwnedRows entry is never cleared
        // and its hidden rows compound with (rather than get replaced by) this value-list filter,
        // and "Clear Filter From <Column>" (which runs this command with allowedValues=[]) never
        // fully unfilters a column that was actually filtered by one of those other mechanisms.
        // ClearColumnOwnedRange only un-hides rows no OTHER active column's filter still needs
        // hidden, and is a no-op when filterCol owns nothing (the common case).
        FilterHiddenRowUpdater.ClearColumnOwnedRange(sheet, filterCol, _range);

        // F8: a plain flat FilterHiddenRows set cannot represent "AND across columns" — hiding a row
        // for column A and then evaluating column B in isolation would un-hide rows that column A
        // hid but column B doesn't care about. Excel hides a row if it fails ANY active column's
        // filter (hidden set = union of every column's exclusions), so we track each column's
        // allowed-values set separately in sheet.ActiveValueFilterColumns and always recompute
        // FilterHiddenRows over the range from that full set of active columns, rather than mutating
        // the flat set from a single column's perspective.
        if (_allowedValues.Count == 0)
            sheet.ActiveValueFilterColumns.Remove(filterCol);
        else
            sheet.ActiveValueFilterColumns[filterCol] = _allowedValues;

        RecomputeHiddenRows(sheet, _range);

        ApplyToStructuredTableIfMatched(sheet);

        WorksheetAutoFilterColumnModel? newAutoFilterColumn = null;
        if (_allowedValues.Count > 0)
        {
            var (nonBlankValues, includeBlank) = SplitBlankSentinel(_allowedValues);
            newAutoFilterColumn = new WorksheetAutoFilterColumnModel((int)_filterColOffset, nonBlankValues, includeBlank);
        }

        _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            newAutoFilterColumn);

        // R91-meta-3: a filter can hide/show rows without moving any data, so a banded structured
        // table's stripes must re-flow around the newly-hidden rows exactly like they already do
        // after an Insert or a Sort (StructuredTableStyleService.RebandTable) — otherwise the
        // remaining visible rows keep their stale pre-filter stripe.
        // R95-commands-filter-table-reband-undo-1: capture the table's full data body immediately
        // before the reband repaints it (mirrors InsertRowsCommand/DeleteRowsCommand/SortCommand),
        // so Revert can restore any explicit user fill the reband's forceFill just overwrote.
        _tableRebandSnapshot = StructuredTableBandingReflow.ReflowIfMatched(ctx.Workbook, sheet, _range);

        return new CommandOutcome(true);
    }

    /// <summary>
    /// If <see cref="_range"/> is exactly a structured table's range (the shape
    /// AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange hands back for a table's header-cell
    /// filter dropdown), mirror the applied/cleared filter into that table's FilterColumns model so
    /// it round-trips through XlsxStructuredTableWriter instead of being silently dropped on save.
    /// </summary>
    private void ApplyToStructuredTableIfMatched(Sheet sheet)
    {
        for (var i = 0; i < sheet.StructuredTables.Count; i++)
        {
            var table = sheet.StructuredTables[i];
            if (!table.Range.Equals(_range))
                continue;

            _tableId = table.Id;
            _previousTableFilterColumns = [.. table.FilterColumns];

            var filterColumns = table.FilterColumns
                .Where(fc => fc.ColumnId != (int)_filterColOffset)
                .ToList();
            if (_allowedValues.Count > 0)
            {
                var (nonBlankValues, includeBlank) = SplitBlankSentinel(_allowedValues);
                filterColumns.Add(new StructuredTableFilterColumnModel((int)_filterColOffset, nonBlankValues, includeBlank));
            }
            filterColumns.Sort(static (a, b) => a.ColumnId.CompareTo(b.ColumnId));

            sheet.StructuredTables[i] = StructuredTableDesignCommandHelpers.CopyTable(table, filterColumns: filterColumns);
            return;
        }
    }

    /// <summary>
    /// R102-commands-filter-blank-sentinel-1: the checklist dropdown represents a selected
    /// '(Blanks)' entry as a literal "" sentinel inside <see cref="_allowedValues"/> (mirroring
    /// <see cref="FilterValueFormatter.ToText"/>'s <c>BlankValue =&gt; ""</c>), because a plain
    /// flat allowed-values list has no separate "include blank" slot of its own. But
    /// ECMA-376's CT_Filters schema (and every producer including Excel itself) represents
    /// "include blank cells in this AutoFilter selection" exclusively via the parent
    /// <c>&lt;filters blank="1"/&gt;</c> attribute -- never via an empty-string
    /// <c>&lt;filter val=""/&gt;</c> entry, which XlsxWorksheetAutoFilterXmlMapper/
    /// XlsxStructuredTableWriter would otherwise emit verbatim since they serialize every
    /// entry in Values unconditionally. Split the "" sentinel out here, at the one choke point
    /// both the worksheet-AutoFilter and structured-table model-construction call sites in this
    /// class go through, so neither can forget to convert it into IncludeBlank=true.
    /// </summary>
    private static (IReadOnlyList<string> Values, bool IncludeBlank) SplitBlankSentinel(IReadOnlyList<string> allowedValues)
    {
        if (!allowedValues.Contains(""))
            return (allowedValues, false);

        return ([.. allowedValues.Where(value => value.Length != 0)], true);
    }

    private static void RecomputeHiddenRows(Sheet sheet, GridRange range)
    {
        // table-semantics-F1: mirrors GetFilterableLastRow's header/totals-aware end bound below --
        // when range is a structured table's own Range loaded with headerRowCount="0" (a genuine,
        // round-tripped Excel feature; see StructuredReferenceResolver.HeaderRowCount() and
        // XlsxStructuredTableModelMapper.MaterializeFilters' own header-count-aware load path),
        // range.Start.Row IS ITSELF a data row, not a header, and must be evaluated against the
        // active filters like every other data row -- unconditionally starting at range.Start.Row + 1
        // permanently exempted that row from every filter recompute (and, via FilterHiddenRows
        // feeding RecalcEngine's SUBTOTAL, from the table's Totals Row aggregate too).
        uint firstRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, range);
        // R100-commands-filter-totalsrow-1: when range is a structured table's own Range with its
        // Totals Row shown, range.End.Row IS the Totals Row itself -- exclude it from the filterable
        // data set the same way GetDataBodyRowBounds already does for every other table-editing
        // command (Sort/InsertDeleteRows/InsertDeleteColumns) and ApplyStructuredTableFiltersCommand.
        uint endRow   = StructuredTableEditEffects.GetFilterableLastRow(sheet, range);

        // G7: Top10/Average/color/custom-criterion filters hide rows by mutating FilterHiddenRows
        // directly, without registering anything in ActiveValueFilterColumns. This recompute must
        // only ever decide the hidden state of rows it "owns" (sheet.ValueFilterHiddenRows, the
        // rows this very mechanism hid last time it ran) — any other row currently hidden was put
        // there by one of those other mechanisms and must survive this recompute untouched.
        if (sheet.ActiveValueFilterColumns.Count == 0)
        {
            FilterHiddenRowUpdater.ClearOwnedRows(sheet, range, sheet.ValueFilterHiddenRows);
            sheet.ValueFilterHiddenRows.Clear();
            return;
        }

        // Pre-build a matcher per active column so we don't rebuild one per row.
        var matchers = new (uint Col, FilterAllowedValueMatcher Matcher)[sheet.ActiveValueFilterColumns.Count];
        var i = 0;
        foreach (var (col, allowedValues) in sheet.ActiveValueFilterColumns)
        {
            matchers[i++] = (col, FilterAllowedValueMatcher.Create(allowedValues));
        }

        // Rows this mechanism owned BEFORE this recompute — only these may be un-hidden below.
        var previouslyOwnedRows = sheet.ValueFilterHiddenRows.Count == 0
            ? null
            : new HashSet<uint>(sheet.ValueFilterHiddenRows);
        sheet.ValueFilterHiddenRows.Clear();

        for (uint row = firstRow; row <= endRow; row++)
        {
            var shouldHide = false;
            foreach (var (col, matcher) in matchers)
            {
                var value = sheet.GetValue(row, col);
                var text  = FilterValueFormatter.ToText(value);
                if (!matcher.Contains(text))
                {
                    shouldHide = true;
                    break;
                }
            }

            if (shouldHide)
            {
                sheet.ValueFilterHiddenRows.Add(row);
                sheet.FilterHiddenRows.Add(row);
            }
            else if (previouslyOwnedRows is not null && previouslyOwnedRows.Contains(row))
            {
                // Only relinquish rows THIS mechanism previously hid, and only when no OTHER
                // active mechanism (a condition/average/top-bottom/color filter on ANY column,
                // tracked in sheet.ColumnFilterOwnedRows) still needs the row hidden. Mirrors
                // ApplyColumnOwnedVisibility's symmetric ownership check on the condition side
                // (finding R13-meta-3) — without it, clearing/loosening a value filter could
                // un-hide a row a still-active condition/color/Top-Bottom filter on another
                // column is responsible for hiding, breaking Excel's AND-across-columns semantics.
                if (!FilterHiddenRowUpdater.IsHiddenByAnyColumnOwnedFilter(sheet, row))
                    sheet.FilterHiddenRows.Remove(row);
            }
        }
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);

        if (!_undoSnapshot.HasSnapshot) return;
        _undoSnapshot.Restore(sheet);

        if (_tableId != -1 && _previousTableFilterColumns is not null &&
            CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
        {
            var table = sheet.StructuredTables[tableIndex];
            sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
                table, filterColumns: _previousTableFilterColumns);
        }

        // R91-meta-3: undoing a filter restores the previous hidden-row set, which just as much
        // changes which rows are visible as applying it did — the pre-reband cell snapshot captured
        // in Apply already reflects exactly the correct banding for that restored visibility (nothing
        // between the snapshot and the reband it preceded touches cell fills), so restoring it here
        // is both sufficient and — unlike re-running RebandTable a second time, which R95-commands-
        // filter-table-reband-undo-1 found would just as destructively re-overwrite any explicit user
        // fill Revert is trying to restore — non-destructive.
        StructuredTableBandingReflow.Restore(sheet, _tableRebandSnapshot);
    }
}

public sealed class CellFillColorFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly CellColor _fillColor;
    private FilterUndoSnapshot _undoSnapshot;
    // R87-commands-autofilter-sort-5-1: keep the worksheet AutoFilter's <colorFilter> filterColumn
    // model in sync with the interactively-applied Filter-by-Cell-Color criterion (mirrors the
    // value-list/Top10/Average/custom-criterion siblings in this file), otherwise the column's
    // filter-funnel icon never shows "active" (BuildActiveAutoFilterColumns/DecorateAutoFilterHeaderCell
    // only look at sheet.AutoFilter.FilterColumns) and the criterion is silently dropped on save/reload.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;
    // R107-commands-autofilter-table-color-sync-1: WorksheetAutoFilterColumnSync above is a no-op
    // whenever _range is a structured table's own Range (tables carry their own <autoFilter> rather
    // than a worksheet-level one) -- keep the TABLE's own FilterColumns model in sync too, mirroring
    // TopBottomFilterCommand/FilterConditionCommand's R106 fix for the same gap, otherwise a Filter by
    // Cell Color applied from a Table's header dropdown hides/shows rows live but is silently dropped
    // from the table's <autoFilter> XML on save/reload.
    private StructuredTableFilterColumnSnapshot? _tableFilterSnapshot;

    public string Label => "Filter by Cell Color";

    public CellFillColorFilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        CellColor fillColor)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _fillColor = fillColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;
        // R100-commands-filter-totalsrow-1: see FilterCommand.RecomputeHiddenRows -- exclude a
        // structured table's shown Totals Row from the filterable data set.
        var lastDataRow = StructuredTableEditEffects.GetFilterableLastRow(sheet, _range);
        // table-semantics-F1: see FilterCommand.RecomputeHiddenRows -- a structured table loaded
        // with headerRowCount="0" has NO header row, so its first row is itself a data row and must
        // be evaluated here too.
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range);
        for (uint row = firstDataRow; row <= lastDataRow; row++)
        {
            // filter-by-color-cf: resolve the color Excel would actually show for this cell,
            // including any conditional-formatting-driven fill, not just the cell's static stored
            // style — otherwise a CF-red cell never matches a "red" filter.
            var cell = sheet.GetCell(row, filterCol);
            var address = new CellAddress(_sheetId, row, filterCol);
            var fillColor = SortCommand.GetEffectiveColor(ctx.Workbook, sheet, address, cell, wantFill: true);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, fillColor == _fillColor);
        }

        _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new WorksheetAutoFilterColumnModel(
                ColumnId: (int)_filterColOffset,
                Values: [],
                IncludeBlank: false,
                CustomFilters: [],
                CustomFiltersAnd: false,
                CustomFiltersAndRaw: null,
                NativeCustomFiltersAttributes: null,
                Top10: null,
                DynamicFilter: null,
                ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: true, Color: _fillColor),
                IconFilter: null,
                DateGroups: [],
                NativeFiltersAttributes: null,
                NativeFilterXmls: []));

        // R107-commands-autofilter-table-color-sync-1: mirror the same colour criterion into the
        // owning structured table's FilterColumns model (a no-op when _range isn't a table's own
        // Range). See StructuredTableFilterColumnModel.ColorFilter's doc comment for why the dxfId
        // is resolved later, at save time, rather than here.
        _tableFilterSnapshot = StructuredTableFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new StructuredTableFilterColumnModel((int)_filterColOffset, Values: [])
            {
                ColorFilter = new WorksheetAutoFilterColorFilterModel(CellColor: true, Color: _fillColor)
            });

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);
        StructuredTableFilterColumnSync.Restore(sheet, _tableFilterSnapshot);
        _undoSnapshot.Restore(sheet);
    }
}

public sealed class CellNoFillColorFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private FilterUndoSnapshot _undoSnapshot;
    // R87-commands-autofilter-sort-5-1: see CellFillColorFilterCommand's field of the same name.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;
    // R107-commands-autofilter-table-color-sync-1: see CellFillColorFilterCommand's field of the same name.
    private StructuredTableFilterColumnSnapshot? _tableFilterSnapshot;

    public string Label => "Filter by No Fill";

    public CellNoFillColorFilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;
        // R100-commands-filter-totalsrow-1: see FilterCommand.RecomputeHiddenRows -- exclude a
        // structured table's shown Totals Row from the filterable data set.
        var lastDataRow = StructuredTableEditEffects.GetFilterableLastRow(sheet, _range);
        // table-semantics-F1: see FilterCommand.RecomputeHiddenRows -- a structured table loaded
        // with headerRowCount="0" has NO header row, so its first row is itself a data row and must
        // be evaluated here too.
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range);
        for (uint row = firstDataRow; row <= lastDataRow; row++)
        {
            // filter-by-color-cf: a CF-driven fill counts as "has a fill" here too, so a CF-red
            // cell must NOT wrongly match "No Fill".
            var cell = sheet.GetCell(row, filterCol);
            var address = new CellAddress(_sheetId, row, filterCol);
            var fillColor = SortCommand.GetEffectiveColor(ctx.Workbook, sheet, address, cell, wantFill: true);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, fillColor is null);
        }

        // No dxfId: per the colorFilter schema, omitting dxfId while cellColor="1" is set is the
        // exact (not approximate) representation of "No Fill" -- unlike an actual chosen fill/font
        // color, "no fill" has no format record to reference.
        _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new WorksheetAutoFilterColumnModel(
                ColumnId: (int)_filterColOffset,
                Values: [],
                IncludeBlank: false,
                CustomFilters: [],
                CustomFiltersAnd: false,
                CustomFiltersAndRaw: null,
                NativeCustomFiltersAttributes: null,
                Top10: null,
                DynamicFilter: null,
                ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: true),
                IconFilter: null,
                DateGroups: [],
                NativeFiltersAttributes: null,
                NativeFilterXmls: []));

        // R107-commands-autofilter-table-color-sync-1: see CellFillColorFilterCommand's call of the
        // same shape.
        _tableFilterSnapshot = StructuredTableFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new StructuredTableFilterColumnModel((int)_filterColOffset, Values: [])
            {
                ColorFilter = new WorksheetAutoFilterColorFilterModel(CellColor: true)
            });

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);
        StructuredTableFilterColumnSync.Restore(sheet, _tableFilterSnapshot);
        _undoSnapshot.Restore(sheet);
    }
}

public sealed class CellFontColorFilterCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _filterColOffset;
    private readonly CellColor _fontColor;
    private FilterUndoSnapshot _undoSnapshot;
    // R87-commands-autofilter-sort-5-1: see CellFillColorFilterCommand's field of the same name.
    private List<WorksheetAutoFilterColumnModel>? _previousAutoFilterColumns;
    // R107-commands-autofilter-table-color-sync-1: see CellFillColorFilterCommand's field of the same name.
    private StructuredTableFilterColumnSnapshot? _tableFilterSnapshot;

    public string Label => "Filter by Font Color";

    public CellFontColorFilterCommand(
        SheetId sheetId,
        GridRange range,
        uint filterColOffset,
        CellColor fontColor)
    {
        _sheetId = sheetId;
        _range = range;
        _filterColOffset = filterColOffset;
        _fontColor = fontColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectInvalidFilterRange(_sheetId, _range, _filterColOffset) is { } invalidRange)
            return invalidRange;
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        _undoSnapshot.Capture(sheet);

        var filterCol = _range.Start.Col + _filterColOffset;
        // R100-commands-filter-totalsrow-1: see FilterCommand.RecomputeHiddenRows -- exclude a
        // structured table's shown Totals Row from the filterable data set.
        var lastDataRow = StructuredTableEditEffects.GetFilterableLastRow(sheet, _range);
        // table-semantics-F1: see FilterCommand.RecomputeHiddenRows -- a structured table loaded
        // with headerRowCount="0" has NO header row, so its first row is itself a data row and must
        // be evaluated here too.
        var firstDataRow = FilterHiddenRowUpdater.GetFilterableFirstRow(sheet, _range);
        for (uint row = firstDataRow; row <= lastDataRow; row++)
        {
            // filter-by-color-cf: resolve the color Excel would actually show for this cell,
            // including any conditional-formatting-driven font color, not just the cell's static
            // stored style — otherwise a CF-red-font cell never matches a "red" font-color filter.
            var cell = sheet.GetCell(row, filterCol);
            var address = new CellAddress(_sheetId, row, filterCol);
            var fontColor = SortCommand.GetEffectiveColor(ctx.Workbook, sheet, address, cell, wantFill: false);
            FilterHiddenRowUpdater.ApplyColumnOwnedVisibility(sheet, filterCol, row, fontColor == _fontColor);
        }

        _previousAutoFilterColumns = WorksheetAutoFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new WorksheetAutoFilterColumnModel(
                ColumnId: (int)_filterColOffset,
                Values: [],
                IncludeBlank: false,
                CustomFilters: [],
                CustomFiltersAnd: false,
                CustomFiltersAndRaw: null,
                NativeCustomFiltersAttributes: null,
                Top10: null,
                DynamicFilter: null,
                ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: false, Color: _fontColor),
                IconFilter: null,
                DateGroups: [],
                NativeFiltersAttributes: null,
                NativeFilterXmls: []));

        // R107-commands-autofilter-table-color-sync-1: see CellFillColorFilterCommand's call of the
        // same shape.
        _tableFilterSnapshot = StructuredTableFilterColumnSync.Apply(
            sheet,
            _range,
            (int)_filterColOffset,
            new StructuredTableFilterColumnModel((int)_filterColOffset, Values: [])
            {
                ColorFilter = new WorksheetAutoFilterColorFilterModel(CellColor: false, Color: _fontColor)
            });

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_undoSnapshot.HasSnapshot)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        WorksheetAutoFilterColumnSync.Restore(sheet, _range, _previousAutoFilterColumns);
        StructuredTableFilterColumnSync.Restore(sheet, _tableFilterSnapshot);
        _undoSnapshot.Restore(sheet);
    }
}

internal struct FilterUndoSnapshot
{
    private uint[]? _hiddenRows;
    private uint[]? _filterHiddenRows;
    // F8: per-column value-filter state (sheet.ActiveValueFilterColumns) must roll back alongside
    // FilterHiddenRows, otherwise undoing a FilterCommand would leave a stale column entry behind
    // that corrupts the next recompute's AND-across-columns union.
    private Dictionary<uint, IReadOnlyList<string>>? _activeValueFilterColumns;
    // G7: sheet.ValueFilterHiddenRows (which rows the value-filter mechanism itself currently owns)
    // must roll back in lockstep with ActiveValueFilterColumns/FilterHiddenRows too, otherwise an
    // undo could leave it out of sync with the restored FilterHiddenRows and corrupt the next
    // recompute's "preserve rows I don't own" logic.
    private uint[]? _valueFilterHiddenRows;
    // R12-sort-filter-1: sheet.ColumnFilterOwnedRows (which rows each condition/average/top-bottom/
    // color column filter currently owns) must roll back alongside FilterHiddenRows too, otherwise
    // an undo could leave a stale/mismatched ownership entry behind that corrupts the next
    // ApplyColumnOwnedVisibility/ClearColumnOwnedRange AND-across-columns decision.
    private Dictionary<uint, HashSet<uint>>? _columnFilterOwnedRows;

    public bool HasSnapshot => _hiddenRows is not null;

    public void Reset()
    {
        _hiddenRows = null;
        _filterHiddenRows = null;
        _activeValueFilterColumns = null;
        _valueFilterHiddenRows = null;
        _columnFilterOwnedRows = null;
    }

    public void Capture(Sheet sheet)
    {
        _hiddenRows = [.. sheet.HiddenRows];
        _filterHiddenRows = [.. sheet.FilterHiddenRows];
        _activeValueFilterColumns = sheet.ActiveValueFilterColumns.Count == 0
            ? null
            : sheet.ActiveValueFilterColumns.ToDictionary(
                kvp => kvp.Key,
                IReadOnlyList<string> (kvp) => [.. kvp.Value]);
        _valueFilterHiddenRows = [.. sheet.ValueFilterHiddenRows];
        _columnFilterOwnedRows = sheet.ColumnFilterOwnedRows.Count == 0
            ? null
            : sheet.ColumnFilterOwnedRows.ToDictionary(
                kvp => kvp.Key,
                HashSet<uint> (kvp) => [.. kvp.Value]);
    }

    public void CaptureIfNeeded(Sheet sheet)
    {
        if (HasSnapshot)
            return;

        Capture(sheet);
    }

    public void Restore(Sheet sheet)
    {
        if (_hiddenRows is null)
            return;

        sheet.HiddenRows.Clear();
        sheet.HiddenRows.UnionWith(_hiddenRows);
        sheet.FilterHiddenRows.Clear();
        if (_filterHiddenRows is not null)
            sheet.FilterHiddenRows.UnionWith(_filterHiddenRows);

        sheet.ActiveValueFilterColumns.Clear();
        if (_activeValueFilterColumns is not null)
        {
            foreach (var (col, allowedValues) in _activeValueFilterColumns)
                sheet.ActiveValueFilterColumns[col] = allowedValues;
        }

        sheet.ValueFilterHiddenRows.Clear();
        if (_valueFilterHiddenRows is not null)
            sheet.ValueFilterHiddenRows.UnionWith(_valueFilterHiddenRows);

        sheet.ColumnFilterOwnedRows.Clear();
        if (_columnFilterOwnedRows is not null)
        {
            foreach (var (col, ownedRows) in _columnFilterOwnedRows)
                sheet.ColumnFilterOwnedRows[col] = [.. ownedRows];
        }
    }
}

/// <summary>
/// R91-meta-3: re-flows a structured table's row banding (<see cref="StructuredTableStyleService.RebandTable"/>)
/// whenever a filter apply/clear changes which of the table's rows are visible — mirrors how
/// <c>InsertDeleteRowsCommand</c> and <see cref="SortCommand"/> already re-flow banding after a
/// mutation that changes physical row order (R90). A filter never moves data, but the row-banding
/// alternation is keyed to VISIBLE row position, so hiding/showing rows is just as much a "the
/// physical layout a table's data occupies changed" event as an insert or a sort, and left the
/// remaining visible rows with a stale stripe otherwise.
/// </summary>
internal static class StructuredTableBandingReflow
{
    /// <summary>
    /// Rebands the structured table whose <see cref="StructuredTableModel.Range"/> exactly matches
    /// <paramref name="range"/> — the shape a table's own header-cell filter dropdown always passes
    /// (mirrors <see cref="FilterCommand.ApplyToStructuredTableIfMatched"/>'s lookup). A no-op when
    /// <paramref name="range"/> is a plain worksheet AutoFilter range with no owning table.
    /// </summary>
    /// <remarks>
    /// R95-commands-filter-table-reband-undo-1: <see cref="StructuredTableStyleService.RebandTable"/>
    /// always repaints the table's ENTIRE data body with forceFill:true (MergeStyleOntoCell's
    /// keepExistingFill is unconditionally false under forceFill), which unconditionally overwrites
    /// any explicit FillColor a user set on a body cell — the same overwrite mechanism r90/r92/r94
    /// already fixed for InsertRowsCommand/DeleteRowsCommand/SortCommand by snapshotting the table's
    /// full data body immediately before calling RebandTable. Returning that same snapshot here (in
    /// this one choke point, rather than duplicating the capture logic at each of FilterCommand's two
    /// call sites) lets the caller restore it on undo instead of destroying the fill permanently.
    /// Returns <c>null</c> when no table matched (nothing to restore).
    /// </remarks>
    public static List<(CellAddress Address, Cell? OldCell)>? ReflowIfMatched(Workbook workbook, Sheet sheet, GridRange range)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.Range.Equals(range))
            {
                var captured = CaptureDataBody(sheet, table);
                StructuredTableStyleService.RebandTable(workbook, sheet, table);
                return captured;
            }
        }

        return null;
    }

    /// <summary>Restores a snapshot captured by <see cref="ReflowIfMatched"/>. A no-op when <paramref name="snapshot"/> is <c>null</c>.</summary>
    public static void Restore(Sheet sheet, List<(CellAddress Address, Cell? OldCell)>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (address, oldCell) in snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, oldCell);
        }
    }

    private static List<(CellAddress Address, Cell? OldCell)> CaptureDataBody(Sheet sheet, StructuredTableModel table)
    {
        var captured = new List<(CellAddress Address, Cell? OldCell)>();
        var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(table);
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                captured.Add((address, sheet.GetCell(address)?.Clone()));
            }
        }

        return captured;
    }
}

/// <summary>
/// R33-commands-autofilter-slicer-1: mirrors an interactively-applied worksheet-level AutoFilter
/// criterion (value list / Top 10 / above-average / below-average) into
/// <see cref="Sheet.AutoFilter"/>'s <see cref="WorksheetAutoFilterModel.FilterColumns"/> — the model
/// <c>XlsxWorksheetAutoFilterXmlMapper.Save</c> actually serializes into the worksheet's own
/// <c>&lt;autoFilter&gt;/&lt;filterColumn&gt;</c> XML. Without this, the criterion only lives in
/// session-only state (<see cref="Sheet.ActiveValueFilterColumns"/> / <see cref="Sheet.ColumnFilterOwnedRows"/>)
/// and is silently discarded the moment the workbook is saved and reopened. Only applies when
/// <c>range</c> matches <see cref="Sheet.AutoFilter"/>'s <see cref="WorksheetAutoFilterModel.Reference"/>
/// exactly — a plain worksheet AutoFilter range, as opposed to a structured table's own filter (which
/// <see cref="FilterCommand.ApplyToStructuredTableIfMatched"/> already handles separately via
/// <see cref="StructuredTableFilterColumnModel"/>).
/// </summary>
internal static class WorksheetAutoFilterColumnSync
{
    /// <summary>
    /// Replaces (or removes, when <paramref name="newColumn"/> is <c>null</c>) the filter-column
    /// entry for <paramref name="columnId"/> on the worksheet AutoFilter matching <paramref name="range"/>.
    /// Returns a snapshot of the previous <see cref="WorksheetAutoFilterModel.FilterColumns"/> list for
    /// undo (via <see cref="Restore"/>), or <c>null</c> when no worksheet AutoFilter matches
    /// <paramref name="range"/> (e.g. the range belongs to a structured table instead).
    /// </summary>
    public static List<WorksheetAutoFilterColumnModel>? Apply(
        Sheet sheet,
        GridRange range,
        int columnId,
        WorksheetAutoFilterColumnModel? newColumn)
    {
        var autoFilter = sheet.AutoFilter;
        if (autoFilter is null || !IsMatchingRange(autoFilter, range))
            return null;

        var previous = new List<WorksheetAutoFilterColumnModel>(autoFilter.FilterColumns);

        autoFilter.FilterColumns.RemoveAll(filterColumn => filterColumn.ColumnId == columnId);
        if (newColumn is not null)
            autoFilter.FilterColumns.Add(newColumn);
        if (autoFilter.FilterColumns.Count > 1)
            autoFilter.FilterColumns.Sort(static (a, b) => a.ColumnId.CompareTo(b.ColumnId));

        return previous;
    }

    /// <summary>Undoes an <see cref="Apply"/> call, restoring the exact previous list contents.</summary>
    public static void Restore(Sheet sheet, GridRange range, List<WorksheetAutoFilterColumnModel>? previousFilterColumns)
    {
        if (previousFilterColumns is null)
            return;

        var autoFilter = sheet.AutoFilter;
        if (autoFilter is null || !IsMatchingRange(autoFilter, range))
            return;

        autoFilter.FilterColumns.Clear();
        autoFilter.FilterColumns.AddRange(previousFilterColumns);
    }

    private static bool IsMatchingRange(WorksheetAutoFilterModel autoFilter, GridRange range) =>
        string.Equals(autoFilter.Reference, range.ToString(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// R106-commands-autofilter-table-sync-1: mirrors an interactively-applied Top 10/Above-Average/
/// custom-criterion AutoFilter criterion into a structured table's own
/// <see cref="StructuredTableModel.FilterColumns"/> -- the same job
/// <see cref="FilterCommand.ApplyToStructuredTableIfMatched"/> already does for a plain value-list
/// filter (finding H18) -- whenever <c>range</c> is exactly a structured table's
/// <see cref="StructuredTableModel.Range"/> (the shape
/// <c>AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange</c> hands back for a table's own
/// header-cell filter dropdown). Without this, Top10/Above-Average/Custom-criterion filters applied
/// from a Table's dropdown hid/showed rows correctly in the live session but were silently dropped
/// from the table's saved &lt;autoFilter&gt; XML on save/reload -- only the sibling
/// <see cref="WorksheetAutoFilterColumnSync"/> path was covered for these commands, which is (by
/// design) a no-op for a table range, since tables carry their own &lt;autoFilter&gt; inside the
/// table part rather than a worksheet-level one.
/// </summary>
internal static class StructuredTableFilterColumnSync
{
    /// <summary>
    /// Replaces (or removes, when <paramref name="newColumn"/> is <c>null</c>) the
    /// <see cref="StructuredTableFilterColumnModel"/> entry for <paramref name="columnId"/> on the
    /// structured table whose <see cref="StructuredTableModel.Range"/> exactly matches
    /// <paramref name="range"/>. Returns a snapshot of the previous
    /// <see cref="StructuredTableModel.FilterColumns"/> list for undo (via <see cref="Restore"/>), or
    /// <c>null</c> when no structured table matches <paramref name="range"/> (e.g. the range is a
    /// plain worksheet AutoFilter range instead).
    /// </summary>
    public static StructuredTableFilterColumnSnapshot? Apply(
        Sheet sheet,
        GridRange range,
        int columnId,
        StructuredTableFilterColumnModel? newColumn)
    {
        for (var i = 0; i < sheet.StructuredTables.Count; i++)
        {
            var table = sheet.StructuredTables[i];
            if (!table.Range.Equals(range))
                continue;

            var previous = new List<StructuredTableFilterColumnModel>(table.FilterColumns);

            var filterColumns = table.FilterColumns
                .Where(fc => fc.ColumnId != columnId)
                .ToList();
            if (newColumn is not null)
                filterColumns.Add(newColumn);
            filterColumns.Sort(static (a, b) => a.ColumnId.CompareTo(b.ColumnId));

            sheet.StructuredTables[i] = StructuredTableDesignCommandHelpers.CopyTable(table, filterColumns: filterColumns);
            return new StructuredTableFilterColumnSnapshot(table.Id, previous);
        }

        return null;
    }

    /// <summary>Undoes an <see cref="Apply"/> call, restoring the exact previous list contents.</summary>
    public static void Restore(Sheet sheet, StructuredTableFilterColumnSnapshot? snapshot)
    {
        if (snapshot is not { } value)
            return;
        if (!CommandGuards.TryFindStructuredTableIndex(sheet, value.TableId, out var tableIndex))
            return;

        var table = sheet.StructuredTables[tableIndex];
        sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
            table, filterColumns: value.PreviousFilterColumns);
    }
}

/// <summary>Snapshot captured by <see cref="StructuredTableFilterColumnSync.Apply"/> for undo.</summary>
internal readonly record struct StructuredTableFilterColumnSnapshot(
    int TableId,
    List<StructuredTableFilterColumnModel> PreviousFilterColumns);

internal readonly struct FilterAllowedValueMatcher
{
    private readonly string? _singleValue;
    private readonly HashSet<string>? _values;

    private FilterAllowedValueMatcher(string singleValue)
    {
        _singleValue = singleValue;
        _values = null;
    }

    private FilterAllowedValueMatcher(HashSet<string> values)
    {
        _singleValue = null;
        _values = values;
    }

    public static FilterAllowedValueMatcher Create(IReadOnlyList<string> values) =>
        values.Count == 1
            ? new FilterAllowedValueMatcher(values[0])
            : new FilterAllowedValueMatcher(new HashSet<string>(values, StringComparer.OrdinalIgnoreCase));

    public bool Contains(string text) =>
        _values is not null
            ? _values.Contains(text)
            : string.Equals(text, _singleValue, StringComparison.OrdinalIgnoreCase);
}

internal static class FilterHiddenRowUpdater
{
    /// <summary>
    /// table-semantics-F1: returns the first row of <paramref name="range"/> that participates in
    /// interactive AutoFilter/slicer matching -- the header-count-aware counterpart to
    /// <see cref="StructuredTableEditEffects.GetFilterableLastRow"/>. When <paramref name="range"/>
    /// is exactly a structured table's <c>Range</c> and that table was loaded with
    /// <c>headerRowCount="0"</c> (<see cref="StructuredTableModel.HeaderRowCount"/> is <c>0</c> -- a
    /// genuine, round-tripped Excel feature; see <c>StructuredReferenceResolver.HeaderRowCount()</c>
    /// and <c>XlsxStructuredTableModelMapper.MaterializeFilters</c>' own header-count-aware load
    /// path), <c>range.Start.Row</c> IS ITSELF a data row -- there is no header row to skip -- so it
    /// is returned unchanged. Mirrors <see cref="StructuredTableEditEffects.GetDataBodyRowBounds"/>'s
    /// <c>FirstDataRow</c> computation exactly. For a plain worksheet-level AutoFilter range (no
    /// matching table) or a table with a header row (the default), returns
    /// <c>range.Start.Row + 1</c> exactly as before.
    /// </summary>
    public static uint GetFilterableFirstRow(Sheet sheet, GridRange range)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.Range.Equals(range))
            {
                var hasHeaderRow = table.HeaderRowCount is null or > 0;
                return table.Range.Start.Row + (hasHeaderRow ? 1u : 0u);
            }
        }

        return range.Start.Row + 1;
    }

    public static void SetHidden(HashSet<uint> filterHiddenRows, uint row, bool hidden)
    {
        if (hidden)
            filterHiddenRows.Add(row);
        else
            filterHiddenRows.Remove(row);
    }

    public static void SetVisible(HashSet<uint> filterHiddenRows, uint row, bool visible)
    {
        SetHidden(filterHiddenRows, row, !visible);
    }

    public static void ClearRange(HashSet<uint> filterHiddenRows, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return;

        var dataRowCount = lastDataRow - firstDataRow + 1;
        if ((uint)filterHiddenRows.Count < dataRowCount)
        {
            filterHiddenRows.RemoveWhere(row => row >= firstDataRow && row <= lastDataRow);
            return;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
            filterHiddenRows.Remove(row);
    }

    /// <summary>
    /// Like <see cref="ClearRange"/>, but only un-hides rows in <paramref name="ownedRows"/> — rows
    /// hidden for some other reason (e.g. a Top10/Average/color/custom-criterion filter on another
    /// column) are left hidden. Used when the value-filter mechanism has no active columns left and
    /// must relinquish only the rows it previously owned (see finding G7). Also consults
    /// <see cref="Sheet.ColumnFilterOwnedRows"/> (finding R13-meta-3) so a row still owned by an
    /// active condition/average/top-bottom/color filter on any column is never un-hidden here.
    /// </summary>
    public static void ClearOwnedRows(Sheet sheet, GridRange range, IReadOnlyCollection<uint> ownedRows)
    {
        if (ownedRows.Count == 0)
            return;

        var filterHiddenRows = sheet.FilterHiddenRows;
        // table-semantics-F1: see GetFilterableFirstRow -- a headerless table's first row is a data
        // row and must be eligible for relinquishment here too.
        var firstDataRow = GetFilterableFirstRow(sheet, range);
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return;

        foreach (var row in ownedRows)
        {
            if (row >= firstDataRow && row <= lastDataRow && !IsHiddenByAnyColumnOwnedFilter(sheet, row))
                filterHiddenRows.Remove(row);
        }
    }

    /// <summary>
    /// Applies a single row's visible/hidden decision for the non-value-list AutoFilter mechanisms
    /// (condition/custom-criterion, Top 10/Above-Average, and cell/font-color filters), recording
    /// ownership per <paramref name="filterCol"/> in <see cref="Sheet.ColumnFilterOwnedRows"/> so a
    /// later re-evaluation of THIS SAME column never un-hides a row that some OTHER active column's
    /// filter (a value-list filter via <see cref="Sheet.ActiveValueFilterColumns"/>, or another
    /// condition/average/top-bottom/color filter) is responsible for hiding (finding
    /// R12-sort-filter-1). Excel ANDs AutoFilter criteria across columns — a row is hidden if it
    /// fails ANY active column's filter — so un-hiding must only ever relinquish rows this column's
    /// own mechanism previously claimed.
    /// </summary>
    public static void ApplyColumnOwnedVisibility(Sheet sheet, uint filterCol, uint row, bool visible)
    {
        if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var owned))
        {
            owned = [];
            sheet.ColumnFilterOwnedRows[filterCol] = owned;
        }

        if (visible)
        {
            // Only relinquish the row from FilterHiddenRows if no OTHER active mechanism (a
            // value-list filter on any column, or another condition/average/top-bottom/color
            // filter's owned rows) is also responsible for hiding it.
            owned.Remove(row);
            if (!IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                sheet.FilterHiddenRows.Remove(row);
        }
        else
        {
            owned.Add(row);
            sheet.FilterHiddenRows.Add(row);
        }
    }

    /// <summary>
    /// True when <paramref name="row"/> is currently hidden by some mechanism OTHER than
    /// <paramref name="excludeCol"/>'s own condition/average/top-bottom/color filter (a value-list
    /// filter on any column, or another column's owned condition/average/top-bottom/color filter).
    /// Used by <see cref="TopBottomFilterCommand"/>/<see cref="AverageFilterCommand"/> to scope their
    /// Top-N boundary / average statistic to the rows still VISIBLE under every OTHER active
    /// column's filter (finding R56-services-autofilter-sort-5-1) -- <paramref name="excludeCol"/>'s
    /// own prior ownership of a row must never exclude it, since that column's filter is the one
    /// about to be recomputed.
    /// </summary>
    internal static bool IsHiddenByAnyOtherActiveMechanism(Sheet sheet, uint excludeCol, uint row)
    {
        if (sheet.ValueFilterHiddenRows.Contains(row))
            return true;

        foreach (var (col, owned) in sheet.ColumnFilterOwnedRows)
        {
            if (col != excludeCol && owned.Contains(row))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when any condition/average/top-bottom/color filter, on ANY column, currently owns
    /// (and therefore is responsible for hiding) <paramref name="row"/>. This is the value-filter
    /// side's counterpart to <see cref="IsHiddenByAnyOtherActiveMechanism"/> — the value-filter
    /// recompute/clear paths have no single "owning column" of their own (a value filter can span
    /// several columns via <see cref="Sheet.ActiveValueFilterColumns"/>), so there is no column to
    /// exclude: any owned row must survive relinquishment (finding R13-meta-3).
    /// </summary>
    public static bool IsHiddenByAnyColumnOwnedFilter(Sheet sheet, uint row)
    {
        foreach (var owned in sheet.ColumnFilterOwnedRows.Values)
        {
            if (owned.Contains(row))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Cheap short-circuit for <see cref="ApplyColumnOwnedVisibility"/>'s callers: true when calling
    /// it for <paramref name="row"/>/<paramref name="visible"/> would be a strict no-op (no ownership
    /// change, no FilterHiddenRows change) — i.e. the row is already NOT owned by this column and
    /// already not hidden (when <paramref name="visible"/> is true), or already owned by this column
    /// (and therefore already hidden) when <paramref name="visible"/> is false. Skipping the call in
    /// that case avoids per-row HashSet churn on dense filters without risking the AND-across-columns
    /// bug this ownership tracking exists to fix (finding R12-sort-filter-1) — an un-owned but
    /// otherwise-hidden row (e.g. hidden directly, or by another column) is intentionally NOT treated
    /// as a no-op so it still gets a chance to be relinquished when this column now wants it visible.
    /// </summary>
    public static bool IsColumnOwnedVisibilityAlreadyCorrect(Sheet sheet, uint filterCol, uint row, bool visible)
    {
        var owned = sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var ownedRows) && ownedRows.Contains(row);
        return visible
            ? !owned && !sheet.FilterHiddenRows.Contains(row)
            : owned;
    }

    /// <summary>
    /// Relinquishes every row in <paramref name="range"/> that <paramref name="filterCol"/>'s own
    /// condition/average/top-bottom/color filter currently owns (e.g. when that filter is being
    /// cleared, or degenerately matches nothing/everything), without disturbing rows some OTHER
    /// active column's filter is hiding. Mirrors <see cref="ApplyColumnOwnedVisibility"/>'s
    /// ownership discipline for the bulk-clear code paths (finding R12-sort-filter-1).
    /// </summary>
    public static void ClearColumnOwnedRange(Sheet sheet, uint filterCol, GridRange range)
    {
        if (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var owned) || owned.Count == 0)
            return;

        // table-semantics-F1: see GetFilterableFirstRow -- a headerless table's first row is a data
        // row and must be eligible for relinquishment here too.
        var firstDataRow = GetFilterableFirstRow(sheet, range);
        var lastDataRow = range.End.Row;
        foreach (var row in owned)
        {
            if (row < firstDataRow || row > lastDataRow)
                continue;
            if (!IsHiddenByAnyOtherActiveMechanism(sheet, filterCol, row))
                sheet.FilterHiddenRows.Remove(row);
        }

        owned.Clear();
    }

    public static bool ContainsAnyInRange(HashSet<uint> filterHiddenRows, GridRange range)
    {
        var firstDataRow = range.Start.Row + 1;
        var lastDataRow = range.End.Row;
        if (filterHiddenRows.Count == 0 || firstDataRow > lastDataRow)
            return false;

        var dataRowCount = lastDataRow - firstDataRow + 1;
        if ((uint)filterHiddenRows.Count < dataRowCount)
        {
            foreach (var row in filterHiddenRows)
            {
                if (row >= firstDataRow && row <= lastDataRow)
                    return true;
            }

            return false;
        }

        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (filterHiddenRows.Contains(row))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Maps a <see cref="ScalarValue"/> to the canonical text a value filter matches against: text as-is,
/// numbers in <see cref="CultureInfo.InvariantCulture"/>, bools <c>TRUE</c>/<c>FALSE</c>, dates
/// <c>yyyy-MM-dd</c>, blanks empty, errors as their code. This is the single source of truth for the
/// filter value text — both the desktop and Avalonia dropdown checklists format cell values with
/// <see cref="ToText"/> so the values they show agree exactly with what <see cref="FilterCommand"/>
/// matches.
/// </summary>
public static class FilterValueFormatter
{
    public static string ToText(ScalarValue value) => value switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        // TryToDateTime, not ToDateTime: a serial outside DateTime's range (date autofill extrapolated
        // too far, Paste Special arithmetic on a date, a value from a loaded file) would otherwise
        // throw here and crash the app on something as ordinary as opening the filter dropdown. Fall
        // back to the raw serial text so the value is still listed and filterable.
        DateTimeValue dt => dt.TryToDateTime(out var dtValue)
            ? dtValue.ToString("yyyy-MM-dd")
            : dt.Value.ToString(CultureInfo.InvariantCulture),
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => ""
    };

    public static void AppendText(StringBuilder builder, ScalarValue value)
    {
        switch (value)
        {
            case TextValue text:
                builder.Append(text.Value);
                break;
            case NumberValue number:
                AppendInvariant(builder, number.Value);
                break;
            case BoolValue boolean:
                builder.Append(boolean.Value ? "TRUE" : "FALSE");
                break;
            case DateTimeValue dateTime:
                AppendDate(builder, dateTime);
                break;
            case ErrorValue error:
                builder.Append(error.Code);
                break;
        }
    }

    private static void AppendInvariant(StringBuilder builder, double value)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var charsWritten, provider: CultureInfo.InvariantCulture))
            builder.Append(buffer[..charsWritten]);
        else
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendDate(StringBuilder builder, DateTimeValue value)
    {
        // See ToText: an out-of-range serial must not throw out of the filter/checklist build.
        if (!value.TryToDateTime(out var date))
        {
            builder.Append(value.Value.ToString(CultureInfo.InvariantCulture));
            return;
        }

        Span<char> buffer = stackalloc char[10];
        if (date.TryFormat(buffer, out var charsWritten, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            builder.Append(buffer[..charsWritten]);
        else
            builder.Append(date.ToString("yyyy-MM-dd"));
    }
}
