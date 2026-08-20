using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class MoveRangeCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: _snapshot below is only the base per-cell record (Cell
    // clone + style), but Apply also captures a companion Dictionary snapshot (comment, author,
    // shown-flag, threaded comment, hyperlink, hyperlink metadata, rich text, phonetic guide,
    // sparkline) keyed by the SAME affected-cell set -- so the command's true retained footprint
    // per cell is comparable to CopyRangeCommand/PasteCellsCommand's single richer record, just
    // spread across more collections. Estimated from _snapshot.Count (the affected-cell count,
    // known only once Apply has captured it) so a large cut+paste-drag or Fill-via-cut actually
    // counts against CommandBus's 50 MB undo byte-budget instead of the flat 200-byte
    // IEstimatesMemory default. _snapshot is null before Apply runs, in which case CommandBus never
    // actually queries this (EstimateBytes is only called after Apply pushes the command).
    private const int BytesPerCell = 400;

    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private IReadOnlyList<CellAddress> _payloadAffectedCells = [];
    private List<CellSnapshot>? _snapshot;
    private Dictionary<CellAddress, string>? _formulaSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
    // author + pinned/"Show Comment" state) and must move with a cell's comment, or a note's
    // author/pinned box is left behind at the source address.
    private Dictionary<CellAddress, string>? _commentAuthorsSnapshot;
    private HashSet<CellAddress>? _shownCommentsSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRunsSnapshot;
    private Dictionary<CellAddress, CellPhoneticGuide>? _phoneticGuideSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _conditionalFormatSnapshot;
    private Dictionary<Guid, string?>? _cfFormulaSnapshot;
    private Dictionary<(Guid Id, int Slot), string?>? _cfThresholdSnapshot;
    private Dictionary<(Guid Id, int Slot), string?>? _dvFormulaSnapshot;
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    // R32-commands-clipboard-deep-1: snapshot of sheet.MergedRegions before Apply relocates any
    // merge that is fully contained in the moved _sourceRange (i.e. the merge(s) being moved along
    // with the selection), so Revert can restore the original merge geometry at the source.
    private List<GridRange>? _mergedRegionsSnapshot;
    // R38-commands-cut-move-2-1: companion snapshot of the DESTINATION sheet's MergedRegions,
    // populated only for a cross-sheet move (when a merge relocates onto a different sheet than
    // it started on), so Revert can restore the destination sheet's original merge list too.
    private List<GridRange>? _destMergedRegionsSnapshot;
    // R38-commands-cut-move-2-2: snapshots of each sheet's StructuredTables list before Apply
    // relocates any table whose Range is fully contained in the moved _sourceRange, so a cut+paste
    // of an entire table keeps Table[Column] structured references resolving against the moved
    // data instead of the now-blank source cells. _destTablesSnapshot is populated only for a
    // cross-sheet move (source and destination are different Sheet instances).
    private List<StructuredTableModel>? _sourceTablesSnapshot;
    private List<StructuredTableModel>? _destTablesSnapshot;
    // R16-structural-edit-shift-sweep-1/2/3 + R16-chart-datasource-editing-2: a plain (non-verbatim)
    // chart.DataRange, workbook/sheet-scoped defined names, and a moved cell's sparkline are all
    // address-bearing state that a Cut+Paste move must relocate along with the cells themselves —
    // otherwise they keep pointing at the now-vacated source range/cell. Verbatim series formulas
    // are already handled above by _chartVerbatimSnapshot/RewriteChartVerbatimFormulas; these three
    // cover the remaining plain-range/address cases that formula rewriting does not touch.
    // R76-commands-cut-move-4-1: named ranges (both workbook- and sheet-scoped) and plain chart/
    // sparkline DataRange ARE now re-anchored to the destination sheet for a cross-sheet move too
    // (see TranslateFullyContainedNamedRanges/-ChartDataRanges/-SparklineDataRanges, which now take
    // the destination sheet explicitly) -- Excel migrates these across sheets, and each one only
    // carries a self-contained GridRange rather than living in a per-sheet collection, so re-homing
    // the range's Sheet in place is enough; no object needs to move between sheets' collections.
    // Conditional-format and data-validation rules remain a documented residual for the cross-sheet
    // case (see the isCrossSheet branch in Apply): unlike the state above, DV/CF rules live in each
    // Sheet's own DataValidations/ConditionalFormats collection, so migrating one across sheets would
    // require removing it from the source sheet's list and adding it to the destination's -- and
    // reversing that same cross-collection move again on Revert -- which is a materially larger,
    // structurally different change from the in-place range translation the rest of this file does.
    private List<RowColumnShiftHelpers.ChartDataRangeWorkbookSnapshot>? _chartDataRangeSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private Dictionary<CellAddress, SparklineModel>? _sparklineSnapshot;
    // R24-sparklines-1: a sparkline hosted OUTSIDE the moved range whose DataRange is fully
    // contained IN it (e.g. a sparkline anchored at F1 plotting A1:D1, when A1:D1 is cut and pasted
    // elsewhere) must have its DataRange relocated too, mirroring _chartDataRangeSnapshot/
    // TranslateFullyContainedChartDataRanges for ChartModel.DataRange. Sparklines whose own Location
    // falls inside the moved range are excluded here (handled instead via CaptureSourcePayloads/
    // CloneSparklineAt, which translates DataRange too -- R25-meta-3 -- when it is also fully
    // contained in the moved range, i.e. the sparkline and its data move together).
    private List<(SparklineModel Sparkline, GridRange OriginalDataRange)>? _sparklineDataRangeSnapshot;
    // R21-undo-redo-deep-2: pairs each relocated spill's original source anchor with its captured
    // payload (Apply already carries the destination Target alongside it) so Revert can re-establish
    // the spill back at the source once RestoreCellSnapshot has put the anchor's formula cell back —
    // mirroring the sibling fix already applied on Apply (R20-array-dynamic-spill-1).
    private List<(CellAddress Source, CellAddress Target, RangeValue Payload)>? _spillRelocations;

    public string Label => "Move Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes => _snapshot is null
        ? 0
        : (int)Math.Min((long)_snapshot.Count * BytesPerCell, int.MaxValue);

    public MoveRangeCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        // R38-commands-cut-move-2-1: the destination may now be on a DIFFERENT sheet than the
        // source (a cross-sheet Cut+Paste). The source range itself must still be self-consistent
        // (both endpoints on the constructor's sheet); only the destination sheet is allowed to
        // differ.
        if (_sourceRange.Start.Sheet != _sheetId || _sourceRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Move source range must be on the constructor's sheet.");

        var isCrossSheet = _destination.Sheet != _sheetId;

        if (!WorksheetBounds.IsValidAddress(_sourceRange.Start) ||
            !WorksheetBounds.IsValidAddress(_sourceRange.End) ||
            !WorksheetBounds.IsValidAddress(_destination))
        {
            return new CommandOutcome(false, "Move range is outside the worksheet bounds.");
        }

        if (!WorksheetBounds.TryGetRectangleEnd(
                _destination,
                _sourceRange.RowCount,
                _sourceRange.ColCount,
                out var targetEnd))
        {
            return new CommandOutcome(false, "Move destination range is outside the worksheet bounds.");
        }

        var targetRange = new GridRange(_destination, targetEnd);
        if (!isCrossSheet && targetRange == _sourceRange)
        {
            _affectedCells = [];
            _payloadAffectedCells = [];
            _snapshot = [];
            _formulaSnapshot = [];
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        var sourceSheet = ctx.GetSheet(_sheetId);
        Sheet destSheet;
        if (isCrossSheet)
        {
            var resolvedDestSheet = ctx.Workbook.GetSheet(_destination.Sheet);
            if (resolvedDestSheet is null)
                return new CommandOutcome(false, "Move destination sheet was not found.");
            destSheet = resolvedDestSheet;
        }
        else
        {
            destSheet = sourceSheet;
        }

        Sheet ResolveSheet(SheetId id) => id == _sheetId ? sourceSheet : destSheet;

        // R32-commands-clipboard-deep-1: a merge fully contained in _sourceRange is the merge being
        // moved (it relocates along with the selection below), not a collision -- excluding it here
        // is what lets Excel's ordinary "cut a merged cell, paste to an empty destination" gesture
        // succeed instead of being rejected against its own source-range self-overlap. Any OTHER
        // merge (only partially overlapping the source, or sitting anywhere in the target) is still
        // a real collision and remains rejected.
        var movingMerges = sourceSheet.MergedRegions.Where(range => _sourceRange.Contains(range)).ToList();
        var mergeCollision =
            sourceSheet.MergedRegions.Any(range => !movingMerges.Contains(range) && _sourceRange.Overlaps(range)) ||
            destSheet.MergedRegions.Any(range => !movingMerges.Contains(range) && targetRange.Overlaps(range));
        if (mergeCollision)
            return new CommandOutcome(false, "Cannot move a range that intersects merged cells.");

        // R38-commands-cut-move-2-2: a structured table fully contained in _sourceRange moves along
        // with the selection (its Range is relocated below); a table only partially overlapping the
        // moved range can't be split any more than a merge can, and a table already sitting at the
        // destination is a real collision.
        var movingTables = sourceSheet.StructuredTables.Where(table => _sourceRange.Contains(table.Range)).ToList();
        var partialOverlapTable = sourceSheet.StructuredTables
            .FirstOrDefault(table => !movingTables.Contains(table) && _sourceRange.Overlaps(table.Range));
        if (partialOverlapTable is not null)
            return new CommandOutcome(false, "Cannot move a range that intersects part of a table.");
        var tableCollision = destSheet.StructuredTables
            .Any(table => !movingTables.Contains(table) && targetRange.Overlaps(table.Range));
        if (tableCollision)
            return new CommandOutcome(false, "Cannot move a range that intersects a table.");

        var affected = CreateAffectedCellList(_sourceRange, targetRange);
        var sourceCells = _sourceRange.AllCells().ToList();
        var targetCells = targetRange.AllCells().ToList();

        if (!isCrossSheet)
        {
            if (sourceSheet.IsProtected)
            {
                foreach (var address in affected)
                {
                    if (!CommandGuards.CanEditCell(ctx.Workbook, sourceSheet, address))
                        return CommandGuards.RejectSheetProtected();
                }

                if (HasComments(sourceSheet, affected) &&
                    !sourceSheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditObjects))
                {
                    return CommandGuards.RejectSheetProtected();
                }
            }
        }
        else
        {
            if (sourceSheet.IsProtected)
            {
                foreach (var address in sourceCells)
                {
                    if (!CommandGuards.CanEditCell(ctx.Workbook, sourceSheet, address))
                        return CommandGuards.RejectSheetProtected();
                }

                if (HasComments(sourceSheet, sourceCells) &&
                    !sourceSheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditObjects))
                {
                    return CommandGuards.RejectSheetProtected();
                }
            }

            if (destSheet.IsProtected)
            {
                foreach (var address in targetCells)
                {
                    if (!CommandGuards.CanEditCell(ctx.Workbook, destSheet, address))
                        return CommandGuards.RejectSheetProtected();
                }

                if (HasComments(destSheet, targetCells) &&
                    !destSheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditObjects))
                {
                    return CommandGuards.RejectSheetProtected();
                }
            }
        }

        // R20-array-dynamic-spill-3: every other cell-mutating command (Copy, Paste, Autofill,
        // ClearContents, Fill, ...) rejects an edit that would touch only PART of a dynamic-array/CSE
        // spill ("You cannot change part of an array"); Move omitted this guard, silently discarding
        // a move of just a non-anchor spill member instead of rejecting it like Excel does.
        //
        // R25-spill-dynamic-deep-1: moving ONLY a live spill's anchor cell (source range is exactly
        // that one cell) is legitimate -- Excel lets a spilled array's anchor be cut/moved on its own,
        // and CaptureSourceSpillPayloads/SetSpillRange below relocates the live spill along with it.
        // RejectIfSplitsArray otherwise treats the anchor like any other array member and requires
        // every body cell to already be part of the move, which would wrongly reject this case, so
        // exclude just the anchor's own address from the check. A source range that includes the
        // anchor alongside only SOME (not all) of the body, or a non-anchor member alone, still fails
        // this narrow condition and falls through to the normal (correctly rejecting) check.
        if (!isCrossSheet)
        {
            var guardAddresses = affected;
            if (_sourceRange.CellCount == 1 &&
                sourceSheet.TryGetSpillExtent(_sourceRange.Start, out var anchorSpillRows, out var anchorSpillCols) &&
                (anchorSpillRows > 1 || anchorSpillCols > 1))
            {
                guardAddresses = affected.Where(address => address != _sourceRange.Start).ToList();
            }

            if (CommandGuards.RejectIfSplitsArray(sourceSheet, guardAddresses) is { } splitsArrayRejection)
                return splitsArrayRejection;
        }
        else
        {
            // A spill lives entirely within one sheet, so (unlike the same-sheet case above) the
            // source and destination checks can never see opposite halves of the same spill --
            // checking each sheet's affected cells separately is exact here, not an approximation.
            var sourceGuardAddresses = sourceCells;
            if (_sourceRange.CellCount == 1 &&
                sourceSheet.TryGetSpillExtent(_sourceRange.Start, out var anchorSpillRows, out var anchorSpillCols) &&
                (anchorSpillRows > 1 || anchorSpillCols > 1))
            {
                sourceGuardAddresses = sourceCells.Where(address => address != _sourceRange.Start).ToList();
            }

            if (CommandGuards.RejectIfSplitsArray(sourceSheet, sourceGuardAddresses) is { } sourceSplitRejection)
                return sourceSplitRejection;
            if (CommandGuards.RejectIfSplitsArray(destSheet, targetCells) is { } destSplitRejection)
                return destSplitRejection;
        }

        _snapshot = CaptureCellSnapshots(ResolveSheet, affected);
        _commentSnapshot = CaptureDictionary(ResolveSheet, static s => s.Comments, affected);
        _commentAuthorsSnapshot = CaptureDictionary(ResolveSheet, static s => s.CommentAuthors, affected);
        _shownCommentsSnapshot = CaptureAddressSet(ResolveSheet, static s => s.ShownComments, affected);
        _threadedCommentSnapshot = CaptureDictionary(ResolveSheet, static s => s.ThreadedComments, affected);
        _hyperlinkSnapshot = CaptureDictionary(ResolveSheet, static s => s.Hyperlinks, affected);
        _hyperlinkMetadataSnapshot = CaptureDictionary(ResolveSheet, static s => s.HyperlinkMetadata, affected);
        _richTextRunsSnapshot = CaptureDictionary(ResolveSheet, static s => s.RichTextRuns, affected);
        _phoneticGuideSnapshot = CaptureDictionary(ResolveSheet, static s => s.CellPhoneticGuides, affected);
        _sparklineSnapshot = CaptureSparklinesByLocation(ResolveSheet, affected);
        _payloadAffectedCells = affected;

        var rowDelta = checked((int)((long)_destination.Row - _sourceRange.Start.Row));
        var colDelta = checked((int)((long)_destination.Col - _sourceRange.Start.Col));

        // R76-commands-cut-move-4-1: named-range migration runs for BOTH the same-sheet and
        // cross-sheet case -- a name whose range is fully contained in the cut range just carries
        // its own (Sheet, GridRange) pair, so re-homing it to the destination sheet needs no
        // collection move (see TranslateFullyContainedNamedRanges).
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        TranslateFullyContainedNamedRanges(ctx.Workbook, _sourceRange, _destination);

        if (!isCrossSheet)
        {
            (_dataValidationSnapshot, _conditionalFormatSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sourceSheet);
            TranslateFullyContainedRules(sourceSheet, _sourceRange, _destination);
        }

        if (movingMerges.Count > 0)
        {
            _mergedRegionsSnapshot = sourceSheet.MergedRegions.ToList();
            if (!isCrossSheet)
            {
                var relocatedMerges = sourceSheet.MergedRegions.Select(range =>
                    movingMerges.Contains(range) ? TranslateRange(range, rowDelta, colDelta) : range);
                sourceSheet.ReplaceMergedRegions(relocatedMerges);
            }
            else
            {
                _destMergedRegionsSnapshot = destSheet.MergedRegions.ToList();
                sourceSheet.ReplaceMergedRegions(sourceSheet.MergedRegions.Where(range => !movingMerges.Contains(range)));
                var relocatedMerges = movingMerges.Select(range =>
                    TranslateRangeToSheet(range, _destination.Sheet, rowDelta, colDelta));
                destSheet.ReplaceMergedRegions(destSheet.MergedRegions.Concat(relocatedMerges));
            }
        }

        if (movingTables.Count > 0)
        {
            _sourceTablesSnapshot = sourceSheet.StructuredTables.ToList();
            if (!isCrossSheet)
            {
                for (var i = 0; i < sourceSheet.StructuredTables.Count; i++)
                {
                    var table = sourceSheet.StructuredTables[i];
                    if (movingTables.Contains(table))
                    {
                        sourceSheet.StructuredTables[i] =
                            CloneStructuredTableWithRange(table, TranslateRange(table.Range, rowDelta, colDelta));
                    }
                }
            }
            else
            {
                _destTablesSnapshot = destSheet.StructuredTables.ToList();
                sourceSheet.StructuredTables.RemoveAll(table => movingTables.Contains(table));
                foreach (var table in movingTables)
                {
                    destSheet.StructuredTables.Add(
                        CloneStructuredTableWithRange(table, TranslateRangeToSheet(table.Range, _destination.Sheet, rowDelta, colDelta)));
                }
            }
        }

        _formulaSnapshot = [];
        if (!isCrossSheet)
        {
            var moveOp = CreateMoveRangeOp(sourceSheet, _sourceRange, _destination);
            RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, moveOp, _formulaSnapshot);
            _cfFormulaSnapshot = [];
            _cfThresholdSnapshot = [];
            _dvFormulaSnapshot = [];
            RowColumnShiftHelpers.RewriteRuleFormulas(ctx.Workbook, moveOp, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
            _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, moveOp);
        }
        else
        {
            // R38-commands-cut-move-2-1: a cross-sheet Cut is still a MOVE, not a copy -- the moved
            // formula's own references must keep pointing at exactly what they pointed at before
            // (gaining an explicit source-sheet qualifier only where the reference stays behind on
            // the source sheet), and any OTHER formula anywhere in the workbook that referenced a
            // cut cell must follow it to the new (sheet, row, col). RewriteAllFormulasCrossSheet
            // performs both directions of that fixup via its own sheet-aware AST rewrite, since the
            // existing MoveRangeOp/FormulaRewriter machinery has no notion of "the host sheet changed"
            // (RewriteCellRefMove only ever adjusts row/col, never SheetName).
            //
            // Residual/documented limitation: conditional-format & data-validation rule formulas AND
            // rule ranges, plus chart verbatim series formulas, are intentionally NOT migrated to the
            // destination sheet here (they stay behind, referencing the now-vacated source range) --
            // matching the pre-existing copy+clear fallback path, which never migrated any of those
            // cross-sheet either (see R76-commands-cut-move-4-1 above _dataValidationSnapshot for the
            // rationale). Named ranges and plain chart/sparkline DataRange, by contrast, ARE migrated
            // for a cross-sheet move too -- see the unconditional calls just below this if/else.
            var crossOp = new CrossSheetMoveOp(
                sourceSheet.Name,
                destSheet.Name,
                _sourceRange.Start.Row,
                _sourceRange.Start.Col,
                _sourceRange.End.Row,
                _sourceRange.End.Col,
                rowDelta,
                colDelta);
            RewriteAllFormulasCrossSheet(ctx.Workbook, crossOp, _formulaSnapshot);
        }

        // R76-commands-cut-move-4-1: unlike the DV/CF rules above, a plain chart/sparkline DataRange
        // is a self-contained (Sheet, GridRange) property on the chart/sparkline object itself (not a
        // member of a per-sheet collection), so re-homing it to the destination sheet for a
        // cross-sheet move needs no object to move between sheets -- runs for both move kinds.
        _chartDataRangeSnapshot = RowColumnShiftHelpers.CaptureChartDataRanges(ctx.Workbook);
        TranslateFullyContainedChartDataRanges(ctx.Workbook, _sourceRange, _destination.Sheet, rowDelta, colDelta);
        _sparklineDataRangeSnapshot = TranslateFullyContainedSparklineDataRanges(ctx.Workbook, _sourceRange, _destination.Sheet, rowDelta, colDelta);

        var payloads = CaptureSourcePayloads(sourceSheet, _sourceRange, _destination);
        // R20-array-dynamic-spill-1: capture any live spill rooted at a source cell BEFORE
        // ClearAddress tears it down, so a moved dynamic-array anchor (e.g. =SEQUENCE with no cell
        // references, whose formula text is unchanged by a plain Move) keeps spilling at its new
        // location instead of silently collapsing to a stale scalar with a blank spill area.
        _spillRelocations = CaptureSourceSpillPayloads(sourceSheet, _sourceRange, _destination);

        foreach (var address in affected)
            ClearAddress(ResolveSheet, address);

        foreach (var payload in payloads)
            WritePayload(ResolveSheet, payload);

        // R78-formula-dynamic-spill-5-1: SetSpillRange's contract requires the caller to check
        // IsSpillBlocked first (Sheet.cs) -- mirror RecalcEngine's own spill-writing branch here.
        // Without this check, relocating a spill anchor onto a destination whose footprint overlaps
        // pre-existing unrelated content wrote live spill values straight over/around that content
        // instead of surfacing #SPILL! at the anchor, leaving orphaned phantom spill entries that
        // only stay masked by luck (Sheet.GetValue prefers _cells over _spillValues) until the real
        // content is cleared, at which point the stale spill value leaks through.
        foreach (var (_, target, spillPayload) in _spillRelocations)
        {
            var targetSheet = ResolveSheet(target.Sheet);
            if (targetSheet.IsSpillBlocked(target, spillPayload.RowCount, spillPayload.ColCount))
            {
                var anchorCell = targetSheet.GetCell(target);
                if (anchorCell is not null)
                    anchorCell.Value = ErrorValue.Spill;
            }
            else
            {
                targetSheet.SetSpillRange(target, spillPayload);
            }
        }

        _affectedCells = MergeAffectedCells(affected, _formulaSnapshot.Keys);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sourceSheet = ctx.GetSheet(_sheetId);
        var isCrossSheet = _destination.Sheet != _sheetId;
        var destSheet = isCrossSheet ? ctx.GetSheet(_destination.Sheet) : sourceSheet;
        Sheet ResolveSheet(SheetId id) => id == _sheetId ? sourceSheet : destSheet;

        if (_formulaSnapshot is not null)
            RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        if (_cfFormulaSnapshot is not null || _cfThresholdSnapshot is not null || _dvFormulaSnapshot is not null)
            RowColumnShiftHelpers.RestoreRuleFormulas(ctx.Workbook, _cfFormulaSnapshot ?? [], _cfThresholdSnapshot ?? [], _dvFormulaSnapshot ?? []);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);
        RowColumnShiftHelpers.RestoreChartDataRanges(ctx.Workbook, _chartDataRangeSnapshot);
        RestoreSparklineDataRanges(_sparklineDataRangeSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
        if (_mergedRegionsSnapshot is not null)
            sourceSheet.ReplaceMergedRegions(_mergedRegionsSnapshot);
        if (_destMergedRegionsSnapshot is not null)
            destSheet.ReplaceMergedRegions(_destMergedRegionsSnapshot);
        if (_sourceTablesSnapshot is not null)
        {
            sourceSheet.StructuredTables.Clear();
            sourceSheet.StructuredTables.AddRange(_sourceTablesSnapshot);
        }
        if (_destTablesSnapshot is not null)
        {
            destSheet.StructuredTables.Clear();
            destSheet.StructuredTables.AddRange(_destTablesSnapshot);
        }

        foreach (var snapshot in _snapshot)
            RestoreCellSnapshot(ResolveSheet, snapshot);

        // R21-undo-redo-deep-2: RestoreCellSnapshot above puts each relocated spill anchor's formula
        // back at its original source address, but (unlike Apply) never re-establishes the spill
        // itself there — replay the payload captured before Apply moved it so the array's spilled
        // members reappear at the source instead of staying blank after undo.
        if (_spillRelocations is not null)
        {
            foreach (var (source, _, payload) in _spillRelocations)
                sourceSheet.SetSpillRange(source, payload);
        }

        RestoreDictionary(ResolveSheet, static s => s.Comments, _commentSnapshot, _payloadAffectedCells);
        RestoreDictionary(ResolveSheet, static s => s.CommentAuthors, _commentAuthorsSnapshot, _payloadAffectedCells);
        RestoreAddressSet(ResolveSheet, static s => s.ShownComments, _shownCommentsSnapshot, _payloadAffectedCells);
        RestoreDictionary(ResolveSheet, static s => s.ThreadedComments, _threadedCommentSnapshot, _payloadAffectedCells);
        RestoreDictionary(ResolveSheet, static s => s.Hyperlinks, _hyperlinkSnapshot, _payloadAffectedCells);
        RestoreDictionary(ResolveSheet, static s => s.HyperlinkMetadata, _hyperlinkMetadataSnapshot, _payloadAffectedCells);
        RestoreDictionary(ResolveSheet, static s => s.RichTextRuns, _richTextRunsSnapshot, _payloadAffectedCells);
        RestoreDictionary(ResolveSheet, static s => s.CellPhoneticGuides, _phoneticGuideSnapshot, _payloadAffectedCells);
        RestoreSparklines(ResolveSheet, _sparklineSnapshot, _payloadAffectedCells);
        // Restore DV/CF rule ranges that were translated during the move.
        RowColumnShiftHelpers.RestoreRuleRangesInPlace(sourceSheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
    }

    private static IReadOnlyList<CellAddress> CreateAffectedCellList(GridRange sourceRange, GridRange targetRange)
    {
        var seen = new HashSet<CellAddress>();
        var affected = new List<CellAddress>(GetSafeListCapacity(sourceRange.CellCount + targetRange.CellCount));

        AddRange(sourceRange);
        AddRange(targetRange);
        return affected;

        void AddRange(GridRange range)
        {
            foreach (var address in range.AllCells())
            {
                if (seen.Add(address))
                    affected.Add(address);
            }
        }
    }

    private static List<MovePayload> CaptureSourcePayloads(Sheet sheet, GridRange sourceRange, CellAddress destination)
    {
        var payloads = new List<MovePayload>(GetSafeListCapacity(sourceRange.CellCount));
        var rowDelta = (long)destination.Row - sourceRange.Start.Row;
        var colDelta = (long)destination.Col - sourceRange.Start.Col;

        // J17-style companion: sparklines are keyed by SparklineModel.Location rather than a
        // Dictionary<CellAddress,_>, so build a lookup up front (mirrors the per-address maps
        // below) to find the sparkline hosted at each moved source cell, if any.
        Dictionary<CellAddress, SparklineModel>? sparklinesByLocation = null;
        if (sheet.Sparklines.Count > 0)
        {
            sparklinesByLocation = new Dictionary<CellAddress, SparklineModel>();
            foreach (var sparkline in sheet.Sparklines)
                sparklinesByLocation[sparkline.Location] = sparkline;
        }

        foreach (var source in sourceRange.AllCells())
        {
            var target = new CellAddress(
                destination.Sheet,
                checked((uint)(source.Row + rowDelta)),
                checked((uint)(source.Col + colDelta)));
            var cell = sheet.GetCell(source)?.Clone();
            if (cell?.FormulaText is { } formulaText)
                RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(cell, formulaText);

            payloads.Add(new MovePayload(
                target,
                cell,
                sheet.GetStyleOnly(source.Row, source.Col),
                sheet.Comments.TryGetValue(source, out var comment) ? comment : null,
                sheet.CommentAuthors.TryGetValue(source, out var commentAuthor) ? commentAuthor : null,
                sheet.ShownComments.Contains(source),
                sheet.ThreadedComments.TryGetValue(source, out var threadedComment)
                    ? CloneThreadedComment(threadedComment)
                    : null,
                sheet.Hyperlinks.TryGetValue(source, out var hyperlink) ? hyperlink : null,
                sheet.HyperlinkMetadata.TryGetValue(source, out var metadata) ? metadata : null,
                sheet.RichTextRuns.TryGetValue(source, out var richRuns) ? richRuns : null,
                sheet.CellPhoneticGuides.TryGetValue(source, out var phoneticGuide) ? phoneticGuide : null,
                sparklinesByLocation is not null && sparklinesByLocation.TryGetValue(source, out var sourceSparkline)
                    ? CloneSparklineAt(
                        sourceSparkline,
                        target,
                        // R25-meta-3: when the sparkline's own DataRange is fully inside the moved
                        // sourceRange too (i.e. the sparkline and its full data move together in one
                        // MoveRange), translate DataRange by the same delta so it keeps following its
                        // data at the destination instead of pointing at the now-cleared source cells.
                        // TranslateFullyContainedSparklineDataRanges handles the opposite case (anchor
                        // OUTSIDE sourceRange, DataRange inside it) before this method runs, so the two
                        // are mutually exclusive and neither double-translates the other's case.
                        sourceRange.Contains(sourceSparkline.DataRange)
                            ? TranslateRange(sourceSparkline.DataRange, rowDelta, colDelta)
                            : sourceSparkline.DataRange)
                    : null));
        }

        return payloads;
    }

    /// <summary>
    /// Captures the live spill payload rooted at each spill-anchor cell within <paramref name="sourceRange"/>
    /// (if any), paired with both its original source address and the address it will occupy at the
    /// destination, so <see cref="Apply"/> can re-establish the spill via <see cref="Sheet.SetSpillRange"/>
    /// once the anchor's formula cell has been moved (R20-array-dynamic-spill-1), and <see cref="Revert"/>
    /// can replay the same payload back at the source once the anchor's formula cell has been restored
    /// there (R21-undo-redo-deep-2). Must be called before the source cells are cleared.
    /// </summary>
    private static List<(CellAddress Source, CellAddress Target, RangeValue Payload)> CaptureSourceSpillPayloads(
        Sheet sheet, GridRange sourceRange, CellAddress destination)
    {
        var rowDelta = (long)destination.Row - sourceRange.Start.Row;
        var colDelta = (long)destination.Col - sourceRange.Start.Col;

        List<(CellAddress, CellAddress, RangeValue)>? result = null;
        foreach (var source in sourceRange.AllCells())
        {
            var payload = sheet.CaptureSpillForRelocate(source);
            if (payload is null)
                continue;

            var target = new CellAddress(
                destination.Sheet,
                checked((uint)(source.Row + rowDelta)),
                checked((uint)(source.Col + colDelta)));
            (result ??= []).Add((source, target, payload));
        }

        return result ?? [];
    }

    private static MoveRangeOp CreateMoveRangeOp(Sheet sheet, GridRange sourceRange, CellAddress destination)
    {
        var rowDelta = checked((int)((long)destination.Row - sourceRange.Start.Row));
        var colDelta = checked((int)((long)destination.Col - sourceRange.Start.Col));
        return new MoveRangeOp(
            sheet.Name,
            sourceRange.Start.Row,
            sourceRange.Start.Col,
            sourceRange.End.Row,
            sourceRange.End.Col,
            rowDelta,
            colDelta);
    }

    private static IReadOnlyList<CellAddress> MergeAffectedCells(
        IReadOnlyList<CellAddress> movedCells,
        IEnumerable<CellAddress> formulaCells)
    {
        var seen = new HashSet<CellAddress>();
        var affected = new List<CellAddress>(movedCells.Count);
        foreach (var address in movedCells)
        {
            if (seen.Add(address))
                affected.Add(address);
        }

        foreach (var address in formulaCells)
        {
            if (seen.Add(address))
                affected.Add(address);
        }

        return affected;
    }

    private static List<CellSnapshot> CaptureCellSnapshots(Func<SheetId, Sheet> resolveSheet, IReadOnlyList<CellAddress> addresses)
    {
        var snapshots = new List<CellSnapshot>(addresses.Count);
        foreach (var address in addresses)
        {
            var sheet = resolveSheet(address.Sheet);
            snapshots.Add(new CellSnapshot(
                address,
                sheet.GetCell(address)?.Clone(),
                sheet.GetStyleOnly(address.Row, address.Col)));
        }

        return snapshots;
    }

    private static Dictionary<CellAddress, TValue> CaptureDictionary<TValue>(
        Func<SheetId, Sheet> resolveSheet,
        Func<Sheet, Dictionary<CellAddress, TValue>> selector,
        IReadOnlyList<CellAddress> addresses)
    {
        var snapshot = new Dictionary<CellAddress, TValue>();
        foreach (var address in addresses)
        {
            var source = selector(resolveSheet(address.Sheet));
            if (source.TryGetValue(address, out var value))
                snapshot[address] = value;
        }

        return snapshot;
    }

    private static HashSet<CellAddress> CaptureAddressSet(
        Func<SheetId, Sheet> resolveSheet,
        Func<Sheet, HashSet<CellAddress>> selector,
        IReadOnlyList<CellAddress> addresses)
    {
        var snapshot = new HashSet<CellAddress>();
        foreach (var address in addresses)
        {
            if (selector(resolveSheet(address.Sheet)).Contains(address))
                snapshot.Add(address);
        }

        return snapshot;
    }

    private static void ClearAddress(Func<SheetId, Sheet> resolveSheet, CellAddress address)
    {
        var sheet = resolveSheet(address.Sheet);
        sheet.ClearCell(address);
        sheet.ClearStyleOnly(address.Row, address.Col);
        sheet.Comments.Remove(address);
        sheet.CommentAuthors.Remove(address);
        sheet.ShownComments.Remove(address);
        sheet.ThreadedComments.Remove(address);
        sheet.Hyperlinks.Remove(address);
        sheet.HyperlinkMetadata.Remove(address);
        sheet.RichTextRuns.Remove(address);
        sheet.CellPhoneticGuides.Remove(address);
        RemoveSparklineAt(sheet, address);
    }

    private static void WritePayload(Func<SheetId, Sheet> resolveSheet, MovePayload payload)
    {
        var sheet = resolveSheet(payload.Target.Sheet);
        if (payload.Cell is not null)
        {
            sheet.SetCell(payload.Target, payload.Cell.Clone());
        }
        else if (payload.StyleOnly.HasValue)
        {
            sheet.ClearCell(payload.Target);
            sheet.SetStyleOnly(payload.Target.Row, payload.Target.Col, payload.StyleOnly.Value);
        }

        if (payload.Comment is not null)
            sheet.Comments[payload.Target] = payload.Comment;
        if (payload.CommentAuthor is not null)
            sheet.CommentAuthors[payload.Target] = payload.CommentAuthor;
        if (payload.CommentShown)
            sheet.ShownComments.Add(payload.Target);
        if (payload.ThreadedComment is not null)
            sheet.ThreadedComments[payload.Target] = CloneThreadedComment(payload.ThreadedComment);
        if (payload.Hyperlink is not null)
            sheet.Hyperlinks[payload.Target] = payload.Hyperlink;
        if (payload.HyperlinkMetadata is not null)
            sheet.HyperlinkMetadata[payload.Target] = payload.HyperlinkMetadata;
        if (payload.RichTextRuns is not null)
            sheet.RichTextRuns[payload.Target] = payload.RichTextRuns;
        if (payload.PhoneticGuide is not null)
            sheet.CellPhoneticGuides[payload.Target] = payload.PhoneticGuide;
        if (payload.Sparkline is not null)
            sheet.Sparklines.Add(payload.Sparkline);
    }

    private static void RestoreCellSnapshot(Func<SheetId, Sheet> resolveSheet, CellSnapshot snapshot)
    {
        var sheet = resolveSheet(snapshot.Address.Sheet);
        if (snapshot.Cell is null)
        {
            sheet.ClearCell(snapshot.Address);
            RestoreStyleOnly(sheet, snapshot.Address, snapshot.StyleOnly);
        }
        else
        {
            sheet.SetCell(snapshot.Address, snapshot.Cell.Clone());
        }
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }

    private static void RestoreDictionary<TValue>(
        Func<SheetId, Sheet> resolveSheet,
        Func<Sheet, Dictionary<CellAddress, TValue>> selector,
        Dictionary<CellAddress, TValue>? snapshot,
        IReadOnlyList<CellAddress> affected)
    {
        foreach (var address in affected)
            selector(resolveSheet(address.Sheet)).Remove(address);

        if (snapshot is null)
            return;

        foreach (var (address, value) in snapshot)
            selector(resolveSheet(address.Sheet))[address] = value;
    }

    private static void RestoreAddressSet(
        Func<SheetId, Sheet> resolveSheet,
        Func<Sheet, HashSet<CellAddress>> selector,
        HashSet<CellAddress>? snapshot,
        IReadOnlyList<CellAddress> affected)
    {
        foreach (var address in affected)
            selector(resolveSheet(address.Sheet)).Remove(address);

        if (snapshot is null)
            return;

        foreach (var address in snapshot)
            selector(resolveSheet(address.Sheet)).Add(address);
    }

    private static bool HasComments(Sheet sheet, IReadOnlyList<CellAddress> addresses)
    {
        foreach (var address in addresses)
        {
            if (sheet.Comments.ContainsKey(address) || sheet.ThreadedComments.ContainsKey(address))
                return true;
        }

        return false;
    }

    private static ThreadedComment CloneThreadedComment(ThreadedComment comment) =>
        comment with { Replies = comment.Replies.Select(reply => reply with { }).ToList() };

    /// <summary>
    /// Builds a copy of <paramref name="table"/> relocated to <paramref name="newRange"/>. A pure
    /// move never resizes the table (same row/column count, same header/data/totals split), so
    /// unlike the insert/delete-row-or-column shift path this needs no column reconciliation --
    /// Columns/FilterColumns carry over unchanged. Native sort-state XML (which may embed the old
    /// range as a raw attribute) is left verbatim: an intentional, narrow residual limitation, no
    /// worse than the pre-existing fallback path which never touched structured tables at all.
    /// </summary>
    private static StructuredTableModel CloneStructuredTableWithRange(StructuredTableModel table, GridRange newRange)
    {
        var clone = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = newRange,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = table.StyleName,
            ShowFirstColumn = table.ShowFirstColumn,
            ShowLastColumn = table.ShowLastColumn,
            ShowRowStripes = table.ShowRowStripes,
            ShowColumnStripes = table.ShowColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes is null
                ? null
                : new Dictionary<string, string>(table.NativeAttributes, StringComparer.Ordinal),
            NativeChildXmls = table.NativeChildXmls?.ToArray(),
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes is null
                ? null
                : new Dictionary<string, string>(table.NativeAutoFilterAttributes, StringComparer.Ordinal),
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls?.ToArray(),
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes is null
                ? null
                : new Dictionary<string, string>(table.NativeStyleInfoAttributes, StringComparer.Ordinal),
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls?.ToArray()
        };
        clone.Columns.AddRange(table.Columns);
        clone.FilterColumns.AddRange(table.FilterColumns);
        return clone;
    }

    /// <summary>
    /// Translates DV and CF rule ranges that are fully contained within <paramref name="sourceRange"/>
    /// by the move offset.  Rules that only partially overlap are left unchanged (documented limitation:
    /// full split would match Excel behaviour but is deferred; see tests).
    /// </summary>
    private static void TranslateFullyContainedRules(Sheet sheet, GridRange sourceRange, CellAddress destination)
    {
        var rowDelta = (long)destination.Row - sourceRange.Start.Row;
        var colDelta = (long)destination.Col - sourceRange.Start.Col;

        if (rowDelta == 0 && colDelta == 0)
            return;

        bool dvChanged = false;
        foreach (var rule in sheet.DataValidations)
        {
            if (IsFullyContained(rule.AppliesTo, sourceRange))
            {
                rule.AppliesTo = TranslateRange(rule.AppliesTo, rowDelta, colDelta);
                dvChanged = true;
            }

            for (var i = 0; i < rule.AdditionalRanges.Count; i++)
            {
                if (IsFullyContained(rule.AdditionalRanges[i], sourceRange))
                {
                    rule.AdditionalRanges[i] = TranslateRange(rule.AdditionalRanges[i], rowDelta, colDelta);
                    dvChanged = true;
                }
            }
        }

        if (dvChanged)
            sheet.DataValidations.NotifyRulesChanged();

        bool cfChanged = false;
        foreach (var rule in sheet.ConditionalFormats)
        {
            if (IsFullyContained(rule.AppliesTo, sourceRange))
            {
                rule.AppliesTo = TranslateRange(rule.AppliesTo, rowDelta, colDelta);
                cfChanged = true;
            }

            if (rule.AdditionalRanges is { Count: > 0 })
            {
                var result = new List<GridRange>(rule.AdditionalRanges.Count);
                var anyChanged = false;
                foreach (var ar in rule.AdditionalRanges)
                {
                    if (IsFullyContained(ar, sourceRange))
                    {
                        result.Add(TranslateRange(ar, rowDelta, colDelta));
                        anyChanged = true;
                    }
                    else
                    {
                        result.Add(ar);
                    }
                }
                if (anyChanged)
                {
                    rule.AdditionalRanges = result;
                    cfChanged = true;
                }
            }
        }

        if (cfChanged)
            sheet.ConditionalFormats.NotifyRulesChanged();
    }

    private static bool IsFullyContained(GridRange candidate, GridRange container) =>
        candidate.Start.Row >= container.Start.Row &&
        candidate.Start.Col >= container.Start.Col &&
        candidate.End.Row   <= container.End.Row   &&
        candidate.End.Col   <= container.End.Col;

    private static GridRange TranslateRange(GridRange range, long rowDelta, long colDelta) =>
        new GridRange(
            new CellAddress(range.Start.Sheet, (uint)(range.Start.Row + rowDelta), (uint)(range.Start.Col + colDelta)),
            new CellAddress(range.End.Sheet,   (uint)(range.End.Row   + rowDelta), (uint)(range.End.Col   + colDelta)));

    /// <summary>
    /// Same as <see cref="TranslateRange"/> but also re-homes the range onto <paramref name="newSheet"/>
    /// -- used only for a cross-sheet move (R38-commands-cut-move-2-1/2-2), where the relocated merge
    /// or table range must carry the destination sheet's id, not the source's.
    /// </summary>
    private static GridRange TranslateRangeToSheet(GridRange range, SheetId newSheet, long rowDelta, long colDelta) =>
        new GridRange(
            new CellAddress(newSheet, (uint)(range.Start.Row + rowDelta), (uint)(range.Start.Col + colDelta)),
            new CellAddress(newSheet, (uint)(range.End.Row   + rowDelta), (uint)(range.End.Col   + colDelta)));

    private static int GetSafeListCapacity(long cellCount) =>
        cellCount is > 0 and <= 1_000_000 ? (int)cellCount : 0;

    private sealed record CellSnapshot(CellAddress Address, Cell? Cell, StyleId? StyleOnly);

    private sealed record MovePayload(
        CellAddress Target,
        Cell? Cell,
        StyleId? StyleOnly,
        string? Comment,
        string? CommentAuthor,
        bool CommentShown,
        ThreadedComment? ThreadedComment,
        string? Hyperlink,
        HyperlinkMetadata? HyperlinkMetadata,
        IReadOnlyList<CellTextRun>? RichTextRuns,
        CellPhoneticGuide? PhoneticGuide,
        SparklineModel? Sparkline);

    /// <summary>
    /// Relocates workbook-scoped (<see cref="Workbook.NamedRanges"/>) and sheet-scoped
    /// (<see cref="Workbook.ScopedNamedRanges"/>) defined names whose range falls entirely inside
    /// the moved <paramref name="sourceRange"/>, so the name continues to refer to the moved data at
    /// its new location instead of the now-vacated source (R16-structural-edit-shift-sweep-1).
    /// Mirrors <see cref="TranslateFullyContainedRules"/>'s "fully contained only" convention for
    /// DV/CF ranges; a name that only partially overlaps the moved range is left unchanged.
    /// R76-commands-cut-move-4-1: <paramref name="destination"/>'s sheet may differ from
    /// <paramref name="sourceRange"/>'s (a cross-sheet Cut+Paste) -- the re-anchored name's range is
    /// re-homed onto that destination sheet via <see cref="TranslateRangeToSheet"/> (a same-sheet
    /// move passes a destination on the same sheet, so this is a strict superset of the previous
    /// same-sheet-only behavior).
    /// </summary>
    private static void TranslateFullyContainedNamedRanges(Workbook workbook, GridRange sourceRange, CellAddress destination)
    {
        var rowDelta = (long)destination.Row - sourceRange.Start.Row;
        var colDelta = (long)destination.Col - sourceRange.Start.Col;
        if (rowDelta == 0 && colDelta == 0 && destination.Sheet == sourceRange.Start.Sheet)
            return;

        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (sourceRange.Contains(range))
                workbook.NamedRanges[name] = TranslateRangeToSheet(range, destination.Sheet, rowDelta, colDelta);
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (sourceRange.Contains(range))
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, TranslateRangeToSheet(range, destination.Sheet, rowDelta, colDelta), metadata, scopeSheet);
            }
        }
    }

    /// <summary>
    /// Relocates a chart's plain (non-verbatim) <see cref="ChartModel.DataRange"/> when it falls
    /// entirely inside the moved range, across every sheet in the workbook (a chart can be hosted on
    /// a different sheet than the data it plots — see <c>RowColumnShiftHelpers.PrintAndCharts.cs</c>).
    /// Verbatim series formulas are already rewritten above via
    /// <see cref="RowColumnShiftHelpers.RewriteChartVerbatimFormulas(Workbook, RewriteOperation)"/>;
    /// this covers the plain-DataRange chart case that formula rewriting does not touch
    /// (R16-structural-edit-shift-sweep-1, R16-chart-datasource-editing-2). R76-commands-cut-move-4-1:
    /// <paramref name="destinationSheet"/> may differ from <paramref name="sourceRange"/>'s sheet for a
    /// cross-sheet Cut+Paste; the DataRange is re-homed onto it via <see cref="TranslateRangeToSheet"/>.
    /// </summary>
    private static void TranslateFullyContainedChartDataRanges(
        Workbook workbook, GridRange sourceRange, SheetId destinationSheet, int rowDelta, int colDelta)
    {
        if (rowDelta == 0 && colDelta == 0 && destinationSheet == sourceRange.Start.Sheet)
            return;

        foreach (var hostSheet in workbook.Sheets)
        {
            foreach (var chart in hostSheet.Charts)
            {
                if (sourceRange.Contains(chart.DataRange))
                    chart.DataRange = TranslateRangeToSheet(chart.DataRange, destinationSheet, rowDelta, colDelta);
            }
        }
    }

    /// <summary>
    /// Relocates a sparkline's plain <see cref="SparklineModel.DataRange"/> when it falls entirely
    /// inside the moved range, mirroring <see cref="TranslateFullyContainedChartDataRanges"/> for
    /// <see cref="ChartModel.DataRange"/> (R24-sparklines-1). Like charts, a sparkline can be hosted
    /// on a different sheet than the data it plots, so every sheet in the workbook is checked, not
    /// just the sheet being moved. Sparklines whose own <see cref="SparklineModel.Location"/> falls
    /// inside <paramref name="sourceRange"/> are skipped here: those are relocated by
    /// <see cref="CaptureSourcePayloads"/>/<see cref="CloneSparklineAt"/> instead, which (R25-meta-3)
    /// also translates DataRange when it is fully contained in <paramref name="sourceRange"/> -- i.e.
    /// the sparkline and its data move together -- so the two methods cover disjoint cases and never
    /// double-translate the same sparkline. Returns the original (sparkline, DataRange) pairs so
    /// <see cref="Revert"/> can restore them via <see cref="RestoreSparklineDataRanges"/>.
    /// R76-commands-cut-move-4-1: <paramref name="destinationSheet"/> may differ from
    /// <paramref name="sourceRange"/>'s sheet for a cross-sheet Cut+Paste; the DataRange is re-homed
    /// onto it via <see cref="TranslateRangeToSheet"/>.
    /// </summary>
    private static List<(SparklineModel Sparkline, GridRange OriginalDataRange)> TranslateFullyContainedSparklineDataRanges(
        Workbook workbook, GridRange sourceRange, SheetId destinationSheet, int rowDelta, int colDelta)
    {
        var snapshot = new List<(SparklineModel, GridRange)>();
        if (rowDelta == 0 && colDelta == 0 && destinationSheet == sourceRange.Start.Sheet)
            return snapshot;

        foreach (var hostSheet in workbook.Sheets)
        {
            foreach (var sparkline in hostSheet.Sparklines)
            {
                if (sourceRange.Contains(sparkline.Location))
                    continue;

                if (sourceRange.Contains(sparkline.DataRange))
                {
                    snapshot.Add((sparkline, sparkline.DataRange));
                    sparkline.DataRange = TranslateRangeToSheet(sparkline.DataRange, destinationSheet, rowDelta, colDelta);
                }
            }
        }

        return snapshot;
    }

    private static void RestoreSparklineDataRanges(List<(SparklineModel Sparkline, GridRange OriginalDataRange)>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (sparkline, originalDataRange) in snapshot)
            sparkline.DataRange = originalDataRange;
    }

    /// <summary>
    /// Captures the sparkline (if any) hosted at each of <paramref name="addresses"/>, keyed by its
    /// <see cref="SparklineModel.Location"/>. Sparklines live in <see cref="Sheet.Sparklines"/> — a
    /// flat list rather than a per-address dictionary like Comments/Hyperlinks — so this (and
    /// <see cref="RemoveSparklineAt"/>/<see cref="RestoreSparklines"/>/<see cref="CloneSparklineAt"/>)
    /// gives the move the same capture/clear/write/restore shape used for the dictionary-backed
    /// per-cell state above (R16-structural-edit-shift-sweep-3). Addresses are grouped by their
    /// resolved sheet so a cross-sheet move (R38-commands-cut-move-2-1) captures source-side and
    /// destination-side sparklines from their own Sheet.Sparklines list.
    /// </summary>
    private static Dictionary<CellAddress, SparklineModel> CaptureSparklinesByLocation(
        Func<SheetId, Sheet> resolveSheet, IReadOnlyList<CellAddress> addresses)
    {
        var snapshot = new Dictionary<CellAddress, SparklineModel>();
        foreach (var sheetGroup in addresses.GroupBy(address => resolveSheet(address.Sheet)))
        {
            var sheet = sheetGroup.Key;
            if (sheet.Sparklines.Count == 0)
                continue;

            var addressSet = new HashSet<CellAddress>(sheetGroup);
            foreach (var sparkline in sheet.Sparklines)
            {
                if (addressSet.Contains(sparkline.Location))
                    snapshot[sparkline.Location] = sparkline;
            }
        }

        return snapshot;
    }

    private static void RemoveSparklineAt(Sheet sheet, CellAddress address)
    {
        for (var i = sheet.Sparklines.Count - 1; i >= 0; i--)
        {
            if (sheet.Sparklines[i].Location == address)
                sheet.Sparklines.RemoveAt(i);
        }
    }

    /// <summary>
    /// Restores <see cref="Sheet.Sparklines"/> from a snapshot captured by
    /// <see cref="CaptureSparklinesByLocation"/>: removes whatever now sits at each affected address
    /// (the moved/cloned sparklines written during Apply) and re-adds the original snapshotted
    /// instances, mirroring <see cref="RestoreDictionary{TValue}"/>.
    /// </summary>
    private static void RestoreSparklines(
        Func<SheetId, Sheet> resolveSheet,
        Dictionary<CellAddress, SparklineModel>? snapshot,
        IReadOnlyList<CellAddress> affected)
    {
        foreach (var address in affected)
            RemoveSparklineAt(resolveSheet(address.Sheet), address);

        if (snapshot is null)
            return;

        foreach (var (address, sparkline) in snapshot)
            resolveSheet(address.Sheet).Sparklines.Add(sparkline);
    }

    // Manual field-by-field clone: SparklineModel is a mutable class (not a record), so there is no
    // built-in `with` copy. A clone (rather than reusing the source instance with a mutated
    // Location) is required here because the source instance is also held by _sparklineSnapshot for
    // undo — mutating it in place would corrupt that snapshot's recorded source-cell state.
    private static SparklineModel CloneSparklineAt(SparklineModel source, CellAddress location, GridRange dataRange) => new()
    {
        Id = source.Id,
        DataRange = dataRange,
        Location = location,
        Kind = source.Kind,
        GroupId = source.GroupId,
        ShowMarkers = source.ShowMarkers,
        ShowHighPoint = source.ShowHighPoint,
        ShowLowPoint = source.ShowLowPoint,
        ShowFirstPoint = source.ShowFirstPoint,
        ShowLastPoint = source.ShowLastPoint,
        ShowNegativePoints = source.ShowNegativePoints,
        ShowAxis = source.ShowAxis,
        DisplayHidden = source.DisplayHidden,
        RightToLeft = source.RightToLeft,
        SeriesColor = source.SeriesColor,
        NegativeColor = source.NegativeColor,
        AxisColor = source.AxisColor,
        MarkersColor = source.MarkersColor,
        HighPointColor = source.HighPointColor,
        LowPointColor = source.LowPointColor,
        FirstPointColor = source.FirstPointColor,
        LastPointColor = source.LastPointColor,
        LineWeight = source.LineWeight,
        MinAxisType = source.MinAxisType,
        MaxAxisType = source.MaxAxisType,
        ManualMin = source.ManualMin,
        ManualMax = source.ManualMax,
        DisplayEmptyCellsAs = source.DisplayEmptyCellsAs,
        DateAxisRange = source.DateAxisRange,
    };

    // ── Cross-sheet formula reference rewrite (R38-commands-cut-move-2-1) ──────────────────────
    // FormulaRewriter's MoveRangeOp/RewriteCellRefMove only ever adjusts a matched reference's
    // row/col -- it has no notion of "the formula's host sheet changed" and never touches
    // CellRefNode.SheetName. That's correct for a same-sheet move (RowColumnShiftHelpers.
    // RewriteAllFormulas above), but wrong for a cross-sheet one: Excel's real Cut/Move rule is
    // that a moved formula keeps pointing at exactly what it pointed at before, so any reference
    // that stays behind on the source sheet (i.e. it is NOT part of the moved range) must gain an
    // explicit source-sheet qualifier once its host formula relocates to a different sheet, and any
    // reference that also moves (points inside the moved range, so it moves along with it) is
    // shifted by the same row/col delta and left sheet-implicit at the new (destination) host. The
    // functions below are a small, self-contained AST rewrite that implements exactly that rule,
    // reusing FormulaRewriter's own Lexer/Parser/FormulaSerializer/FormulaNode building blocks
    // (all public) without needing to add a new RewriteOperation case to FormulaRewriter.cs itself.

    private sealed record CrossSheetMoveOp(
        string SourceSheetName,
        string DestSheetName,
        uint SourceStartRow,
        uint SourceStartCol,
        uint SourceEndRow,
        uint SourceEndCol,
        int RowDelta,
        int ColDelta);

    /// <summary>
    /// Mirrors <see cref="RowColumnShiftHelpers.RewriteAllFormulas"/>'s full-workbook scan, but using
    /// <see cref="RewriteFormulaCrossSheet"/> instead of <c>FormulaRewriter.Rewrite</c> so that both
    /// the moved cells' own formulas AND any other formula elsewhere in the workbook that references
    /// a moved cell get the sheet-aware fixup described above.
    /// </summary>
    private static void RewriteAllFormulasCrossSheet(
        Workbook workbook, CrossSheetMoveOp op, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var hostIsSourceSheet = string.Equals(sheet.Name, op.SourceSheetName, StringComparison.OrdinalIgnoreCase);
            foreach (var addr in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(addr);
                if (cell?.FormulaText is null)
                    continue;

                var hostIsMoving = hostIsSourceSheet && IsInSourceRange(op, addr.Row, addr.Col);
                var rewritten = RewriteFormulaCrossSheet(cell.FormulaText, op, sheet.Name, hostIsMoving);
                if (rewritten is null)
                    continue;

                snapshot[addr] = cell.FormulaText;
                RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(cell, rewritten);
            }
        }
    }

    private static bool IsInSourceRange(CrossSheetMoveOp op, uint row, uint col) =>
        row >= op.SourceStartRow && row <= op.SourceEndRow &&
        col >= op.SourceStartCol && col <= op.SourceEndCol;

    private static string? RewriteFormulaCrossSheet(
        string formulaText, CrossSheetMoveOp op, string hostSheetName, bool hostIsMoving)
    {
        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();
            bool changed = false;
            var rewritten = RewriteNodeCrossSheet(ast, op, hostSheetName, hostIsMoving, ref changed);
            return changed ? FormulaSerializer.Serialize(rewritten) : null;
        }
        catch
        {
            return null; // malformed formula — leave untouched
        }
    }

    private static FormulaNode RewriteNodeCrossSheet(
        FormulaNode node, CrossSheetMoveOp op, string hostSheetName, bool hostIsMoving, ref bool changed)
    {
        return node switch
        {
            CellRefNode cr => RewriteCellRefCrossSheet(cr, op, hostSheetName, hostIsMoving, ref changed),
            RangeRefNode rr => RewriteRangeCrossSheet(rr, op, hostSheetName, hostIsMoving, ref changed),
            FullColumnRangeRefNode fcr => RewriteFullColumnRangeCrossSheet(fcr, op, hostIsMoving, ref changed),
            FullRowRangeRefNode frr => RewriteFullRowRangeCrossSheet(frr, op, hostIsMoving, ref changed),
            BinaryOpNode b => b with
            {
                Left = RewriteNodeCrossSheet(b.Left, op, hostSheetName, hostIsMoving, ref changed),
                Right = RewriteNodeCrossSheet(b.Right, op, hostSheetName, hostIsMoving, ref changed)
            },
            UnaryOpNode u => u with
            {
                Operand = RewriteNodeCrossSheet(u.Operand, op, hostSheetName, hostIsMoving, ref changed)
            },
            FunctionCallNode f => RewriteFunctionArgsCrossSheet(f, op, hostSheetName, hostIsMoving, ref changed),
            _ => node // NumberNode, StringNode, BooleanNode, NamedRangeNode, StructuredReferenceNode,
                      // StructuredCurrentRowReferenceNode, ArrayConstantNode, ErrorNode,
                      // OmittedArgumentNode -- none carry sheet/cell-address state a Move affects.
        };
    }

    private static FunctionCallNode RewriteFunctionArgsCrossSheet(
        FunctionCallNode f, CrossSheetMoveOp op, string hostSheetName, bool hostIsMoving, ref bool changed)
    {
        var newArgs = new List<FormulaNode>(f.Arguments.Count);
        foreach (var arg in f.Arguments)
            newArgs.Add(RewriteNodeCrossSheet(arg, op, hostSheetName, hostIsMoving, ref changed));
        return f with { Arguments = newArgs };
    }

    private static FormulaNode RewriteCellRefCrossSheet(
        CellRefNode cr, CrossSheetMoveOp op, string hostSheetName, bool hostIsMoving, ref bool changed)
    {
        // An unqualified reference implicitly resolves against whatever sheet the formula
        // currently lives on (hostSheetName), the same convention FormulaRewriter itself uses.
        var effectiveSheet = cr.SheetName ?? hostSheetName;
        if (!string.Equals(effectiveSheet, op.SourceSheetName, StringComparison.OrdinalIgnoreCase))
            return cr;

        if (IsInSourceRange(op, cr.Row, cr.ColumnNumber))
        {
            long newRow = (long)cr.Row + op.RowDelta;
            long newCol = (long)cr.ColumnNumber + op.ColDelta;
            if (newRow < 1 || newRow > CellAddress.MaxRow || newCol < 1 || newCol > CellAddress.MaxCol)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }

            changed = true;
            return cr with
            {
                Row = (uint)newRow,
                ColumnName = CellAddress.NumberToColumnName((uint)newCol),
                SheetName = hostIsMoving ? null : op.DestSheetName
            };
        }

        // The target cell stays behind on the source sheet. If the host formula itself is moving
        // to the destination sheet, an implicit reference must gain an explicit source-sheet
        // qualifier so it keeps pointing at the untouched cell instead of silently resolving
        // against the new (destination) host sheet.
        if (hostIsMoving && cr.SheetName is null)
        {
            changed = true;
            return cr with { SheetName = op.SourceSheetName };
        }

        return cr;
    }

    private static FormulaNode RewriteRangeCrossSheet(
        RangeRefNode rr, CrossSheetMoveOp op, string hostSheetName, bool hostIsMoving, ref bool changed)
    {
        if (rr.EndSheetName is not null)
            return rr; // 3-D sheet-span reference: out of scope, left untouched (matches
                       // FormulaRewriter's own conservative handling of spans for structural ops).

        // An unqualified range implicitly resolves against whatever sheet the formula currently
        // lives on (hostSheetName), the same convention FormulaRewriter itself uses.
        var effectiveSheet = rr.SheetName ?? hostSheetName;
        if (!string.Equals(effectiveSheet, op.SourceSheetName, StringComparison.OrdinalIgnoreCase))
            return rr;

        var startInSource = IsInSourceRange(op, rr.Start.Row, rr.Start.ColumnNumber);
        var endInSource = IsInSourceRange(op, rr.End.Row, rr.End.ColumnNumber);

        if (startInSource && endInSource)
        {
            var newStart = ShiftCellRefCrossSheet(rr.Start, op, ref changed);
            var newEnd = ShiftCellRefCrossSheet(rr.End, op, ref changed);
            if (newStart is ErrorNode || newEnd is ErrorNode)
            {
                changed = true;
                return new ErrorNode(ErrorValue.Ref);
            }

            changed = true;
            return rr with
            {
                Start = (CellRefNode)newStart,
                End = (CellRefNode)newEnd,
                SheetName = hostIsMoving ? null : op.DestSheetName
            };
        }

        if (!startInSource && !endInSource)
        {
            if (hostIsMoving && rr.SheetName is null)
            {
                changed = true;
                return rr with { SheetName = op.SourceSheetName };
            }

            return rr;
        }

        // Partial overlap (one endpoint inside the moved range, the other outside): correctly
        // splitting the range would require rewriting it into two pieces, which real Excel does
        // support but this rewrite intentionally leaves alone -- a documented limitation matching
        // the same conservative "leave partial overlaps unchanged" behavior already used elsewhere
        // in this file (e.g. TranslateFullyContainedRules/-NamedRanges/-ChartDataRanges).
        return rr;
    }

    private static FormulaNode ShiftCellRefCrossSheet(CellRefNode cr, CrossSheetMoveOp op, ref bool changed)
    {
        long newRow = (long)cr.Row + op.RowDelta;
        long newCol = (long)cr.ColumnNumber + op.ColDelta;
        if (newRow < 1 || newRow > CellAddress.MaxRow || newCol < 1 || newCol > CellAddress.MaxCol)
        {
            changed = true;
            return new ErrorNode(ErrorValue.Ref);
        }

        changed = true;
        return cr with { Row = (uint)newRow, ColumnName = CellAddress.NumberToColumnName((uint)newCol) };
    }

    private static FormulaNode RewriteFullColumnRangeCrossSheet(
        FullColumnRangeRefNode fcr, CrossSheetMoveOp op, bool hostIsMoving, ref bool changed)
    {
        // A whole-column reference is never "fully contained" in a bounded rectangular move range
        // (it spans every row), so the only cross-sheet fixup that ever applies here is requalifying
        // an implicit reference when its own host formula relocates to a different sheet.
        if (hostIsMoving && fcr.SheetName is null)
        {
            changed = true;
            return fcr with { SheetName = op.SourceSheetName };
        }

        return fcr;
    }

    private static FormulaNode RewriteFullRowRangeCrossSheet(
        FullRowRangeRefNode frr, CrossSheetMoveOp op, bool hostIsMoving, ref bool changed)
    {
        if (hostIsMoving && frr.SheetName is null)
        {
            changed = true;
            return frr with { SheetName = op.SourceSheetName };
        }

        return frr;
    }
}
