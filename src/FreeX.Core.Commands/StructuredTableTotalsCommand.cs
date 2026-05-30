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

        var table = sheet.StructuredTables.FirstOrDefault(candidate => candidate.Id == _tableId);
        if (table is null)
            return new CommandOutcome(false, "Table was not found.");
        if (!table.TotalsRowShown)
            return new CommandOutcome(false, "Table totals row is not shown.");
        if (table.Columns.Count == 0)
            return new CommandOutcome(false, "Table has no columns.");

        _previousCells.Clear();
        var totalsRow = table.Range.End.Row;
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var address = new CellAddress(_sheetId, totalsRow, table.Range.Start.Col + (uint)index);
            _previousCells[address] = sheet.GetCell(address.Row, address.Col)?.Clone();
            if (ResolveTotalsValue(sheet, table, table.Columns[index], index) is { } value)
                sheet.SetCell(address, value);
            else
                sheet.SetCell(address, BlankValue.Instance);
        }

        return new CommandOutcome(true);
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

    private static ScalarValue? ResolveTotalsValue(
        Sheet sheet,
        StructuredTableModel table,
        StructuredTableColumnModel column,
        int columnIndex)
    {
        if (!string.IsNullOrWhiteSpace(column.TotalsRowLabel))
            return new TextValue(column.TotalsRowLabel);
        if (!string.IsNullOrWhiteSpace(column.TotalsRowFormula))
            return new TextValue(column.TotalsRowFormula);
        if (string.IsNullOrWhiteSpace(column.TotalsRowFunction))
            return null;

        var values = ReadColumnValues(sheet, table, columnIndex).ToList();
        var numbers = values
            .Select(TryGetNumber)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return column.TotalsRowFunction.Trim().ToLowerInvariant() switch
        {
            "sum" => new NumberValue(numbers.Sum()),
            "average" or "avg" => new NumberValue(numbers.Count == 0 ? 0 : numbers.Average()),
            "count" => new NumberValue(values.Count(IsNonBlank)),
            "countnums" or "countNums" => new NumberValue(numbers.Count),
            "min" => new NumberValue(numbers.Count == 0 ? 0 : numbers.Min()),
            "max" => new NumberValue(numbers.Count == 0 ? 0 : numbers.Max()),
            _ => null
        };
    }

    private static IEnumerable<ScalarValue> ReadColumnValues(Sheet sheet, StructuredTableModel table, int columnIndex)
    {
        var col = table.Range.Start.Col + (uint)columnIndex;
        var lastDataRow = table.TotalsRowShown ? table.Range.End.Row - 1 : table.Range.End.Row;
        for (var row = table.Range.Start.Row + 1; row <= lastDataRow; row++)
            yield return sheet.GetValue(row, col);
    }

    private static double? TryGetNumber(ScalarValue value) =>
        value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            BoolValue boolean => boolean.Value ? 1 : 0,
            _ => null
        };

    private static bool IsNonBlank(ScalarValue value) => value is not BlankValue;
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

        var table = sheet.StructuredTables.FirstOrDefault(candidate => candidate.Id == _tableId);
        if (table is null)
            return new CommandOutcome(false, "Table was not found.");
        if (table.TotalsRowShown == _showTotalsRow)
            return new CommandOutcome(true, AffectedCells: [table.Range.End]);
        if (table.Columns.Count == 0)
            return new CommandOutcome(false, "Table has no columns.");

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
