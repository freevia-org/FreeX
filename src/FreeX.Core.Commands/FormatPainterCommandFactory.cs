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
        GridRange? sourceMergeSpan = null;
        if (mergeRegion is not null && mergeRegion.Value == sourceRange)
        {
            sourceMergeSpan = mergeRegion.Value;
            sourceRange = new GridRange(mergeRegion.Value.Start, mergeRegion.Value.Start);
        }

        var commands = new List<IWorkbookCommand>();
        if (sourceRange.RowCount == 1 && sourceRange.ColCount == 1)
        {
            // Excel treats "merged" as part of the copied format: painting from a merged source
            // recreates that merge in the destination, expanding the target footprint (anchored at
            // the original target start) up to the next whole multiple of the source merge's own
            // span so a single clicked cell ends up spanning the same width/height as the source
            // merge, then tiling that merge shape across the rest of an oversized target selection.
            var effectiveTargetRange = sourceMergeSpan is { } mergeSpan
                ? ExpandTargetToMergeMultiple(targetRange, mergeSpan)
                : targetRange;

            var styleId = GetSourceStyleId(sourceSheet, sourceRange.Start);
            var sourceStyle = workbook.GetStyle(styleId);
            commands.Add(new ApplyStyleCommand(effectiveTargetRange.Start.Sheet, effectiveTargetRange, StyleDiff.FromStyle(sourceStyle)));
            commands.Add(new FormatPainterDataValidationCommand(sourceSheet.Id, sourceRange, effectiveTargetRange));
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
                    sourceRange.Start.Row + effectiveTargetRange.RowCount - 1,
                    sourceRange.Start.Col + effectiveTargetRange.ColCount - 1));
            commands.Add(new PasteConditionalFormatsCommand(effectiveTargetRange.Start.Sheet, cfSourceRange, effectiveTargetRange.Start, transpose: false));

            if (sourceMergeSpan is { } span)
                AddTiledMerges(commands, span, effectiveTargetRange);

            return new CompositeWorkbookCommand("Format Painter", commands);
        }

        // R119-commands-format-painter-multicell-merge-leak: a merge covers cells but renders only
        // its anchor's style -- the other covered cells keep their pre-merge StyleId internally,
        // purely so a later Unmerge can restore it (see MergeCellsCommand.Apply). When the source
        // selection is a multi-cell range that merely CONTAINS one or more merged regions (rather
        // than collapsing to exactly one merge, handled above), the per-cell tiling below must not
        // read a covered cell's own StyleId via GetSourceStyleId -- that would leak the hidden
        // pre-merge leftover onto the target. Collect every merge fully inside sourceRange so each
        // tiled source address can be redirected to its merge's anchor instead.
        var sourceMerges = sourceSheet.MergedRegions.Where(sourceRange.Contains).ToList();

        foreach (var targetAddress in targetRange.AllCells())
        {
            var sourceAddress = new CellAddress(
                sourceSheet.Id,
                sourceRange.Start.Row + ((targetAddress.Row - targetRange.Start.Row) % sourceRange.RowCount),
                sourceRange.Start.Col + ((targetAddress.Col - targetRange.Start.Col) % sourceRange.ColCount));
            GridRange? coveringMerge = null;
            foreach (var merge in sourceMerges)
            {
                if (merge.Contains(sourceAddress))
                {
                    coveringMerge = merge;
                    break;
                }
            }
            var styleSourceAddress = coveringMerge?.Start ?? sourceAddress;
            var sourceStyle = workbook.GetStyle(GetSourceStyleId(sourceSheet, styleSourceAddress));
            commands.Add(new ApplyStyleCommand(
                targetRange.Start.Sheet,
                new GridRange(targetAddress, targetAddress),
                StyleDiff.FromStyle(sourceStyle)));
        }

        // Recreate each contained merge's shape at every tiled repetition in the target, the same
        // way AddTiledMerges already does for the whole-selection-is-one-merge branch above --
        // otherwise the anchor's style would be painted onto the target's covered cells too, but
        // with no merge joining them, leaving a block of identically-styled but unmerged cells
        // instead of the single merged block Excel actually reproduces.
        if (sourceMerges.Count > 0)
            AddTiledMergesForMultiCellSource(commands, sourceMerges, sourceRange, targetRange);

        commands.Add(new FormatPainterDataValidationCommand(sourceSheet.Id, sourceRange, targetRange));
        // Unlike PasteCommandFactory's Paste-Special "all merging conditional formats" branch (which
        // deliberately merges the rule once, anchored at the destination's start), Format Painter's own
        // multi-cell source pattern must repeat the conditional format the same way it already repeats
        // direct style (the per-cell loop above) and data validation (FormatPainterDataValidationCommand's
        // own MapPatternRange tiling) -- otherwise only the first source-sized tile of the target keeps
        // its color scale / icon set / etc. and every repeat of the pattern silently loses it.
        AddTiledConditionalFormats(commands, sourceRange, targetRange);
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

    // Widens targetRange (keeping its own Start anchor) so its row/column counts are each a whole
    // multiple of mergeSpan's own row/column counts -- e.g. a 1x3 source merge painted onto a single
    // clicked cell expands that 1-cell target up to the 1x3 footprint the merge needs, exactly the
    // way real Excel expands the destination to fit the merge shape it is about to recreate.
    private static GridRange ExpandTargetToMergeMultiple(GridRange targetRange, GridRange mergeSpan)
    {
        var expandedRowCount = CeilToMultiple(targetRange.RowCount, mergeSpan.RowCount);
        var expandedColCount = CeilToMultiple(targetRange.ColCount, mergeSpan.ColCount);
        return new GridRange(
            targetRange.Start,
            new CellAddress(
                targetRange.Start.Sheet,
                targetRange.Start.Row + expandedRowCount - 1,
                targetRange.Start.Col + expandedColCount - 1));
    }

    private static uint CeilToMultiple(uint value, uint multiple) =>
        ((value + multiple - 1) / multiple) * multiple;

    // Recreates the source's merge as one or more same-sized merged blocks tiled across
    // effectiveTargetRange (whose dimensions ExpandTargetToMergeMultiple already rounded up to a
    // whole multiple of mergeSpan's own dimensions), matching Excel's "Format Painter spreads merges
    // through a workbook" behavior for both a single destination cell and a larger destination drag.
    private static void AddTiledMerges(List<IWorkbookCommand> commands, GridRange mergeSpan, GridRange effectiveTargetRange)
    {
        var mergeRowCount = mergeSpan.RowCount;
        var mergeColCount = mergeSpan.ColCount;
        for (var tileStartRow = effectiveTargetRange.Start.Row; tileStartRow <= effectiveTargetRange.End.Row; tileStartRow += mergeRowCount)
        {
            for (var tileStartCol = effectiveTargetRange.Start.Col; tileStartCol <= effectiveTargetRange.End.Col; tileStartCol += mergeColCount)
            {
                var tileRange = new GridRange(
                    new CellAddress(effectiveTargetRange.Start.Sheet, tileStartRow, tileStartCol),
                    new CellAddress(effectiveTargetRange.Start.Sheet, tileStartRow + mergeRowCount - 1, tileStartCol + mergeColCount - 1));
                commands.Add(new MergeCellsCommand(effectiveTargetRange.Start.Sheet, tileRange));
            }
        }
    }

    // Tiles every merge that is fully contained within a multi-cell sourceRange across
    // targetRange, one source-sized (or, at the trailing edge, source-clipped) tile at a time --
    // the multi-cell-source counterpart of AddTiledMerges above (which only handles the whole
    // selection collapsing to exactly one merge). Each merge's position is kept relative to
    // sourceRange.Start so it lands at the matching offset inside every repeated tile, mirroring
    // how the per-cell style loop above computes sourceAddress via the same modulo tiling.
    private static void AddTiledMergesForMultiCellSource(
        List<IWorkbookCommand> commands,
        IReadOnlyList<GridRange> sourceMerges,
        GridRange sourceRange,
        GridRange targetRange)
    {
        for (var tileStartRow = targetRange.Start.Row; tileStartRow <= targetRange.End.Row; tileStartRow += sourceRange.RowCount)
        {
            for (var tileStartCol = targetRange.Start.Col; tileStartCol <= targetRange.End.Col; tileStartCol += sourceRange.ColCount)
            {
                foreach (var merge in sourceMerges)
                {
                    var targetMergeStartRow = tileStartRow + (merge.Start.Row - sourceRange.Start.Row);
                    var targetMergeStartCol = tileStartCol + (merge.Start.Col - sourceRange.Start.Col);
                    if (targetMergeStartRow > targetRange.End.Row || targetMergeStartCol > targetRange.End.Col)
                        continue; // this tile's copy of the merge falls entirely past the target's trailing (clipped) edge

                    var targetMergeEndRow = Math.Min(targetMergeStartRow + merge.RowCount - 1, targetRange.End.Row);
                    var targetMergeEndCol = Math.Min(targetMergeStartCol + merge.ColCount - 1, targetRange.End.Col);
                    if (targetMergeEndRow == targetMergeStartRow && targetMergeEndCol == targetMergeStartCol)
                        continue; // clipped down to a single cell at the trailing edge -- nothing to merge

                    var tileRange = new GridRange(
                        new CellAddress(targetRange.Start.Sheet, targetMergeStartRow, targetMergeStartCol),
                        new CellAddress(targetRange.Start.Sheet, targetMergeEndRow, targetMergeEndCol));
                    commands.Add(new MergeCellsCommand(targetRange.Start.Sheet, tileRange));
                }
            }
        }
    }

    // Repeats the multi-cell source's conditional-format pattern across targetRange one source-sized
    // (or, at the trailing edge, source-clipped) tile at a time, mirroring the tiling
    // FormatPainterDataValidationCommand.MapPatternRange already does for data validation and the
    // per-cell loop above already does for direct style -- a single untiled
    // PasteConditionalFormatsCommand call only ever populates the first tile of the target.
    private static void AddTiledConditionalFormats(List<IWorkbookCommand> commands, GridRange sourceRange, GridRange targetRange)
    {
        for (var tileStartRow = targetRange.Start.Row; tileStartRow <= targetRange.End.Row; tileStartRow += sourceRange.RowCount)
        {
            var tileRowCount = Math.Min(sourceRange.RowCount, targetRange.End.Row - tileStartRow + 1);
            for (var tileStartCol = targetRange.Start.Col; tileStartCol <= targetRange.End.Col; tileStartCol += sourceRange.ColCount)
            {
                var tileColCount = Math.Min(sourceRange.ColCount, targetRange.End.Col - tileStartCol + 1);
                var tileSourceRange = new GridRange(
                    sourceRange.Start,
                    new CellAddress(
                        sourceRange.Start.Sheet,
                        sourceRange.Start.Row + tileRowCount - 1,
                        sourceRange.Start.Col + tileColCount - 1));
                var tileDestination = new CellAddress(targetRange.Start.Sheet, tileStartRow, tileStartCol);
                commands.Add(new PasteConditionalFormatsCommand(targetRange.Start.Sheet, tileSourceRange, tileDestination, transpose: false));
            }
        }
    }
}
