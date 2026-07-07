using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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

        var sourceRules = sourceSheet.DataValidations.Select(DataValidationCopySupport.CloneValidation).ToList();
        _previous = targetSheet.DataValidations.Select(DataValidationCopySupport.CloneValidation).ToList();
        var destinationRange = GetDestinationRange(_sourceRange, _destination, _transpose);
        targetSheet.DataValidations.RemoveAll(rule => rule.AppliesTo.Overlaps(destinationRange));

        foreach (var rule in sourceRules)
        {
            // A rule can be anchored purely by an AdditionalRanges entry (AppliesTo elsewhere,
            // or vice versa), so every range the rule covers must be checked against the copied
            // source, not just the primary AppliesTo range. Each overlapping piece becomes its
            // own pasted rule with a fresh AppliesTo and no stale AdditionalRanges copied along
            // (matching FormatPainterDataValidationCommand's includeAdditionalRanges:false).
            foreach (var sourceRuleRange in EnumerateRuleRanges(rule))
            {
                var intersection = Intersect(sourceRuleRange, _sourceRange);
                if (intersection is null)
                    continue;

                var mappedRange = MapRange(intersection.Value, _sourceRange, _destination, _transpose);
                var rowDelta = (int)mappedRange.Start.Row - (int)intersection.Value.Start.Row;
                var colDelta = (int)mappedRange.Start.Col - (int)intersection.Value.Start.Col;
                targetSheet.DataValidations.Add(DataValidationCopySupport.CloneValidation(
                    rule,
                    mappedRange,
                    targetSheet.Name,
                    rowDelta,
                    colDelta,
                    includeAdditionalRanges: false));
            }
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
            sheet.DataValidations.Add(DataValidationCopySupport.CloneValidation(rule));
    }

    private static GridRange GetDestinationRange(GridRange sourceRange, CellAddress destination, bool transpose)
    {
        var rowCount = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var colCount = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        return new GridRange(
            destination,
            new CellAddress(destination.Sheet, destination.Row + rowCount - 1, destination.Col + colCount - 1));
    }

    private static GridRange? Intersect(GridRange first, GridRange second) =>
        GridRange.TryIntersect(first, second, out var intersection) ? intersection : null;

    private static IEnumerable<GridRange> EnumerateRuleRanges(DataValidation rule)
    {
        yield return rule.AppliesTo;
        foreach (var range in rule.AdditionalRanges)
            yield return range;
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

}
