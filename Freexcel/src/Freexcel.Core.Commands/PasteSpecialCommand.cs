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

public sealed class PasteCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly bool _transpose;
    private Dictionary<CellAddress, string?>? _previous;
    private Dictionary<CellAddress, ThreadedComment?>? _previousThreaded;

    public string Label => "Paste Comments";

    public PasteCommentsCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste comments source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var sourceComments = _sourceRange.AllCells()
            .Where(sourceSheet.Comments.ContainsKey)
            .Select(address => (Address: address, Comment: sourceSheet.Comments[address]))
            .ToList();
        var sourceThreadedComments = _sourceRange.AllCells()
            .Where(sourceSheet.ThreadedComments.ContainsKey)
            .Select(address => (Address: address, Comment: sourceSheet.ThreadedComments[address]))
            .ToList();
        _previous = [];
        _previousThreaded = [];
        var affected = new List<CellAddress>();
        foreach (var (source, comment) in sourceComments)
        {
            var destination = MapDestination(source, _sourceRange, _destination, _transpose);
            _previous[destination] = targetSheet.Comments.TryGetValue(destination, out var oldComment)
                ? oldComment
                : null;
            targetSheet.Comments[destination] = comment;
            affected.Add(destination);
        }

        foreach (var (source, comment) in sourceThreadedComments)
        {
            var destination = MapDestination(source, _sourceRange, _destination, _transpose);
            _previousThreaded[destination] = targetSheet.ThreadedComments.TryGetValue(destination, out var oldComment)
                ? oldComment
                : null;
            targetSheet.ThreadedComments[destination] = comment;
            affected.Add(destination);
        }

        return new CommandOutcome(true, AffectedCells: affected.Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null || _previousThreaded is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, comment) in _previous)
        {
            if (comment is null)
                sheet.Comments.Remove(address);
            else
                sheet.Comments[address] = comment;
        }

        foreach (var (address, comment) in _previousThreaded)
        {
            if (comment is null)
                sheet.ThreadedComments.Remove(address);
            else
                sheet.ThreadedComments[address] = comment;
        }
    }

    private static CellAddress MapDestination(
        CellAddress source,
        GridRange sourceRange,
        CellAddress destination,
        bool transpose)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return transpose
            ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
            : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
    }
}

public sealed class PasteDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly bool _transpose;
    private List<DataValidation>? _previous;

    public string Label => "Paste Data Validation";

    public PasteDataValidationCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste validation source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var sourceRules = sourceSheet.DataValidations.Select(CloneValidation).ToList();
        _previous = targetSheet.DataValidations.Select(CloneValidation).ToList();
        var destinationRange = GetDestinationRange(_sourceRange, _destination, _transpose);
        targetSheet.DataValidations.RemoveAll(rule => rule.AppliesTo.Overlaps(destinationRange));

        foreach (var rule in sourceRules)
        {
            if (!rule.AppliesTo.Overlaps(_sourceRange))
                continue;

            var intersection = Intersect(rule.AppliesTo, _sourceRange);
            if (intersection is null)
                continue;

            targetSheet.DataValidations.Add(CloneValidation(rule, MapRange(intersection.Value, _sourceRange, _destination, _transpose)));
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.DataValidations.Clear();
        foreach (var rule in _previous)
            sheet.DataValidations.Add(CloneValidation(rule));
    }

    private static GridRange GetDestinationRange(GridRange sourceRange, CellAddress destination, bool transpose)
    {
        var rowCount = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var colCount = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        return new GridRange(
            destination,
            new CellAddress(destination.Sheet, destination.Row + rowCount - 1, destination.Col + colCount - 1));
    }

    private static GridRange? Intersect(GridRange first, GridRange second)
    {
        if (!first.Overlaps(second))
            return null;

        var sheet = first.Start.Sheet;
        var startRow = Math.Max(first.Start.Row, second.Start.Row);
        var startCol = Math.Max(first.Start.Col, second.Start.Col);
        var endRow = Math.Min(first.End.Row, second.End.Row);
        var endCol = Math.Min(first.End.Col, second.End.Col);
        return new GridRange(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));
    }

    private static GridRange MapRange(GridRange range, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        var first = MapAddress(range.Start, sourceRange, destination, transpose);
        var second = MapAddress(range.End, sourceRange, destination, transpose);
        return new GridRange(first, second);
    }

    private static CellAddress MapAddress(CellAddress source, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return transpose
            ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
            : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
    }

    private static DataValidation CloneValidation(DataValidation source) =>
        CloneValidation(source, source.AppliesTo);

    private static DataValidation CloneValidation(DataValidation source, GridRange range) =>
        new()
        {
            AppliesTo = range,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage
        };
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
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet)
            return new CommandOutcome(false, "Source range must be on one sheet.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var destinationEndCol = _destinationStartCol + _sourceRange.ColCount - 1;
        _previousWidths = new Dictionary<uint, double>();
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
        {
            if (targetSheet.ColumnWidths.TryGetValue(col, out var width))
                _previousWidths[col] = width;
        }

        for (uint offset = 0; offset < _sourceRange.ColCount; offset++)
        {
            var sourceCol = _sourceRange.Start.Col + offset;
            var destinationCol = _destinationStartCol + offset;
            if (sourceSheet.ColumnWidths.TryGetValue(sourceCol, out var width))
                targetSheet.ColumnWidths[destinationCol] = width;
            else
                targetSheet.ColumnWidths.Remove(destinationCol);
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
        CellAddress destination,
        bool isLinkedToSourceRange = false,
        string? sourceSheetName = null)
    {
        _sheetId = sheetId;
        _picture = new PictureModel
        {
            Anchor = destination,
            SourceRowCount = sourceRange.RowCount,
            SourceColumnCount = sourceRange.ColCount,
            IsLinkedToSourceRange = isLinkedToSourceRange,
            LinkedSourceRange = isLinkedToSourceRange ? sourceRange : null,
            LinkedSourceSheetName = isLinkedToSourceRange ? sourceSheetName : null,
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
