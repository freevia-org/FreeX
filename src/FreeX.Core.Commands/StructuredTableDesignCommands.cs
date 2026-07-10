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
    private readonly List<PivotCacheModel> _renamedPivotCaches = [];

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
        sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
            _previousTable,
            name: normalizedName,
            displayName: normalizedName);

        // Structured references carry the table name as a bare literal (TableName[Column]) with no
        // table-ID indirection, so every formula referencing the old name must be rewritten across the
        // whole workbook or it would evaluate to #NAME? — mirrors RenameSheetCommand's sheet-qualified
        // reference rewrite via the same FormulaRewriter/RewriteOperation mechanism.
        _formulaSnapshot.Clear();
        _namedFormulaSnapshot.Clear();
        var renameOp = new RenameTableOp(_previousTable.Name, normalizedName);
        RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, renameOp, _formulaSnapshot);
        RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, renameOp, _namedFormulaSnapshot);

        // N32's table-sourced pivot re-derivation (PivotTableRefreshService.Refresh) looks up the
        // live table purely by cache.SourceTableName — if that stays pointed at the old name after a
        // rename, the lookup fails forever and the pivot silently stops tracking the table's extent.
        // Repoint every pivot cache that was sourced from this table so the name stays in sync.
        _renamedPivotCaches.Clear();
        foreach (var cache in ctx.Workbook.PivotCaches)
        {
            if (cache.SourceType == PivotCacheSourceType.Table &&
                string.Equals(cache.SourceTableName, _previousTable.Name, StringComparison.OrdinalIgnoreCase))
            {
                cache.SourceTableName = normalizedName;
                _renamedPivotCaches.Add(cache);
            }
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

        foreach (var cache in _renamedPivotCaches)
            cache.SourceTableName = _previousTable.Name;
        _renamedPivotCaches.Clear();

        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
        _previousTable = null;
    }
}

public sealed class ResizeStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly GridRange _newRange;
    private StructuredTableModel? _previousTable;
    private readonly Dictionary<CellAddress, Cell?> _previousCells = [];
    private RefreshStructuredTableTotalsCommand? _totalsRefreshCommand;

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

        _previousTable = table;
        var columns = BuildColumns(sheet, table, _newRange).ToList();
        var filterColumns = table.FilterColumns
            .Where(filter => filter.ColumnId >= 0 && filter.ColumnId < columns.Count)
            .ToList();

        var resizedTable = StructuredTableDesignCommandHelpers.CopyTable(
            table,
            range: _newRange,
            columns: columns,
            filterColumns: filterColumns);
        sheet.StructuredTables[tableIndex] = resizedTable;

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

        if (resizedTable.TotalsRowShown && resizedTable.Range.End.Row > table.Range.End.Row)
        {
            _totalsRefreshCommand = new RefreshStructuredTableTotalsCommand(_sheetId, _tableId);
            var totalsRefreshOutcome = _totalsRefreshCommand.Apply(ctx);
            if (totalsRefreshOutcome.Success && totalsRefreshOutcome.AffectedCells is { Count: > 0 } totalsAffected)
                affectedCells.AddRange(totalsAffected);
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
                yield return existing with { Id = ordinal, Name = name };
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
            yield return new StructuredTableColumnModel(ordinal, columnName);
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
        if (workbook.NamedRanges.Keys.Any(existing => string.Equals(existing, normalizedName, StringComparison.OrdinalIgnoreCase)))
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
