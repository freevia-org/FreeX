using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class FormatPainterDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _targetRange;
    private List<DataValidation>? _previous;

    public string Label => "Format Painter Data Validation";

    public FormatPainterDataValidationCommand(
        SheetId sourceSheetId,
        GridRange sourceRange,
        GridRange targetRange)
    {
        _sourceSheetId = sourceSheetId;
        _sourceRange = sourceRange;
        _targetRange = targetRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceSheetId ||
            _sourceRange.End.Sheet != _sourceSheetId ||
            _targetRange.Start.Sheet != _targetRange.End.Sheet)
        {
            return new CommandOutcome(false, "Format painter validation source range or target range is invalid.");
        }

        var sourceSheet = ctx.GetSheet(_sourceSheetId);
        var targetSheet = ctx.GetSheet(_targetRange.Start.Sheet);
        if (CommandGuards.RejectIfProtected(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var sourceRules = sourceSheet.DataValidations.Select(DataValidationCopySupport.CloneValidation).ToList();
        _previous = targetSheet.DataValidations.Select(DataValidationCopySupport.CloneValidation).ToList();
        RemoveValidationFromTargetRange(targetSheet);

        foreach (var copiedRule in CreateCopiedRules(sourceRules, targetSheet.Name))
            targetSheet.DataValidations.Add(copiedRule);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null)
            return;

        var sheet = ctx.GetSheet(_targetRange.Start.Sheet);
        sheet.DataValidations.Clear();
        foreach (var rule in _previous)
            sheet.DataValidations.Add(DataValidationCopySupport.CloneValidation(rule));
    }

    private IEnumerable<DataValidation> CreateCopiedRules(
        IReadOnlyList<DataValidation> sourceRules,
        string targetSheetName)
    {
        if (_sourceRange.RowCount == 1 && _sourceRange.ColCount == 1)
        {
            foreach (var rule in sourceRules.Where(rule => RuleAppliesToSourceCell(rule, _sourceRange.Start)))
            {
                var rowDelta = (int)_targetRange.Start.Row - (int)_sourceRange.Start.Row;
                var colDelta = (int)_targetRange.Start.Col - (int)_sourceRange.Start.Col;
                yield return DataValidationCopySupport.CloneValidation(
                    rule,
                    _targetRange,
                    targetSheetName,
                    rowDelta,
                    colDelta,
                    includeAdditionalRanges: false);
            }

            yield break;
        }

        foreach (var rule in sourceRules)
        {
            foreach (var sourceRuleRange in EnumerateRuleRanges(rule))
            {
                if (!GridRange.TryIntersect(sourceRuleRange, _sourceRange, out var sourceIntersection))
                    continue;

                foreach (var mappedRange in MapPatternRange(sourceIntersection))
                {
                    var rowDelta = (int)mappedRange.Start.Row - (int)sourceIntersection.Start.Row;
                    var colDelta = (int)mappedRange.Start.Col - (int)sourceIntersection.Start.Col;
                    yield return DataValidationCopySupport.CloneValidation(
                        rule,
                        mappedRange,
                        targetSheetName,
                        rowDelta,
                        colDelta,
                        includeAdditionalRanges: false);
                }
            }
        }
    }

    private IEnumerable<GridRange> MapPatternRange(GridRange sourceIntersection)
    {
        var relativeStartRow = (ulong)sourceIntersection.Start.Row - _sourceRange.Start.Row;
        var relativeEndRow = (ulong)sourceIntersection.End.Row - _sourceRange.Start.Row;
        var relativeStartCol = (ulong)sourceIntersection.Start.Col - _sourceRange.Start.Col;
        var relativeEndCol = (ulong)sourceIntersection.End.Col - _sourceRange.Start.Col;

        for (var tileStartRow = (ulong)_targetRange.Start.Row;
             tileStartRow <= _targetRange.End.Row;
             tileStartRow += _sourceRange.RowCount)
        {
            var mappedStartRow = tileStartRow + relativeStartRow;
            if (mappedStartRow > _targetRange.End.Row)
                continue;

            var mappedEndRow = Math.Min(tileStartRow + relativeEndRow, _targetRange.End.Row);
            for (var tileStartCol = (ulong)_targetRange.Start.Col;
                 tileStartCol <= _targetRange.End.Col;
                 tileStartCol += _sourceRange.ColCount)
            {
                var mappedStartCol = tileStartCol + relativeStartCol;
                if (mappedStartCol > _targetRange.End.Col)
                    continue;

                var mappedEndCol = Math.Min(tileStartCol + relativeEndCol, _targetRange.End.Col);
                yield return new GridRange(
                    new CellAddress(_targetRange.Start.Sheet, (uint)mappedStartRow, (uint)mappedStartCol),
                    new CellAddress(_targetRange.Start.Sheet, (uint)mappedEndRow, (uint)mappedEndCol));
            }
        }
    }

    private void RemoveValidationFromTargetRange(Sheet targetSheet)
    {
        for (var i = targetSheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = targetSheet.DataValidations[i];
            var allRanges = EnumerateRuleRanges(rule).ToArray();
            if (!allRanges.Any(range => range.Overlaps(_targetRange)))
                continue;

            targetSheet.DataValidations.RemoveAt(i);
            var remainingRanges = allRanges
                .SelectMany(range => GridRangeSubtraction.Subtract(range, _targetRange))
                .ToList();
            for (var r = remainingRanges.Count - 1; r >= 0; r--)
            {
                var replacement = DataValidationCopySupport.CloneValidation(
                    rule,
                    remainingRanges[r],
                    hostSheetName: null,
                    rowDelta: 0,
                    colDelta: 0,
                    includeAdditionalRanges: false);
                targetSheet.DataValidations.Insert(i, replacement);
            }
        }
    }

    private static bool RuleAppliesToSourceCell(DataValidation rule, CellAddress address) =>
        rule.AppliesTo.Contains(address) ||
        rule.AdditionalRanges.Any(range => range.Contains(address));

    private static IEnumerable<GridRange> EnumerateRuleRanges(DataValidation rule)
    {
        yield return rule.AppliesTo;
        foreach (var range in rule.AdditionalRanges)
            yield return range;
    }
}
