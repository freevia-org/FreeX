using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
