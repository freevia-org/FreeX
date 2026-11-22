using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

// Excel parity: right-click inside a table > Insert > Table Rows Above / Table Columns to the Left
// (and the Table Rows Below / Columns to the Right variants), Delete > Table Rows / Table Columns.
// Unlike whole-sheet row/column insert-delete these shift ONLY the cells of the table's own columns
// (rows) or rows (columns) -- the cells below/right of the table in that band move, everything else
// on the sheet stays put -- and the table grows or shrinks accordingly. They are built on the
// band-scoped Insert/Delete Cells commands (which already rewrite every address-bearing structure
// for a partial shift) with the table guard lifted for the one table the command resizes itself.
//
// Verified against real Excel (ListRows.Add / ListRows.Delete / ListColumns.Add / ListColumns.Delete):
//   - a new row/column is blank, calculated columns auto-fill, a new column is named "ColumnN";
//   - deleting every data row keeps one blank data row (the rows below shift up by count - 1);
//   - A1 references into the shifted band follow the cells, references to deleted cells become #REF!;
//   - row properties (hidden flag, height) belong to the sheet row, not the table, so they do NOT shift
//     with the table's cells -- a manually hidden row stays hidden at the same row number;
//   - a table with an active filter refuses to insert rows in the middle or delete rows ("Can't move
//     cells in a filtered range or table."); only appending rows at the end is allowed and the appended
//     row is not hidden. Inserting/deleting a table column keeps the filters of the surviving columns
//     (re-indexed) and the same rows hidden; deleting a filtered column drops its criterion and
//     re-applies the remaining ones (rows only that column hid reappear);
//   - the table's sort state survives, its key ranges adjusted like any range; deleting the key column
//     removes its sort condition.

/// <summary>Cell snapshot taken as a command mutates, restored in reverse order on undo.</summary>
internal sealed class TableShiftCellUndo
{
    private readonly List<(CellAddress Address, Cell? OldCell)> _cells = [];

    internal IEnumerable<CellAddress> Addresses => _cells.Select(entry => entry.Address);

    internal void Capture(Sheet sheet, CellAddress address) =>
        _cells.Add((address, sheet.GetCell(address)?.Clone()));

    internal void CaptureRange(Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                Capture(sheet, new CellAddress(range.Start.Sheet, row, col));
        }
    }

    internal void Restore(Sheet sheet)
    {
        for (var i = _cells.Count - 1; i >= 0; i--)
        {
            var (address, oldCell) = _cells[i];
            if (oldCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, oldCell);
        }

        _cells.Clear();
    }
}

internal static class StructuredTableShiftSupport
{
    /// <summary>Copies <paramref name="table"/> with a new shape; the caller supplies the already-adjusted native sort state.</summary>
    internal static StructuredTableModel Reshape(
        StructuredTableModel table,
        GridRange range,
        IReadOnlyList<StructuredTableColumnModel> columns,
        IReadOnlyList<StructuredTableFilterColumnModel> filterColumns,
        string? sortStateXml = null)
    {
        var state = table.CaptureCopyState() with
        {
            Range = range,
            Columns = columns,
            FilterColumns = filterColumns,
            NativeSortStateXml = sortStateXml
        };
        return StructuredTableModel.FromCopyState(state);
    }

    /// <summary>Re-flows table banding, snapshotting every cell it may repaint so undo is exact.</summary>
    internal static void Reband(Workbook workbook, Sheet sheet, StructuredTableModel table, TableShiftCellUndo undo)
    {
        undo.CaptureRange(sheet, table.Range);
        StructuredTableStyleService.RebandTable(workbook, sheet, table);
    }


    internal const string FilteredShiftMessage = "Can't move cells in a filtered range or table.";

    /// <summary>True when the table carries an active filter (Excel's FilterMode), even one that currently hides nothing.</summary>
    internal static bool IsFiltered(Sheet sheet, StructuredTableModel table)
    {
        if (table.FilterColumns.Count > 0)
            return true;

        var firstCol = table.Range.Start.Col;
        var lastCol = table.Range.End.Col;
        foreach (var col in sheet.ActiveValueFilterColumns.Keys)
        {
            if (col >= firstCol && col <= lastCol)
                return true;
        }

        foreach (var (col, rows) in sheet.ColumnFilterOwnedRows)
        {
            if (col >= firstCol && col <= lastCol && rows.Count > 0)
                return true;
        }

        return false;
    }

    /// <summary>Snapshot of every sheet-wide filter bookkeeping collection a table column shift may touch.</summary>
    internal sealed class FilterStateUndo
    {
        private readonly HashSet<uint> _filterHidden;
        private readonly HashSet<uint> _valueFilterHidden;
        private readonly Dictionary<uint, IReadOnlyList<string>> _activeValues;
        private readonly Dictionary<uint, HashSet<uint>> _owned;

        internal FilterStateUndo(Sheet sheet)
        {
            _filterHidden = [.. sheet.FilterHiddenRows];
            _valueFilterHidden = [.. sheet.ValueFilterHiddenRows];
            _activeValues = new Dictionary<uint, IReadOnlyList<string>>(sheet.ActiveValueFilterColumns);
            _owned = sheet.ColumnFilterOwnedRows.ToDictionary(kvp => kvp.Key, kvp => new HashSet<uint>(kvp.Value));
        }

        internal void Restore(Sheet sheet)
        {
            sheet.FilterHiddenRows.Clear();
            sheet.FilterHiddenRows.UnionWith(_filterHidden);
            sheet.ValueFilterHiddenRows.Clear();
            sheet.ValueFilterHiddenRows.UnionWith(_valueFilterHidden);
            sheet.ActiveValueFilterColumns.Clear();
            foreach (var (col, values) in _activeValues)
                sheet.ActiveValueFilterColumns[col] = values;
            sheet.ColumnFilterOwnedRows.Clear();
            foreach (var (col, rows) in _owned)
                sheet.ColumnFilterOwnedRows[col] = new HashSet<uint>(rows);
        }
    }

    /// <summary>Re-keys the column-keyed filter bookkeeping for columns inside the table's span after a column insert.</summary>
    internal static void ShiftFilterColumnKeysRight(Sheet sheet, uint insertCol, uint tableLastCol, uint count)
    {
        ShiftKeys(sheet.ActiveValueFilterColumns, insertCol, tableLastCol, (int)count);
        ShiftKeys(sheet.ColumnFilterOwnedRows, insertCol, tableLastCol, (int)count);
    }

    /// <summary>Drops the filter state of deleted columns (releasing rows only they hid) and re-keys the survivors.</summary>
    internal static void ReleaseAndShiftFilterColumnKeysLeft(
        Sheet sheet, GridRange oldRange, uint firstDeletedCol, uint count, StructuredTableModel shrunk)
    {
        var lastDeletedCol = firstDeletedCol + count - 1;
        for (var col = firstDeletedCol; col <= lastDeletedCol; col++)
        {
            sheet.ActiveValueFilterColumns.Remove(col);
            FilterHiddenRowUpdater.ClearColumnOwnedRange(sheet, col, oldRange);
            sheet.ColumnFilterOwnedRows.Remove(col);
        }

        ShiftKeys(sheet.ActiveValueFilterColumns, lastDeletedCol + 1, oldRange.End.Col, -(int)count);
        ShiftKeys(sheet.ColumnFilterOwnedRows, lastDeletedCol + 1, oldRange.End.Col, -(int)count);

        // Excel re-applies the surviving criteria: rows that only the deleted column's filter hid reappear.
        ApplyStructuredTableFiltersCommand.RecomputeHiddenRows(sheet, shrunk);
        sheet.ValueFilterHiddenRows.RemoveWhere(row => !sheet.FilterHiddenRows.Contains(row));
    }

    private static void ShiftKeys<TValue>(Dictionary<uint, TValue> map, uint from, uint to, int delta)
    {
        if (map.Count == 0)
            return;

        var moved = map.Where(pair => pair.Key >= from && pair.Key <= to).ToList();
        foreach (var (key, _) in moved)
            map.Remove(key);
        foreach (var (key, value) in moved)
            map[(uint)(key + delta)] = value;
    }

    private static readonly Regex RangeRef = new(
        @"^\$?([A-Za-z]{1,3})\$?(\d+)(?::\$?([A-Za-z]{1,3})\$?(\d+))?$", RegexOptions.Compiled);

    /// <summary>
    /// Adjusts a table's sortState XML for a band insert/delete: every <c>ref</c> moves like an ordinary
    /// range; a sort condition whose key range disappears (its column or all its rows were deleted) is
    /// dropped, and the whole state is dropped when no condition survives (Excel: SortFields.Count = 0).
    /// </summary>
    internal static string? AdjustSortState(string? xml, bool rowAxis, bool insert, uint at, uint count)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return xml;

        XElement element;
        try
        {
            element = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return xml;
        }

        var hadConditions = false;
        foreach (var condition in element.Elements().Where(e => e.Name.LocalName == "sortCondition").ToList())
        {
            hadConditions = true;
            if (condition.Attribute("ref") is not { } conditionRef)
                continue;
            var adjusted = AdjustRef(conditionRef.Value, rowAxis, insert, at, count);
            if (adjusted is null)
                condition.Remove();
            else
                conditionRef.Value = adjusted;
        }

        if (hadConditions && !element.Elements().Any(e => e.Name.LocalName == "sortCondition"))
            return null;

        if (element.Attribute("ref") is { } rootRef)
        {
            var adjusted = AdjustRef(rootRef.Value, rowAxis, insert, at, count);
            if (adjusted is null)
                rootRef.Remove();
            else
                rootRef.Value = adjusted;
        }

        return element.ToString(SaveOptions.DisableFormatting);
    }

    private static string? AdjustRef(string reference, bool rowAxis, bool insert, uint at, uint count)
    {
        var match = RangeRef.Match(reference.Trim());
        if (!match.Success)
            return reference;

        var startCol = CellAddress.ColumnNameToNumber(match.Groups[1].Value.ToUpperInvariant());
        var startRow = uint.Parse(match.Groups[2].Value);
        var endCol = match.Groups[3].Success ? CellAddress.ColumnNameToNumber(match.Groups[3].Value.ToUpperInvariant()) : startCol;
        var endRow = match.Groups[4].Success ? uint.Parse(match.Groups[4].Value) : startRow;

        var start = rowAxis ? startRow : startCol;
        var end = rowAxis ? endRow : endCol;
        if (!AdjustSpan(ref start, ref end, insert, at, count))
            return null;

        if (rowAxis)
        {
            startRow = start;
            endRow = end;
        }
        else
        {
            startCol = start;
            endCol = end;
        }

        var first = CellAddress.NumberToColumnName(startCol) + startRow;
        var last = CellAddress.NumberToColumnName(endCol) + endRow;
        return first == last ? first : first + ":" + last;
    }

    private static bool AdjustSpan(ref uint start, ref uint end, bool insert, uint at, uint count)
    {
        if (insert)
        {
            if (start >= at)
            {
                start += count;
                end += count;
            }
            else if (end >= at)
            {
                end += count;
            }

            return true;
        }

        var lastDeleted = at + count - 1;
        if (end < at)
            return true;
        if (start > lastDeleted)
        {
            start -= count;
            end -= count;
            return true;
        }

        if (start >= at && end <= lastDeleted)
            return false;

        start = start < at ? start : at;
        end = end > lastDeleted ? end - count : at - 1;
        return true;
    }

    internal static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));

    internal static bool IsBlank(Cell? cell) =>
        cell is null || (cell.FormulaText is null && cell.Value is BlankValue);

    internal static IReadOnlyList<CellAddress> BuildAffected(
        Workbook workbook,
        StructuredTableModel? table,
        IEnumerable<CellAddress> shiftAffected,
        TableShiftCellUndo undo)
    {
        var seen = new HashSet<CellAddress>(shiftAffected);
        foreach (var address in undo.Addresses)
            seen.Add(address);
        if (table is not null)
        {
            foreach (var address in ResizeStructuredTableCommand.FindFormulaCellsReferencingTable(workbook, table))
                seen.Add(address);
        }

        return [.. seen];
    }
}

/// <summary>Excel "Insert &gt; Table Rows Above/Below": adds rows inside a table, shifting only the table's columns down.</summary>
public sealed class InsertTableRowsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly int _dataRowIndex;
    private readonly uint _count;
    private InsertCellsCommand? _shift;
    private StructuredTableModel? _previousTable;
    private TableShiftCellUndo? _undo;
    private IReadOnlyList<CellAddress> _affectedCells = [];

    public string Label => "Insert Table Rows";
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <param name="dataRowIndex">Zero-based data-body position the new rows are inserted BEFORE (data row count appends).</param>
    public InsertTableRowsCommand(SheetId sheetId, int tableId, int dataRowIndex, uint count = 1)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _dataRowIndex = dataRowIndex;
        _count = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _shift = null;
        _previousTable = null;
        _undo = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();
        if (_count == 0)
            return new CommandOutcome(true, IsNoOp: true);

        var table = sheet.StructuredTables[tableIndex];
        var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(table);
        var dataRowCount = (int)(lastDataRow - firstDataRow + 1);
        if (_dataRowIndex < 0 || _dataRowIndex > dataRowCount)
            return new CommandOutcome(false, "The table row to insert at is outside the table.");
        if (_dataRowIndex < dataRowCount && StructuredTableShiftSupport.IsFiltered(sheet, table))
            return new CommandOutcome(false, StructuredTableShiftSupport.FilteredShiftMessage);
        if ((ulong)table.Range.End.Row + _count > CellAddress.MaxRow)
            return new CommandOutcome(false, $"Cannot insert table rows: the table would extend past the last row ({CellAddress.MaxRow}).");

        var insertRow = firstDataRow + (uint)_dataRowIndex;
        var band = StructuredTableShiftSupport.Range(
            _sheetId, insertRow, table.Range.Start.Col, insertRow + _count - 1, table.Range.End.Col);
        var shift = new InsertCellsCommand(_sheetId, band, InsertCellsShiftDirection.Down)
        {
            AllowedStructuredTableId = _tableId
        };
        var shiftOutcome = shift.Apply(ctx);
        if (!shiftOutcome.Success)
            return shiftOutcome;

        _shift = shift;
        _previousTable = table;
        _undo = new TableShiftCellUndo();

        var grownRange = StructuredTableShiftSupport.Range(
            _sheetId, table.Range.Start.Row, table.Range.Start.Col, table.Range.End.Row + _count, table.Range.End.Col);
        var grown = StructuredTableShiftSupport.Reshape(table, grownRange, table.Columns, table.FilterColumns,
            StructuredTableShiftSupport.AdjustSortState(table.NativeSortStateXml, rowAxis: true, insert: true, insertRow, _count));
        sheet.StructuredTables[tableIndex] = grown;

        FillCalculatedColumns(sheet, grown, insertRow, insertRow + _count - 1, firstDataRow);
        StructuredTableShiftSupport.Reband(ctx.Workbook, sheet, grown, _undo);

        _affectedCells = StructuredTableShiftSupport.BuildAffected(
            ctx.Workbook, grown, shiftOutcome.AffectedCells ?? [], _undo);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    // Excel extends a calculated column into every row inserted in the table. The stored formula is
    // anchored on the table's first data row, so each new row gets it row-shifted from that anchor.
    private void FillCalculatedColumns(Sheet sheet, StructuredTableModel grown, uint firstNewRow, uint lastNewRow, uint anchorRow)
    {
        for (var columnIndex = 0; columnIndex < grown.Columns.Count; columnIndex++)
        {
            var formula = grown.Columns[columnIndex].CalculatedColumnFormula;
            if (string.IsNullOrWhiteSpace(formula))
                continue;

            var col = grown.Range.Start.Col + (uint)columnIndex;
            for (var row = firstNewRow; row <= lastNewRow; row++)
            {
                var address = new CellAddress(_sheetId, row, col);
                _undo!.Capture(sheet, address);
                var filled = Cell.FromFormula(StructuredTableEditEffects.ShiftFormulaRows(formula, anchorRow, row, sheet.Name));
                if (sheet.GetStyleOnly(row, col) is { } inheritedStyle)
                    filled.StyleId = inheritedStyle;
                sheet.SetCell(address, filled);
            }
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_shift is null || _previousTable is null || _undo is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undo.Restore(sheet);
        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
        _shift.Revert(ctx);
        _affectedCells = [.. _shift.AffectedCells];
        _shift = null;
        _previousTable = null;
        _undo = null;
    }
}

/// <summary>Excel "Delete &gt; Table Rows": removes data rows, shifting only the table's columns up.</summary>
public sealed class DeleteTableRowsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly int _dataRowIndex;
    private readonly uint _count;
    private DeleteCellsCommand? _shift;
    private StructuredTableModel? _previousTable;
    private TableShiftCellUndo? _undo;
    private IReadOnlyList<CellAddress> _affectedCells = [];

    public string Label => "Delete Table Rows";
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <param name="dataRowIndex">Zero-based data-body position of the first row to delete.</param>
    public DeleteTableRowsCommand(SheetId sheetId, int tableId, int dataRowIndex, uint count = 1)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _dataRowIndex = dataRowIndex;
        _count = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _shift = null;
        _previousTable = null;
        _undo = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();
        if (_count == 0)
            return new CommandOutcome(true, IsNoOp: true);

        var table = sheet.StructuredTables[tableIndex];
        var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(table);
        var dataRowCount = (uint)(lastDataRow - firstDataRow + 1);
        if (_dataRowIndex < 0 || (ulong)_dataRowIndex + _count > dataRowCount)
            return new CommandOutcome(false, "The table rows to delete are outside the table.");
        if (StructuredTableShiftSupport.IsFiltered(sheet, table))
            return new CommandOutcome(false, StructuredTableShiftSupport.FilteredShiftMessage);

        // Excel never leaves a table without a data row: deleting every data row removes all but the
        // first one and merely clears that one.
        var clearsAllRows = _count == dataRowCount;
        var firstDeleted = firstDataRow + (uint)_dataRowIndex;
        var shiftCount = clearsAllRows ? _count - 1 : _count;
        if (clearsAllRows)
            firstDeleted = firstDataRow + 1;

        if (clearsAllRows && shiftCount == 0 && RowIsBlank(sheet, table, firstDataRow))
            return new CommandOutcome(true, IsNoOp: true);

        DeleteCellsCommand? shift = null;
        IReadOnlyList<CellAddress> shiftAffected = [];
        if (shiftCount > 0)
        {
            var band = StructuredTableShiftSupport.Range(
                _sheetId, firstDeleted, table.Range.Start.Col, firstDeleted + shiftCount - 1, table.Range.End.Col);
            shift = new DeleteCellsCommand(_sheetId, band, DeleteCellsShiftDirection.Up)
            {
                AllowedStructuredTableId = _tableId
            };
            var shiftOutcome = shift.Apply(ctx);
            if (!shiftOutcome.Success)
                return shiftOutcome;
            shiftAffected = shiftOutcome.AffectedCells ?? [];
        }

        _shift = shift;
        _previousTable = table;
        _undo = new TableShiftCellUndo();

        var shrunk = table;
        if (shiftCount > 0)
        {
            var shrunkRange = StructuredTableShiftSupport.Range(
                _sheetId, table.Range.Start.Row, table.Range.Start.Col, table.Range.End.Row - shiftCount, table.Range.End.Col);
            shrunk = StructuredTableShiftSupport.Reshape(table, shrunkRange, table.Columns, table.FilterColumns,
                StructuredTableShiftSupport.AdjustSortState(table.NativeSortStateXml, rowAxis: true, insert: false, firstDeleted, shiftCount));
            sheet.StructuredTables[tableIndex] = shrunk;
        }

        if (clearsAllRows)
            ClearRow(sheet, table, firstDataRow);

        StructuredTableShiftSupport.Reband(ctx.Workbook, sheet, shrunk, _undo);

        var affected = new List<CellAddress>(shiftAffected);
        if (clearsAllRows)
        {
            for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
                affected.Add(new CellAddress(_sheetId, firstDataRow, col));
        }

        _affectedCells = StructuredTableShiftSupport.BuildAffected(ctx.Workbook, shrunk, affected, _undo);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    private static bool RowIsBlank(Sheet sheet, StructuredTableModel table, uint row)
    {
        for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
        {
            if (!StructuredTableShiftSupport.IsBlank(sheet.GetCell(new CellAddress(sheet.Id, row, col))))
                return false;
        }

        return true;
    }

    private void ClearRow(Sheet sheet, StructuredTableModel table, uint row)
    {
        for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
        {
            var address = new CellAddress(_sheetId, row, col);
            var existing = sheet.GetCell(address);
            if (StructuredTableShiftSupport.IsBlank(existing))
                continue;

            _undo!.Capture(sheet, address);
            var blank = Cell.FromValue(BlankValue.Instance);
            blank.StyleId = existing?.StyleId ?? StyleId.Default;
            sheet.SetCell(address, blank);
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null || _undo is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undo.Restore(sheet);
        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
        if (_shift is not null)
        {
            _shift.Revert(ctx);
            _affectedCells = [.. _shift.AffectedCells];
        }

        _shift = null;
        _previousTable = null;
        _undo = null;
    }
}

/// <summary>Excel "Insert &gt; Table Columns to the Left/Right": adds columns inside a table, shifting only the table's rows right.</summary>
public sealed class InsertTableColumnsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly int _columnIndex;
    private readonly uint _count;
    private InsertCellsCommand? _shift;
    private StructuredTableModel? _previousTable;
    private TableShiftCellUndo? _undo;
    private StructuredTableShiftSupport.FilterStateUndo? _filterUndo;
    private IReadOnlyList<CellAddress> _affectedCells = [];

    public string Label => "Insert Table Columns";
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <param name="columnIndex">Zero-based table column position the new columns are inserted BEFORE (column count appends).</param>
    public InsertTableColumnsCommand(SheetId sheetId, int tableId, int columnIndex, uint count = 1)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _columnIndex = columnIndex;
        _count = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _shift = null;
        _previousTable = null;
        _undo = null;
        _filterUndo = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();
        if (_count == 0)
            return new CommandOutcome(true, IsNoOp: true);

        var table = sheet.StructuredTables[tableIndex];
        var columnCount = (int)table.Range.ColCount;
        if (_columnIndex < 0 || _columnIndex > columnCount)
            return new CommandOutcome(false, "The table column to insert at is outside the table.");
        if ((ulong)table.Range.End.Col + _count > CellAddress.MaxCol)
            return new CommandOutcome(false, $"Cannot insert table columns: the table would extend past the last column ({CellAddress.MaxCol}).");

        var insertCol = table.Range.Start.Col + (uint)_columnIndex;
        var band = StructuredTableShiftSupport.Range(
            _sheetId, table.Range.Start.Row, insertCol, table.Range.End.Row, insertCol + _count - 1);
        var shift = new InsertCellsCommand(_sheetId, band, InsertCellsShiftDirection.Right)
        {
            AllowedStructuredTableId = _tableId
        };
        var shiftOutcome = shift.Apply(ctx);
        if (!shiftOutcome.Success)
            return shiftOutcome;

        _shift = shift;
        _previousTable = table;
        _undo = new TableShiftCellUndo();
        _filterUndo = new StructuredTableShiftSupport.FilterStateUndo(sheet);
        StructuredTableShiftSupport.ShiftFilterColumnKeysRight(sheet, insertCol, table.Range.End.Col, _count);

        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var columns = BuildColumns(sheet, table, insertCol, hasHeaderRow, out var newNames);
        var filterColumns = table.FilterColumns
            .Select(filter => filter.ColumnId >= _columnIndex
                ? StructuredTableModel.DeepCloneFilterColumn(filter, filter.ColumnId + (int)_count)
                : filter)
            .ToList();
        var grownRange = StructuredTableShiftSupport.Range(
            _sheetId, table.Range.Start.Row, table.Range.Start.Col, table.Range.End.Row, table.Range.End.Col + _count);
        var grown = StructuredTableShiftSupport.Reshape(table, grownRange, columns, filterColumns,
            StructuredTableShiftSupport.AdjustSortState(table.NativeSortStateXml, rowAxis: false, insert: true, insertCol, _count));
        sheet.StructuredTables[tableIndex] = grown;

        if (hasHeaderRow)
        {
            for (var i = 0; i < newNames.Count; i++)
            {
                var col = insertCol + (uint)i;
                var address = new CellAddress(_sheetId, table.Range.Start.Row, col);
                _undo.Capture(sheet, address);
                var header = Cell.FromValue(new TextValue(newNames[i]));
                if (sheet.GetStyleOnly(address.Row, col) is { } inheritedStyle)
                    header.StyleId = inheritedStyle;
                sheet.SetCell(address, header);
            }
        }

        StructuredTableShiftSupport.Reband(ctx.Workbook, sheet, grown, _undo);

        _affectedCells = StructuredTableShiftSupport.BuildAffected(
            ctx.Workbook, grown, shiftOutcome.AffectedCells ?? [], _undo);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    private List<StructuredTableColumnModel> BuildColumns(
        Sheet sheet, StructuredTableModel table, uint insertCol, bool hasHeaderRow, out List<string> newNames)
    {
        var rewriteOp = new InsertCellsShiftRightOp(
            sheet.Name,
            table.Range.Start.Row, table.Range.End.Row,
            insertCol, CellAddress.MaxCol,
            insertCol, _count);

        var usedNames = new HashSet<string>(table.Columns.Select(column => column.Name), StringComparer.OrdinalIgnoreCase);
        if (hasHeaderRow)
        {
            for (var col = table.Range.Start.Col; col <= table.Range.End.Col + _count; col++)
            {
                if (sheet.GetCell(table.Range.Start.Row, col)?.Value is TextValue { Value.Length: > 0 } text)
                    usedNames.Add(text.Value);
            }
        }

        var nextId = 1;
        foreach (var existing in table.Columns)
            nextId = Math.Max(nextId, existing.Id + 1);

        newNames = [];
        var columns = new List<StructuredTableColumnModel>();
        for (var i = 0; i < _columnIndex && i < table.Columns.Count; i++)
            columns.Add(table.Columns[i]);

        var candidate = 1;
        for (var i = 0; i < _count; i++)
        {
            string name;
            do
            {
                name = $"Column{candidate++}";
            }
            while (usedNames.Contains(name));

            usedNames.Add(name);
            newNames.Add(name);
            columns.Add(new StructuredTableColumnModel(nextId++, name));
        }

        for (var i = _columnIndex; i < table.Columns.Count; i++)
        {
            var survivor = table.Columns[i];
            columns.Add(survivor with
            {
                CalculatedColumnFormula = Rewrite(survivor.CalculatedColumnFormula, rewriteOp, sheet.Name),
                TotalsRowFormula = Rewrite(survivor.TotalsRowFormula, rewriteOp, sheet.Name)
            });
        }

        return columns;
    }

    private static string? Rewrite(string? formula, RewriteOperation op, string sheetName) =>
        string.IsNullOrWhiteSpace(formula) ? formula : FormulaRewriter.Rewrite(formula, op, sheetName) ?? formula;

    public void Revert(ICommandContext ctx)
    {
        if (_shift is null || _previousTable is null || _undo is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undo.Restore(sheet);
        _filterUndo?.Restore(sheet);
        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _previousTable;
        _shift.Revert(ctx);
        _affectedCells = [.. _shift.AffectedCells];
        _shift = null;
        _previousTable = null;
        _undo = null;
        _filterUndo = null;
    }
}

/// <summary>Excel "Delete &gt; Table Columns": removes columns, shifting only the table's rows left.</summary>
public sealed class DeleteTableColumnsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly int _columnIndex;
    private readonly uint _count;
    private DeleteCellsCommand? _shift;
    private StructuredTableColumnDeleteEffects? _structuredEffects;
    private StructuredTableModel? _tableBeforeResize;
    private TableShiftCellUndo? _undo;
    private StructuredTableShiftSupport.FilterStateUndo? _filterUndo;
    private IReadOnlyList<CellAddress> _affectedCells = [];

    public string Label => "Delete Table Columns";
    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <param name="columnIndex">Zero-based table column position of the first column to delete.</param>
    public DeleteTableColumnsCommand(SheetId sheetId, int tableId, int columnIndex, uint count = 1)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _columnIndex = columnIndex;
        _count = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _shift = null;
        _structuredEffects = null;
        _tableBeforeResize = null;
        _undo = null;
        _filterUndo = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            return CommandGuards.RejectStructuredTableNotFound();
        if (_count == 0)
            return new CommandOutcome(true, IsNoOp: true);

        var table = sheet.StructuredTables[tableIndex];
        var columnCount = table.Range.ColCount;
        if (_columnIndex < 0 || (ulong)_columnIndex + _count > columnCount)
            return new CommandOutcome(false, "The table columns to delete are outside the table.");
        if (_count >= columnCount)
            return new CommandOutcome(false, "A table must keep at least one column.");

        var firstDeletedCol = table.Range.Start.Col + (uint)_columnIndex;
        var lastDeletedCol = firstDeletedCol + _count - 1;

        // Structured references naming a deleted column become #REF! (workbook-wide), exactly as the
        // whole-column delete does. Scoped to this table so a stacked table sharing the columns is
        // left alone.
        StructuredTableColumnDeleteEffects effects;
        try
        {
            effects = StructuredTableColumnDeleteEffects.Capture(sheet, firstDeletedCol, _count, _tableId);
            effects.Apply(ctx.Workbook);
        }
        catch (InvalidOperationException ex)
        {
            return new CommandOutcome(false, ex.Message);
        }

        // The effects pass can replace the table model (it rewrites column formula text); re-read it.
        var current = sheet.StructuredTables[tableIndex];
        var band = StructuredTableShiftSupport.Range(
            _sheetId, current.Range.Start.Row, firstDeletedCol, current.Range.End.Row, lastDeletedCol);
        var shift = new DeleteCellsCommand(_sheetId, band, DeleteCellsShiftDirection.Left)
        {
            AllowedStructuredTableId = _tableId
        };
        var shiftOutcome = shift.Apply(ctx);
        if (!shiftOutcome.Success)
        {
            effects.Revert();
            return shiftOutcome;
        }

        _shift = shift;
        _structuredEffects = effects;
        _tableBeforeResize = current;
        _undo = new TableShiftCellUndo();
        _filterUndo = new StructuredTableShiftSupport.FilterStateUndo(sheet);

        var rewriteOp = new DeleteCellsShiftLeftOp(
            sheet.Name,
            current.Range.Start.Row, current.Range.End.Row,
            firstDeletedCol, lastDeletedCol,
            CellAddress.MaxCol, _count);
        var columns = new List<StructuredTableColumnModel>();
        for (var i = 0; i < current.Columns.Count; i++)
        {
            if (i >= _columnIndex && i < _columnIndex + (int)_count)
                continue;

            var survivor = current.Columns[i];
            columns.Add(i < _columnIndex
                ? survivor
                : survivor with
                {
                    CalculatedColumnFormula = Rewrite(survivor.CalculatedColumnFormula, rewriteOp, sheet.Name),
                    TotalsRowFormula = Rewrite(survivor.TotalsRowFormula, rewriteOp, sheet.Name)
                });
        }

        var filterColumns = new List<StructuredTableFilterColumnModel>();
        foreach (var filter in current.FilterColumns)
        {
            if (filter.ColumnId >= _columnIndex && filter.ColumnId < _columnIndex + (int)_count)
                continue;

            filterColumns.Add(filter.ColumnId >= _columnIndex + (int)_count
                ? StructuredTableModel.DeepCloneFilterColumn(filter, filter.ColumnId - (int)_count)
                : filter);
        }

        var shrunkRange = StructuredTableShiftSupport.Range(
            _sheetId, current.Range.Start.Row, current.Range.Start.Col, current.Range.End.Row, current.Range.End.Col - _count);
        var shrunk = StructuredTableShiftSupport.Reshape(current, shrunkRange, columns, filterColumns,
            StructuredTableShiftSupport.AdjustSortState(current.NativeSortStateXml, rowAxis: false, insert: false, firstDeletedCol, _count));
        sheet.StructuredTables[tableIndex] = shrunk;
        StructuredTableShiftSupport.ReleaseAndShiftFilterColumnKeysLeft(sheet, current.Range, firstDeletedCol, _count, shrunk);

        StructuredTableShiftSupport.Reband(ctx.Workbook, sheet, shrunk, _undo);

        var affected = new List<CellAddress>(shiftOutcome.AffectedCells ?? []);
        affected.AddRange(effects.Affected);
        _affectedCells = StructuredTableShiftSupport.BuildAffected(ctx.Workbook, shrunk, affected, _undo);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    private static string? Rewrite(string? formula, RewriteOperation op, string sheetName) =>
        string.IsNullOrWhiteSpace(formula) ? formula : FormulaRewriter.Rewrite(formula, op, sheetName) ?? formula;

    public void Revert(ICommandContext ctx)
    {
        if (_shift is null || _tableBeforeResize is null || _undo is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        _undo.Restore(sheet);
        _filterUndo?.Restore(sheet);
        if (CommandGuards.TryFindStructuredTableIndex(sheet, _tableId, out var tableIndex))
            sheet.StructuredTables[tableIndex] = _tableBeforeResize;
        _shift.Revert(ctx);
        _structuredEffects?.Revert();
        _affectedCells = [.. _shift.AffectedCells];
        _shift = null;
        _structuredEffects = null;
        _tableBeforeResize = null;
        _undo = null;
        _filterUndo = null;
    }
}
