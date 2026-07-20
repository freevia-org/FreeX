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
    public static IWorkbookCommand CreateCommand(
        Workbook workbook,
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> cells,
        GridRange destinationRange,
        KeyboardInsertDeleteDialogChoice choice,
        bool isCut = false)
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

        var pasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheetId,
            sourceRange,
            cells,
            destinationRange.Start,
            PasteCellsMode.All,
            default);

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
                    new CutMoveFollowUpCommand(sheetId, sourceRange, destinationRange, cells)
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
            IReadOnlyList<(CellAddress Source, Cell Cell)> cutCells)
        {
            _sheetId = sheetId;
            _sourceRange = sourceRange;
            _destinationRange = destinationRange;
            _cutCells = cutCells;
        }

        public CommandOutcome Apply(ICommandContext ctx)
        {
            var sheet = ctx.GetSheet(_sheetId);
            var rowDelta = checked((int)((long)_destinationRange.Start.Row - _sourceRange.Start.Row));
            var colDelta = checked((int)((long)_destinationRange.Start.Col - _sourceRange.Start.Col));

            var affected = new List<CellAddress>();
            if (rowDelta == 0 && colDelta == 0)
                return new CommandOutcome(true, AffectedCells: affected);

            var moveOp = new MoveRangeOp(
                sheet.Name,
                _sourceRange.Start.Row,
                _sourceRange.Start.Col,
                _sourceRange.End.Row,
                _sourceRange.End.Col,
                rowDelta,
                colDelta);

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

                var corrected = FormulaRewriter.Rewrite(originalFormula, moveOp, sheet.Name) ?? originalFormula;
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
            // _destinationRange, already handled above) and the (cleared) _sourceRange are excluded.
            _externalFormulaSnapshot = [];
            foreach (var otherSheet in ctx.Workbook.Sheets)
            {
                foreach (var addr in otherSheet.EnumerateFormulaCells())
                {
                    if (_sourceRange.Contains(addr) || _destinationRange.Contains(addr))
                        continue;

                    var cell = otherSheet.GetCell(addr);
                    if (cell?.FormulaText is null)
                        continue;

                    var rewritten = FormulaRewriter.Rewrite(cell.FormulaText, moveOp, otherSheet.Name);
                    if (rewritten is null)
                        continue;

                    _externalFormulaSnapshot[addr] = cell.FormulaText;
                    cell.FormulaText = rewritten;
                    affected.Add(addr);
                }
            }

            // (3) CF/DV rules scoped entirely to the cut source range follow the move.
            TranslateFullyContainedRules(sheet, rowDelta, colDelta);

            // (4) Detach the vacated source's merge/hyperlink now that paste has already carried them
            // to the destination (PasteMergedRegionsCommand / the paste's hyperlink carry both re-read
            // sheet.MergedRegions/Hyperlinks at THEIR OWN Apply time, which already ran by this point
            // in the composite -- doing this any earlier would starve them of the state they need to
            // recreate the merge/hyperlink at the destination in the first place).
            _removedMergedRegions = sheet.MergedRegions.Where(region => IsFullyContained(region, _sourceRange)).ToList();
            if (_removedMergedRegions.Count > 0)
            {
                sheet.ReplaceMergedRegions(
                    sheet.MergedRegions.Where(region => !_removedMergedRegions.Contains(region)));
            }

            _removedHyperlinks = [];
            _removedHyperlinkMetadata = [];
            foreach (var address in _sourceRange.AllCells())
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

        private void TranslateFullyContainedRules(Sheet sheet, int rowDelta, int colDelta)
        {
            _dvAppliesToSnapshot = [];
            _dvAdditionalSnapshot = [];
            var dvChanged = false;
            foreach (var rule in sheet.DataValidations)
            {
                if (IsFullyContained(rule.AppliesTo, _sourceRange))
                {
                    _dvAppliesToSnapshot.Add((rule, rule.AppliesTo));
                    rule.AppliesTo = Translate(rule.AppliesTo, rowDelta, colDelta);
                    dvChanged = true;
                }

                for (var i = 0; i < rule.AdditionalRanges.Count; i++)
                {
                    if (IsFullyContained(rule.AdditionalRanges[i], _sourceRange))
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
                if (IsFullyContained(rule.AppliesTo, _sourceRange))
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
                        if (IsFullyContained(ar, _sourceRange))
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
