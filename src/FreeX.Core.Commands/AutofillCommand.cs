using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Fills a range by repeating the last cell of <paramref name="sourceRange"/>.
/// Formulas have relative cell references incremented by the fill offset.
/// </summary>
public sealed class AutofillCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _fillRange;
    private List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

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

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        var sourceAddr = GetSourceEdgeAddress(plan);
        var sourceCell = sheet.GetCell(sourceAddr);
        var scalarSeries = TryCreateScalarSeries(sheet, plan);

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        var writtenCells = new List<CellAddress>(capacity);

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                var addr = new CellAddress(_sheetId, row, col);
                var oldCell = sheet.GetCell(addr);
                var oldStyleOnly = oldCell is null ? sheet.GetStyleOnly(row, col) : null;
                _snapshot.Add((addr, oldCell?.Clone(), oldStyleOnly));
                writtenCells.Add(addr);

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
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(addr);
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(addr.Row, addr.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(addr.Row, addr.Col);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }
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

    private int GetFillCellCapacity()
    {
        var count = _fillRange.CellCount;
        return count <= int.MaxValue ? (int)count : 0;
    }

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
        var naturalSlope = ComputeLinearFitSlope(numbers);
        var step = plan.Direction is FillDirection.Up or FillDirection.Left ? -naturalSlope : naturalSlope;

        return new ScalarSeries(lastValue, step, plan.Axis, createValue);
    }

    /// <summary>
    /// Fits a straight line (least-squares) through <paramref name="numbers"/> (treated as
    /// y-values at evenly spaced x = 0, 1, 2, ...) and returns its slope, matching Excel's
    /// fill-handle behavior for a linear numeric/date trend. For exactly two values this
    /// reduces to the plain two-point slope (numbers[1] - numbers[0]).
    /// </summary>
    private static double ComputeLinearFitSlope(IReadOnlyList<double> numbers)
    {
        var n = numbers.Count;
        if (n < 2)
            return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += numbers[i];
            sumXY += i * numbers[i];
            sumXX += (double)i * i;
        }

        var denominator = n * sumXX - sumX * sumX;
        if (denominator == 0)
            return 0;

        return (n * sumXY - sumX * sumY) / denominator;
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
