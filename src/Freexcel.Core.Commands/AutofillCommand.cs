using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Fills a range by repeating the last cell of <paramref name="sourceRange"/>.
/// Formulas have relative cell references incremented by the fill offset.
/// </summary>
public sealed class AutofillCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _fillRange;
    private List<(CellAddress Addr, Cell? OldCell)>? _snapshot;

    public string Label => "Autofill";

    public AutofillCommand(SheetId sheetId, GridRange sourceRange, GridRange fillRange)
    {
        _sheetId     = sheetId;
        _sourceRange = sourceRange;
        _fillRange   = fillRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var targets = _fillRange.AllCells().ToList();
        if (targets.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return new CommandOutcome(false, "The sheet is protected.");

        var sourceAddr = _sourceRange.End;
        var sourceCell = sheet.GetCell(sourceAddr);
        var scalarSeries = TryCreateScalarSeries(sheet);

        _snapshot = [];

        foreach (var addr in targets)
        {
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));

            if (sourceCell is null)
            {
                sheet.ClearCell(addr);
                continue;
            }

            int rowOffset = (int)addr.Row - (int)sourceAddr.Row;
            int colOffset = (int)addr.Col - (int)sourceAddr.Col;

            Cell newCell;
            if (scalarSeries is not null)
            {
                var offset = scalarSeries.Direction == FillDirection.Down
                    ? (int)addr.Row - (int)sourceAddr.Row
                    : (int)addr.Col - (int)sourceAddr.Col;
                newCell = Cell.FromValue(scalarSeries.CreateValue(scalarSeries.LastValue + scalarSeries.Step * offset));
            }
            else if (sourceCell.HasFormula && sourceCell.FormulaText is not null)
            {
                var shifted = FormulaRewriter.Rewrite(sourceCell.FormulaText,
                    new PasteOffsetOp(rowOffset, colOffset), sheet.Name)
                    ?? sourceCell.FormulaText;
                newCell = Cell.FromFormula(shifted);
            }
            else
            {
                newCell = Cell.FromValue(sourceCell.Value);
            }

            newCell.StyleId = sourceCell.StyleId;
            sheet.SetCell(addr, newCell);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null) sheet.ClearCell(addr);
            else sheet.SetCell(addr, oldCell.Clone());
        }
    }


    private ScalarSeries? TryCreateScalarSeries(Sheet sheet)
    {
        var isVertical = _sourceRange.ColCount == 1 && _sourceRange.RowCount >= 2;
        var isHorizontal = _sourceRange.RowCount == 1 && _sourceRange.ColCount >= 2;
        if (!isVertical && !isHorizontal)
            return null;

        var values = _sourceRange.AllCells()
            .Select(addr => sheet.GetCell(addr)?.Value)
            .ToList();

        Func<double, ScalarValue>? createValue;
        if (values.All(value => value is NumberValue))
            createValue = serial => new NumberValue(serial);
        else if (values.All(value => value is DateTimeValue))
            createValue = serial => new DateTimeValue(serial);
        else
            return null;

        var numbers = values.Select(value => value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            _ => 0
        }).ToList();
        var step = numbers[^1] - numbers[^2];
        return new ScalarSeries(numbers[^1], step, isVertical ? FillDirection.Down : FillDirection.Right, createValue);
    }

    private sealed record ScalarSeries(
        double LastValue,
        double Step,
        FillDirection Direction,
        Func<double, ScalarValue> CreateValue);

    private enum FillDirection
    {
        Down,
        Right
    }

}
