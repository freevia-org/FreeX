using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class FormatPainterCommandFactory
{
    public static IWorkbookCommand Create(
        Workbook workbook,
        Sheet sourceSheet,
        CellAddress sourceAddress,
        GridRange targetRange)
        => Create(workbook, sourceSheet, new GridRange(sourceAddress, sourceAddress), targetRange);

    public static IWorkbookCommand Create(
        Workbook workbook,
        Sheet sourceSheet,
        GridRange sourceRange,
        GridRange targetRange)
    {
        // A click on a merged cell selects the FULL merge range (1xN or Nx1), but a merge renders
        // ONLY the anchor cell's style -- the covered cells' styles are hidden pre-merge leftovers
        // the user never saw. When the whole source range is exactly one merged region, collapse it
        // to the anchor cell so this paints that single visible format uniformly across the target
        // instead of tiling each target cell from a different (hidden) source-column/row style.
        var mergeRegion = sourceSheet.GetMergeRegion(sourceRange.Start);
        if (mergeRegion is not null && mergeRegion.Value == sourceRange)
        {
            sourceRange = new GridRange(mergeRegion.Value.Start, mergeRegion.Value.Start);
        }

        var commands = new List<IWorkbookCommand>();
        if (sourceRange.RowCount == 1 && sourceRange.ColCount == 1)
        {
            var styleId = GetSourceStyleId(sourceSheet, sourceRange.Start);
            var sourceStyle = workbook.GetStyle(styleId);
            commands.Add(new ApplyStyleCommand(targetRange.Start.Sheet, targetRange, StyleDiff.FromStyle(sourceStyle)));
            commands.Add(new FormatPainterDataValidationCommand(sourceSheet.Id, sourceRange, targetRange));
            // A conditional-format rule (e.g. a color scale) is a range-level construct, not a
            // per-cell one, so painting a single source cell across a bigger target selection must
            // project any rule covering that cell onto the WHOLE target range -- exactly like the
            // style copy and FormatPainterDataValidationCommand just above already do for this same
            // 1x1-source branch -- rather than only the single clipped source cell.
            // PasteConditionalFormatsCommand clips a rule to the intersection of its own AppliesTo and
            // the "source range" passed in, then maps that clipped shape onto the destination anchor,
            // so widening the source range here to the target's own footprint (anchored at the source
            // cell) makes the mapped-and-clipped result land on the full target range.
            var cfSourceRange = new GridRange(
                sourceRange.Start,
                new CellAddress(
                    sourceSheet.Id,
                    sourceRange.Start.Row + targetRange.RowCount - 1,
                    sourceRange.Start.Col + targetRange.ColCount - 1));
            commands.Add(new PasteConditionalFormatsCommand(targetRange.Start.Sheet, cfSourceRange, targetRange.Start, transpose: false));
            return new CompositeWorkbookCommand("Format Painter", commands);
        }

        foreach (var targetAddress in targetRange.AllCells())
        {
            var sourceAddress = new CellAddress(
                sourceSheet.Id,
                sourceRange.Start.Row + ((targetAddress.Row - targetRange.Start.Row) % sourceRange.RowCount),
                sourceRange.Start.Col + ((targetAddress.Col - targetRange.Start.Col) % sourceRange.ColCount));
            var sourceStyle = workbook.GetStyle(GetSourceStyleId(sourceSheet, sourceAddress));
            commands.Add(new ApplyStyleCommand(
                targetRange.Start.Sheet,
                new GridRange(targetAddress, targetAddress),
                StyleDiff.FromStyle(sourceStyle)));
        }

        commands.Add(new FormatPainterDataValidationCommand(sourceSheet.Id, sourceRange, targetRange));
        // Mirrors PasteCommandFactory's tiled paste-special branch: the conditional-format rule itself
        // is merged once, anchored at the destination's start, rather than tiled per output cell.
        commands.Add(new PasteConditionalFormatsCommand(targetRange.Start.Sheet, sourceRange, targetRange.Start, transpose: false));
        return new CompositeWorkbookCommand("Format Painter", commands);
    }

    public static IWorkbookCommand Create(
        Workbook workbook,
        StyleId sourceStyleId,
        GridRange targetRange)
    {
        var sourceStyle = workbook.GetStyle(sourceStyleId);
        return new ApplyStyleCommand(targetRange.Start.Sheet, targetRange, StyleDiff.FromStyle(sourceStyle));
    }

    private static StyleId GetSourceStyleId(Sheet sourceSheet, CellAddress sourceAddress) =>
        sourceSheet.GetCell(sourceAddress)?.StyleId
        ?? sourceSheet.GetStyleOnly(sourceAddress.Row, sourceAddress.Col)
        ?? StyleId.Default;
}
