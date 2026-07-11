using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Fills a range by repeating or continuing the series of <paramref name="sourceRange"/>.
/// Formulas have relative cell references incremented by the fill offset. When
/// <paramref name="fillRange"/> is a sub-range of <paramref name="sourceRange"/> (the user dragged
/// the fill handle inward instead of extending it), the cells beyond the shrunk boundary are
/// cleared instead, matching Excel.
/// </summary>
public sealed class AutofillCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _fillRange;
    private readonly bool _ctrlHeld;
    private List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;
    private List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>? _hyperlinkSnapshot;
    private List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>? _richTextRunsSnapshot;
    private List<GridRange>? _createdMergedRegions;

    public string Label => "Autofill";

    /// <param name="ctrlHeld">
    /// True when the user held Ctrl while releasing the fill-handle drag. Excel uses Ctrl to flip
    /// the fill handle's default behavior for a detected series (2+ source cells, or any
    /// text/list series): it becomes a plain copy of the last value instead. For a LONE plain
    /// number/date cell (no natural multi-cell series to detect), the default itself is
    /// type-dependent: a number defaults to a copy (Ctrl forces an incrementing series instead),
    /// while a date defaults to a day-increment series (Ctrl forces a copy instead) -- see
    /// <see cref="WantsSingleCellSeriesDefault"/>.
    /// </param>
    public AutofillCommand(SheetId sheetId, GridRange sourceRange, GridRange fillRange, bool ctrlHeld = false)
    {
        _sheetId     = sheetId;
        _sourceRange = sourceRange;
        _fillRange   = fillRange;
        _ctrlHeld    = ctrlHeld;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        if (_sourceRange.Contains(_fillRange) && _fillRange != _sourceRange)
            return ApplyInwardClear(ctx, sheet);

        if (!TryGetFillPlan(out var plan))
            return new CommandOutcome(false, "The autofill range must be adjacent to the source range and aligned by row or column.");

        // Excel refuses to fill across a merged region: the merge's non-anchor cells must never
        // receive independent content, and a fill that only partially covers a merge would leave
        // the merge's data model out of sync (mirrors MoveRangeCommand/SortCommand's merge guard).
        // The one shape Excel DOES allow through: the whole source range is a single uniformly
        // sized merge (e.g. a "Q1" header merged across 2 columns) -- that tiles new,
        // identically-sized merges across the fill range instead of refusing outright, mirroring
        // SortCommand's own uniform-merge carve-out.
        var overlappingMerges = sheet.MergedRegions.Where(region => _fillRange.Overlaps(region) || _sourceRange.Overlaps(region)).ToList();
        if (overlappingMerges.Count > 0)
        {
            var tileSize = TryGetUniformMergeTileSize(overlappingMerges, plan);
            if (tileSize is null)
                return new CommandOutcome(false, "Cannot autofill a range that intersects merged cells.");

            return ApplyMergeTiledFill(ctx, sheet, plan, tileSize.Value);
        }

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                    return CommandGuards.RejectSheetProtected();
            }
        }
        if (CommandGuards.RejectIfSplitsArray(sheet, _fillRange.AllCells()) is { } splitsArrayRejection)
            return splitsArrayRejection;

        var sourceAddr = GetSourceEdgeAddress(plan);
        var sourceCell = sheet.GetCell(sourceAddr);
        var sourceHasFormula = sourceCell is { HasFormula: true, FormulaText: not null };
        var sourceLength = plan.Axis == FillAxis.Vertical ? (int)_sourceRange.RowCount : (int)_sourceRange.ColCount;
        var naturalScalarSeries = sourceHasFormula ? null : TryCreateScalarSeries(sheet, plan);
        var naturalListSeries = sourceHasFormula || naturalScalarSeries is not null ? null : TryCreateListSeries(sheet, plan);
        // Ctrl flips the natural default for a detected series (a 2+ cell scalar trend, or any
        // text/list series): it becomes a plain copy instead. Ctrl has no effect on formula fills.
        var forceCopyOnly = !sourceHasFormula && _ctrlHeld && (naturalScalarSeries is not null || naturalListSeries is not null);
        // A lone plain number/date cell (no natural multi-cell series above) has a type-dependent
        // default instead of always defaulting to copy -- see WantsSingleCellSeriesDefault.
        var forcedSeries = !sourceHasFormula && naturalScalarSeries is null && naturalListSeries is null
            && _sourceRange.CellCount == 1 && WantsSingleCellSeriesDefault(sourceCell?.Value, _ctrlHeld)
                ? TryCreateForcedSingleCellSeries(sourceCell, plan)
                : null;
        var scalarSeries = forceCopyOnly ? null : naturalScalarSeries ?? forcedSeries;
        var listSeries = forceCopyOnly ? null : naturalListSeries;

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        _hyperlinkSnapshot = new List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>(capacity);
        _richTextRunsSnapshot = new List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>(capacity);
        var writtenCells = new List<CellAddress>(capacity);

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                var addr = new CellAddress(_sheetId, row, col);
                var oldCell = sheet.GetCell(addr);
                var oldStyleOnly = oldCell is null ? sheet.GetStyleOnly(row, col) : null;
                _snapshot.Add((addr, oldCell?.Clone(), oldStyleOnly));
                SnapshotAnnotations(sheet, addr);
                writtenCells.Add(addr);

                if (sourceCell is null)
                {
                    sheet.ClearCell(addr);
                    ClearAnnotations(sheet, addr);
                    continue;
                }

                var offset = plan.Axis == FillAxis.Vertical
                    ? Math.Abs((int)addr.Row - (int)sourceAddr.Row)
                    : Math.Abs((int)addr.Col - (int)sourceAddr.Col);

                Cell newCell;
                CellAddress annotationSourceAddr;
                if (scalarSeries is not null)
                {
                    newCell = Cell.FromValue(scalarSeries.CreateValue(scalarSeries.LastValue + scalarSeries.Step * offset));
                    newCell.StyleId = ResolvePatternSourceStyleId(sheet, plan, addr, sourceLength, sourceCell);
                    annotationSourceAddr = sourceAddr;
                }
                else if (listSeries is not null)
                {
                    newCell = Cell.FromValue(listSeries.ValueAt(offset));
                    newCell.StyleId = ResolvePatternSourceStyleId(sheet, plan, addr, sourceLength, sourceCell);
                    annotationSourceAddr = sourceAddr;
                }
                else
                {
                    // No detected trend/list series: replay the source range's own per-cell
                    // pattern cyclically instead of collapsing every destination cell to the
                    // single edge cell. A 2+ cell source (e.g. a running-total formula pair, or
                    // an alternating copy like "A","B") repeats its whole shape every
                    // sourceLength cells, matching Excel's fill-handle behavior.
                    var patternSourceAddr = ResolvePatternSourceAddress(plan, addr, sourceLength);
                    var patternSourceCell = sheet.GetCell(patternSourceAddr);
                    if (patternSourceCell is null)
                    {
                        sheet.ClearCell(addr);
                        ClearAnnotations(sheet, addr);
                        continue;
                    }

                    if (!forceCopyOnly && patternSourceCell.HasFormula && patternSourceCell.FormulaText is not null)
                    {
                        int rowOffset = (int)addr.Row - (int)patternSourceAddr.Row;
                        int colOffset = (int)addr.Col - (int)patternSourceAddr.Col;
                        var shifted = FormulaRewriter.Rewrite(patternSourceCell.FormulaText,
                            new PasteOffsetOp(rowOffset, colOffset), sheet.Name)
                            ?? patternSourceCell.FormulaText;
                        newCell = Cell.FromFormula(shifted);
                    }
                    else
                    {
                        newCell = Cell.FromValue(patternSourceCell.Value);
                    }

                    newCell.StyleId = patternSourceCell.StyleId;
                    annotationSourceAddr = patternSourceAddr;
                }

                sheet.SetCell(addr, newCell);
                // A detected trend/list series computes a brand-new value for this cell that
                // differs from annotationSourceAddr's own text (e.g. "Item1" -> "Item2"), so any
                // character-position rich-text run formatting copied verbatim from the source
                // would describe the wrong text -- stale runs, exactly like EditCellsCommand
                // clears RichTextRuns/Hyperlinks whenever a cell's content is genuinely replaced
                // rather than copied unchanged. Only the plain pattern-copy branch (no series
                // detected) reproduces the source cell's exact value, so only it is safe to carry
                // rich-text runs forward.
                var copyRichTextRuns = scalarSeries is null && listSeries is null;
                CopyAnnotations(sheet, annotationSourceAddr, addr, copyRichTextRuns);
            }
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    /// <summary>
    /// Excel semantics for dragging the fill handle inward: the portion of the original
    /// selection beyond the new (shrunk) boundary is cleared, exactly like Clear Contents.
    /// </summary>
    private CommandOutcome ApplyInwardClear(ICommandContext ctx, Sheet sheet)
    {
        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                    return CommandGuards.RejectSheetProtected();
            }
        }
        if (CommandGuards.RejectIfSplitsArray(sheet, _fillRange.AllCells()) is { } splitsArrayRejection)
            return splitsArrayRejection;

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        _hyperlinkSnapshot = new List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>(capacity);
        _richTextRunsSnapshot = new List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>(capacity);
        var writtenCells = new List<CellAddress>(capacity);

        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                var addr = new CellAddress(_sheetId, row, col);
                var oldCell = sheet.GetCell(addr);
                var oldStyleOnly = oldCell is null ? sheet.GetStyleOnly(row, col) : null;
                _snapshot.Add((addr, oldCell?.Clone(), oldStyleOnly));
                SnapshotAnnotations(sheet, addr);
                writtenCells.Add(addr);

                // Clear Contents semantics (like ClearContentsCommand): drop the value but keep
                // the cell's formatting in place, matching Excel's fill-handle-inward gesture.
                // Clear Contents also drops hyperlinks and rich-text run formatting, so this must
                // clear the parallel annotation dictionaries the same way ClearContentsCommand does.
                var cleared = Cell.FromValue(BlankValue.Instance);
                if (oldCell is not null)
                    cleared.StyleId = oldCell.StyleId;
                else if (oldStyleOnly.HasValue)
                    cleared.StyleId = oldStyleOnly.Value;
                sheet.SetCell(addr, cleared);
                ClearAnnotations(sheet, addr);
            }
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    /// <summary>
    /// Excel allows exactly one merged-cell autofill shape through: the whole source range is a
    /// SINGLE merged region, and the fill range is an exact multiple of that merge's size along
    /// the fill axis (e.g. a "Q1" header merged across 2 columns, filled right into a 4-column
    /// range to produce two more same-size "Q2"/"Q3" merges). The merge's long axis must run
    /// parallel to the fill direction: a horizontal fill only accepts single-ROW merges, a
    /// vertical fill only accepts single-COLUMN merges. Any other overlap -- a partial merge, a
    /// destination that already has its own (potentially differently sized) merges, or a merge
    /// whose long axis crosses the fill direction -- still refuses, matching Excel's own "merged
    /// cells need to be identically sized" refusal.
    /// </summary>
    private (uint RowSpan, uint ColSpan)? TryGetUniformMergeTileSize(IReadOnlyList<GridRange> overlappingMerges, FillPlan plan)
    {
        if (overlappingMerges.Count != 1 || overlappingMerges[0] != _sourceRange)
            return null;

        var merge = overlappingMerges[0];
        if (plan.Axis == FillAxis.Horizontal)
        {
            if (merge.RowCount != 1 || merge.ColCount < 2)
                return null;
            if (_fillRange.RowCount != 1 || _fillRange.ColCount % merge.ColCount != 0)
                return null;
        }
        else
        {
            if (merge.ColCount != 1 || merge.RowCount < 2)
                return null;
            if (_fillRange.ColCount != 1 || _fillRange.RowCount % merge.RowCount != 0)
                return null;
        }

        return (merge.RowCount, merge.ColCount);
    }

    /// <summary>
    /// Handles the merged-cell autofill shape <see cref="TryGetUniformMergeTileSize"/> allows
    /// through: tiles new, identically-sized merged regions across the fill range, continuing
    /// whatever series/pattern the lone source merge's anchor value would otherwise produce (the
    /// same series detection a single plain source cell gets -- <see cref="TryCreateForcedSingleCellSeries"/>
    /// / <see cref="TryCreateSingleCellListSeries"/> -- since a merge's non-anchor cells never
    /// hold an independent value to build a multi-cell trend from). Every new tile's non-anchor
    /// cells are left independent-content-free, matching the merge invariant that only a merge's
    /// top-left anchor cell may hold a value.
    /// </summary>
    private CommandOutcome ApplyMergeTiledFill(ICommandContext ctx, Sheet sheet, FillPlan plan, (uint RowSpan, uint ColSpan) tileSize)
    {
        for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)
        {
            for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                    return CommandGuards.RejectSheetProtected();
            }
        }
        if (CommandGuards.RejectIfSplitsArray(sheet, _fillRange.AllCells()) is { } splitsArrayRejection)
            return splitsArrayRejection;

        var sourceAnchor = _sourceRange.Start;
        var sourceCell = sheet.GetCell(sourceAnchor);
        var sourceHasFormula = sourceCell is { HasFormula: true, FormulaText: not null };
        var naturalListSeries = sourceHasFormula ? null : TryCreateSingleCellListSeries(sourceCell, plan);
        var forceCopyOnly = !sourceHasFormula && _ctrlHeld && naturalListSeries is not null;
        var wantsForcedScalarSeries = !sourceHasFormula && naturalListSeries is null
            && WantsSingleCellSeriesDefault(sourceCell?.Value, _ctrlHeld);
        var scalarSeries = forceCopyOnly ? null : (wantsForcedScalarSeries ? TryCreateForcedSingleCellSeries(sourceCell, plan) : null);
        var listSeries = forceCopyOnly ? null : naturalListSeries;

        var isVerticalTile = plan.Axis == FillAxis.Vertical;
        var reversed = plan.Direction is FillDirection.Up or FillDirection.Left;
        var tileCount = isVerticalTile
            ? (int)(_fillRange.RowCount / tileSize.RowSpan)
            : (int)(_fillRange.ColCount / tileSize.ColSpan);

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        _hyperlinkSnapshot = new List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>(capacity);
        _richTextRunsSnapshot = new List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>(capacity);
        _createdMergedRegions = [];
        var writtenCells = new List<CellAddress>(capacity);

        for (var t = 0; t < tileCount; t++)
        {
            uint tileRowStart, tileColStart;
            if (isVerticalTile)
            {
                tileRowStart = reversed
                    ? _fillRange.End.Row - (uint)(t + 1) * tileSize.RowSpan + 1
                    : _fillRange.Start.Row + (uint)t * tileSize.RowSpan;
                tileColStart = _fillRange.Start.Col;
            }
            else
            {
                tileRowStart = _fillRange.Start.Row;
                tileColStart = reversed
                    ? _fillRange.End.Col - (uint)(t + 1) * tileSize.ColSpan + 1
                    : _fillRange.Start.Col + (uint)t * tileSize.ColSpan;
            }

            var tileRange = new GridRange(
                new CellAddress(_sheetId, tileRowStart, tileColStart),
                new CellAddress(_sheetId, tileRowStart + tileSize.RowSpan - 1, tileColStart + tileSize.ColSpan - 1));
            var anchor = tileRange.Start;

            foreach (var addr in tileRange.AllCells())
            {
                var oldCell = sheet.GetCell(addr);
                var oldStyleOnly = oldCell is null ? sheet.GetStyleOnly(addr.Row, addr.Col) : null;
                _snapshot.Add((addr, oldCell?.Clone(), oldStyleOnly));
                SnapshotAnnotations(sheet, addr);
                writtenCells.Add(addr);
            }

            var offset = t + 1;
            Cell newCell;
            if (scalarSeries is not null)
            {
                newCell = Cell.FromValue(scalarSeries.CreateValue(scalarSeries.LastValue + scalarSeries.Step * offset));
            }
            else if (listSeries is not null)
            {
                newCell = Cell.FromValue(listSeries.ValueAt(offset));
            }
            else if (sourceHasFormula)
            {
                var rowOffset = (int)anchor.Row - (int)sourceAnchor.Row;
                var colOffset = (int)anchor.Col - (int)sourceAnchor.Col;
                var shifted = FormulaRewriter.Rewrite(sourceCell!.FormulaText!, new PasteOffsetOp(rowOffset, colOffset), sheet.Name)
                    ?? sourceCell.FormulaText!;
                newCell = Cell.FromFormula(shifted);
            }
            else
            {
                newCell = Cell.FromValue(sourceCell?.Value ?? BlankValue.Instance);
            }

            newCell.StyleId = sourceCell?.StyleId ?? StyleId.Default;
            sheet.SetCell(anchor, newCell);
            foreach (var addr in tileRange.AllCells())
            {
                if (addr == anchor) continue;
                sheet.ClearCell(addr);
                ClearAnnotations(sheet, addr);
            }

            var copyRichTextRuns = scalarSeries is null && listSeries is null;
            CopyAnnotations(sheet, sourceAnchor, anchor, copyRichTextRuns);

            sheet.AddMergedRegion(tileRange);
            _createdMergedRegions.Add(tileRange);
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        if (_createdMergedRegions is not null)
        {
            foreach (var region in _createdMergedRegions)
                sheet.RemoveMergedRegion(region);
        }

        foreach (var (addr, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(addr);
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(addr.Row, addr.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(addr.Row, addr.Col);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }
        }

        if (_hyperlinkSnapshot is not null)
        {
            foreach (var (address, hadTarget, target, hadMetadata, metadata) in _hyperlinkSnapshot)
            {
                if (hadTarget && target is not null)
                    sheet.Hyperlinks[address] = target;
                else
                    sheet.Hyperlinks.Remove(address);

                if (hadMetadata && metadata is not null)
                    sheet.HyperlinkMetadata[address] = metadata;
                else
                    sheet.HyperlinkMetadata.Remove(address);
            }
        }

        if (_richTextRunsSnapshot is not null)
        {
            foreach (var (address, hadRuns, runs) in _richTextRunsSnapshot)
            {
                if (hadRuns && runs is not null)
                    sheet.RichTextRuns[address] = runs;
                else
                    sheet.RichTextRuns.Remove(address);
            }
        }
    }

    /// <summary>Snapshots a destination cell's hyperlink/rich-text annotations before overwriting it, for undo.</summary>
    private void SnapshotAnnotations(Sheet sheet, CellAddress addr)
    {
        _hyperlinkSnapshot!.Add((
            addr,
            sheet.Hyperlinks.TryGetValue(addr, out var oldTarget),
            oldTarget,
            sheet.HyperlinkMetadata.TryGetValue(addr, out var oldMetadata),
            oldMetadata));
        _richTextRunsSnapshot!.Add((
            addr,
            sheet.RichTextRuns.TryGetValue(addr, out var oldRuns),
            oldRuns));
    }

    /// <summary>
    /// Copies (or removes) a destination cell's hyperlink/rich-text annotations to match the
    /// source cell that produced its new value, so a fill never leaves stale annotations behind
    /// (mirrors FillCellsCommand.Apply). <paramref name="copyRichTextRuns"/> is false when the
    /// destination's value was computed by a trend/list series rather than copied verbatim from
    /// <paramref name="source"/>; in that case the source's per-character rich-text runs describe
    /// text that no longer matches the new cell and must be dropped instead of copied.
    /// </summary>
    private static void CopyAnnotations(Sheet sheet, CellAddress source, CellAddress target, bool copyRichTextRuns = true)
    {
        if (sheet.Hyperlinks.TryGetValue(source, out var sourceTarget))
            sheet.Hyperlinks[target] = sourceTarget;
        else
            sheet.Hyperlinks.Remove(target);

        if (sheet.HyperlinkMetadata.TryGetValue(source, out var sourceMetadata))
            sheet.HyperlinkMetadata[target] = sourceMetadata;
        else
            sheet.HyperlinkMetadata.Remove(target);

        if (!copyRichTextRuns)
        {
            sheet.RichTextRuns.Remove(target);
            return;
        }

        if (sheet.RichTextRuns.TryGetValue(source, out var sourceRuns))
            sheet.RichTextRuns[target] = sourceRuns;
        else
            sheet.RichTextRuns.Remove(target);
    }

    /// <summary>Drops a destination cell's hyperlink/rich-text annotations (Clear Contents semantics).</summary>
    private static void ClearAnnotations(Sheet sheet, CellAddress addr)
    {
        sheet.Hyperlinks.Remove(addr);
        sheet.HyperlinkMetadata.Remove(addr);
        sheet.RichTextRuns.Remove(addr);
    }


    private bool TryGetFillPlan(out FillPlan plan)
    {
        plan = default;

        if (_sourceRange.Start.Sheet != _fillRange.Start.Sheet)
            return false;

        if (_sourceRange.Overlaps(_fillRange))
            return false;

        if (_sourceRange.ColCount == _fillRange.ColCount &&
            _sourceRange.Start.Col == _fillRange.Start.Col &&
            _sourceRange.End.Col == _fillRange.End.Col)
        {
            if (_fillRange.Start.Row == _sourceRange.End.Row + 1)
            {
                plan = new FillPlan(FillDirection.Down, FillAxis.Vertical);
                return true;
            }

            if (_sourceRange.Start.Row > 1 && _fillRange.End.Row + 1 == _sourceRange.Start.Row)
            {
                plan = new FillPlan(FillDirection.Up, FillAxis.Vertical);
                return true;
            }
        }

        if (_sourceRange.RowCount == _fillRange.RowCount &&
            _sourceRange.Start.Row == _fillRange.Start.Row &&
            _sourceRange.End.Row == _fillRange.End.Row)
        {
            if (_fillRange.Start.Col == _sourceRange.End.Col + 1)
            {
                plan = new FillPlan(FillDirection.Right, FillAxis.Horizontal);
                return true;
            }

            if (_sourceRange.Start.Col > 1 && _fillRange.End.Col + 1 == _sourceRange.Start.Col)
            {
                plan = new FillPlan(FillDirection.Left, FillAxis.Horizontal);
                return true;
            }
        }

        return false;
    }

    private CellAddress GetSourceEdgeAddress(FillPlan plan) => plan.Direction switch
    {
        FillDirection.Down => _sourceRange.End,
        FillDirection.Right => _sourceRange.End,
        FillDirection.Up => _sourceRange.Start,
        FillDirection.Left => _sourceRange.Start,
        _ => _sourceRange.End
    };

    /// <summary>
    /// Resolves which cell within <see cref="_sourceRange"/> a given destination cell should
    /// mirror when replaying the source's per-cell pattern (formula shape or plain copy) rather
    /// than a detected trend/list series. Excel repeats the whole source pattern cyclically every
    /// <paramref name="sourceLength"/> cells: the cell adjacent to the source mirrors the source
    /// cell nearest the fill edge, and each subsequent cell advances one step further into the
    /// pattern, wrapping back to the start of the pattern after <paramref name="sourceLength"/>
    /// cells.
    /// </summary>
    private CellAddress ResolvePatternSourceAddress(FillPlan plan, CellAddress addr, int sourceLength)
    {
        if (sourceLength <= 0)
            sourceLength = 1;

        switch (plan.Direction)
        {
            case FillDirection.Down:
            {
                var stepsAway = (int)addr.Row - (int)_sourceRange.End.Row - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, _sourceRange.Start.Row + (uint)patternIndex, addr.Col);
            }
            case FillDirection.Up:
            {
                var stepsAway = (int)_sourceRange.Start.Row - (int)addr.Row - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, _sourceRange.End.Row - (uint)patternIndex, addr.Col);
            }
            case FillDirection.Right:
            {
                var stepsAway = (int)addr.Col - (int)_sourceRange.End.Col - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, addr.Row, _sourceRange.Start.Col + (uint)patternIndex);
            }
            case FillDirection.Left:
            default:
            {
                var stepsAway = (int)_sourceRange.Start.Col - (int)addr.Col - 1;
                var patternIndex = Mod(stepsAway, sourceLength);
                return new CellAddress(_sheetId, addr.Row, _sourceRange.End.Col - (uint)patternIndex);
            }
        }
    }

    /// <summary>
    /// Resolves the style a detected trend/list series destination cell should carry. Excel
    /// continues the source SELECTION's own per-cell format pattern (e.g. an alternating
    /// Currency/General pair) cyclically into the series-filled cells rather than stamping every
    /// destination with the single edge cell's format -- this reuses the exact same cyclic
    /// position <see cref="ResolvePatternSourceAddress"/> already computes for the plain
    /// pattern-copy path below.
    /// </summary>
    private StyleId ResolvePatternSourceStyleId(Sheet sheet, FillPlan plan, CellAddress addr, int sourceLength, Cell fallback)
    {
        var patternSourceAddr = ResolvePatternSourceAddress(plan, addr, sourceLength);
        return sheet.GetCell(patternSourceAddr)?.StyleId ?? fallback.StyleId;
    }

    private int GetFillCellCapacity()
    {
        var count = _fillRange.CellCount;
        return count <= int.MaxValue ? (int)count : 0;
    }

    private ScalarSeries? TryCreateScalarSeries(Sheet sheet, FillPlan plan)
    {
        var isVertical = _sourceRange.ColCount == 1 && _sourceRange.RowCount >= 2;
        var isHorizontal = _sourceRange.RowCount == 1 && _sourceRange.ColCount >= 2;
        if (!isVertical && !isHorizontal)
            return null;

        var values = _sourceRange.AllCells()
            .Select(addr => sheet.GetCell(addr)?.Value)
            .ToList();

        Func<double, ScalarValue>? createValue;
        if (values.All(value => value is NumberValue))
            createValue = serial => new NumberValue(serial);
        else if (values.All(value => value is DateTimeValue))
            createValue = serial => new DateTimeValue(serial);
        else
            return null;

        var numbers = values.Select(value => value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            _ => 0
        }).ToList();
        var naturalSlope = ComputeLinearFitSlope(numbers);
        // Excel's fill handle continues the least-squares regression line itself, not a step
        // applied from the raw edge value: for a non-collinear source (e.g. 1, 2, 6) the fitted
        // line's intercept differs from any single sampled point, so anchoring on the actual
        // first/last value would offset every filled cell by that fitted-vs-actual gap. Anchor on
        // the regression line's value at the source's edge index instead, so
        // anchor + step*offset always lies on the fitted line (this reduces to the plain edge
        // value -- the old behavior -- whenever the source is already perfectly linear, since the
        // line then passes exactly through every sampled point).
        var meanX = (numbers.Count - 1) / 2.0;
        var intercept = numbers.Average() - naturalSlope * meanX;
        var anchor = plan.Direction is FillDirection.Up or FillDirection.Left
            ? intercept
            : intercept + naturalSlope * (numbers.Count - 1);
        var step = plan.Direction is FillDirection.Up or FillDirection.Left ? -naturalSlope : naturalSlope;

        return new ScalarSeries(anchor, step, plan.Axis, createValue);
    }

    /// <summary>
    /// Excel's default fill-handle action for a LONE plain number/date source (no natural
    /// multi-cell trend or text/list series detected) depends on the value's type: a number
    /// defaults to a copy (Ctrl forces the +1/day increment series below instead); a date
    /// defaults to the day-increment series itself (Ctrl forces a copy instead). Without this
    /// distinction, a single date cell would (wrongly) just copy by default like a plain number.
    /// </summary>
    private static bool WantsSingleCellSeriesDefault(ScalarValue? value, bool ctrlHeld) =>
        value is DateTimeValue ? !ctrlHeld : ctrlHeld;

    /// <summary>
    /// Builds the incrementing series (step of 1 day/unit) for a lone plain number/date source
    /// cell, per <see cref="WantsSingleCellSeriesDefault"/>. Takes the source cell directly
    /// (rather than a <see cref="_sourceRange"/>-relative lookup) so it can also serve
    /// <see cref="ApplyMergeTiledFill"/>'s single merged-cell source, which spans multiple grid
    /// cells even though it is logically one source value.
    /// </summary>
    private static ScalarSeries? TryCreateForcedSingleCellSeries(Cell? sourceCell, FillPlan plan)
    {
        Func<double, ScalarValue> createValue;
        double seed;
        switch (sourceCell?.Value)
        {
            case NumberValue number:
                createValue = serial => new NumberValue(serial);
                seed = number.Value;
                break;
            case DateTimeValue date:
                createValue = serial => new DateTimeValue(serial);
                seed = date.Value;
                break;
            default:
                return null;
        }

        var step = plan.Direction is FillDirection.Up or FillDirection.Left ? -1 : 1;
        return new ScalarSeries(seed, step, plan.Axis, createValue);
    }

    /// <summary>
    /// Detects the two non-numeric Excel fill-handle series: text ending in a number
    /// (e.g. "Item 1", "Item 2" -&gt; "Item 3") and membership in one of Excel's built-in
    /// auto-fill lists (weekday/month names, full or abbreviated), which wrap around after
    /// the last entry. Requires at least one source cell and, for text-with-number, either a
    /// single source cell (auto-increments by 1) or a source range whose trailing numbers all
    /// share the same prefix/suffix and advance by a constant step.
    /// </summary>
    private ListSeries? TryCreateListSeries(Sheet sheet, FillPlan plan)
    {
        var isVertical = _sourceRange.ColCount == 1 && _sourceRange.RowCount >= 1;
        var isHorizontal = _sourceRange.RowCount == 1 && _sourceRange.ColCount >= 1;
        if (!isVertical && !isHorizontal)
            return null;

        var texts = _sourceRange.AllCells()
            .Select(addr => sheet.GetCell(addr)?.Value)
            .Select(value => value is TextValue text ? text.Value : null)
            .ToList();
        if (texts.Any(text => text is null))
            return null;
        var values = texts.Cast<string>().ToList();

        return TryCreateTrailingNumberSeries(values, plan)
            ?? TryCreateBuiltInListSeries(values, plan);
    }

    /// <summary>
    /// Single-source-cell variant of <see cref="TryCreateListSeries"/> for
    /// <see cref="ApplyMergeTiledFill"/>'s merged source, whose logical "cell" is one anchor
    /// value rather than a <see cref="_sourceRange"/> of individually addressable cells (the
    /// merge's non-anchor cells hold no independent value at all, so the multi-cell overload's
    /// per-cell scan over <see cref="_sourceRange"/> cannot be reused directly).
    /// </summary>
    private static ListSeries? TryCreateSingleCellListSeries(Cell? sourceCell, FillPlan plan)
    {
        if (sourceCell?.Value is not TextValue text)
            return null;

        return TryCreateTrailingNumberSeries([text.Value], plan)
            ?? TryCreateBuiltInListSeries([text.Value], plan);
    }

    /// <summary>Text ending in a run of digits (optionally with leading zeros): "Item 1" -&gt; "Item 2", ...</summary>
    private static ListSeries? TryCreateTrailingNumberSeries(IReadOnlyList<string> values, FillPlan plan)
    {
        var parsed = values.Select(TrySplitTrailingNumber).ToList();
        if (parsed.Any(part => part is null))
            return null;

        var prefix = parsed[0]!.Value.Prefix;
        var width = parsed[0]!.Value.Width;
        if (parsed.Any(part => part!.Value.Prefix != prefix))
            return null;

        var numbers = parsed.Select(part => (double)part!.Value.Number).ToList();
        double step = numbers.Count >= 2
            ? ComputeLinearFitSlope(numbers)
            : (plan.Direction is FillDirection.Up or FillDirection.Left ? -1 : 1);
        var lastNumber = plan.Direction is FillDirection.Up or FillDirection.Left ? numbers[0] : numbers[^1];
        var directedStep = plan.Direction is FillDirection.Up or FillDirection.Left ? -step : step;

        return new ListSeries(plan.Axis, offset =>
        {
            var next = (long)Math.Round(lastNumber + directedStep * offset);
            var digits = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (next >= 0 && digits.Length < width)
                digits = digits.PadLeft(width, '0');
            return new TextValue(prefix + digits);
        });
    }

    private static (string Prefix, int Width, long Number)? TrySplitTrailingNumber(string text)
    {
        var i = text.Length;
        while (i > 0 && char.IsAsciiDigit(text[i - 1]))
            i--;
        if (i == text.Length)
            return null; // no trailing digits at all

        var digits = text[i..];
        if (!long.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
            return null; // too large / not a plain digit run

        return (text[..i], digits.Length, number);
    }

    private static readonly string[][] BuiltInLists =
    [
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    ];

    /// <summary>Excel's built-in weekday/month name lists, wrapping around after the last entry.</summary>
    private static ListSeries? TryCreateBuiltInListSeries(IReadOnlyList<string> values, FillPlan plan)
    {
        foreach (var list in BuiltInLists)
        {
            var indices = values
                .Select(value => Array.FindIndex(list, entry => string.Equals(entry, value, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (indices.Any(index => index < 0))
                continue;

            var step = indices.Count >= 2
                ? (int)Math.Round(ComputeLinearFitSlope(indices.Select(i => (double)i).ToList()))
                : (plan.Direction is FillDirection.Up or FillDirection.Left ? -1 : 1);
            var lastIndex = plan.Direction is FillDirection.Up or FillDirection.Left ? indices[0] : indices[^1];
            var directedStep = plan.Direction is FillDirection.Up or FillDirection.Left ? -step : step;
            if (directedStep == 0)
                directedStep = 1;

            return new ListSeries(plan.Axis, offset =>
            {
                var index = Mod(lastIndex + directedStep * (int)offset, list.Length);
                return new TextValue(list[index]);
            });
        }

        return null;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>
    /// Fits a straight line (least-squares) through <paramref name="numbers"/> (treated as
    /// y-values at evenly spaced x = 0, 1, 2, ...) and returns its slope, matching Excel's
    /// fill-handle behavior for a linear numeric/date trend. For exactly two values this
    /// reduces to the plain two-point slope (numbers[1] - numbers[0]).
    /// </summary>
    private static double ComputeLinearFitSlope(IReadOnlyList<double> numbers)
    {
        var n = numbers.Count;
        if (n < 2)
            return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += numbers[i];
            sumXY += i * numbers[i];
            sumXX += (double)i * i;
        }

        var denominator = n * sumXX - sumX * sumX;
        if (denominator == 0)
            return 0;

        return (n * sumXY - sumX * sumY) / denominator;
    }

    /// <param name="LastValue">
    /// The series' anchor value at the fill's starting edge (offset 0): for
    /// <see cref="TryCreateForcedSingleCellSeries"/> this is the literal seed cell value, but for
    /// <see cref="TryCreateScalarSeries"/> it is the least-squares regression line's fitted value
    /// at the source's edge index (not necessarily the actual sampled cell value), so that
    /// <c>LastValue + Step * offset</c> always lies on the fitted trend line.
    /// </param>
    private sealed record ScalarSeries(
        double LastValue,
        double Step,
        FillAxis Axis,
        Func<double, ScalarValue> CreateValue);

    private sealed record ListSeries(FillAxis Axis, Func<int, ScalarValue> ValueAt);

    private readonly record struct FillPlan(FillDirection Direction, FillAxis Axis);

    private enum FillDirection
    {
        Down,
        Right,
        Up,
        Left
    }

    private enum FillAxis
    {
        Vertical,
        Horizontal
    }

}
