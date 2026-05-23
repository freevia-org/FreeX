using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum PasteSpecialOperation
{
    None,
    Add,
    Subtract,
    Multiply,
    Divide
}

public enum PasteSpecialContentKind
{
    Default,
    AllUsingSourceTheme,
    AllExceptBorders,
    AllMergingConditionalFormats,
    ValuesAndNumberFormats,
    ValuesAndSourceFormatting,
    FormulasAndNumberFormats
}

public readonly record struct PasteSpecialOptions(
    bool Transpose = false,
    PasteSpecialOperation Operation = PasteSpecialOperation.None,
    bool SkipBlanks = false,
    PasteSpecialContentKind ContentKind = PasteSpecialContentKind.Default);

public sealed class PasteSpecialCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _sourceCells;
    private readonly CellAddress _destination;
    private readonly PasteSpecialOptions _options;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Paste Special";

    public PasteSpecialCellsCommand(
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Address, Cell Cell)> sourceCells,
        CellAddress destination,
        PasteSpecialOptions options)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _sourceCells = sourceCells;
        _destination = destination;
        _options = options;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste destination must be on the target sheet.");
        if (!Enum.IsDefined(_options.Operation))
            return new CommandOutcome(false, "Paste Special operation is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        var cells = BuildDestinationCells(sheet).ToList();
        if (sheet.IsProtected)
        {
            foreach (var (address, _) in cells)
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return new CommandOutcome(false, "The sheet is protected.");
        }

        _snapshot = [];
        foreach (var (address, cell) in cells)
        {
            _snapshot.Add((address, sheet.GetCell(address)?.Clone(), sheet.GetStyleOnly(address.Row, address.Col)));
            sheet.SetCell(address, cell);
        }

        return new CommandOutcome(true, AffectedCells: cells.Select(c => c.Address).ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(address);
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(address.Row, address.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(address.Row, address.Col);
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }
        }
    }

    private IEnumerable<(CellAddress Address, Cell Cell)> BuildDestinationCells(Sheet sheet)
    {
        foreach (var (sourceAddress, sourceCell) in _sourceCells)
        {
            if (_options.SkipBlanks && IsBlank(sourceCell))
                continue;

            var rowOffset = sourceAddress.Row - _sourceRange.Start.Row;
            var colOffset = sourceAddress.Col - _sourceRange.Start.Col;
            var destination = _options.Transpose
                ? new CellAddress(_sheetId, _destination.Row + colOffset, _destination.Col + rowOffset)
                : new CellAddress(_sheetId, _destination.Row + rowOffset, _destination.Col + colOffset);

            var cell = sourceCell.Clone();
            if (_options.Operation != PasteSpecialOperation.None)
            {
                var existing = sheet.GetCell(destination)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
                existing.StyleId = sheet.GetStyleOnly(destination.Row, destination.Col) ?? existing.StyleId;
                cell = existing;
                cell.Value = ApplyOperation(existing.Value, sourceCell.Value, _options.Operation);
                cell.FormulaText = null;
            }

            yield return (destination, cell);
        }
    }

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    private static ScalarValue ApplyOperation(ScalarValue destination, ScalarValue source, PasteSpecialOperation operation)
    {
        if (!TryNumber(destination, out var left) || !TryNumber(source, out var right))
            return ErrorValue.Value;

        return operation switch
        {
            PasteSpecialOperation.Add => new NumberValue(left + right),
            PasteSpecialOperation.Subtract => new NumberValue(left - right),
            PasteSpecialOperation.Multiply => new NumberValue(left * right),
            PasteSpecialOperation.Divide when Math.Abs(right) < 0.000000000001 => ErrorValue.DivByZero,
            PasteSpecialOperation.Divide => new NumberValue(left / right),
            _ => source
        };
    }

    private static bool TryNumber(ScalarValue value, out double number)
    {
        if (value is NumberValue n)
        {
            number = n.Value;
            return true;
        }

        if (value is BlankValue)
        {
            number = 0;
            return true;
        }

        number = 0;
        return false;
    }
}
