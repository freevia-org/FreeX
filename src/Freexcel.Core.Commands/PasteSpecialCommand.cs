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

public readonly record struct PasteSpecialOptions(
    bool Transpose = false,
    PasteSpecialOperation Operation = PasteSpecialOperation.None);

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

public sealed class PasteColumnWidthsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly uint _destinationStartCol;
    private Dictionary<uint, double>? _previousWidths;

    public string Label => "Paste Column Widths";

    public PasteColumnWidthsCommand(SheetId sheetId, GridRange sourceRange, uint destinationStartCol)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destinationStartCol = destinationStartCol;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sheetId || _sourceRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Source range must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var destinationEndCol = _destinationStartCol + _sourceRange.ColCount - 1;
        _previousWidths = new Dictionary<uint, double>();
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
        {
            if (sheet.ColumnWidths.TryGetValue(col, out var width))
                _previousWidths[col] = width;
        }

        for (uint offset = 0; offset < _sourceRange.ColCount; offset++)
        {
            var sourceCol = _sourceRange.Start.Col + offset;
            var destinationCol = _destinationStartCol + offset;
            if (sheet.ColumnWidths.TryGetValue(sourceCol, out var width))
                sheet.ColumnWidths[destinationCol] = width;
            else
                sheet.ColumnWidths.Remove(destinationCol);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousWidths is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var destinationEndCol = _destinationStartCol + _sourceRange.ColCount - 1;
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
            sheet.ColumnWidths.Remove(col);
        foreach (var (col, width) in _previousWidths)
            sheet.ColumnWidths[col] = width;
    }
}

public sealed class PasteRangeAsPictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly PictureModel _picture;
    private bool _added;

    public string Label => "Paste Picture";

    public PasteRangeAsPictureCommand(
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Address, string Text)> sourceCells,
        CellAddress destination)
    {
        _sheetId = sheetId;
        _picture = new PictureModel
        {
            Anchor = destination,
            SourceRowCount = sourceRange.RowCount,
            SourceColumnCount = sourceRange.ColCount,
            Width = Math.Max(80, sourceRange.ColCount * 80),
            Height = Math.Max(40, sourceRange.RowCount * 20)
        };

        foreach (var (address, text) in sourceCells)
        {
            if (address.Row < sourceRange.Start.Row ||
                address.Row > sourceRange.End.Row ||
                address.Col < sourceRange.Start.Col ||
                address.Col > sourceRange.End.Col)
                continue;

            _picture.Cells.Add(new PictureCellSnapshot(
                address.Row - sourceRange.Start.Row,
                address.Col - sourceRange.Start.Col,
                text));
        }
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_picture.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Picture anchor must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        sheet.Pictures.Add(_picture);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).Pictures.Remove(_picture);
        _added = false;
    }
}

public static class PasteLinkService
{
    public static IReadOnlyList<(CellAddress Address, Cell Cell)> CreateLinkedCells(
        GridRange sourceRange,
        CellAddress destination,
        string sourceSheetName,
        bool transpose)
    {
        var linkedCells = new List<(CellAddress Address, Cell Cell)>();
        for (uint row = sourceRange.Start.Row; row <= sourceRange.End.Row; row++)
        {
            for (uint col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            {
                var rowOffset = row - sourceRange.Start.Row;
                var colOffset = col - sourceRange.Start.Col;
                var target = transpose
                    ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
                    : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
                var sourceAddress = new CellAddress(sourceRange.Start.Sheet, row, col);
                linkedCells.Add((target, Cell.FromFormula($"{QuoteSheetName(sourceSheetName)}!{sourceAddress.ToA1()}")));
            }
        }

        return linkedCells;
    }

    private static string QuoteSheetName(string sheetName) =>
        "'" + sheetName.Replace("'", "''", StringComparison.Ordinal) + "'";
}
