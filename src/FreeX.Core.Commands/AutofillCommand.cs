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
public sealed class AutofillCommand : IWorkbookCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: mirrors FillCellsCommand's rationale -- the undo snapshot
    // holds a Cell clone plus style, hyperlink/metadata, rich-text runs and phonetic guide PER FILL
    // TARGET (see Apply below), scaling with _fillRange.CellCount, not a flat per-command constant.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _fillRange;
    private readonly bool _ctrlHeld;
    private readonly IReadOnlyList<IReadOnlyList<string>> _customLists;
    private List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;
    private List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>? _hyperlinkSnapshot;
    private List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>? _richTextRunsSnapshot;
    private List<(CellAddress Address, bool HadPhoneticGuide, CellPhoneticGuide? PhoneticGuide)>? _phoneticGuideSnapshot;
    // R142-comments-notes-1: fills (fill handle / merge-tiled fill / inward-clear shrink) must
    // carry a source cell's legacy note (Comments/CommentAuthors/ShownComments) and threaded
    // comment across to the destination, and undo must restore exactly what was there before --
    // mirrors CopyRangeCommand's CellSnapshot comment fields, kept as a parallel snapshot list
    // here to match this command's existing per-annotation-kind snapshot shape.
    private List<(CellAddress Address, bool HadComment, string? Comment, bool HadCommentAuthor, string? CommentAuthor, bool HadShown, bool HadThreadedComment, ThreadedComment? ThreadedComment)>? _commentSnapshot;
    private List<GridRange>? _createdMergedRegions;
    // autofill-series-F1: dragging the fill handle one row below (or one column right of) a
    // Structured Table's current Range must auto-expand the table and propagate a calculated
    // column's formula into the newly grown row(s), exactly like a typed edit does via
    // EditCellsCommand -> StructuredTableEditEffects.Apply (Commands.cs). Populated by Apply()
    // below and unwound (in reverse order) before the base fill snapshot is reverted.
    private readonly List<IWorkbookCommand> _appliedTableEffects = [];

    public string Label => "Autofill";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_fillRange.CellCount * BytesPerCell, int.MaxValue);

    /// <param name="ctrlHeld">
    /// True when the user held Ctrl while releasing the fill-handle drag. Excel uses Ctrl to flip
    /// the fill handle's default behavior for a detected series (2+ source cells, or any
    /// text/list series): it becomes a plain copy of the last value instead. For a LONE plain
    /// number/date cell (no natural multi-cell series to detect), the default itself is
    /// type-dependent: a number defaults to a copy (Ctrl forces an incrementing series instead),
    /// while a date defaults to a day-increment series (Ctrl forces a copy instead) -- see
    /// <see cref="WantsSingleCellSeriesDefault"/>.
    /// </param>
    /// <param name="customLists">
    /// User-defined custom autofill lists (Excel: File ▸ Options ▸ Advanced ▸ Edit Custom
    /// Lists), checked after Excel's built-in weekday/month lists so a fill-handle drag off a
    /// value like "North" wraps through the user's own list ("South", "East", "West", "North",
    /// ...) instead of falling through to a plain copy. Defaults to none: this command has no
    /// dependency on where custom lists are persisted, so a host that adds custom-list storage
    /// passes its saved lists here.
    /// </param>
    public AutofillCommand(SheetId sheetId, GridRange sourceRange, GridRange fillRange, bool ctrlHeld = false, IReadOnlyList<IReadOnlyList<string>>? customLists = null)
    {
        _sheetId     = sheetId;
        _sourceRange = sourceRange;
        _fillRange   = fillRange;
        _ctrlHeld    = ctrlHeld;
        _customLists = customLists ?? [];
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
        if (CommandGuards.RejectIfSplitsArray(sheet, _fillRange.AllCells(), allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
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
        _phoneticGuideSnapshot = new List<(CellAddress Address, bool HadPhoneticGuide, CellPhoneticGuide? PhoneticGuide)>(capacity);
        _commentSnapshot = new List<(CellAddress Address, bool HadComment, string? Comment, bool HadCommentAuthor, string? CommentAuthor, bool HadShown, bool HadThreadedComment, ThreadedComment? ThreadedComment)>(capacity);
        var writtenCells = new List<CellAddress>(capacity);
        // Fed to StructuredTableEditEffects.Apply below, in the same row/column order the fill
        // itself writes -- required so a multi-row drag past a table's last row grows the table
        // cumulatively (each successive address sees the table range already grown by the
        // previous one), matching how EditCellsCommand feeds it a multi-cell edit batch.
        var tableEffectEdits = new List<(CellAddress Address, Cell NewCell)>(capacity);

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
                    var (seriesAnchor, seriesStep) = scalarSeries.LineFor(addr);
                    newCell = Cell.FromValue(scalarSeries.CreateValue(seriesAnchor + seriesStep * offset));
                    newCell.StyleId = ResolvePatternSourceStyleId(sheet, plan, addr, sourceLength, sourceCell);
                    annotationSourceAddr = sourceAddr;
                }
                else if (listSeries is not null)
                {
                    newCell = Cell.FromValue(listSeries.LineFor(addr)(offset));
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
                tableEffectEdits.Add((addr, newCell));
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

        // autofill-series-F1: replay the same table auto-expand (N33) / calculated-column
        // propagation (N34) effects a typed edit gets, for every cell this fill actually wrote a
        // value/formula into (sourceCell-null clears and pattern-source-null clears never reach
        // tableEffectEdits, matching EditCellsCommand's isRealContentEdit gate upstream in
        // StructuredTableEditEffects.Apply). Extra cells the effects wrote (e.g. propagated
        // calculated-column formulas in sibling columns) are folded into AffectedCells so they
        // get recalculated, same as EditCellsCommand does.
        var tableEffectCells = StructuredTableEditEffects.Apply(ctx, tableEffectEdits, _appliedTableEffects);
        if (tableEffectCells.Count > 0)
            writtenCells.AddRange(tableEffectCells);

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
        if (CommandGuards.RejectIfSplitsArray(sheet, _fillRange.AllCells(), allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
            return splitsArrayRejection;

        var capacity = GetFillCellCapacity();
        _snapshot = new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity);
        _hyperlinkSnapshot = new List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>(capacity);
        _richTextRunsSnapshot = new List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>(capacity);
        _phoneticGuideSnapshot = new List<(CellAddress Address, bool HadPhoneticGuide, CellPhoneticGuide? PhoneticGuide)>(capacity);
        _commentSnapshot = new List<(CellAddress Address, bool HadComment, string? Comment, bool HadCommentAuthor, string? CommentAuthor, bool HadShown, bool HadThreadedComment, ThreadedComment? ThreadedComment)>(capacity);
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
        if (CommandGuards.RejectIfSplitsArray(sheet, _fillRange.AllCells(), allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
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
        _phoneticGuideSnapshot = new List<(CellAddress Address, bool HadPhoneticGuide, CellPhoneticGuide? PhoneticGuide)>(capacity);
        _commentSnapshot = new List<(CellAddress Address, bool HadComment, string? Comment, bool HadCommentAuthor, string? CommentAuthor, bool HadShown, bool HadThreadedComment, ThreadedComment? ThreadedComment)>(capacity);
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
                var (seriesAnchor, seriesStep) = scalarSeries.LineFor(anchor);
                newCell = Cell.FromValue(scalarSeries.CreateValue(seriesAnchor + seriesStep * offset));
            }
            else if (listSeries is not null)
            {
                newCell = Cell.FromValue(listSeries.LineFor(anchor)(offset));
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

        // Unwind table auto-expand / calculated-column propagation before the base fill
        // snapshot below, mirroring EditCellsCommand.Revert's ordering: those sub-commands may
        // have grown the table's Range or written into sibling calculated-column rows this
        // command's own _snapshot never touched, so they must come off first.
        StructuredTableEditEffects.Revert(ctx, _appliedTableEffects);

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

        if (_phoneticGuideSnapshot is not null)
        {
            foreach (var (address, hadPhoneticGuide, phoneticGuide) in _phoneticGuideSnapshot)
            {
                if (hadPhoneticGuide && phoneticGuide is not null)
                    sheet.CellPhoneticGuides[address] = phoneticGuide;
                else
                    sheet.CellPhoneticGuides.Remove(address);
            }
        }

        if (_commentSnapshot is not null)
        {
            foreach (var (address, hadComment, comment, hadCommentAuthor, commentAuthor, hadShown, hadThreadedComment, threadedComment) in _commentSnapshot)
            {
                if (hadComment && comment is not null)
                    sheet.Comments[address] = comment;
                else
                    sheet.Comments.Remove(address);

                if (hadCommentAuthor && commentAuthor is not null)
                    sheet.CommentAuthors[address] = commentAuthor;
                else
                    sheet.CommentAuthors.Remove(address);

                if (hadShown)
                    sheet.ShownComments.Add(address);
                else
                    sheet.ShownComments.Remove(address);

                if (hadThreadedComment && threadedComment is not null)
                    sheet.ThreadedComments[address] = CloneThreadedComment(threadedComment);
                else
                    sheet.ThreadedComments.Remove(address);
            }
        }
    }

    /// <summary>Snapshots a destination cell's hyperlink/rich-text/phonetic-guide/comment annotations before overwriting it, for undo.</summary>
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
        _phoneticGuideSnapshot!.Add((
            addr,
            sheet.CellPhoneticGuides.TryGetValue(addr, out var oldPhoneticGuide),
            oldPhoneticGuide));
        _commentSnapshot!.Add((
            addr,
            sheet.Comments.TryGetValue(addr, out var oldComment),
            oldComment,
            sheet.CommentAuthors.TryGetValue(addr, out var oldCommentAuthor),
            oldCommentAuthor,
            sheet.ShownComments.Contains(addr),
            sheet.ThreadedComments.TryGetValue(addr, out var oldThreadedComment),
            oldThreadedComment is null ? null : CloneThreadedComment(oldThreadedComment)));
    }

    /// <summary>
    /// Copies (or removes) a destination cell's hyperlink/rich-text/comment annotations to match
    /// the source cell that produced its new value, so a fill never leaves stale annotations
    /// behind (mirrors FillCellsCommand.Apply). <paramref name="copyRichTextRuns"/> is false when
    /// the destination's value was computed by a trend/list series rather than copied verbatim
    /// from <paramref name="source"/>; in that case the source's per-character rich-text runs
    /// describe text that no longer matches the new cell and must be dropped instead of copied.
    /// Legacy notes and threaded comments are unaffected by that distinction -- like Hyperlinks,
    /// Excel carries a cell's note/comment along with a fill-handle drag regardless of whether the
    /// destination's value is a verbatim copy or a computed series member (R142-comments-notes-1).
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

        CopyCommentAnnotations(sheet, source, target);

        if (!copyRichTextRuns)
        {
            sheet.RichTextRuns.Remove(target);
            sheet.CellPhoneticGuides.Remove(target);
            return;
        }

        if (sheet.RichTextRuns.TryGetValue(source, out var sourceRuns))
            sheet.RichTextRuns[target] = sourceRuns;
        else
            sheet.RichTextRuns.Remove(target);

        if (sheet.CellPhoneticGuides.TryGetValue(source, out var sourcePhoneticGuide))
            sheet.CellPhoneticGuides[target] = sourcePhoneticGuide;
        else
            sheet.CellPhoneticGuides.Remove(target);
    }

    /// <summary>
    /// Copies (or removes) a destination cell's legacy note (Comments/CommentAuthors/
    /// ShownComments) and threaded comment to match <paramref name="source"/>, mirroring
    /// CopyRangeCommand's comment-carry behavior. When <paramref name="source"/> equals
    /// <paramref name="target"/> (self-copy) this is a no-op by construction since every branch
    /// reads then writes/removes the same key. A fresh, independent threaded-comment thread is
    /// minted for the destination (Id cleared) so multiple filled cells sharing one source note
    /// don't collide on the same persisted thread id on save (mirrors CopyRangeCommand.
    /// ClonedThreadedCommentForNewAddress).
    /// </summary>
    private static void CopyCommentAnnotations(Sheet sheet, CellAddress source, CellAddress target)
    {
        if (sheet.Comments.TryGetValue(source, out var sourceComment))
            sheet.Comments[target] = sourceComment;
        else
            sheet.Comments.Remove(target);

        if (sheet.CommentAuthors.TryGetValue(source, out var sourceCommentAuthor))
            sheet.CommentAuthors[target] = sourceCommentAuthor;
        else
            sheet.CommentAuthors.Remove(target);

        if (sheet.ShownComments.Contains(source))
            sheet.ShownComments.Add(target);
        else
            sheet.ShownComments.Remove(target);

        if (sheet.ThreadedComments.TryGetValue(source, out var sourceThreadedComment))
            sheet.ThreadedComments[target] = ClonedThreadedCommentForNewAddress(sourceThreadedComment);
        else
            sheet.ThreadedComments.Remove(target);
    }

    /// <summary>Drops a destination cell's hyperlink/rich-text/phonetic-guide/comment annotations (Clear Contents semantics).</summary>
    private static void ClearAnnotations(Sheet sheet, CellAddress addr)
    {
        sheet.Hyperlinks.Remove(addr);
        sheet.HyperlinkMetadata.Remove(addr);
        sheet.RichTextRuns.Remove(addr);
        sheet.CellPhoneticGuides.Remove(addr);
        sheet.Comments.Remove(addr);
        sheet.CommentAuthors.Remove(addr);
        sheet.ShownComments.Remove(addr);
        sheet.ThreadedComments.Remove(addr);
    }

    /// <summary>Deep-clones a threaded comment (including its reply list) for a snapshot, preserving its Id. Mirrors CopyRangeCommand.CloneThreadedComment.</summary>
    private static ThreadedComment CloneThreadedComment(ThreadedComment comment) =>
        comment with { Replies = comment.Replies.Select(reply => reply with { }).ToList() };

    /// <summary>
    /// Clones a threaded comment for a NEW destination address, clearing its Id (and each reply's
    /// Id) so the copy mints its own independent, address-derived thread id on save instead of
    /// colliding with the source's persisted <c>&lt;threadedComment id="..."&gt;</c>. Mirrors
    /// CopyRangeCommand.ClonedThreadedCommentForNewAddress.
    /// </summary>
    private static ThreadedComment ClonedThreadedCommentForNewAddress(ThreadedComment comment) =>
        comment with
        {
            Id = null,
            Replies = comment.Replies.Select(reply => reply with { Id = null }).ToList(),
        };


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
        // A trend can only be fitted across 2+ samples along the axis actually being filled: a
        // single row filled DOWN (or a single column filled RIGHT) has only one source value per
        // destination line, so it must fall through to Apply()'s per-line copy/lone-cell
        // defaults instead of (wrongly) fitting a trend across the orthogonal axis using the
        // source's OTHER dimension.
        if (plan.Axis == FillAxis.Vertical ? _sourceRange.RowCount < 2 : _sourceRange.ColCount < 2)
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

        // Excel fits an independent least-squares trend per LINE of the source: each column of
        // a rectangular (multi-row AND multi-column) source continues its own series when
        // filling down/up, and each row continues its own series when filling left/right,
        // rather than flattening the whole rectangle into one shared sequence. A source shaped
        // along a single line (the classic 1-column or 1-row case) naturally reduces to exactly
        // one line below.
        var lines = new Dictionary<uint, (double Anchor, double Step)>();
        foreach (var (lineKey, cells) in EnumerateSeriesLines(plan))
        {
            var numbers = cells.Select(addr => ToSeriesNumber(sheet.GetCell(addr)?.Value)).ToList();
            lines[lineKey] = FitScalarLine(numbers, plan);
        }

        return new ScalarSeries(lines, plan.Axis, createValue);
    }

    private static double ToSeriesNumber(ScalarValue? value) => value switch
    {
        NumberValue number => number.Value,
        DateTimeValue date => date.Value,
        _ => 0
    };

    /// <summary>
    /// Fits one series line's least-squares regression, anchored at the fill's starting edge.
    /// Excel's fill handle continues the fitted regression line itself, not a step applied from
    /// the raw edge value: for a non-collinear line (e.g. 1, 2, 6) the fitted line's intercept
    /// differs from any single sampled point, so anchoring on the actual first/last value would
    /// offset every filled cell by that fitted-vs-actual gap. Anchor on the regression line's
    /// value at the source's edge index instead, so anchor + step*offset always lies on the
    /// fitted line (this reduces to the plain edge value -- the old behavior -- whenever the
    /// line is already perfectly linear, since the line then passes exactly through every
    /// sampled point).
    /// </summary>
    private static (double Anchor, double Step) FitScalarLine(IReadOnlyList<double> numbers, FillPlan plan)
    {
        var naturalSlope = ComputeLinearFitSlope(numbers);
        var meanX = (numbers.Count - 1) / 2.0;
        var intercept = numbers.Average() - naturalSlope * meanX;
        var anchor = plan.Direction is FillDirection.Up or FillDirection.Left
            ? intercept
            : intercept + naturalSlope * (numbers.Count - 1);
        var step = plan.Direction is FillDirection.Up or FillDirection.Left ? -naturalSlope : naturalSlope;
        return (anchor, step);
    }

    /// <summary>
    /// Splits <see cref="_sourceRange"/> into the independent series lines Excel fits/continues
    /// separately when dragging the fill handle: one line per column (keyed by column) when
    /// filling down/up, one line per row (keyed by row) when filling left/right. A source
    /// shaped along the fill axis (e.g. a single column filled down) yields exactly one line
    /// covering the whole range.
    /// </summary>
    private IEnumerable<(uint LineKey, IReadOnlyList<CellAddress> Cells)> EnumerateSeriesLines(FillPlan plan)
    {
        if (plan.Axis == FillAxis.Vertical)
        {
            for (var col = _sourceRange.Start.Col; col <= _sourceRange.End.Col; col++)
            {
                var cells = new List<CellAddress>();
                for (var row = _sourceRange.Start.Row; row <= _sourceRange.End.Row; row++)
                    cells.Add(new CellAddress(_sheetId, row, col));
                yield return (col, cells);
            }
        }
        else
        {
            for (var row = _sourceRange.Start.Row; row <= _sourceRange.End.Row; row++)
            {
                var cells = new List<CellAddress>();
                for (var col = _sourceRange.Start.Col; col <= _sourceRange.End.Col; col++)
                    cells.Add(new CellAddress(_sheetId, row, col));
                yield return (row, cells);
            }
        }
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
        var lines = new Dictionary<uint, (double Anchor, double Step)> { [0] = (seed, step) };
        return new ScalarSeries(lines, plan.Axis, createValue);
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
        var texts = _sourceRange.AllCells()
            .Select(addr => sheet.GetCell(addr)?.Value)
            .Select(value => value is TextValue text ? text.Value : null)
            .ToList();
        if (texts.Any(text => text is null))
            return null;

        // Same per-line split as TryCreateScalarSeries: each column continues its own list
        // series when filling down/up, each row when filling left/right, instead of flattening a
        // rectangular (multi-row AND multi-column) source into one shared sequence.
        var lines = new Dictionary<uint, Func<int, ScalarValue>>();
        foreach (var (lineKey, cells) in EnumerateSeriesLines(plan))
        {
            var lineValues = cells.Select(addr => ((TextValue)sheet.GetCell(addr)!.Value).Value).ToList();
            var lineFunc = TryCreateTrailingNumberSeries(lineValues, plan)
                ?? TryCreateBuiltInListSeries(lineValues, plan, _customLists);
            if (lineFunc is null)
                return null;

            lines[lineKey] = lineFunc;
        }

        return new ListSeries(lines, plan.Axis);
    }

    /// <summary>
    /// Single-source-cell variant of <see cref="TryCreateListSeries"/> for
    /// <see cref="ApplyMergeTiledFill"/>'s merged source, whose logical "cell" is one anchor
    /// value rather than a <see cref="_sourceRange"/> of individually addressable cells (the
    /// merge's non-anchor cells hold no independent value at all, so the multi-cell overload's
    /// per-cell scan over <see cref="_sourceRange"/> cannot be reused directly).
    /// </summary>
    private ListSeries? TryCreateSingleCellListSeries(Cell? sourceCell, FillPlan plan)
    {
        if (sourceCell?.Value is not TextValue text)
            return null;

        var lineFunc = TryCreateTrailingNumberSeries([text.Value], plan)
            ?? TryCreateBuiltInListSeries([text.Value], plan, _customLists);
        if (lineFunc is null)
            return null;

        return new ListSeries(new Dictionary<uint, Func<int, ScalarValue>> { [0] = lineFunc }, plan.Axis);
    }

    /// <summary>Text ending in a run of digits (optionally with leading zeros): "Item 1" -&gt; "Item 2", ...</summary>
    private static Func<int, ScalarValue>? TryCreateTrailingNumberSeries(IReadOnlyList<string> values, FillPlan plan)
    {
        var parsed = values.Select(TrySplitTrailingNumber).ToList();
        if (parsed.Any(part => part is null))
            return null;

        var prefix = parsed[0]!.Value.Prefix;
        var width = parsed[0]!.Value.Width;
        if (parsed.Any(part => part!.Value.Prefix != prefix))
            return null;

        var numbers = parsed.Select(part => (double)part!.Value.Number).ToList();
        // Like the built-in-list path below, this single-sample fallback step is direction-INDEPENDENT
        // (+1 = the next value of an increasing sequence) so that the directedStep flip immediately
        // below is the ONLY place direction is applied. Baking direction in here as well would
        // double-negate it for Up/Left, silently cancelling the flip so a lone "Item5" dragged UP
        // counted forward (Item6, Item7) instead of backward (Item4, Item3).
        double step = numbers.Count >= 2
            ? ComputeLinearFitSlope(numbers)
            : 1;
        var lastNumber = plan.Direction is FillDirection.Up or FillDirection.Left ? numbers[0] : numbers[^1];
        var directedStep = plan.Direction is FillDirection.Up or FillDirection.Left ? -step : step;

        return offset =>
        {
            var next = (long)Math.Round(lastNumber + directedStep * offset);
            var digits = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (next >= 0 && digits.Length < width)
                digits = digits.PadLeft(width, '0');
            // Mirrors PasteCommandFactory.TruncateToExcelCellTextLimit's cap on literal cell text:
            // a fill-handle drag off a seed whose prefix is already near Excel's 32,767-character
            // cell limit must not push the generated series text past it.
            return new TextValue(PasteCommandFactory.TruncateToExcelCellTextLimit(prefix + digits));
        };
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

    /// <summary>
    /// Excel's built-in weekday/month name lists, followed by any user-defined custom autofill
    /// lists (Excel: File ▸ Options ▸ Advanced ▸ Edit Custom Lists) supplied by the caller, in
    /// declaration order; whichever list matches every value wraps around after its last entry.
    /// </summary>
    private static Func<int, ScalarValue>? TryCreateBuiltInListSeries(IReadOnlyList<string> values, FillPlan plan, IReadOnlyList<IReadOnlyList<string>>? customLists = null)
    {
        foreach (var list in EnumerateAutoFillLists(customLists))
        {
            var indices = values
                .Select(value => IndexOfIgnoreCase(list, value))
                .ToList();
            if (indices.Any(index => index < 0))
                continue;

            // Unlike TryCreateTrailingNumberSeries (kept bit-for-bit as-is per the fill-handle
            // no-regression contract), this single-sample fallback step is direction-INDEPENDENT
            // -- always +1, exactly like the 2+-sample linear fit above always yields a positive
            // step for an increasing (list-forward) sequence -- so that the directedStep flip
            // immediately below is the only place direction is applied. Baking direction into
            // this step too would double-negate it for Up/Left, silently cancelling the flip and
            // making a lone list seed dragged backward advance forward instead of reversing.
            var step = indices.Count >= 2
                ? (int)Math.Round(ComputeLinearFitSlope(indices.Select(i => (double)i).ToList()))
                : 1;
            var lastIndex = plan.Direction is FillDirection.Up or FillDirection.Left ? indices[0] : indices[^1];
            var directedStep = plan.Direction is FillDirection.Up or FillDirection.Left ? -step : step;
            // Note: unlike the single-sample fallback above (which always seeds a nonzero
            // ±1 step so a lone seed value still advances through the list), a genuine 0
            // step computed here from 2+ identical samples is NOT overridden -- Excel's
            // fill handle treats 2+ identical list values the same as 2+ identical
            // numbers/dates: a flat series that copies the value, not one that advances.

            // Excel reproduces the seed's own case style (ALL-CAPS, all-lowercase, or the
            // list's canonical Title Case) rather than always emitting the canonical entry
            // verbatim. Detect each seed's style against its matched canonical entry; when
            // every seed agrees on one style, re-case the generated entries to match. A
            // mixed/inconsistent style (or any seed that isn't upper/lower/Title at all)
            // falls back to the canonical Title-Case text, same as before this fix.
            var caseStyle = DetectUniformCaseStyle(values, indices, list);

            return offset =>
            {
                var index = Mod(lastIndex + directedStep * (int)offset, list.Count);
                return new TextValue(PasteCommandFactory.TruncateToExcelCellTextLimit(ApplyCaseStyle(list[index], caseStyle)));
            };
        }

        return null;
    }

    /// <summary>
    /// Detects the fill-handle's text list series for a single seed value -- a trailing number
    /// (e.g. "Item 1" -&gt; "Item 2"), membership in one of Excel's built-in weekday/month lists,
    /// or membership in one of the supplied <paramref name="customLists"/> (Excel: File ▸
    /// Options ▸ Advanced ▸ Edit Custom Lists) -- without requiring a full
    /// <see cref="AutofillCommand"/> drag operation. Used by
    /// <c>FreeX.App.Presentation.FillSeries.FillSeriesPlanner</c>'s "AutoFill" series type in
    /// Fill ▸ Series so that dialog option replays the exact same detection the fill handle
    /// itself uses, instead of silently routing through the numeric-only Linear builder.
    /// </summary>
    public static Func<int, ScalarValue>? TryCreateAutoFillTextSeries(IReadOnlyList<string> seedValues, IReadOnlyList<IReadOnlyList<string>>? customLists = null)
    {
        var plan = new FillPlan(FillDirection.Down, FillAxis.Vertical);
        return TryCreateTrailingNumberSeries(seedValues, plan)
            ?? TryCreateBuiltInListSeries(seedValues, plan, customLists);
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateAutoFillLists(IReadOnlyList<IReadOnlyList<string>>? customLists)
    {
        foreach (var list in BuiltInLists)
            yield return list;

        if (customLists is null)
            yield break;

        foreach (var list in customLists)
            yield return list;
    }

    private static int IndexOfIgnoreCase(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private enum TextCaseStyle
    {
        /// <summary>The list's own canonical spelling (e.g. "Monday") -- also the fallback
        /// used when the seed(s) don't share one consistent recognizable style.</summary>
        Canonical,
        Upper,
        Lower
    }

    /// <summary>
    /// Classifies <paramref name="value"/> against its matched <paramref name="canonical"/>
    /// list entry as all-uppercase, all-lowercase, an exact canonical match, or -- for
    /// anything else (mixed case, partial caps, etc.) -- falls back to
    /// <see cref="TextCaseStyle.Canonical"/> so the generated series stays canonically spelled.
    /// </summary>
    private static TextCaseStyle DetectCaseStyle(string value, string canonical)
    {
        if (string.Equals(value, canonical, StringComparison.Ordinal))
            return TextCaseStyle.Canonical;
        if (string.Equals(value, canonical.ToUpperInvariant(), StringComparison.Ordinal))
            return TextCaseStyle.Upper;
        if (string.Equals(value, canonical.ToLowerInvariant(), StringComparison.Ordinal))
            return TextCaseStyle.Lower;
        return TextCaseStyle.Canonical;
    }

    /// <summary>
    /// Determines the single case style shared by every seed value (each compared against the
    /// list entry it matched via <paramref name="indices"/>). If any two seeds disagree on
    /// style -- e.g. one all-caps and one Title Case -- there is no one consistent style to
    /// reproduce, so this falls back to <see cref="TextCaseStyle.Canonical"/> (matching FreeX's
    /// pre-existing behavior for that case).
    /// </summary>
    private static TextCaseStyle DetectUniformCaseStyle(IReadOnlyList<string> values, IReadOnlyList<int> indices, IReadOnlyList<string> list)
    {
        var style = DetectCaseStyle(values[0], list[indices[0]]);
        for (var i = 1; i < values.Count; i++)
        {
            if (DetectCaseStyle(values[i], list[indices[i]]) != style)
                return TextCaseStyle.Canonical;
        }

        return style;
    }

    private static string ApplyCaseStyle(string canonicalText, TextCaseStyle style) => style switch
    {
        TextCaseStyle.Upper => canonicalText.ToUpperInvariant(),
        TextCaseStyle.Lower => canonicalText.ToLowerInvariant(),
        _ => canonicalText
    };

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

    /// <param name="Lines">
    /// Per-line (Anchor, Step) pairs, keyed by column (filling down/up) or row (filling
    /// left/right): each line of a rectangular source continues its own independently-fitted
    /// trend rather than sharing one flattened sequence. Anchor is the line's fitted value at
    /// the fill's starting edge (offset 0): for <see cref="TryCreateForcedSingleCellSeries"/> it
    /// is the literal seed cell value, but for <see cref="TryCreateScalarSeries"/> it is the
    /// least-squares regression line's fitted value at the source's edge index (not necessarily
    /// the actual sampled cell value), so that <c>Anchor + Step * offset</c> always lies on the
    /// fitted line. A series with exactly one line (the lone-cell and single-column/row-source
    /// cases) always resolves to that line via <see cref="LineFor"/>'s fallback, regardless of
    /// which key it happens to be stored under.
    /// </param>
    private sealed record ScalarSeries(
        IReadOnlyDictionary<uint, (double Anchor, double Step)> Lines,
        FillAxis Axis,
        Func<double, ScalarValue> CreateValue)
    {
        public (double Anchor, double Step) LineFor(CellAddress addr)
        {
            var key = Axis == FillAxis.Vertical ? addr.Col : addr.Row;
            return Lines.TryGetValue(key, out var line) ? line : Lines.Values.First();
        }
    }

    /// <summary>
    /// Per-line list-series functions, keyed the same way as <see cref="ScalarSeries.Lines"/>.
    /// </summary>
    private sealed record ListSeries(IReadOnlyDictionary<uint, Func<int, ScalarValue>> Lines, FillAxis Axis)
    {
        public Func<int, ScalarValue> LineFor(CellAddress addr)
        {
            var key = Axis == FillAxis.Vertical ? addr.Col : addr.Row;
            return Lines.TryGetValue(key, out var valueAt) ? valueAt : Lines.Values.First();
        }
    }

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
