using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum FillCellsDirection
{
    Down,
    Right,
    Up,
    Left
}

public sealed class FillCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly FillCellsDirection _direction;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => _direction switch
    {
        FillCellsDirection.Down => "Fill Down",
        FillCellsDirection.Right => "Fill Right",
        FillCellsDirection.Up => "Fill Up",
        FillCellsDirection.Left => "Fill Left",
        _ => "Fill"
    };

    public FillCellsCommand(SheetId sheetId, GridRange range, FillCellsDirection direction)
    {
        _sheetId = sheetId;
        _range = range;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var targets = GetTargetAddresses().ToList();
        if (targets.Count == 0)
            return new CommandOutcome(false, "The fill range must include at least one target cell.");
        if (targets.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return new CommandOutcome(false, "The sheet is protected.");

        _snapshot = [];
        foreach (var target in targets)
        {
            _snapshot.Add((target, sheet.GetCell(target)?.Clone()));
            var source = GetSourceAddress(target);
            var sourceCell = sheet.GetCell(source);
            if (sourceCell is null)
            {
                sheet.ClearCell(target);
                continue;
            }

            sheet.SetCell(target, CloneForTarget(sourceCell, source, target, sheet.Name));
        }

        return new CommandOutcome(true, AffectedCells: targets);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, oldCell.Clone());
        }
    }

    private IEnumerable<CellAddress> GetTargetAddresses()
    {
        switch (_direction)
        {
            case FillCellsDirection.Down:
                for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
                for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
            case FillCellsDirection.Right:
                for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
                for (uint col = _range.Start.Col + 1; col <= _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
            case FillCellsDirection.Up:
                for (uint row = _range.Start.Row; row < _range.End.Row; row++)
                for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
            case FillCellsDirection.Left:
                for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
                for (uint col = _range.Start.Col; col < _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
        }
    }

    private CellAddress GetSourceAddress(CellAddress target) => _direction switch
    {
        FillCellsDirection.Down => new CellAddress(_sheetId, _range.Start.Row, target.Col),
        FillCellsDirection.Right => new CellAddress(_sheetId, target.Row, _range.Start.Col),
        FillCellsDirection.Up => new CellAddress(_sheetId, _range.End.Row, target.Col),
        FillCellsDirection.Left => new CellAddress(_sheetId, target.Row, _range.End.Col),
        _ => target
    };

    private static Cell CloneForTarget(Cell sourceCell, CellAddress source, CellAddress target, string sheetName)
    {
        var result = sourceCell.Clone();
        if (sourceCell.HasFormula && sourceCell.FormulaText is { } formula)
        {
            var rowOffset = (int)target.Row - (int)source.Row;
            var colOffset = (int)target.Col - (int)source.Col;
            result.FormulaText = FormulaRewriter.Rewrite(formula,
                new PasteOffsetOp(rowOffset, colOffset), sheetName) ?? formula;
        }

        return result;
    }
}
