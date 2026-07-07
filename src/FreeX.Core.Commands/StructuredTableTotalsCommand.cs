using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RefreshStructuredTableTotalsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly Dictionary<CellAddress, Cell?> _previousCells = [];

    public string Label => "Refresh Table Totals";

    public RefreshStructuredTableTotalsCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();
        if (!table.TotalsRowShown)
            return new CommandOutcome(false, "Table totals row is not shown.");
        if (table.Columns.Count == 0)
            return CommandGuards.RejectStructuredTableHasNoColumns();

        _previousCells.Clear();
        var totalsRow = table.Range.End.Row;
        var affectedCells = new List<CellAddress>(table.Columns.Count);
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var address = new CellAddress(_sheetId, totalsRow, table.Range.Start.Col + (uint)index);
            affectedCells.Add(address);
            _previousCells[address] = sheet.GetCell(address.Row, address.Col)?.Clone();
            if (ResolveTotalsCell(table.Columns[index]) is { } cell)
                sheet.SetCell(address, cell);
            else
                sheet.SetCell(address, BlankValue.Instance);
        }

        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousCells.Count == 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, cell) in _previousCells)
        {
            if (cell is null)
                sheet.ClearCell(address.Row, address.Col);
            else
                sheet.SetCell(address, cell);
        }
        _previousCells.Clear();
    }

    // P106: Excel's table totals row is always backed by a live =SUBTOTAL(10x,[Column]) formula for
    // every built-in totalsRowFunction (never a static constant) — the 100-series function numbers
    // make SUBTOTAL itself skip manually/filter/group-hidden rows at evaluation time, so no separate
    // hidden-row-aware aggregation is needed here anymore. Writing a formula (instead of a
    // precomputed NumberValue) also keeps the total live across future data edits and recalcs
    // correctly even when it is regenerated before FillGrownCalculatedColumns's newly written
    // formula cells have been recalculated (their cached Values are still blank at that point; a
    // static aggregate computed then would freeze at a wrong, stale number forever).
    private static readonly Dictionary<string, int> TotalsRowFunctionSubtotalNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["average"] = 101,
        ["avg"] = 101,
        ["count"] = 103,
        ["countnums"] = 102,
        ["max"] = 104,
        ["min"] = 105,
        ["stddev"] = 107,
        ["sum"] = 109,
        ["var"] = 110,
    };

    private static Cell? ResolveTotalsCell(StructuredTableColumnModel column)
    {
        if (!string.IsNullOrWhiteSpace(column.TotalsRowLabel))
            return Cell.FromValue(new TextValue(column.TotalsRowLabel));
        if (!string.IsNullOrWhiteSpace(column.TotalsRowFormula))
            return Cell.FromFormula(column.TotalsRowFormula);
        if (string.IsNullOrWhiteSpace(column.TotalsRowFunction))
            return null;

        var function = column.TotalsRowFunction.Trim();
        if (!TotalsRowFunctionSubtotalNumbers.TryGetValue(function, out var subtotalNumber))
            return null;

        var escapedColumnName = EscapeStructuredReferenceColumnName(column.Name);
        return Cell.FromFormula($"SUBTOTAL({subtotalNumber.ToString(CultureInfo.InvariantCulture)},[{escapedColumnName}])");
    }

    // R12-xlsx-tables-3: a column header containing '[', ']', '#', or an apostrophe must have each
    // such character individually escaped with a leading apostrophe, or FreeX's own formula lexer
    // (see FreeX.Core.Formula.Lexer.ReadStructuredReferenceSelectorSlow) either mis-parses the
    // selector (an unescaped '[' opens a nested/combined-selector bracket group it can never close)
    // or StructuredReferenceResolver.FindColumnIndex simply fails to match the literal header text,
    // leaving the totals cell's SUBTOTAL formula resolving to #NAME?. Escaping only ']' (the
    // previous behavior) left '[' and '#' broken.
    private static readonly char[] StructuredReferenceEscapableChars = ['[', ']', '#', '\''];

    private static string EscapeStructuredReferenceColumnName(string columnName)
    {
        if (columnName.AsSpan().IndexOfAny(StructuredReferenceEscapableChars) < 0)
            return columnName;

        var builder = new StringBuilder(columnName.Length + 4);
        foreach (var ch in columnName)
        {
            if (ch is '[' or ']' or '#' or '\'')
                builder.Append('\'');
            builder.Append(ch);
        }

        return builder.ToString();
    }
}

public sealed class SetStructuredTableTotalsRowCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly bool _showTotalsRow;
    private List<StructuredTableModel>? _previousTables;
    private IWorkbookCommand? _rowCommand;
    private RefreshStructuredTableTotalsCommand? _refreshCommand;

    public string Label => _showTotalsRow ? "Show Table Totals Row" : "Hide Table Totals Row";

    public SetStructuredTableTotalsRowCommand(SheetId sheetId, int tableId, bool showTotalsRow)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _showTotalsRow = showTotalsRow;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousTables = null;
        _rowCommand = null;
        _refreshCommand = null;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();
        if (table.TotalsRowShown == _showTotalsRow)
            return new CommandOutcome(true, AffectedCells: [table.Range.End]);
        if (table.Columns.Count == 0)
            return CommandGuards.RejectStructuredTableHasNoColumns();

        return _showTotalsRow
            ? ShowTotalsRow(ctx, sheet, table)
            : HideTotalsRow(ctx, sheet, table);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _refreshCommand?.Revert(ctx);
        _rowCommand?.Revert(ctx);

        if (_previousTables is not null)
            RestoreStructuredTables(sheet, _previousTables);

        _refreshCommand = null;
        _rowCommand = null;
        _previousTables = null;
    }

    private CommandOutcome ShowTotalsRow(ICommandContext ctx, Sheet sheet, StructuredTableModel table)
    {
        if (table.Range.End.Row >= CellAddress.MaxRow)
            return new CommandOutcome(false, "Cannot show table totals row below the last worksheet row.");

        var insertRow = table.Range.End.Row + 1;
        if (sheet.StructuredTables.Any(candidate => candidate.Range.End.Row >= insertRow && candidate.Range.End.Row >= CellAddress.MaxRow))
            return new CommandOutcome(false, "Cannot show table totals row: another table would move past the last worksheet row.");

        _previousTables = sheet.StructuredTables.ToList();
        _rowCommand = new InsertRowsCommand(_sheetId, insertRow);
        var insertOutcome = _rowCommand.Apply(ctx);
        if (!insertOutcome.Success)
            return insertOutcome;

        ReplaceStructuredTables(sheet, BuildTablesAfterInsert(_previousTables, table.Id, insertRow));

        _refreshCommand = new RefreshStructuredTableTotalsCommand(_sheetId, table.Id);
        var refreshOutcome = _refreshCommand.Apply(ctx);
        if (!refreshOutcome.Success)
        {
            Revert(ctx);
            return refreshOutcome;
        }

        return new CommandOutcome(true, AffectedCells: [new CellAddress(_sheetId, insertRow, table.Range.Start.Col)]);
    }

    private CommandOutcome HideTotalsRow(ICommandContext ctx, Sheet sheet, StructuredTableModel table)
    {
        if (table.Range.End.Row <= table.Range.Start.Row)
            return new CommandOutcome(false, "Cannot hide the only row in a table.");

        var totalsRow = table.Range.End.Row;
        _previousTables = sheet.StructuredTables.ToList();
        _rowCommand = new DeleteRowsCommand(_sheetId, totalsRow);
        var deleteOutcome = _rowCommand.Apply(ctx);
        if (!deleteOutcome.Success)
            return deleteOutcome;

        ReplaceStructuredTables(sheet, BuildTablesAfterDelete(_previousTables, table.Id, totalsRow));
        return new CommandOutcome(true, AffectedCells: [new CellAddress(_sheetId, totalsRow, table.Range.Start.Col)]);
    }

    private static IEnumerable<StructuredTableModel> BuildTablesAfterInsert(
        IReadOnlyList<StructuredTableModel> tables,
        int targetTableId,
        uint insertRow)
    {
        foreach (var table in tables)
        {
            if (table.Id == targetTableId)
            {
                yield return CopyWith(
                    table,
                    new GridRange(
                        table.Range.Start,
                        new CellAddress(table.Range.End.Sheet, table.Range.End.Row + 1, table.Range.End.Col)),
                    totalsRowShown: true,
                    totalsRowCount: 1,
                    updateTotalsRowCount: true);
                continue;
            }

            yield return CopyWith(table, ShiftRangeForInsert(table.Range, insertRow));
        }
    }

    private static IEnumerable<StructuredTableModel> BuildTablesAfterDelete(
        IReadOnlyList<StructuredTableModel> tables,
        int targetTableId,
        uint deletedRow)
    {
        foreach (var table in tables)
        {
            if (table.Id == targetTableId)
            {
                yield return CopyWith(
                    table,
                    new GridRange(
                        table.Range.Start,
                        new CellAddress(table.Range.End.Sheet, table.Range.End.Row - 1, table.Range.End.Col)),
                    totalsRowShown: false,
                    totalsRowCount: 0,
                    updateTotalsRowCount: true);
                continue;
            }

            if (ShiftRangeForDelete(table.Range, deletedRow) is { } shiftedRange)
                yield return CopyWith(table, shiftedRange);
        }
    }

    private static GridRange ShiftRangeForInsert(GridRange range, uint insertRow)
    {
        if (range.Start.Row >= insertRow)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row + 1, range.End.Col));
        }

        if (range.End.Row >= insertRow)
        {
            return new GridRange(
                range.Start,
                new CellAddress(range.End.Sheet, range.End.Row + 1, range.End.Col));
        }

        return range;
    }

    private static GridRange? ShiftRangeForDelete(GridRange range, uint deletedRow)
    {
        if (range.End.Row < deletedRow)
            return range;

        if (range.Start.Row > deletedRow)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row - 1, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row - 1, range.End.Col));
        }

        if (range.RowCount <= 1)
            return null;

        return new GridRange(
            range.Start,
            new CellAddress(range.End.Sheet, range.End.Row - 1, range.End.Col));
    }

    private static StructuredTableModel CopyWith(
        StructuredTableModel table,
        GridRange range,
        bool? totalsRowShown = null,
        int? totalsRowCount = null,
        bool updateTotalsRowCount = false)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = range,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = totalsRowShown ?? table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = updateTotalsRowCount ? totalsRowCount : table.TotalsRowCount,
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

        copy.Columns.AddRange(table.Columns);
        copy.FilterColumns.AddRange(table.FilterColumns);
        return copy;
    }

    private static void ReplaceStructuredTables(Sheet sheet, IEnumerable<StructuredTableModel> tables)
    {
        sheet.StructuredTables.Clear();
        sheet.StructuredTables.AddRange(tables);
    }

    private static void RestoreStructuredTables(Sheet sheet, IReadOnlyList<StructuredTableModel> tables)
    {
        sheet.StructuredTables.Clear();
        sheet.StructuredTables.AddRange(tables);
    }
}
