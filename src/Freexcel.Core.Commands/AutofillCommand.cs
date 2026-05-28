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
        if (!TryGetFillPlan(out var plan))
            return new CommandOutcome(false, "The autofill range must be adjacent to the source range and aligned by row or column.");

        var targets = _fillRange.AllCells().ToList();
        if (targets.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return new CommandOutcome(false, "The sheet is protected.");

        var sourceAddr = GetSourceEdgeAddress(plan);
        var sourceCell = sheet.GetCell(sourceAddr);
        var scalarSeries = TryCreateScalarSeries(sheet, plan);

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
                var offset = scalarSeries.Axis == FillAxis.Vertical
                    ? Math.Abs((int)addr.Row - (int)sourceAddr.Row)
                    : Math.Abs((int)addr.Col - (int)sourceAddr.Col);
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


    private bool TryGetFillPlan(out FillPlan plan)
    {
        plan = default;

        if (_sourceRange.Start.Sheet != _fillRange.Start.Sheet)
            return false;

        if (_sourceRange.Overlaps(_fillRange))
            return false;

        if (_sourceRange.ColCount == _fillRange.ColCount &&
            _sourceRange.Start.Col == _fillRange.Start.Col &&
            _sourceRange.End.Col == _fillRange.End.Col)
        {
            if (_fillRange.Start.Row == _sourceRange.End.Row + 1)
            {
                plan = new FillPlan(FillDirection.Down, FillAxis.Vertical);
                return true;
            }

            if (_sourceRange.Start.Row > 1 && _fillRange.End.Row + 1 == _sourceRange.Start.Row)
            {
                plan = new FillPlan(FillDirection.Up, FillAxis.Vertical);
                return true;
            }
        }

        if (_sourceRange.RowCount == _fillRange.RowCount &&
            _sourceRange.Start.Row == _fillRange.Start.Row &&
            _sourceRange.End.Row == _fillRange.End.Row)
        {
            if (_fillRange.Start.Col == _sourceRange.End.Col + 1)
            {
                plan = new FillPlan(FillDirection.Right, FillAxis.Horizontal);
                return true;
            }

            if (_sourceRange.Start.Col > 1 && _fillRange.End.Col + 1 == _sourceRange.Start.Col)
            {
                plan = new FillPlan(FillDirection.Left, FillAxis.Horizontal);
                return true;
            }
        }

        return false;
    }

    private CellAddress GetSourceEdgeAddress(FillPlan plan) => plan.Direction switch
    {
        FillDirection.Down => _sourceRange.End,
        FillDirection.Right => _sourceRange.End,
        FillDirection.Up => _sourceRange.Start,
        FillDirection.Left => _sourceRange.Start,
        _ => _sourceRange.End
    };

    private ScalarSeries? TryCreateScalarSeries(Sheet sheet, FillPlan plan)
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
        var lastValue = plan.Direction is FillDirection.Up or FillDirection.Left ? numbers[0] : numbers[^1];
        var step = plan.Direction switch
        {
            FillDirection.Up or FillDirection.Left => numbers[0] - numbers[1],
            _ => numbers[^1] - numbers[^2]
        };

        return new ScalarSeries(lastValue, step, plan.Axis, createValue);
    }

    private sealed record ScalarSeries(
        double LastValue,
        double Step,
        FillAxis Axis,
        Func<double, ScalarValue> CreateValue);

    private readonly record struct FillPlan(FillDirection Direction, FillAxis Axis);

    private enum FillDirection
    {
        Down,
        Right,
        Up,
        Left
    }

    private enum FillAxis
    {
        Vertical,
        Horizontal
    }

}
