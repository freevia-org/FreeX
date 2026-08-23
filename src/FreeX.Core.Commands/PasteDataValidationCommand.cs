using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteDataValidationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
    private readonly bool _transpose;
    private readonly IReadOnlyList<GridRange>? _sourceAreas;
    private List<DataValidation>? _previous;

    public string Label => "Paste Data Validation";

    // R78-commands-paste-special-5-4: `sourceAreas`, when supplied with more than one area,
    // records every individually Ctrl+clicked area of a multi-area source selection (mirroring
    // InternalClipboard.SourceAreas in MainWindow.ClipboardCommands.cs). `sourceRange` remains
    // only the BOUNDING BOX of those areas, so without this, a rule that only overlaps the gap
    // between disjoint areas (never part of the selection) would still be treated as "copied" and
    // cloned onto the destination.
    public PasteDataValidationCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose, IReadOnlyList<GridRange>? sourceAreas = null)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
        _sourceAreas = sourceAreas is { Count: > 1 } ? sourceAreas : null;
    }

    // R34-commands-paste-special-3-2: when the caller knows the full destination selection (not
    // just its top-left anchor), this overload lets the paste tile the copied validation rule(s)
    // across every whole repeat of the source range that fits the selection -- mirroring how
    // PasteCommandFactory.CreateInternalPasteCommand tiles Values/Formulas/Formats/All onto a
    // destination selection that is a whole multiple of the copied range, instead of only ever
    // filling the selection's first (top-left) cell.
    public PasteDataValidationCommand(SheetId sheetId, GridRange sourceRange, GridRange destinationRange, bool transpose, IReadOnlyList<GridRange>? sourceAreas = null)
        : this(sheetId, sourceRange, destinationRange.Start, transpose, sourceAreas)
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
        var clearFootprint = _destinationRange ?? PastePlacementPolicy.GetDestinationRange(_sourceRange, _destination, _transpose);
        // R52-commands-data-validation-apply-3-1/-3-2: a real Excel paste only supersedes
        // validation on the destination cells themselves -- a pre-existing rule whose AppliesTo
        // (or AdditionalRanges, per -3-2) merely overlaps the paste footprint must be shrunk to
        // its surviving (non-overlapping) portion(s), not deleted wholesale, or cells outside the
        // paste destination silently lose validation they were never part of pasting over.
        ClearOverlappingValidationRanges(targetSheet, clearFootprint);

        foreach (var tileAnchor in PastePlacementPolicy.EnumerateTileAnchors(
                     _sourceRange,
                     _destination,
                     _destinationRange,
                     _transpose))
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
                    foreach (var intersection in IntersectWithSource(sourceRuleRange))
                    {
                        var mappedRange = PastePlacementPolicy.MapRange(intersection, _sourceRange, tileAnchor, _transpose);
                        // Transpose swaps each relative reference's own (row,col) offset from the
                        // rule's own AppliesTo anchor onto the pasted rule's new anchor -- it is NOT
                        // the uniform per-cell translation PasteOffsetOp applies. Mirrors
                        // PasteConditionalFormatsCommand.CloneRuleForDestination's pasteOp selection
                        // (R56-commands-paste-special-5-1 / the CF sibling fix for this same anti-pattern),
                        // using this piece's own intersection.Start (source anchor) and mappedRange.Start
                        // (destination anchor) so a rule that only partially overlaps the copied range
                        // still transposes relative to its own anchor rather than the whole copied
                        // block's corner.
                        RewriteOperation pasteOp = _transpose
                            ? new PasteTransposeOp(intersection.Start.Row, intersection.Start.Col, mappedRange.Start.Row, mappedRange.Start.Col)
                            : new PasteOffsetOp(
                                (int)mappedRange.Start.Row - (int)intersection.Start.Row,
                                (int)mappedRange.Start.Col - (int)intersection.Start.Col);
                        targetSheet.DataValidations.Add(DataValidationCopySupport.CloneValidation(
                            rule,
                            mappedRange,
                            targetSheet.Name,
                            pasteOp,
                            includeAdditionalRanges: false));
                    }
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

    private static GridRange? Intersect(GridRange first, GridRange second) =>
        GridRange.TryIntersect(first, second, out var intersection) ? intersection : null;

    // R78-commands-paste-special-5-4: when _sourceAreas records a multi-area (Ctrl+click) source,
    // a rule's range is only "copied" over the portion(s) that overlap an ACTUAL copied area --
    // intersecting against the whole _sourceRange bounding box would also pick up a rule that only
    // touches the gap between disjoint areas, which was never part of the selection. With no (or a
    // single) area recorded, this is unchanged from intersecting against the whole bounding box.
    private IEnumerable<GridRange> IntersectWithSource(GridRange sourceRuleRange)
    {
        if (_sourceAreas is not { } areas)
        {
            if (Intersect(sourceRuleRange, _sourceRange) is { } intersection)
                yield return intersection;
            yield break;
        }

        foreach (var area in areas)
        {
            if (Intersect(sourceRuleRange, area) is { } intersection)
                yield return intersection;
        }
    }

    // R52-commands-data-validation-apply-3-1/-3-2: mirrors ClearDataValidationCommand.Apply's
    // subtract-and-replace loop (SetDataValidationCommand.cs) -- checking AppliesTo AND
    // AdditionalRanges for overlap (-3-2) and, for any rule that overlaps, replacing it with
    // clones covering only the surviving (non-overlapping) remainder of each of its ranges,
    // instead of deleting the whole rule just because part of it touches the paste footprint.
    private static void ClearOverlappingValidationRanges(Sheet sheet, GridRange footprint)
    {
        for (var i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            var allRanges = new[] { rule.AppliesTo }.Concat(rule.AdditionalRanges).ToArray();
            if (!allRanges.Any(range => range.Overlaps(footprint)))
                continue;

            sheet.DataValidations.RemoveAt(i);
            var remainingRanges = allRanges.SelectMany(range => Subtract(range, footprint)).ToList();
            // includeAdditionalRanges:false -- each surviving fragment (from AppliesTo OR from an
            // AdditionalRanges entry, per -3-2) becomes its own standalone rule; carrying the
            // ORIGINAL rule's AdditionalRanges along would silently reintroduce the very range(s)
            // this loop just subtracted out.
            var replacements = remainingRanges
                .Select(range => DataValidationCopySupport.CloneValidation(
                    rule, range, hostSheetName: null, rowDelta: 0, colDelta: 0, includeAdditionalRanges: false))
                .ToList();
            for (var r = replacements.Count - 1; r >= 0; r--)
                sheet.DataValidations.Insert(i, replacements[r]);
        }
    }

    private static IEnumerable<GridRange> Subtract(GridRange source, GridRange remove)
    {
        if (!source.Overlaps(remove))
        {
            yield return source;
            yield break;
        }

        var top = Math.Max(source.Start.Row, remove.Start.Row);
        var bottom = Math.Min(source.End.Row, remove.End.Row);
        var left = Math.Max(source.Start.Col, remove.Start.Col);
        var right = Math.Min(source.End.Col, remove.End.Col);
        var sheet = source.Start.Sheet;

        if (source.Start.Row < top)
            yield return MakeRange(sheet, source.Start.Row, source.Start.Col, top - 1, source.End.Col);

        if (bottom < source.End.Row)
            yield return MakeRange(sheet, bottom + 1, source.Start.Col, source.End.Row, source.End.Col);

        if (source.Start.Col < left)
            yield return MakeRange(sheet, top, source.Start.Col, bottom, left - 1);

        if (right < source.End.Col)
            yield return MakeRange(sheet, top, right + 1, bottom, source.End.Col);
    }

    private static GridRange MakeRange(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));

    private static IEnumerable<GridRange> EnumerateRuleRanges(DataValidation rule)
    {
        yield return rule.AppliesTo;
        foreach (var range in rule.AdditionalRanges)
            yield return range;
    }

}
