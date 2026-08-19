using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

public static class InsertCopiedCellsPlanner
{
    /// <summary>
    /// Builds the composite command for the "Insert Copied Cells"/"Insert Cut Cells" context-menu
    /// action: shift the existing destination cells out of the way, then paste the captured
    /// clipboard cells into the freed space.
    /// </summary>
    /// <param name="isCut">
    /// <c>true</c> when the clipboard content was cut (not copied). Excel's "Insert Cut Cells" MOVES
    /// the data: after the shifted-in paste lands, the original source range must be cleared or the
    /// data is silently duplicated instead of moved (R29-undo-redo-remaining-deep-1). Defaults to
    /// <c>false</c> (plain copy semantics, matching the pre-existing behavior) for callers that don't
    /// yet distinguish cut from copy.
    /// </param>
    /// <param name="sourceAreas">
    /// R110-insert-copied-cells-multiarea-1: every individually Ctrl+clicked area of a multi-area
    /// source selection (mirrors <c>InternalClipboard.SourceAreas</c>/the r108 fix to the plain
    /// Ctrl+V path). Forwarded into <see cref="PasteCommandFactory.CreateInternalPasteCommand"/> so
    /// its CF/DV carry restricts itself to the ACTUAL copied areas instead of treating the whole
    /// bounding box -- including the untouched gap between disjoint areas -- as copied. Defaults to
    /// <c>null</c> (single contiguous area, matching the pre-existing behavior) for callers that
    /// don't yet distinguish multi-area from single-area selections.
    /// </param>
    /// <param name="sourceSheetOverride">
    /// R146-insert-copied-cells-hyperlink-1: mirrors the identical parameter on
    /// <see cref="PasteCommandFactory.CreateInternalPasteCommand"/> (external-refs-F1) -- forwarded
    /// into that factory call below so a cross-window "Insert Copied Cells"/"Insert Cut Cells" (the
    /// copy happened in a different open FreeX window than the one receiving the insert) still
    /// resolves the source Sheet for hyperlink/rich-text-run/merged-region/comment/CF carry instead
    /// of silently missing via <c>workbook.GetSheet(sourceRange.Start.Sheet)</c>, which can only ever
    /// resolve against the DESTINATION workbook. Callers that don't have one (or know source and
    /// destination share one workbook, the common case) pass null/omit it, which preserves the prior
    /// lookup behavior exactly.
    /// </param>
    public static IWorkbookCommand CreateCommand(
        Workbook workbook,
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> cells,
        GridRange destinationRange,
        KeyboardInsertDeleteDialogChoice choice,
        bool isCut = false,
        IReadOnlyList<GridRange>? sourceAreas = null,
        Sheet? sourceSheetOverride = null)
    {
        var insertRange = CreateInsertRange(sheetId, destinationRange.Start, sourceRange);
        IWorkbookCommand insertCommand = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftDown => new InsertCellsCommand(
                sheetId,
                insertRange,
                InsertCellsShiftDirection.Down),
            KeyboardInsertDeleteDialogChoice.EntireRow => new InsertRowsCommand(
                sheetId,
                destinationRange.Start.Row,
                sourceRange.RowCount),
            KeyboardInsertDeleteDialogChoice.EntireColumn => new InsertColumnsCommand(
                sheetId,
                destinationRange.Start.Col,
                sourceRange.ColCount),
            _ => new InsertCellsCommand(
                sheetId,
                insertRange,
                InsertCellsShiftDirection.Right)
        };

        // R54-meta-1: describes exactly which live workbook state `insertCommand` (above) will have
        // already relocated by the time CutMoveFollowUpCommand runs (it is always the LAST command in
        // the composite), so the follow-up can translate its captured _sourceRange to match wherever
        // the insert's shift actually left the (blanked) source cells, rather than scanning for
        // references/rules/merges at the ORIGINAL pre-insert coordinates the insert already moved.
        var postInsertShift = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftDown => new PostInsertShift(
                IsRowAxis: true, Threshold: insertRange.Start.Row, Amount: sourceRange.RowCount,
                BandStart: insertRange.Start.Col, BandEnd: insertRange.End.Col),
            KeyboardInsertDeleteDialogChoice.EntireRow => new PostInsertShift(
                IsRowAxis: true, Threshold: destinationRange.Start.Row, Amount: sourceRange.RowCount,
                BandStart: 0u, BandEnd: CellAddress.MaxCol),
            KeyboardInsertDeleteDialogChoice.EntireColumn => new PostInsertShift(
                IsRowAxis: false, Threshold: destinationRange.Start.Col, Amount: sourceRange.ColCount,
                BandStart: 0u, BandEnd: CellAddress.MaxRow),
            _ => new PostInsertShift(
                IsRowAxis: false, Threshold: insertRange.Start.Col, Amount: sourceRange.ColCount,
                BandStart: insertRange.Start.Row, BandEnd: insertRange.End.Row),
        };

        // R146-insert-copied-cells-hyperlink-1: call the GridRange-destination overload directly
        // (rather than the CellAddress-destination convenience overload used before) because only
        // the GridRange overload exposes sourceSheetOverride -- new GridRange(destination, destination)
        // below is exactly what the CellAddress overload builds internally, so the destination passed
        // to the paste is unchanged.
        var pasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheetId,
            sourceRange,
            cells,
            new GridRange(destinationRange.Start, destinationRange.Start),
            PasteCellsMode.All,
            default,
            sourceAreas,
            mergeConditionalFormats: false,
            sourceSheetOverride: sourceSheetOverride);

        // Unlike the ordinary Ctrl+V-after-Cut path (ClipboardPastePlanner.ShouldClearCutSourceAfterPaste),
        // this composite always clears the source when isCut is true, with no overlap guard: the clear
        // below runs BEFORE the insert/paste and targets the pre-shift (original) source coordinates, so
        // it can never collide with where the pasted cells land -- overlap is only a hazard for the
        // in-place overwrite paste that guard was written for.
        if (isCut)
        {
            // The clear runs BEFORE the insert/paste (not after, unlike the ordinary paste-after-cut
            // composite) because an EntireRow/EntireColumn insert shifts every cell at/after the
            // insertion line -- including the source range itself when it sits at or past that line.
            // Clearing first always targets the pre-shift (original) coordinates; clearing last would
            // target stale coordinates once the shift has moved the real data elsewhere.
            //
            // NOTE: the clear below intentionally keeps isCutSource at its default (false) -- passing
            // true here would remove the source's merge/hyperlink BEFORE pasteCommand runs, but
            // PasteMergedRegionsCommand/the paste's hyperlink carry both re-read the CURRENT sheet
            // state at their own Apply time to decide what to recreate at the destination, so an
            // early removal would starve them of the very state they need and the destination would
            // never receive the merge/hyperlink at all. CutMoveFollowUpCommand (below, runs LAST)
            // detaches the vacated source's merge/hyperlink only AFTER paste has already carried them
            // to the destination (R53-commands-insert-copied-cut-cells-3-2).
            return new CompositeWorkbookCommand(
                "Insert Cut Cells",
                [
                    new ClearContentsCommand(sourceRange.Start.Sheet, sourceRange),
                    insertCommand,
                    pasteCommand,
                    // R53-commands-insert-copied-cut-cells-3-1/3-2/3-3: the paste above is the plain
                    // copy-paste machinery (correct for Insert Copied Cells), which (a) always applies
                    // a blanket relative-offset rewrite to every pasted formula's own references --
                    // wrong for a MOVE, where only references to cells that moved along with the
                    // selection should shift, and everything else must stay literal -- (b) never
                    // repoints OTHER cells' formulas that reference the cut range, nor CF/DV rules
                    // scoped to it, and (c) never detaches the vacated source's merge/hyperlink (which
                    // the paste half independently duplicated at the destination). This follow-up
                    // corrects all three, mirroring the true-move semantics MoveRangeCommand already
                    // applies for the ordinary Ctrl+X/Ctrl+V path.
                    new CutMoveFollowUpCommand(sheetId, sourceRange, destinationRange, cells, postInsertShift)
                ]);
        }

        return new CompositeWorkbookCommand("Insert Copied Cells", [insertCommand, pasteCommand]);
    }

    private static GridRange CreateInsertRange(SheetId sheetId, CellAddress destination, GridRange sourceRange)
    {
        var end = new CellAddress(
            sheetId,
            destination.Row + sourceRange.RowCount - 1,
            destination.Col + sourceRange.ColCount - 1);
        return new GridRange(destination, end);
    }

    /// <summary>
    /// Describes the row/column shift <c>insertCommand</c> (the sibling command that runs BEFORE
    /// <see cref="CutMoveFollowUpCommand"/> in the same composite) applies to any live workbook state
    /// -- cell references, CF/DV rule ranges, merges, hyperlinks -- that sits at/after
    /// <see cref="Threshold"/> along the shifted axis and within [<see cref="BandStart"/>..<see cref="BandEnd"/>]
    /// on the other axis (R54-meta-1).
    /// </summary>
    private readonly record struct PostInsertShift(bool IsRowAxis, uint Threshold, uint Amount, uint BandStart, uint BandEnd);

    /// <summary>
    /// Translates <paramref name="range"/> by <paramref name="shift"/> if it falls entirely at/after
    /// the shift's threshold and within its band -- i.e. if it is exactly the sort of range the
    /// insert command would already have relocated -- otherwise returns it unchanged.
    /// </summary>
    private static GridRange AdjustForInsertShift(GridRange range, PostInsertShift shift)
    {
        if (shift.IsRowAxis)
        {
            if (range.Start.Col < shift.BandStart || range.End.Col > shift.BandEnd)
                return range;
            if (range.Start.Row < shift.Threshold)
                return range;
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row + shift.Amount, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row + shift.Amount, range.End.Col));
        }

        if (range.Start.Row < shift.BandStart || range.End.Row > shift.BandEnd)
            return range;
        if (range.Start.Col < shift.Threshold)
            return range;
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col + shift.Amount),
            new CellAddress(range.End.Sheet, range.End.Row, range.End.Col + shift.Amount));
    }

    /// <summary>
    /// Runs after the clear+insert+paste half of an "Insert Cut Cells" composite and corrects the
    /// three ways that plain copy-paste machinery diverges from a true Excel move
    /// (R53-commands-insert-copied-cut-cells-3-1/3-3):
    /// <list type="number">
    /// <item>Each pasted formula's OWN references: the paste applied a blanket relative-offset shift
    /// (correct for a copy); this recomputes the formula from its original pre-cut text using move
    /// semantics instead -- only references to cells that moved along with the selection shift, every
    /// other reference stays exactly as written.</item>
    /// <item>External references: any OTHER formula anywhere in the workbook that pointed at a cut
    /// cell is repointed to follow it to the destination, mirroring
    /// <c>MoveRangeCommand</c>'s workbook-wide formula rewrite.</item>
    /// <item>CF/DV rules whose AppliesTo/AdditionalRanges are fully contained in the cut source range
    /// are translated to the destination, mirroring <c>MoveRangeCommand.TranslateFullyContainedRules</c>.</item>
    /// </list>
    /// </summary>
    private sealed class CutMoveFollowUpCommand : IWorkbookCommand
    {
        private readonly SheetId _sheetId;
        private readonly GridRange _sourceRange;
        private readonly GridRange _destinationRange;
        private readonly IReadOnlyList<(CellAddress Source, Cell Cell)> _cutCells;
        private readonly PostInsertShift _postInsertShift;

        private Dictionary<CellAddress, string?>? _destinationFormulaSnapshot;
        private Dictionary<CellAddress, string>? _externalFormulaSnapshot;
        private List<(DataValidation Rule, GridRange OriginalAppliesTo)>? _dvAppliesToSnapshot;
        private List<(DataValidation Rule, int Index, GridRange OriginalRange)>? _dvAdditionalSnapshot;
        private List<(ConditionalFormat Rule, GridRange OriginalAppliesTo)>? _cfAppliesToSnapshot;
        private List<(ConditionalFormat Rule, IReadOnlyList<GridRange> OriginalAdditionalRanges)>? _cfAdditionalSnapshot;
        private List<GridRange>? _removedMergedRegions;
        private Dictionary<CellAddress, string>? _removedHyperlinks;
        private Dictionary<CellAddress, HyperlinkMetadata>? _removedHyperlinkMetadata;

        public string Label => "Insert Cut Cells";

        public CutMoveFollowUpCommand(
            SheetId sheetId,
            GridRange sourceRange,
            GridRange destinationRange,
            IReadOnlyList<(CellAddress Source, Cell Cell)> cutCells,
            PostInsertShift postInsertShift)
        {
            _sheetId = sheetId;
            _sourceRange = sourceRange;
            _destinationRange = destinationRange;
            _cutCells = cutCells;
            _postInsertShift = postInsertShift;
        }

        public CommandOutcome Apply(ICommandContext ctx)
        {
            var sheet = ctx.GetSheet(_sheetId);
            var rowDelta = checked((int)((long)_destinationRange.Start.Row - _sourceRange.Start.Row));
            var colDelta = checked((int)((long)_destinationRange.Start.Col - _sourceRange.Start.Col));

            var affected = new List<CellAddress>();
            if (rowDelta == 0 && colDelta == 0)
                return new CommandOutcome(true, AffectedCells: affected);

            // R54-meta-1: `insertCommand` (in the composite built by CreateCommand) runs BEFORE this
            // follow-up and, if its shift band overlapped _sourceRange, has already relocated every
            // live reference/rule/merge/hyperlink that pointed at (or lived inside) it to a NEW
            // location -- a whole-row/column (or band-scoped) insert rewrites EVERY workbook formula
            // referencing that region, whether or not it has anything to do with this cut/paste.
            // `effectiveSourceRange` is where the (blanked) source cells -- and anything that pointed
            // at them -- now actually live; it equals _sourceRange unchanged whenever the insert's
            // shift band did not overlap it (the ordinary case exercised by the pre-existing tests).
            var effectiveSourceRange = AdjustForInsertShift(_sourceRange, _postInsertShift);

            // Own-formula fixup (below) must use the ORIGINAL, pre-insert _sourceRange: `originalFormula`
            // is the pre-cut captured text, whose own references (including any pointing at sibling
            // cells still inside the selection) were written against the PRE-insert coordinate system.
            var ownMoveOp = new MoveRangeOp(
                sheet.Name,
                _sourceRange.Start.Row,
                _sourceRange.Start.Col,
                _sourceRange.End.Row,
                _sourceRange.End.Col,
                rowDelta,
                colDelta);

            // External-reference repoint (below) scans CURRENT (post-insert) formula text elsewhere in
            // the workbook, so it must match against the POST-insert (effective) source rectangle, with
            // the delta recomputed from that same effective rectangle to the (unaffected) destination.
            var externalRowDelta = checked((int)((long)_destinationRange.Start.Row - effectiveSourceRange.Start.Row));
            var externalColDelta = checked((int)((long)_destinationRange.Start.Col - effectiveSourceRange.Start.Col));
            var externalMoveOp = new MoveRangeOp(
                sheet.Name,
                effectiveSourceRange.Start.Row,
                effectiveSourceRange.Start.Col,
                effectiveSourceRange.End.Row,
                effectiveSourceRange.End.Col,
                externalRowDelta,
                externalColDelta);

            // (1) Own-formula fixup: recompute each pasted formula from its ORIGINAL (pre-cut)
            // captured text using move semantics, overwriting whatever the blanket-offset paste wrote.
            _destinationFormulaSnapshot = [];
            foreach (var (source, originalCell) in _cutCells)
            {
                if (originalCell.FormulaText is not { } originalFormula)
                    continue;

                var destAddress = new CellAddress(
                    _destinationRange.Start.Sheet,
                    checked((uint)((long)source.Row + rowDelta)),
                    checked((uint)((long)source.Col + colDelta)));

                var destCell = sheet.GetCell(destAddress);
                if (destCell is null)
                    continue;

                var corrected = FormulaRewriter.Rewrite(originalFormula, ownMoveOp, sheet.Name) ?? originalFormula;
                if (destCell.FormulaText == corrected)
                    continue;

                _destinationFormulaSnapshot[destAddress] = destCell.FormulaText;
                var updated = destCell.Clone();
                updated.FormulaText = corrected;
                sheet.SetCell(destAddress, updated);
                affected.Add(destAddress);
            }

            // (2) External-reference repoint: any OTHER formula in the workbook that referenced a cut
            // cell must follow it to the destination. The moved cells themselves (now living in
            // _destinationRange, already handled above) and the (cleared, possibly insert-shifted)
            // effectiveSourceRange are excluded.
            _externalFormulaSnapshot = [];
            foreach (var otherSheet in ctx.Workbook.Sheets)
            {
                foreach (var addr in otherSheet.EnumerateFormulaCells())
                {
                    if (effectiveSourceRange.Contains(addr) || _destinationRange.Contains(addr))
                        continue;

                    var cell = otherSheet.GetCell(addr);
                    if (cell?.FormulaText is null)
                        continue;

                    var rewritten = FormulaRewriter.Rewrite(cell.FormulaText, externalMoveOp, otherSheet.Name);
                    if (rewritten is null)
                        continue;

                    _externalFormulaSnapshot[addr] = cell.FormulaText;
                    cell.FormulaText = rewritten;
                    affected.Add(addr);
                }
            }

            // (3) CF/DV rules scoped entirely to the cut source range follow the move. Rule ranges are
            // CURRENT (post-insert) state too, so match against effectiveSourceRange and translate by
            // the same external delta used for external formulas above.
            TranslateFullyContainedRules(sheet, externalRowDelta, externalColDelta, effectiveSourceRange);

            // (4) Detach the vacated source's merge/hyperlink now that paste has already carried them
            // to the destination (PasteMergedRegionsCommand / the paste's hyperlink carry both re-read
            // sheet.MergedRegions/Hyperlinks at THEIR OWN Apply time, which already ran by this point
            // in the composite -- doing this any earlier would starve them of the state they need to
            // recreate the merge/hyperlink at the destination in the first place). sheet.MergedRegions/
            // Hyperlinks are current (post-insert) state, so use effectiveSourceRange here too.
            _removedMergedRegions = sheet.MergedRegions.Where(region => IsFullyContained(region, effectiveSourceRange)).ToList();
            if (_removedMergedRegions.Count > 0)
            {
                sheet.ReplaceMergedRegions(
                    sheet.MergedRegions.Where(region => !_removedMergedRegions.Contains(region)));
            }

            _removedHyperlinks = [];
            _removedHyperlinkMetadata = [];
            foreach (var address in effectiveSourceRange.AllCells())
            {
                if (sheet.Hyperlinks.TryGetValue(address, out var link))
                {
                    _removedHyperlinks[address] = link;
                    sheet.Hyperlinks.Remove(address);
                    affected.Add(address);
                }

                if (sheet.HyperlinkMetadata.TryGetValue(address, out var metadata))
                {
                    _removedHyperlinkMetadata[address] = metadata;
                    sheet.HyperlinkMetadata.Remove(address);
                }
            }

            return new CommandOutcome(true, AffectedCells: affected);
        }

        public void Revert(ICommandContext ctx)
        {
            var sheet = ctx.GetSheet(_sheetId);

            if (_removedHyperlinks is not null)
            {
                foreach (var (address, link) in _removedHyperlinks)
                    sheet.Hyperlinks[address] = link;
            }

            if (_removedHyperlinkMetadata is not null)
            {
                foreach (var (address, metadata) in _removedHyperlinkMetadata)
                    sheet.HyperlinkMetadata[address] = metadata;
            }

            if (_removedMergedRegions is not null && _removedMergedRegions.Count > 0)
            {
                foreach (var region in _removedMergedRegions)
                    sheet.AddMergedRegion(region);
            }

            RestoreRules();

            if (_externalFormulaSnapshot is not null)
            {
                foreach (var (addr, original) in _externalFormulaSnapshot)
                {
                    var cell = ctx.Workbook.GetSheet(addr.Sheet)?.GetCell(addr);
                    if (cell is not null)
                        cell.FormulaText = original;
                }
            }

            if (_destinationFormulaSnapshot is not null)
            {
                foreach (var (addr, original) in _destinationFormulaSnapshot)
                {
                    var cell = sheet.GetCell(addr);
                    if (cell is null)
                        continue;
                    var reverted = cell.Clone();
                    reverted.FormulaText = original;
                    sheet.SetCell(addr, reverted);
                }
            }
        }

        private void TranslateFullyContainedRules(Sheet sheet, int rowDelta, int colDelta, GridRange sourceContainer)
        {
            _dvAppliesToSnapshot = [];
            _dvAdditionalSnapshot = [];
            var dvChanged = false;
            foreach (var rule in sheet.DataValidations)
            {
                if (IsFullyContained(rule.AppliesTo, sourceContainer))
                {
                    _dvAppliesToSnapshot.Add((rule, rule.AppliesTo));
                    rule.AppliesTo = Translate(rule.AppliesTo, rowDelta, colDelta);
                    dvChanged = true;
                }

                for (var i = 0; i < rule.AdditionalRanges.Count; i++)
                {
                    if (IsFullyContained(rule.AdditionalRanges[i], sourceContainer))
                    {
                        _dvAdditionalSnapshot.Add((rule, i, rule.AdditionalRanges[i]));
                        rule.AdditionalRanges[i] = Translate(rule.AdditionalRanges[i], rowDelta, colDelta);
                        dvChanged = true;
                    }
                }
            }

            if (dvChanged)
                sheet.DataValidations.NotifyRulesChanged();

            _cfAppliesToSnapshot = [];
            _cfAdditionalSnapshot = [];
            var cfChanged = false;
            foreach (var rule in sheet.ConditionalFormats)
            {
                if (IsFullyContained(rule.AppliesTo, sourceContainer))
                {
                    _cfAppliesToSnapshot.Add((rule, rule.AppliesTo));
                    rule.AppliesTo = Translate(rule.AppliesTo, rowDelta, colDelta);
                    cfChanged = true;
                }

                if (rule.AdditionalRanges is { Count: > 0 } additional)
                {
                    var result = new List<GridRange>(additional.Count);
                    var anyChanged = false;
                    foreach (var ar in additional)
                    {
                        if (IsFullyContained(ar, sourceContainer))
                        {
                            result.Add(Translate(ar, rowDelta, colDelta));
                            anyChanged = true;
                        }
                        else
                        {
                            result.Add(ar);
                        }
                    }

                    if (anyChanged)
                    {
                        _cfAdditionalSnapshot.Add((rule, additional));
                        rule.AdditionalRanges = result;
                        cfChanged = true;
                    }
                }
            }

            if (cfChanged)
                sheet.ConditionalFormats.NotifyRulesChanged();
        }

        private void RestoreRules()
        {
            if (_dvAppliesToSnapshot is not null)
            {
                foreach (var (rule, original) in _dvAppliesToSnapshot)
                    rule.AppliesTo = original;
            }

            if (_dvAdditionalSnapshot is not null)
            {
                foreach (var (rule, index, original) in _dvAdditionalSnapshot)
                    rule.AdditionalRanges[index] = original;
            }

            if (_cfAppliesToSnapshot is not null)
            {
                foreach (var (rule, original) in _cfAppliesToSnapshot)
                    rule.AppliesTo = original;
            }

            if (_cfAdditionalSnapshot is not null)
            {
                foreach (var (rule, original) in _cfAdditionalSnapshot)
                    rule.AdditionalRanges = original;
            }
        }

        private static bool IsFullyContained(GridRange candidate, GridRange container) =>
            candidate.Start.Row >= container.Start.Row &&
            candidate.Start.Col >= container.Start.Col &&
            candidate.End.Row <= container.End.Row &&
            candidate.End.Col <= container.End.Col;

        private static GridRange Translate(GridRange range, int rowDelta, int colDelta) =>
            new(
                new CellAddress(range.Start.Sheet, (uint)(range.Start.Row + rowDelta), (uint)(range.Start.Col + colDelta)),
                new CellAddress(range.End.Sheet, (uint)(range.End.Row + rowDelta), (uint)(range.End.Col + colDelta)));
    }
}
