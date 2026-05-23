using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
