using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
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

    // R34-commands-paste-special-3-2: when the caller knows the full destination selection (not
    // just its top-left anchor), this overload lets the paste tile the copied validation rule(s)
    // across every whole repeat of the source range that fits the selection -- mirroring how
    // PasteCommandFactory.CreateInternalPasteCommand tiles Values/Formulas/Formats/All onto a
    // destination selection that is a whole multiple of the copied range, instead of only ever
    // filling the selection's first (top-left) cell.
    public PasteDataValidationCommand(SheetId sheetId, GridRange sourceRange, GridRange destinationRange, bool transpose)
        : this(sheetId, sourceRange, destinationRange.Start, transpose)
    {
        _destinationRange = destinationRange;
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
        // When a full destination selection was supplied, clear overlapping rules across the
        // whole selection (matching the tiled-paste footprint below); otherwise fall back to the
        // single-anchor footprint sized to the copied source range, as before.
        var clearFootprint = _destinationRange ?? GetDestinationRange(_sourceRange, _destination, _transpose);
        targetSheet.DataValidations.RemoveAll(rule => rule.AppliesTo.Overlaps(clearFootprint));

        foreach (var tileAnchor in EnumerateTileAnchors())
        {
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

                    var mappedRange = MapRange(intersection.Value, _sourceRange, tileAnchor, _transpose);
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

    // R34-commands-paste-special-3-2: when the constructor was given the full destination
    // selection (not just its top-left anchor) and that selection is larger than the copied
    // source range in either dimension, repeat the paste at every whole tile of the source range
    // that fits -- exactly mirroring PasteCommandFactory.CreateInternalPasteCommand's
    // shouldTileDestinationRange/CreateTiledInternalPasteCommand period-based tiling. A trailing
    // partial tile (selection size not an exact multiple of the source range) is left untouched,
    // matching that same tiling behavior. When no destination range was supplied, or the
    // selection is no larger than the source range, this yields just the single anchor cell so
    // the original (non-tiled) behavior is unchanged.
    private IEnumerable<CellAddress> EnumerateTileAnchors()
    {
        if (_destinationRange is not { } destinationRange)
        {
            yield return _destination;
            yield break;
        }

        var pasteRows = _transpose ? _sourceRange.ColCount : _sourceRange.RowCount;
        var pasteCols = _transpose ? _sourceRange.RowCount : _sourceRange.ColCount;
        var targetRows = destinationRange.RowCount;
        var targetCols = destinationRange.ColCount;

        if (targetRows <= pasteRows && targetCols <= pasteCols)
        {
            yield return destinationRange.Start;
            yield break;
        }

        for (var rowOffset = 0U; rowOffset + pasteRows <= targetRows; rowOffset += pasteRows)
        {
            for (var colOffset = 0U; colOffset + pasteCols <= targetCols; colOffset += pasteCols)
            {
                yield return new CellAddress(
                    destinationRange.Start.Sheet,
                    destinationRange.Start.Row + rowOffset,
                    destinationRange.Start.Col + colOffset);
            }
        }
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
