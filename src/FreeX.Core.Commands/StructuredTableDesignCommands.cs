using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RenameStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly string _newName;
    private StructuredTableModel? _previousTable;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly List<(PivotCacheModel Cache, int? PreviousSourceTableId)> _renamedPivotCaches = [];
    // R100: workbook-wide CF/DV/chart formula rewrites, so a manual table rename (Table
    // Design > Table Name, and the Name Manager) fixes every conditional-format rule,
    // data-validation rule, and chart series/error-bar formula in the ENTIRE workbook that
    // referenced the old name -- not just ordinary sheet-cell formulas (RewriteAllFormulas
    // above) and this table's own CalculatedColumnFormula/TotalsRowFormula metadata
    // (RewriteTableSelfReferenceFormulas above). Mirrors the fix already applied to
    // DuplicateSheetCommand's CF/DV/chart rewrite for a cloned table's renamed identity,
    // but scoped to the whole workbook instead of just the freshly duplicated sheet, since
    // an ordinary rename can be referenced from ANY sheet.
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;

    public string Label => "Table Name";

    public RenameStructuredTableCommand(SheetId sheetId, int tableId, string newName)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _newName = newName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousTable = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();

        if (StructuredTableDesignCommandHelpers.ValidateTableName(ctx.Workbook, _newName, _sheetId, _tableId) is { } error)
            return new CommandOutcome(false, error);

        var normalizedName = _newName.Trim();
        _previousTable = sheet.StructuredTables[tableIndex];
        var renameOp = new RenameTableOp(_previousTable.Name, normalizedName);

        // A table's own CalculatedColumnFormula/TotalsRowFormula metadata can carry a
        // fully-qualified self-reference to its OWN (old) name (e.g. a totals-to-totals custom
        // aggregate like "Table1[[#Totals],[Revenue]]-Table1[[#Totals],[Cost]]" -- the only way to
        // write a cross-column custom total). That metadata is copied verbatim by CopyTable below
        // unless rewritten here first, so it must be run through the same RenameTableOp used for
        // ordinary sheet-cell formulas or it goes stale: still naming the pre-rename table, and
        // later gets re-persisted to XLSX (XlsxStructuredTableWriter) or re-injected into a totals
        // cell (RefreshStructuredTableTotalsCommand.ResolveTotalsCell) verbatim.
        var renamedColumns = RewriteTableSelfReferenceFormulas(_previousTable, renameOp, sheet.Name);
        sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
            _previousTable,
            name: normalizedName,
            displayName: normalizedName,
            columns: renamedColumns);

        // Structured references carry the table name as a bare literal (TableName[Column]) with no
        // table-ID indirection, so every formula referencing the old name must be rewritten across the
        // whole workbook or it would evaluate to #NAME? — mirrors RenameSheetCommand's sheet-qualified
        // reference rewrite via the same FormulaRewriter/RewriteOperation mechanism.
        _formulaSnapshot.Clear();
        _namedFormulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, renameOp, _formulaSnapshot);
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, renameOp, _namedFormulaSnapshot);

        // R100: a structured reference to this table can also be embedded in a conditional-
        // format rule's FormulaText/threshold values, a data-validation rule's Formula1/
        // Formula2, or a chart's series/data-label/error-bar formula -- on THIS table's own
        // sheet or any other sheet in the workbook. None of those are cell formulas, so
        // RewriteAllFormulas above never touches them; left unrewritten they would keep
        // naming the OLD table and silently break (CF/DV formulas evaluate to #NAME?, chart
        // series lose their data). RewriteRuleFormulas is the same revert-safe primitive the
        // row/col insert/delete commands already use for CF/DV formula-text rewrites; run it
        // over every sheet here since a table rename (unlike a row/col shift) can affect
        // rules anywhere in the workbook. Chart formulas use a dedicated table-rename-safe
        // rewrite (RewriteAllChartFormulasForTableRename) instead of the shared
        // RewriteChartVerbatimFormulas helper -- that helper pre-splits on every unquoted
        // top-level comma to support multi-area range unions, which corrupts a structured
        // reference like "Table1[[#Headers],[Values]]" (the comma is INSIDE the brackets).
        _cfFormulaSnapshot.Clear();
        _cfThresholdSnapshot.Clear();
        _dvFormulaSnapshot.Clear();
        foreach (var s in ctx.Workbook.Sheets)
        {
            RowColumnShiftHelpers.RewriteRuleFormulas(
                s, renameOp, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
        }

        // The CF viewport context cache is keyed on (sheet.Id, sheet.ContentVersion,
        // sheet.ConditionalFormats.Version) and caches a precompiled AST per CF rule object
        // reference, so mutating cf.FormulaText in place above never invalidates it on its
        // own -- bump Version explicitly (mirrors RenameSheetCommand's T7 pass) or a stale
        // cache hit would keep evaluating the OLD table name after the rename.
        if (_cfFormulaSnapshot.Count > 0 || _cfThresholdSnapshot.Count > 0)
        {
            foreach (var s in ctx.Workbook.Sheets)
            {
                if (s.ConditionalFormats.Any(cf =>
                        _cfFormulaSnapshot.ContainsKey(cf.Id) ||
                        _cfThresholdSnapshot.Keys.Any(k => k.Id == cf.Id)))
                {
                    s.ConditionalFormats.NotifyRulesChanged();
                }
            }
        }

        _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        RowColumnShiftHelpers.RewriteAllChartFormulasForTableRename(ctx.Workbook, renameOp);

        // N32's table-sourced pivot re-derivation (PivotTableRefreshService.Refresh) looks up the
        // live table purely by cache.SourceTableName — if that stays pointed at the old name after a
        // rename, the lookup fails forever and the pivot silently stops tracking the table's extent.
        // Repoint every pivot cache that was sourced from this table so the name stays in sync.
        //
        // R104: once a cache has established a stable SourceTableId (see PivotTableRefreshService),
        // that id — not the old name string — is the only reliable way to tell "this cache was sourced
        // from THIS table". Matching by name alone here would wrongly repoint an unrelated, already-
        // orphaned cache that merely happens to share this table's old name (e.g. left dangling by a
        // prior "Convert to Range" on some other, unrelated table). Only fall back to a name match for
        // a cache that has no id yet (nothing has refreshed it since it was loaded), and establish the
        // id at the same time so this table's identity is pinned down going forward.
        _renamedPivotCaches.Clear();
        foreach (var cache in ctx.Workbook.PivotCaches)
        {
            if (cache.SourceType != PivotCacheSourceType.Table)
                continue;

            var matchesById = cache.SourceTableId == _previousTable.Id;
            var matchesByUnestablishedName = cache.SourceTableId is null &&
                string.Equals(cache.SourceTableName, _previousTable.Name, StringComparison.OrdinalIgnoreCase);
            if (!matchesById && !matchesByUnestablishedName)
                continue;

            _renamedPivotCaches.Add((cache, cache.SourceTableId));
            cache.SourceTableName = normalizedName;
            cache.SourceTableId = _previousTable.Id;
        }

        var affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            [_previousTable.Range.Start], _formulaSnapshot);
        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot);

        // R100: mirror the CF/DV/chart rewrite above.
        var cfSheetsToNotify = _cfFormulaSnapshot.Count > 0 || _cfThresholdSnapshot.Count > 0
            ? new HashSet<SheetId>()
            : null;
        foreach (var s in ctx.Workbook.Sheets)
        {
            if (cfSheetsToNotify is not null &&
                s.ConditionalFormats.Any(cf =>
                    _cfFormulaSnapshot.ContainsKey(cf.Id) ||
                    _cfThresholdSnapshot.Keys.Any(k => k.Id == cf.Id)))
            {
                cfSheetsToNotify.Add(s.Id);
            }

            RowColumnShiftHelpers.RestoreRuleFormulas(
                s, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
        }

        if (cfSheetsToNotify is not null)
        {
            foreach (var sheetId in cfSheetsToNotify)
                ctx.Workbook.GetSheet(sheetId)?.ConditionalFormats.NotifyRulesChanged();
        }

        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        _chartVerbatimSnapshot = null;

        foreach (var (cache, previousSourceTableId) in _renamedPivotCaches)
        {
            cache.SourceTableName = _previousTable.Name;
            cache.SourceTableId = previousSourceTableId;
        }
        _renamedPivotCaches.Clear();

        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
        _previousTable = null;
    }

    /// <summary>
    /// Rewrites <paramref name="renameOp"/>'s old-table-name references inside every column's
    /// <see cref="StructuredTableColumnModel.CalculatedColumnFormula"/>/<see cref="StructuredTableColumnModel.TotalsRowFormula"/>
    /// so the table's own persisted formula metadata stays in sync with the rename, matching how
    /// <see cref="RowColumnShiftHelpers.RewriteAllFormulas"/> keeps ordinary sheet-cell formulas in
    /// sync. A malformed stored formula is left untouched (mirrors <see cref="FormulaRewriter.Rewrite"/>'s
    /// own malformed-formula behavior for cell formulas elsewhere in this command).
    /// </summary>
    private static IReadOnlyList<StructuredTableColumnModel> RewriteTableSelfReferenceFormulas(
        StructuredTableModel table, RenameTableOp renameOp, string hostSheetName)
    {
        if (table.Columns.Count == 0)
            return table.Columns;

        var columns = new List<StructuredTableColumnModel>(table.Columns.Count);
        foreach (var column in table.Columns)
        {
            var calculatedColumnFormula = RewriteFormulaTableSelfReference(column.CalculatedColumnFormula, renameOp, hostSheetName);
            var totalsRowFormula = RewriteFormulaTableSelfReference(column.TotalsRowFormula, renameOp, hostSheetName);
            columns.Add(column with
            {
                CalculatedColumnFormula = calculatedColumnFormula,
                TotalsRowFormula = totalsRowFormula
            });
        }

        return columns;
    }

    private static string? RewriteFormulaTableSelfReference(string? formulaText, RenameTableOp renameOp, string hostSheetName)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return formulaText;

        return FormulaRewriter.Rewrite(formulaText, renameOp, hostSheetName) ?? formulaText;
    }
}

// R125-commands-undo-byte-budget-further: evaluated for IEstimatesMemory ("table resize" is
// explicitly one of the flagged categories) but deliberately NOT given one. _previousCells below
// only ever holds the table's totals-row relocation and grown-calculated-column formula cells,
// both bounded by the table's COLUMN count, not its full row*column extent -- shrinking a table
// does not blank or snapshot the dropped rows' cell contents at all (that data is simply left
// behind on the sheet, outside the new table Range, entirely untouched by this command). So there
// is no real per-cell retention here for an estimator to report; the flat 200-byte
// IEstimatesMemory default already fits.
public sealed class ResizeStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly GridRange _newRange;
    private StructuredTableModel? _previousTable;
    private readonly Dictionary<CellAddress, Cell?> _previousCells = [];
    private RefreshStructuredTableTotalsCommand? _totalsRefreshCommand;
    // R115: only populated when the shrink actually drops a column that carried an active
    // FilterColumns criterion -- see the cleanup block in Apply for why each of these is needed.
    private HashSet<uint>? _previousFilterHiddenRows;
    private HashSet<uint>? _previousValueFilterHiddenRows;
    private Dictionary<uint, IReadOnlyList<string>>? _previousActiveValueFilterColumns;
    private Dictionary<uint, HashSet<uint>>? _previousColumnFilterOwnedRows;

    public string Label => "Resize Table";

    public ResizeStructuredTableCommand(SheetId sheetId, int tableId, GridRange newRange)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _newRange = newRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousTable = null;
        _previousCells.Clear();
        _totalsRefreshCommand = null;
        _previousFilterHiddenRows = null;
        _previousValueFilterHiddenRows = null;
        _previousActiveValueFilterColumns = null;
        _previousColumnFilterOwnedRows = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();

        var table = sheet.StructuredTables[tableIndex];
        if (ValidateResizeRange(table, _newRange) is { } error)
            return new CommandOutcome(false, error);

        if (sheet.StructuredTables.Any(t => t.Id != _tableId && t.Range.Overlaps(_newRange)))
            return new CommandOutcome(false, "A table cannot overlap another table.");

        // Excel requires a table to have exactly one discrete cell per row/column intersection, so
        // growing/shrinking a table into a range that contains a merged cell is rejected -- mirrors
        // CreateStructuredTableCommand's symmetric guard (and MergeCellsCommand.Apply's "Cannot
        // merge cells that overlap a table" check enforced from the other direction) for the same
        // tables-and-merges-don't-mix rule.
        if (sheet.MergedRegions.Any(region => region.Overlaps(_newRange)))
            return new CommandOutcome(false, "A table cannot overlap a merged cell.");

        // R128: mirrors CreateStructuredTableCommand.Apply's Round-65 spill-overlap guard (shared
        // via CommandGuards.RejectIfStructuredTableRangeOverlapsSpill), which this command was
        // missing entirely. Without it, growing a table into a live dynamic-array spill's footprint
        // silently succeeded and Sheet.IsSpillBlocked would then treat the spill's anchor/members as
        // occupied by the table on the next recalc, turning the anchor into #SPILL! and permanently
        // blanking the members (ClearSpillRange already ran by then -- nothing recovers them).
        if (CommandGuards.RejectIfStructuredTableRangeOverlapsSpill(sheet, _newRange) is { } spillOutcome)
            return spillOutcome;

        _previousTable = table;
        var columns = BuildColumns(sheet, table, _newRange).ToList();
        var filterColumns = table.FilterColumns
            .Where(filter => filter.ColumnId >= 0 && filter.ColumnId < columns.Count)
            .ToList();

        // R115: a column that fell outside the new (narrower) range takes its FilterColumns
        // criterion with it above, but Excel also stops that criterion from hiding any row the
        // moment the column leaves the table -- AutoFilter state for a table is scoped to its
        // CURRENT column set. Left untouched, rows hidden solely by a now-dropped filter would stay
        // hidden forever: the column's dropdown (the only supported UI to clear it) only renders for
        // columns still inside the table's range, so there would be no path left to un-hide them.
        var droppedFilters = table.FilterColumns
            .Where(filter => filter.ColumnId < 0 || filter.ColumnId >= columns.Count)
            .ToList();

        var resizedTable = StructuredTableDesignCommandHelpers.CopyTable(
            table,
            range: _newRange,
            columns: columns,
            filterColumns: filterColumns);
        sheet.StructuredTables[tableIndex] = resizedTable;

        if (droppedFilters.Count > 0)
            ReleaseDroppedColumnFilters(sheet, table, resizedTable, droppedFilters);

        // Excel auto-fills a calculated column's formula into every newly added row when a table
        // grows — mirror that here so new rows aren't left blank in that column. When the table has
        // a totals row, growing downward must first push that totals row's own aggregate content down
        // to the new last row (turning the previous totals row into an ordinary data row) — otherwise
        // the new last row would inherit TotalsRowShown semantics while still holding the old totals
        // formula/label, and the true former totals row would get silently overwritten as if it were
        // a plain calculated-column data cell.
        var relocatedTotalsRowCells = RelocateTotalsRowIfNeeded(sheet, table, resizedTable);
        FillGrownCalculatedColumns(sheet, table, resizedTable);

        var affectedCells = new List<CellAddress> { _newRange.Start };
        affectedCells.AddRange(relocatedTotalsRowCells);

        // R127: must run on EVERY resize that leaves the totals row shown, not just a downward
        // grow. CopyTable carries TotalsRowShown through unchanged for a shrink too, so the new
        // Range.End.Row is unconditionally treated as the totals row by every consumer (see
        // StructuredReferenceResolver.DataBodyRange/IsDataBodyRow) the instant sheet.StructuredTables
        // is replaced above -- regardless of whether that row still holds the stale ordinary data it
        // had before the shrink. Without an unconditional refresh here, shrinking a table with a shown
        // totals row left that stale data sitting in the new last row while every structured reference
        // against the table (e.g. SUM(Table[Column])) silently excluded it as "the totals row".
        // Gating this refresh on growth only (the previous behavior) fixed the grow direction and left
        // the shrink direction broken.
        if (resizedTable.TotalsRowShown)
        {
            _totalsRefreshCommand = new RefreshStructuredTableTotalsCommand(_sheetId, _tableId);
            var totalsRefreshOutcome = _totalsRefreshCommand.Apply(ctx);
            if (totalsRefreshOutcome.Success && totalsRefreshOutcome.AffectedCells is { Count: > 0 } totalsAffected)
                affectedCells.AddRange(totalsAffected);
        }

        // A formula elsewhere in the workbook that references this table by a structured reference
        // (e.g. D1=SUM(Table1[Amount])) had its dependency-graph edges registered against the
        // table's PRE-resize extent. Growing/shrinking the table's Range above does not by itself
        // touch that formula, so unless it is surfaced here, the standard post-command pipeline
        // (WorkbookCellEditService.UpdateFormulaDependencies, driven off AffectedCells) never
        // re-registers it -- leaving it wired to the stale range forever, so a later edit to a
        // newly-added row would not dirty/recalculate it. Listing it here forces that re-registration
        // (against the now-live, resized table) without altering the formula text itself.
        var seenAffected = new HashSet<CellAddress>(affectedCells);
        foreach (var address in FindFormulaCellsReferencingTable(ctx.Workbook, resizedTable))
        {
            if (seenAffected.Add(address))
                affectedCells.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null)
            return;

        _totalsRefreshCommand?.Revert(ctx);
        _totalsRefreshCommand = null;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, cell) in _previousCells)
        {
            if (cell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, cell);
        }
        _previousCells.Clear();

        if (_previousFilterHiddenRows is not null)
        {
            sheet.FilterHiddenRows.Clear();
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
            _previousFilterHiddenRows = null;
        }
        if (_previousValueFilterHiddenRows is not null)
        {
            sheet.ValueFilterHiddenRows.Clear();
            sheet.ValueFilterHiddenRows.UnionWith(_previousValueFilterHiddenRows);
            _previousValueFilterHiddenRows = null;
        }
        if (_previousActiveValueFilterColumns is not null)
        {
            sheet.ActiveValueFilterColumns.Clear();
            foreach (var (col, values) in _previousActiveValueFilterColumns)
                sheet.ActiveValueFilterColumns[col] = values;
            _previousActiveValueFilterColumns = null;
        }
        if (_previousColumnFilterOwnedRows is not null)
        {
            sheet.ColumnFilterOwnedRows.Clear();
            foreach (var (col, owned) in _previousColumnFilterOwnedRows)
                sheet.ColumnFilterOwnedRows[col] = owned;
            _previousColumnFilterOwnedRows = null;
        }

        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
        _previousTable = null;
    }

    /// <summary>
    /// When a table with a shown totals row grows downward (the new range's last row is past the
    /// previous last row), Excel keeps the totals row as the very last row of the table and turns
    /// whatever used to be the totals row into a new ordinary data row. The totals row's own
    /// aggregate/label content lives in <see cref="StructuredTableColumnModel"/> metadata, not just
    /// the sheet cell, so it will be correctly regenerated at its new position by the totals refresh
    /// this command triggers below — but the sheet cell that used to hold that totals content must
    /// first be cleared here so it doesn't linger as stale data in the row that is now part of the
    /// data body (<see cref="FillGrownCalculatedColumns"/> only ever writes calculated-column formula
    /// cells, and would otherwise leave every non-calculated column's old totals text/number sitting
    /// in the new data row).
    /// <para>
    /// R127: intentionally grow-only. On a shrink, the OLD totals row (<c>previousTable.Range.End.Row</c>)
    /// ends up entirely outside <paramref name="resizedTable"/>'s (smaller) range -- it is no longer part
    /// of the table at all, so there is no "becomes an ordinary data row" transition to relocate here;
    /// like any other row/column a shrink drops from the table, it is simply left as an ordinary sheet
    /// cell with whatever content it already had. What a shrink DOES need is for the cell that becomes
    /// the NEW last row to stop holding its old (now stale) data and instead hold live totals content --
    /// that is handled unconditionally by the <see cref="RefreshStructuredTableTotalsCommand"/> call in
    /// <see cref="Apply"/>, which overwrites every column's cell at the resized table's actual
    /// <c>Range.End.Row</c> regardless of resize direction.
    /// </para>
    /// </summary>
    private IReadOnlyList<CellAddress> RelocateTotalsRowIfNeeded(Sheet sheet, StructuredTableModel previousTable, StructuredTableModel resizedTable)
    {
        if (!previousTable.TotalsRowShown || !resizedTable.TotalsRowShown)
            return [];
        if (resizedTable.Range.End.Row <= previousTable.Range.End.Row)
            return [];

        var oldTotalsRow = previousTable.Range.End.Row;
        var relocatedCells = new List<CellAddress>();
        // Only clear the columns that were actually part of the OLD table. If the resize also
        // grows the table wider, the extra columns at the old totals row were never part of the
        // previous table -- they are ordinary (possibly user-populated) cells that become part of
        // the grown table's data body, and must be left untouched rather than blanked.
        for (var col = previousTable.Range.Start.Col; col <= previousTable.Range.End.Col; col++)
        {
            var address = new CellAddress(_sheetId, oldTotalsRow, col);
            // Preserve the totals row's existing formatting on the cell that now becomes an
            // ordinary data row -- only the totals content/formula is being relocated, not the
            // cell's style.
            var blank = Cell.FromValue(BlankValue.Instance);
            blank.StyleId = sheet.GetCell(address)?.StyleId ?? StyleId.Default;
            SnapshotAndSetCell(sheet, address, blank);
            relocatedCells.Add(address);
        }

        return relocatedCells;
    }

    /// <summary>
    /// Fills each calculated column's formula into rows that are newly part of the data body after a
    /// resize — matching Excel's auto-fill-on-resize behavior for structured tables, where growing a
    /// table downward extends every calculated column's formula into the new rows instead of leaving
    /// them blank. The totals row itself needs no fill here: when shown, it is relocated by
    /// <see cref="RelocateTotalsRowIfNeeded"/> and then regenerated by
    /// <see cref="RefreshStructuredTableTotalsCommand"/> right after this runs. Existing data cells
    /// are never touched; only cells the resize newly brought into the table's data body are written,
    /// and every overwritten cell is snapshotted so Revert can restore it exactly.
    /// <para>
    /// <see cref="StructuredTableColumnModel.CalculatedColumnFormula"/> is always stored anchored to
    /// the table's first data-body row (matching the OOXML <c>&lt;calculatedColumnFormula&gt;</c>
    /// convention that native XLSX loads populate, and the same normalization
    /// <see cref="PropagateCalculatedColumnCommand"/> applies when persisting a newly detected
    /// calculated column) — so each new row's formula must be row-shifted from that anchor row
    /// (via <see cref="StructuredTableEditEffects.ShiftFormulaRows"/>), never written verbatim,
    /// or every auto-expanded row would be frozen on the anchor row's operands instead of Excel's
    /// per-row relative references.
    /// </para>
    /// </summary>
    private void FillGrownCalculatedColumns(Sheet sheet, StructuredTableModel previousTable, StructuredTableModel resizedTable)
    {
        var previousLastDataRow = previousTable.TotalsRowShown && previousTable.Range.End.Row > previousTable.Range.Start.Row
            ? previousTable.Range.End.Row - 1
            : previousTable.Range.End.Row;
        var newLastDataRow = resizedTable.TotalsRowShown && resizedTable.Range.End.Row > resizedTable.Range.Start.Row
            ? resizedTable.Range.End.Row - 1
            : resizedTable.Range.End.Row;

        if (newLastDataRow <= previousLastDataRow)
            return;

        var (anchorRow, _) = StructuredTableEditEffects.GetDataBodyRowBounds(resizedTable);
        var firstNewRow = Math.Max(previousLastDataRow + 1, resizedTable.Range.Start.Row + 1);
        for (var columnIndex = 0; columnIndex < resizedTable.Columns.Count; columnIndex++)
        {
            var formula = resizedTable.Columns[columnIndex].CalculatedColumnFormula;
            if (string.IsNullOrWhiteSpace(formula))
                continue;

            var col = resizedTable.Range.Start.Col + (uint)columnIndex;
            for (var row = firstNewRow; row <= newLastDataRow; row++)
            {
                var address = new CellAddress(_sheetId, row, col);
                var shiftedFormula = StructuredTableEditEffects.ShiftFormulaRows(formula, anchorRow, row, sheet.Name);
                SnapshotAndSetCell(sheet, address, Cell.FromFormula(shiftedFormula));
            }
        }
    }

    private void SnapshotAndSetCell(Sheet sheet, CellAddress address, Cell cell)
    {
        if (!_previousCells.ContainsKey(address))
            _previousCells[address] = sheet.GetCell(address)?.Clone();
        sheet.SetCell(address, cell);
    }

    /// <summary>
    /// R115: releases every sheet-wide filter mechanism a column carries once it falls out of the
    /// table's resized (narrower) range, so rows it hid solely on its own reappear -- matching Excel,
    /// which scopes a table's AutoFilter state to its CURRENT column set, so shrinking a column out of
    /// a table drops that column's criterion entirely. Two independent mechanisms can be responsible
    /// for a dropped column's hidden rows (see Sheet.ActiveValueFilterColumns/.ColumnFilterOwnedRows
    /// doc comments for the full split):
    /// <list type="bullet">
    /// <item>a plain value-list AutoFilter criterion, mirrored into
    /// <see cref="Sheet.ActiveValueFilterColumns"/> keyed by this column's absolute index (kept in
    /// lockstep with a <see cref="StructuredTableFilterColumnModel"/> entry with no
    /// CustomFilters/NativeFilterXmls) -- removing the stale entry here, then recomputing via
    /// <see cref="ApplyStructuredTableFiltersCommand.RecomputeHiddenRows"/> against the table's own
    /// (already-reconciled) surviving FilterColumns, un-hides any row that failed only this dropped
    /// criterion while correctly keeping rows a SURVIVING filter still excludes.</item>
    /// <item>a Top10/Above-Average/custom-condition/color filter, which owns its hidden rows directly
    /// in <see cref="Sheet.ColumnFilterOwnedRows"/> without ever populating ActiveValueFilterColumns --
    /// <see cref="FilterHiddenRowUpdater.ClearColumnOwnedRange"/> relinquishes exactly the rows this
    /// column's own mechanism owns, leaving any row a different active column's filter still needs
    /// hidden untouched.</item>
    /// </list>
    /// Mirrors <see cref="ConvertStructuredTableToRangeCommand.Apply"/>'s equivalent cleanup for the
    /// whole-table-removal case, scoped down to just the columns this resize actually dropped.
    /// Snapshots every touched sheet-wide collection first so <see cref="Revert"/> can restore them
    /// exactly.
    /// </summary>
    private void ReleaseDroppedColumnFilters(
        Sheet sheet,
        StructuredTableModel previousTable,
        StructuredTableModel resizedTable,
        List<StructuredTableFilterColumnModel> droppedFilters)
    {
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];
        _previousValueFilterHiddenRows = [.. sheet.ValueFilterHiddenRows];
        _previousActiveValueFilterColumns = sheet.ActiveValueFilterColumns.Count == 0
            ? null
            : sheet.ActiveValueFilterColumns.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        _previousColumnFilterOwnedRows = sheet.ColumnFilterOwnedRows.Count == 0
            ? null
            : sheet.ColumnFilterOwnedRows.ToDictionary(kvp => kvp.Key, kvp => new HashSet<uint>(kvp.Value));

        foreach (var filter in droppedFilters)
        {
            // The table's Start.Col is pinned by ValidateResizeRange (Resize Table keeps the header
            // cell fixed), so the OLD table's Range.Start.Col still correctly locates this
            // (about-to-be-dropped) column's absolute sheet position.
            var absoluteCol = previousTable.Range.Start.Col + (uint)filter.ColumnId;
            sheet.ActiveValueFilterColumns.Remove(absoluteCol);
            FilterHiddenRowUpdater.ClearColumnOwnedRange(sheet, absoluteCol, previousTable.Range);
        }

        ApplyStructuredTableFiltersCommand.RecomputeHiddenRows(sheet, resizedTable);
    }

    /// <summary>
    /// Finds every formula cell in the workbook whose AST contains a structured reference
    /// (<see cref="StructuredReferenceNode"/> or <see cref="StructuredCurrentRowReferenceNode"/>)
    /// naming <paramref name="table"/> explicitly (by <see cref="StructuredTableModel.Name"/> or
    /// <see cref="StructuredTableModel.DisplayName"/>) -- i.e. formulas anywhere else in the
    /// workbook that depend on this table, whose registered dependencies need refreshing after a
    /// resize. A malformed formula is skipped rather than surfaced (it has no resolvable
    /// dependencies either way).
    /// </summary>
    private static IEnumerable<CellAddress> FindFormulaCellsReferencingTable(Workbook workbook, StructuredTableModel table)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var address in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(address);
                if (cell?.FormulaText is null)
                    continue;

                FormulaNode ast;
                try
                {
                    ast = cell.CachedAst as FormulaNode ?? FormulaEvaluator.ParseFormula(cell.FormulaText);
                }
                catch (FormulaParseException)
                {
                    continue;
                }

                if (ReferencesTableByName(ast, table.Name, table.DisplayName))
                    yield return address;
            }
        }
    }

    private static bool ReferencesTableByName(FormulaNode node, string tableName, string? displayName)
    {
        switch (node)
        {
            case StructuredReferenceNode structuredRef:
                return MatchesTableName(structuredRef.TableName, tableName, displayName);
            case StructuredCurrentRowReferenceNode currentRowRef:
                return MatchesTableName(currentRowRef.TableName, tableName, displayName);
            case BinaryOpNode binary:
                return ReferencesTableByName(binary.Left, tableName, displayName) ||
                       ReferencesTableByName(binary.Right, tableName, displayName);
            case UnaryOpNode unary:
                return ReferencesTableByName(unary.Operand, tableName, displayName);
            case FunctionCallNode call:
                foreach (var argument in call.Arguments)
                {
                    if (ReferencesTableByName(argument, tableName, displayName))
                        return true;
                }
                return false;
            default:
                // NumberNode, StringNode, BooleanNode, CellRefNode, RangeRefNode,
                // FullColumnRangeRefNode, FullRowRangeRefNode, NamedRangeNode, ErrorNode,
                // ArrayConstantNode, and OmittedArgumentNode never nest a structured reference.
                return false;
        }
    }

    private static bool MatchesTableName(string? candidate, string tableName, string? displayName) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        (string.Equals(candidate, tableName, StringComparison.OrdinalIgnoreCase) ||
         (displayName is not null && string.Equals(candidate, displayName, StringComparison.OrdinalIgnoreCase)));

    private static string? ValidateResizeRange(StructuredTableModel table, GridRange range)
    {
        if (range.Start.Sheet != table.Range.Start.Sheet || range.End.Sheet != table.Range.End.Sheet)
            return "Table range must remain on the table sheet.";
        if (range.Start != table.Range.Start)
            return "Resize Table keeps the current table header cell fixed.";
        if (range.RowCount < 2)
            return "Table range must include at least two rows.";
        if (range.ColCount == 0)
            return "Table range must include at least one column.";

        return null;
    }

    private static IEnumerable<StructuredTableColumnModel> BuildColumns(
        Sheet sheet,
        StructuredTableModel table,
        GridRange range)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var targetColumnCount = checked((int)range.ColCount);

        // ECMA-376 tableColumn ids increment monotonically and are never reused/renumbered when a
        // column is removed, so a table can legitimately carry a non-contiguous id sequence (e.g.
        // {1, 2, 4} after an earlier column deletion). Preserve every surviving column's original Id
        // here — matching the id-preserving reconciliation RowColumnShiftHelpers.AddressState.cs
        // already uses for plain column insert/delete — instead of renumbering by position, and only
        // allocate a fresh id (one past the highest id ever seen on this table) for genuinely new
        // columns.
        var nextId = 1;
        foreach (var existing in table.Columns)
            nextId = Math.Max(nextId, existing.Id + 1);

        for (var index = 0; index < targetColumnCount; index++)
        {
            var ordinal = index + 1;
            if (index < table.Columns.Count)
            {
                var existing = table.Columns[index];
                var name = string.IsNullOrWhiteSpace(existing.Name)
                    ? MakeUniqueColumnName($"Column{ordinal.ToString(CultureInfo.InvariantCulture)}", usedNames)
                    : existing.Name;
                usedNames.Add(name);
                yield return string.Equals(name, existing.Name, StringComparison.Ordinal)
                    ? existing
                    : existing with { Name = name };
                continue;
            }

            var rawName = hasHeaderRow
                ? HeaderText(sheet.GetValue(range.Start.Row, range.Start.Col + (uint)index))
                : string.Empty;
            var baseName = string.IsNullOrWhiteSpace(rawName)
                ? $"Column{ordinal.ToString(CultureInfo.InvariantCulture)}"
                : rawName.Trim();
            var columnName = MakeUniqueColumnName(baseName, usedNames);
            usedNames.Add(columnName);
            yield return new StructuredTableColumnModel(nextId++, columnName);
        }
    }

    private static string HeaderText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToShortDateString(),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static string MakeUniqueColumnName(string baseName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; suffix <= 10000; suffix++)
        {
            var candidate = $"{baseName}{suffix.ToString(CultureInfo.InvariantCulture)}";
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        return $"{baseName}{Guid.NewGuid():N}"[..Math.Min(31, baseName.Length + 32)];
    }
}

public sealed class ConvertStructuredTableToRangeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private StructuredTableModel? _removedTable;
    private int _removedIndex = -1;
    private HashSet<uint>? _previousFilterHiddenRows;
    private HashSet<uint>? _previousValueFilterHiddenRows;
    private Dictionary<uint, IReadOnlyList<string>>? _previousActiveValueFilterColumns;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly List<PivotCacheModel> _orphanedPivotCaches = [];

    public string Label => "Convert to Range";

    public ConvertStructuredTableToRangeCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _removedTable = null;
        _removedIndex = -1;
        _previousFilterHiddenRows = null;
        _previousValueFilterHiddenRows = null;
        _previousActiveValueFilterColumns = null;
        _formulaSnapshot.Clear();
        _orphanedPivotCaches.Clear();
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();

        _removedIndex = tableIndex;
        _removedTable = sheet.StructuredTables[tableIndex];

        // Excel's real Convert-to-Range lowers every structured reference into this table
        // (elsewhere in the workbook, and inside the table's own formulas) to the equivalent
        // absolute A1 reference before the table disappears — otherwise every such formula would
        // evaluate to #NAME?/#REF! the instant the table model is gone. Must run while the table
        // is still in sheet.StructuredTables, since resolution needs its live column layout.
        ConvertToRangeStructuredReferenceLowering.LowerAllFormulas(ctx.Workbook, sheet, _removedTable, _formulaSnapshot);

        sheet.StructuredTables.RemoveAt(tableIndex);

        // R106 (consolidated R107-round2 into CommandGuards.PinOrphanedPivotCacheSourceTableIds, the
        // shared "table name about to be freed" guard used by every command that removes a table --
        // see its doc comment for the full hazard/rationale).
        _orphanedPivotCaches.AddRange(CommandGuards.PinOrphanedPivotCacheSourceTableIds(ctx.Workbook, _removedTable));

        // Excel's real Convert-to-Range clears the table's filter state so every row reappears —
        // the table's per-column dropdown UI (and its filter bookkeeping) is gone once the table
        // model is removed above, so any rows it hid would otherwise stay stranded hidden forever.
        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];
        FilterHiddenRowUpdater.ClearRange(sheet.FilterHiddenRows, _removedTable.Range);

        _previousValueFilterHiddenRows = [.. sheet.ValueFilterHiddenRows];
        sheet.ValueFilterHiddenRows.RemoveWhere(row =>
            row > _removedTable.Range.Start.Row && row <= _removedTable.Range.End.Row);

        _previousActiveValueFilterColumns = sheet.ActiveValueFilterColumns.Count == 0
            ? null
            : sheet.ActiveValueFilterColumns.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        for (var col = _removedTable.Range.Start.Col; col <= _removedTable.Range.End.Col; col++)
            sheet.ActiveValueFilterColumns.Remove(col);

        var affectedCells = RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
            [_removedTable.Range.Start], _formulaSnapshot);
        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        CommandGuards.UnpinOrphanedPivotCacheSourceTableIds(_orphanedPivotCaches);
        _orphanedPivotCaches.Clear();

        var insertIndex = _removedIndex >= 0 && _removedIndex <= sheet.StructuredTables.Count
            ? _removedIndex
            : sheet.StructuredTables.Count;
        sheet.StructuredTables.Insert(insertIndex, _removedTable);

        if (_previousFilterHiddenRows is not null)
        {
            sheet.FilterHiddenRows.Clear();
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
        }

        if (_previousValueFilterHiddenRows is not null)
        {
            sheet.ValueFilterHiddenRows.Clear();
            sheet.ValueFilterHiddenRows.UnionWith(_previousValueFilterHiddenRows);
        }

        for (var col = _removedTable.Range.Start.Col; col <= _removedTable.Range.End.Col; col++)
            sheet.ActiveValueFilterColumns.Remove(col);
        if (_previousActiveValueFilterColumns is not null)
        {
            foreach (var (col, values) in _previousActiveValueFilterColumns)
                sheet.ActiveValueFilterColumns[col] = values;
        }

        _removedTable = null;
        _removedIndex = -1;
        _previousFilterHiddenRows = null;
        _previousValueFilterHiddenRows = null;
        _previousActiveValueFilterColumns = null;
    }
}

public static class StructuredTableDesignCommandHelpers
{
    /// <summary>
    /// N33: detects the Excel "auto-expand a Table" gesture — typing a value into the row directly
    /// below the table's last row (within its column span) or the column directly to the right of the
    /// table's last column (within its row span) — and returns the grown range the table should be
    /// resized to via <see cref="ResizeStructuredTableCommand"/>. Returns null when
    /// <paramref name="editedAddress"/> is not an auto-expand gesture for <paramref name="table"/>
    /// (e.g. it is inside the existing range, diagonal to a corner, or more than one row/column away).
    /// Mirrors Excel: a table never auto-expands into a cell already covered by another table, and —
    /// P105 — never auto-expands downward when the table's last row is a shown totals row: Excel
    /// suppresses the row-expand gesture entirely directly below a totals row (the user must use the
    /// Resize Table handle/dialog instead), so typing into that row must be treated as an ordinary,
    /// out-of-table edit rather than growing the table and destroying the just-typed value under the
    /// totals refresh.
    /// </summary>
    public static GridRange? TryGetAutoExpandRange(Sheet sheet, StructuredTableModel table, CellAddress editedAddress)
    {
        if (editedAddress.Sheet != table.Range.Start.Sheet)
            return null;

        var range = table.Range;

        // One row below the table's current last row, still within its column span: grow downward.
        // Never when that last row is a shown totals row — Excel does not auto-expand below one.
        var isRowExpand = !table.TotalsRowShown &&
            editedAddress.Row == range.End.Row + 1 &&
            editedAddress.Col >= range.Start.Col && editedAddress.Col <= range.End.Col;

        // One column to the right of the table's current last column, still within its row span:
        // grow rightward. Excel only extends into the header/data rows, never past the totals row,
        // so restrict to the existing row span (which already excludes any additional rows below).
        var isColumnExpand = editedAddress.Col == range.End.Col + 1 &&
            editedAddress.Row >= range.Start.Row && editedAddress.Row <= range.End.Row;

        if (!isRowExpand && !isColumnExpand)
            return null;

        var candidate = isRowExpand
            ? new GridRange(range.Start, new CellAddress(range.Start.Sheet, editedAddress.Row, range.End.Col))
            : new GridRange(range.Start, new CellAddress(range.Start.Sheet, range.End.Row, editedAddress.Col));

        // Never grow into a cell another table already occupies.
        if (sheet.StructuredTables.Any(other => other.Id != table.Id && other.Range.Overlaps(candidate)))
            return null;

        return candidate;
    }

    public static string? ValidateTableName(Workbook workbook, string? name, SheetId? exceptSheetId = null, int? exceptTableId = null)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
            return "Table name is invalid: it cannot be blank.";
        if (normalizedName.Length > 255)
            return "Table name is invalid: it cannot exceed 255 characters.";
        if (!IsValidTableNameStart(normalizedName[0]) || normalizedName.Any(ch => !IsValidTableNameChar(ch)))
            return "Table name is invalid: use letters, numbers, underscores, and periods; start with a letter or underscore.";
        if (CellAddress.TryParse(normalizedName, SheetId.New(), out _) || IsR1C1Reference(normalizedName))
            return "Table name is invalid: it cannot look like a cell reference.";
        // Table names and defined names (named ranges AND named formulas/constants, both
        // workbook-global and sheet-scoped) share a single unified namespace in Excel: Name
        // Manager's "New Name" refuses a name already used by a table, and Excel's own table
        // auto-namer likewise skips any identifier already taken by a defined name. Checking only
        // the workbook-global NamedRanges dictionary (the pre-existing check below) missed three
        // of the four defined-name surfaces -- a table could still be silently created or renamed
        // to collide with a workbook-global named FORMULA/constant, or with any SHEET-SCOPED named
        // range or formula, producing the same corrupt-on-repair state real Excel refuses to write.
        if (workbook.NamedRanges.Keys.Any(existing => string.Equals(existing, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return $"A named range named '{normalizedName}' already exists.";
        if (workbook.NamedFormulas.Keys.Any(existing => string.Equals(existing, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return $"A named range named '{normalizedName}' already exists.";
        if (workbook.ScopedNamedRanges.Keys.Any(key => string.Equals(key.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return $"A named range named '{normalizedName}' already exists.";
        if (workbook.ScopedNamedFormulas.Keys.Any(key => string.Equals(key.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return $"A named range named '{normalizedName}' already exists.";

        foreach (var sheet in workbook.Sheets)
        foreach (var table in sheet.StructuredTables)
        {
            if (exceptSheetId == sheet.Id && exceptTableId == table.Id)
                continue;

            if (string.Equals(table.Name, normalizedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(table.DisplayName, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return $"A table named '{normalizedName}' already exists.";
            }
        }

        return null;
    }

    public static StructuredTableModel CopyTable(
        StructuredTableModel table,
        string? name = null,
        string? displayName = null,
        GridRange? range = null,
        IReadOnlyList<StructuredTableColumnModel>? columns = null,
        IReadOnlyList<StructuredTableFilterColumnModel>? filterColumns = null)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = name ?? table.Name,
            DisplayName = displayName ?? table.DisplayName,
            Range = range ?? table.Range,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = table.StyleName,
            ShowFirstColumn = table.ShowFirstColumn,
            ShowLastColumn = table.ShowLastColumn,
            ShowRowStripes = table.ShowRowStripes,
            ShowColumnStripes = table.ShowColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes,
            NativeChildXmls = table.NativeChildXmls,
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
        };

        copy.Columns.AddRange(columns ?? table.Columns);
        copy.FilterColumns.AddRange(filterColumns ?? table.FilterColumns);
        return copy;
    }

    private static bool IsValidTableNameStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsValidTableNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private static bool IsR1C1Reference(string name)
    {
        if (name.Length < 4 || char.ToUpperInvariant(name[0]) != 'R')
            return false;

        var cIndex = name.IndexOf("C", 1, StringComparison.OrdinalIgnoreCase);
        if (cIndex <= 1 || cIndex == name.Length - 1)
            return false;

        return uint.TryParse(name[1..cIndex], out var row) &&
               uint.TryParse(name[(cIndex + 1)..], out var col) &&
               row is >= 1 and <= CellAddress.MaxRow &&
               col is >= 1 and <= CellAddress.MaxCol;
    }
}
