using System.Buffers;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum InsertCellsShiftDirection
{
    Right,
    Down
}

public enum DeleteCellsShiftDirection
{
    Left,
    Up
}

public sealed class InsertCellsCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: _capturedCells below retains a full (Address, Cell) pair
    // for every occupied cell shifted by the insert (see CellShiftCapture/CaptureCells), plus
    // several companion per-cell dictionary snapshots (comments, hyperlinks, rich text, phonetic
    // guides) keyed by the same set -- one of the richest undo snapshot shapes in the codebase.
    // Estimated from _capturedCells.Count, known only once Apply has captured it (this command's
    // affected-cell count depends on how many cells are actually occupied in the shifted band, not
    // just the requested insert range). _capturedCells is null before Apply runs, in which case
    // CommandBus never actually queries this (EstimateBytes is only called after Apply pushes the
    // command).
    private const int BytesPerCell = 400;

    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly InsertCellsShiftDirection _direction;
    private CellShiftSnapshot? _snapshot;
    private RowColumnMutationSnapshot? _mutationSnapshot;
    // R96-commands-undo-affected-cells-2: the raw captured (original-address, cell) pairs behind
    // _snapshot, kept alongside it so Revert can recompute AffectedCells at the CURRENT (post-Revert)
    // address of every relocated formula cell -- see RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress.
    private IReadOnlyList<(CellAddress Address, Cell Cell)>? _capturedCells;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private List<CellAddress>? _movedDestinationCells;
    // R52-commands-clear-delete-3-1: style-only (formatted-but-empty) cells are invisible to
    // sheet.GetOccupiedCellMap() (which only sees value/formula-bearing Cell entries), so the
    // band-scoped shift below never moved or cleared them, silently destroying or misplacing
    // format-only cells such as a fill color applied to an empty cell.
    private List<(uint Row, uint Col, StyleId StyleId)>? _styleOnlySnapshot;
    // R86-meta-3: mirrors DeleteCellsCommand's own _sparklineSnapshot field below — this band-scoped
    // Insert Cells path never touched sheet.Sparklines at all before this fix, the exact gap
    // R84-commands-clear-delete-5-1 fixed on the Delete side (see ShiftSparklinesInBandRight/Down).
    private List<SparklineBandSnapshot>? _sparklineSnapshot;

    public string Label => "Insert Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes => _capturedCells is null
        ? 0
        : (int)Math.Min((long)_capturedCells.Count * BytesPerCell, int.MaxValue);

    public InsertCellsCommand(SheetId sheetId, GridRange range, InsertCellsShiftDirection direction)
    {
        _sheetId = sheetId;
        _range = range;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Insert range must be on the target sheet.");
        if (!Enum.IsDefined(_direction))
            return new CommandOutcome(false, "Insert shift direction is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _mutationSnapshot = RowColumnMutationSnapshot.Capture(ctx.Workbook, sheet);

        if (_direction == InsertCellsShiftDirection.Right)
        {
            uint width = _range.ColCount;
            var shiftRegion = CellShiftRegion.Rightward(_range);

            // R23-array-formula-legacy-cse-1: reject if this band-scoped shift would carry some
            // members of a legacy CSE array / dynamic-array spill along while leaving others behind
            // (Excel's "You cannot change part of an array"). An array whose full extent lies inside
            // the shifted band still moves as one atomic unit — see ArrayMembersWithinShiftRegion.
            if (CommandGuards.RejectIfSplitsArray(sheet, ArrayMembersWithinShiftRegion(sheet, shiftRegion)) is { } splitsArrayRejection)
                return splitsArrayRejection;

            if (MergeWouldBeTorn(sheet, shiftRegion, shiftDirection: ShiftAxis.Column))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: unlike whole-row/whole-column insert (which shifts AutoFilter.Reference,
            // FilterHiddenRows, form controls, pivot tables, sparklines, and watched cells via
            // RowColumnShiftHelpers.CaptureAddressBearingState), this band-scoped shift only moves
            // cells within a bounded row/column range — silently shifting an axis-wide reference like
            // AutoFilter.Reference here would corrupt rows/columns outside the shifted band. Excel's
            // own behavior is to refuse the operation outright when it would partially disrupt a
            // table/AutoFilter range, so mirror that instead of silently leaving stale state behind.
            if (AutoFilterOverlapsBand(ctx.Workbook, sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = CaptureCellsForMove(sheet, shiftRegion,
                orig => new CellAddress(orig.Sheet, orig.Row, orig.Col + width));
            // R38-commands-insert-delete-shift-2-2: capture.MaxCol only sees value-bearing Cell
            // entries (sheet.GetOccupiedCellMap()), so a blank merged region (e.g. from "merge first,
            // type later") abutting the last column is invisible to it. Without also consulting
            // MergedRegions here, AdjustMergesShiftRight would silently clamp/truncate such a merge
            // past the sheet edge instead of blocking the insert the way Excel does for any content
            // — including merges — that would be pushed past the last column.
            var mergeMaxCol = MaxAffectedMergeEndColForShiftRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col);
            // R71-commands-insert-delete-cells-4-1: a comment/hyperlink/rich-text-run/style-only
            // cell anchored at the last column carries no Cell entry (invisible to capture.MaxCol)
            // and is not a merge (invisible to mergeMaxCol), so without this it would sail past
            // this guard and then be silently relocated past CellAddress.MaxCol by
            // ShiftAnnotationsInBandRight/ShiftStyleOnlyInBandRight below.
            var annotationMaxCol = MaxAnnotationColInBandForShiftRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col);
            if ((capture.MaxCol > 0 && capture.MaxCol + width > CellAddress.MaxCol) ||
                (mergeMaxCol > 0 && mergeMaxCol + width > CellAddress.MaxCol) ||
                (annotationMaxCol > 0 && annotationMaxCol + width > CellAddress.MaxCol))
                return new CommandOutcome(false, $"Cannot insert cells: data would be pushed past the last column ({CellAddress.MaxCol}).");

            _snapshot = capture.Snapshot;
            _capturedCells = capture.Cells;

            // Snapshot and shift annotations (comments, hyperlinks, style-only) in the band
            ShiftAnnotationsInBandRight(sheet.Comments, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must shift in lockstep with it within
            // the same band, or a note's author/pinned box goes stale at its old address.
            ShiftAnnotationsInBandRight(sheet.CommentAuthors, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            ShiftAnnotationsSetInBandRight(sheet.ShownComments, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            ShiftAnnotationsInBandRight(sheet.ThreadedComments, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            ShiftAnnotationsInBandRight(sheet.Hyperlinks, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            ShiftAnnotationsInBandRight(sheet.HyperlinkMetadata, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            ShiftAnnotationsInBandRight(sheet.RichTextRuns, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            ShiftAnnotationsInBandRight(sheet.CellPhoneticGuides, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            // R52-commands-clear-delete-3-1: shift style-only (formatted-but-empty) cells in lockstep
            // with the value cells in the same band, so a fill/border applied to an empty cell moves
            // (or is displaced) exactly the way the same formatting on a value-bearing cell would.
            _styleOnlySnapshot = CaptureStyleOnlyEntries(sheet);
            ShiftStyleOnlyInBandRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            // Snapshot and update merged regions
            sheet.ReplaceMergedRegions(AdjustMergesShiftRight(sheet.MergedRegions, _range.Start.Row, _range.End.Row, _range.Start.Col, width));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            RowColumnShiftHelpers.AdjustRulesInsertShiftRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            // R21-defined-name-management-1: plain (GridRange-backed) named ranges fully inside the
            // band's row span are shifted right in lockstep with the cells, the same way whole-row/
            // whole-column insert already does via RowColumnShiftHelpers.ShiftNamedRangeRowsUp/Down/
            // ColumnsUp/Down — this band-scoped path never touched NamedRanges/ScopedNamedRanges at all.
            ShiftNamedRangesInBandRight(ctx.Workbook, _sheetId, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            // R86-meta-3: see ShiftSparklinesInBandRight for the move/grow rationale (mirrors
            // ShiftNamedRangesInBandRight immediately above, but for sheet.Sparklines).
            _sparklineSnapshot = CaptureSparklines(sheet);
            ShiftSparklinesInBandRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            InsertShiftRight(sheet, capture.Cells);
            // R71-commands-insert-delete-cells-4-2: Excel's Insert Cells default ("Insert Options"
            // smart-tag) copies the formatting of the cell immediately to the LEFT into the
            // newly-vacated band instead of leaving it at General/default formatting.
            InheritVacatedFormatShiftRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col);
            // R21-undo-redo-deep-1: record the shifted-to address of every moved cell so a moved
            // dynamic-array anchor whose formula text is unchanged by the shift (e.g. SEQUENCE with
            // no cell references) still gets queued for recalculation — RewriteAllFormulas below only
            // records cells whose formula TEXT actually changed.
            _movedDestinationCells = capture.Cells.Count == 0
                ? null
                : capture.Cells.Select(c => new CellAddress(c.Address.Sheet, c.Address.Row, c.Address.Col + width)).ToList();

            var insertRightOp = new InsertCellsShiftRightOp(
                sheet.Name,
                _range.Start.Row, _range.End.Row,
                _range.Start.Col, CellAddress.MaxCol,
                _range.Start.Col, width);
            // O34: rewrite in-document hyperlink bookmark CONTENT (not just the dictionary key,
            // already shifted above by ShiftAnnotationsInBandRight) so a "Place in This Document"
            // link whose Bookmark text points at a cell inside the shifted band keeps pointing at
            // the same logical cell, matching whole-row/whole-column insert.
            _mutationSnapshot!.RewriteReferences(ctx.Workbook, sheet, insertRightOp);
        }
        else
        {
            uint height = _range.RowCount;
            var shiftRegion = CellShiftRegion.Downward(_range);

            // R23-array-formula-legacy-cse-1: see the Shift-Right branch above.
            if (CommandGuards.RejectIfSplitsArray(sheet, ArrayMembersWithinShiftRegion(sheet, shiftRegion)) is { } splitsArrayRejection)
                return splitsArrayRejection;

            if (MergeWouldBeTorn(sheet, shiftRegion, shiftDirection: ShiftAxis.Row))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: see the Shift-Right branch above for why this band-scoped operation must refuse
            // rather than silently shift AutoFilter/table state it cannot safely relocate.
            if (AutoFilterOverlapsBand(ctx.Workbook, sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = CaptureCellsForMove(sheet, shiftRegion,
                orig => new CellAddress(orig.Sheet, orig.Row + height, orig.Col));
            // R38-commands-insert-delete-shift-2-2: see the Shift-Right branch above — a blank
            // merged region abutting the last row is likewise invisible to capture.MaxRow.
            var mergeMaxRow = MaxAffectedMergeEndRowForShiftDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row);
            // R71-commands-insert-delete-cells-4-1: see the Shift-Right branch above.
            var annotationMaxRow = MaxAnnotationRowInBandForShiftDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row);
            if ((capture.MaxRow > 0 && capture.MaxRow + height > CellAddress.MaxRow) ||
                (mergeMaxRow > 0 && mergeMaxRow + height > CellAddress.MaxRow) ||
                (annotationMaxRow > 0 && annotationMaxRow + height > CellAddress.MaxRow))
                return new CommandOutcome(false, $"Cannot insert cells: data would be pushed past the last row ({CellAddress.MaxRow}).");

            _snapshot = capture.Snapshot;
            _capturedCells = capture.Cells;

            // Snapshot and shift annotations in the band
            ShiftAnnotationsInBandDown(sheet.Comments, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must shift in lockstep with it within
            // the same band, or a note's author/pinned box goes stale at its old address.
            ShiftAnnotationsInBandDown(sheet.CommentAuthors, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            ShiftAnnotationsSetInBandDown(sheet.ShownComments, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            ShiftAnnotationsInBandDown(sheet.ThreadedComments, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            ShiftAnnotationsInBandDown(sheet.Hyperlinks, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            ShiftAnnotationsInBandDown(sheet.HyperlinkMetadata, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            ShiftAnnotationsInBandDown(sheet.RichTextRuns, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            ShiftAnnotationsInBandDown(sheet.CellPhoneticGuides, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            // R52-commands-clear-delete-3-1: see the Shift-Right branch above.
            _styleOnlySnapshot = CaptureStyleOnlyEntries(sheet);
            ShiftStyleOnlyInBandDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            // Snapshot and update merged regions
            sheet.ReplaceMergedRegions(AdjustMergesShiftDown(sheet.MergedRegions, _range.Start.Col, _range.End.Col, _range.Start.Row, height));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            RowColumnShiftHelpers.AdjustRulesInsertShiftDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            // R21-defined-name-management-1: see the Shift-Right branch above.
            ShiftNamedRangesInBandDown(ctx.Workbook, _sheetId, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            // R86-meta-3: see ShiftSparklinesInBandDown for the move/grow rationale (mirrors
            // ShiftNamedRangesInBandDown immediately above, but for sheet.Sparklines).
            _sparklineSnapshot = CaptureSparklines(sheet);
            ShiftSparklinesInBandDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            InsertShiftDown(sheet, capture.Cells);
            // R71-commands-insert-delete-cells-4-2: see the Shift-Right branch above.
            InheritVacatedFormatShiftDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row);
            // R21-undo-redo-deep-1: see the Shift-Right branch above.
            _movedDestinationCells = capture.Cells.Count == 0
                ? null
                : capture.Cells.Select(c => new CellAddress(c.Address.Sheet, c.Address.Row + height, c.Address.Col)).ToList();

            var insertDownOp = new InsertCellsShiftDownOp(
                sheet.Name,
                _range.Start.Row, CellAddress.MaxRow,
                _range.Start.Col, _range.End.Col,
                _range.Start.Row, height);
            // O34: rewrite in-document hyperlink bookmark CONTENT (not just the dictionary key,
            // already shifted above by ShiftAnnotationsInBandDown) so a "Place in This Document"
            // link whose Bookmark text points at a cell inside the shifted band keeps pointing at
            // the same logical cell, matching whole-row/whole-column insert.
            _mutationSnapshot!.RewriteReferences(ctx.Workbook, sheet, insertDownOp);
        }

        // R98-commands-dependency-vacated-2: mirror InsertRowsCommand's
        // VacatedAddressesForRelocatedFormulaCells fix (InsertDeleteRowsCommand.cs) on this
        // band-scoped, shift-direction sibling. Every entry in capture.Cells physically relocates
        // from its captured Address to a new address (Col + width / Row + height) via
        // InsertShiftRight/Down above, always leaving the OLD address blank afterward -- Insert only
        // ever shifts cells further from the origin, so nothing can move back into a vacated slot.
        // _range.AllCells() below only happens to cover the OLD address for cells that originated
        // INSIDE the target range itself (that range's own footprint IS the newly-vacated band) --
        // a formula cell further along the same shiftRegion (e.g. beyond _range.End.Col for
        // Shift-Right, since the region extends all the way to CellAddress.MaxCol) relocates too,
        // but its old address was never surfaced. VacatedAddressesForRelocatedFormulaCells below
        // yields EVERY captured formula cell's original address unconditionally -- the ones already
        // covered by _range.AllCells() are harmless duplicates, de-duped by
        // BuildAffectedCellsForFormulaRewrite.
        _affectedCells = _mutationSnapshot!.BuildAffectedCells(
            _range.AllCells()
                .Concat(_movedDestinationCells ?? Enumerable.Empty<CellAddress>())
                .Concat(VacatedAddressesForRelocatedFormulaCells(_capturedCells ?? [])));
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    // R98-commands-dependency-vacated-2: shared by InsertCellsCommand and DeleteCellsCommand's Apply
    // (see call sites) -- yields the pre-shift Address of every captured formula cell, which is left
    // blank once the cell physically relocates (Insert) or is deleted/relocated (Delete).
    internal static IEnumerable<CellAddress> VacatedAddressesForRelocatedFormulaCells(
        IReadOnlyList<(CellAddress Address, Cell Cell)> capturedCells)
    {
        foreach (var (address, cell) in capturedCells)
        {
            if (cell.FormulaText is null)
                continue;

            yield return address;
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R96-commands-undo-affected-cells-2: RestoreFormulas below clears _mutationSnapshot!.FormulaTexts as its
        // last step, so capture its keys (the post-shift addresses of every stationary formula cell
        // whose text was rewritten by Apply) now, before that happens -- needed to recompute
        // _affectedCells at the end of this method.
        if (_mutationSnapshot is null) return;
        var formulaSnapshotAddressesBeforeRestore = _mutationSnapshot.RestoreRewrittenFormulas(ctx.Workbook);

        // R98-commands-dependency-vacated-2: capture the CURRENT (post-Apply, pre-Revert) address of
        // every relocated formula cell now, before _capturedCells is nulled at the end of this
        // method -- see VacatedAddressesAfterRevert below for why it must be surfaced.
        var vacatedAfterRevert = VacatedAddressesAfterRevert(_capturedCells ?? [], _movedDestinationCells).ToList();

        // ORDER IS REQUIRED: restore formulas before restoring cell positions.
        //
        // The formula snapshot is keyed by POST-Apply (shifted) addresses.
        // RestoreFormulas looks up cells at those shifted addresses and writes FormulaText back
        // in place on the live Cell object.  If Snapshot.Restore ran first, cells would be moved
        // back to original positions and the shifted-address lookup would find nothing — original
        // formula text would be permanently lost.
        //
        // Do NOT reorder. The undo-redo-undo convergence test in InsertDeleteCellsCommandTests
        // verifies that this sequence leaves the model identical to its initial state.
        _snapshot.Restore(ctx.GetSheet(_sheetId));
        _snapshot = null;

        _mutationSnapshot.RestoreCommonState(ctx.Workbook, sheet, restoreRulesInPlace: true);
        RestoreStyleOnlyEntries(sheet, _styleOnlySnapshot);
        RestoreSparklines(sheet, _sparklineSnapshot);

        // R96-commands-undo-affected-cells-2: recompute AffectedCells to reflect where every
        // relocated formula cell ACTUALLY ended up after this Revert -- its original, pre-shift
        // address (mirroring InsertRowsCommand/InsertColumnsCommand's own Revert-time recompute).
        // CommandBus.Undo reads this live property instead of the frozen forward payload.
        // R98-commands-dependency-vacated-2: also surface the address the Revert's own move-back
        // just vacated (vacatedAfterRevert, captured above) -- symmetric to the Apply-side fix.
        _affectedCells = _mutationSnapshot.BuildAffectedCells(
            RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress(_capturedCells ?? [])
                .Concat(formulaSnapshotAddressesBeforeRestore)
                .Concat(vacatedAfterRevert),
            includeRewrittenFormulaAddresses: false);
        _capturedCells = null;
    }

    // R98-commands-dependency-vacated-2: capturedCells and movedDestinationCells are built from the
    // SAME capture.Cells list in the SAME order by Apply (movedDestinationCells is an unfiltered
    // projection of every entry), so they are index-aligned here. For each captured formula cell,
    // movedDestinationCells[i] is the CURRENT (post-Apply) address Revert's move-back is about to
    // vacate.
    private static IEnumerable<CellAddress> VacatedAddressesAfterRevert(
        IReadOnlyList<(CellAddress Address, Cell Cell)> capturedCells,
        IReadOnlyList<CellAddress>? movedDestinationCells)
    {
        if (movedDestinationCells is null)
            yield break;

        var count = Math.Min(capturedCells.Count, movedDestinationCells.Count);
        for (var i = 0; i < count; i++)
        {
            if (capturedCells[i].Cell.FormulaText is null)
                continue;

            yield return movedDestinationCells[i];
        }
    }

    private void InsertShiftRight(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var width = _range.ColCount;
        var originalCells = RentOriginalCells(sheet, captured);
        // R21-undo-redo-deep-1: capture any live spill rooted at each moved cell BEFORE it is
        // cleared/moved, so a relocated dynamic-array anchor (e.g. SEQUENCE with no cell references,
        // whose formula text never changes on a shift) keeps spilling at its new address instead of
        // silently collapsing to a stale scalar (mirrors InsertDeleteRowsCommand.MoveCellsForInsert,
        // R20-array-dynamic-spill-1).
        var spillPayloads = new RangeValue?[captured.Count];
        for (var i = 0; i < captured.Count; i++)
            spillPayloads[i] = sheet.CaptureSpillForRelocate(captured[i].Address);
        try
        {
            foreach (var (address, _) in captured)
                sheet.ClearCell(address);

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                var newAddress = new CellAddress(address.Sheet, address.Row, address.Col + width);
                sheet.SetCell(newAddress, originalCells[i]);
                if (spillPayloads[i] is { } payload)
                    sheet.SetSpillRange(newAddress, payload);
            }
        }
        finally
        {
            ReturnOriginalCells(originalCells);
        }
    }

    private void InsertShiftDown(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var height = _range.RowCount;
        var originalCells = RentOriginalCells(sheet, captured);
        // R21-undo-redo-deep-1: see InsertShiftRight above.
        var spillPayloads = new RangeValue?[captured.Count];
        for (var i = 0; i < captured.Count; i++)
            spillPayloads[i] = sheet.CaptureSpillForRelocate(captured[i].Address);
        try
        {
            foreach (var (address, _) in captured)
                sheet.ClearCell(address);

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                var newAddress = new CellAddress(address.Sheet, address.Row + height, address.Col);
                sheet.SetCell(newAddress, originalCells[i]);
                if (spillPayloads[i] is { } payload)
                    sheet.SetSpillRange(newAddress, payload);
            }
        }
        finally
        {
            ReturnOriginalCells(originalCells);
        }
    }

    // ── Vacated-band format inheritance (R71-commands-insert-delete-cells-4-2) ───────────────
    // Excel's Insert Cells default ("Insert Options" smart-tag) copies the formatting of the cell
    // immediately to the left (Shift-Right) or above (Shift-Down) of the newly-vacated band into
    // every new blank cell, instead of leaving it at General/default formatting. Each row
    // (Shift-Right) / column (Shift-Down) has its own neighbor, so a multi-row/column insert can
    // pick up a different inherited format per row/column.

    /// <summary>
    /// For each row in [bandStartRow..bandEndRow], applies the StyleId of the cell one column to
    /// the left of <paramref name="startCol"/> to every new blank cell in [startCol..endCol] on
    /// that row (a no-op if there is no column to the left, i.e. the band starts at column A, or
    /// the neighbor is unformatted).
    /// </summary>
    private static void InheritVacatedFormatShiftRight(Sheet sheet, uint bandStartRow, uint bandEndRow, uint startCol, uint endCol)
    {
        if (startCol <= 1)
            return;

        var neighborCol = startCol - 1;
        for (var row = bandStartRow; row <= bandEndRow; row++)
        {
            if (GetEffectiveStyleId(sheet, row, neighborCol) is not { } style)
                continue;

            for (var col = startCol; col <= endCol; col++)
                sheet.SetStyleOnly(row, col, style);
        }
    }

    /// <summary>Shift-Down analogue of <see cref="InheritVacatedFormatShiftRight"/>: inherits from the row above.</summary>
    private static void InheritVacatedFormatShiftDown(Sheet sheet, uint bandStartCol, uint bandEndCol, uint startRow, uint endRow)
    {
        if (startRow <= 1)
            return;

        var neighborRow = startRow - 1;
        for (var col = bandStartCol; col <= bandEndCol; col++)
        {
            if (GetEffectiveStyleId(sheet, neighborRow, col) is not { } style)
                continue;

            for (var row = startRow; row <= endRow; row++)
                sheet.SetStyleOnly(row, col, style);
        }
    }

    /// <summary>
    /// Returns the effective StyleId of a cell for format-inheritance purposes: its live
    /// Cell.StyleId if occupied (unless that is the default style), else its style-only override
    /// if any, else null (fully default — nothing to propagate).
    /// </summary>
    private static StyleId? GetEffectiveStyleId(Sheet sheet, uint row, uint col)
    {
        var cell = sheet.GetCell(row, col);
        if (cell is not null)
            return cell.StyleId == StyleId.Default ? null : cell.StyleId;

        return sheet.GetStyleOnly(row, col);
    }

    /// <summary>
    /// R23-array-formula-legacy-cse-1: for every legacy CSE array or dynamic-array spill that has at
    /// least one member inside <paramref name="region"/>, collects only the members that fall inside
    /// the region. Feeding this into <see cref="CommandGuards.RejectIfSplitsArray"/> allows an array
    /// whose full extent lies entirely inside the shifted band (it moves — or is deleted — as one
    /// atomic unit, e.g. inserting cells above a spilling anchor within its own column) while
    /// rejecting an array straddling the band boundary (some members would shift/delete, others
    /// would stay put), matching Excel's "You cannot change part of an array" rule.
    /// </summary>
    internal static List<CellAddress> ArrayMembersWithinShiftRegion(Sheet sheet, CellShiftRegion region)
    {
        var result = new List<CellAddress>();
        if (!sheet.HasArrayOrSpillMembers)
            return result;

        var seenAnchors = new HashSet<CellAddress>();
        foreach (var (row, col) in sheet.GetOccupiedCellMap().Keys)
        {
            var address = new CellAddress(sheet.Id, row, col);
            if (!sheet.TryGetArrayExtent(address, out var anchor, out var rows, out var cols))
                continue;
            if (!seenAnchors.Add(anchor))
                continue;

            for (var r = 0u; r < rows; r++)
                for (var c = 0u; c < cols; c++)
                {
                    var member = new CellAddress(anchor.Sheet, anchor.Row + r, anchor.Col + c);
                    if (region.Contains(member))
                        result.Add(member);
                }
        }

        return result;
    }

    internal static CellShiftSnapshot CaptureCells(Sheet sheet, CellShiftRegion region)
        => CaptureCellsForMove(sheet, region).Snapshot;

    internal static CellShiftCapture CaptureCellsForDelete(
        Sheet sheet, CellShiftRegion region, Func<CellAddress, CellAddress?>? currentAddressOf = null)
        => CaptureCellsForMove(sheet, region, currentAddressOf);

    private static CellShiftCapture CaptureCellsForMove(
        Sheet sheet, CellShiftRegion region, Func<CellAddress, CellAddress?>? currentAddressOf = null)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        var snapshotCells = new List<(CellAddress Address, Cell Cell)>(
            CountCellsInRegion(occupiedCells, region));
        uint maxRow = 0;
        uint maxCol = 0;

        foreach (var ((row, col), cell) in occupiedCells)
        {
            if (!region.Contains(row, col))
                continue;

            if (row > maxRow)
                maxRow = row;
            if (col > maxCol)
                maxCol = col;

            var address = new CellAddress(sheet.Id, row, col);
            snapshotCells.Add((address, cell.Clone()));
        }

        return new CellShiftCapture(
            new CellShiftSnapshot(region, snapshotCells, currentAddressOf),
            snapshotCells,
            maxRow,
            maxCol);
    }

    private static int CountCellsInRegion(IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells, CellShiftRegion region)
    {
        var count = 0;
        foreach (var ((row, col), _) in occupiedCells)
        {
            if (region.Contains(row, col))
                count++;
        }

        return count;
    }

    internal static Cell[] RentOriginalCells(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        if (captured.Count == 0)
            return Array.Empty<Cell>();

        var originalCells = ArrayPool<Cell>.Shared.Rent(captured.Count);
        for (var i = 0; i < captured.Count; i++)
            originalCells[i] = sheet.GetCell(captured[i].Address)!;
        return originalCells;
    }

    internal static void ReturnOriginalCells(Cell[] originalCells)
    {
        if (originalCells.Length != 0)
            ArrayPool<Cell>.Shared.Return(originalCells, clearArray: true);
    }

    internal static void ClearRange(Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                sheet.ClearCell(row, col);
        }
    }

    // ── Merge guard ──────────────────────────────────────────────────────────

    private enum ShiftAxis { Row, Column }

    /// <summary>
    /// Returns true if any merged region partially overlaps the shift band.
    /// "Partial overlap" = the region intersects the band but is not fully contained within it.
    /// Merges fully outside the band or fully inside are fine.
    /// </summary>
    private static bool MergeWouldBeTorn(Sheet sheet, CellShiftRegion band, ShiftAxis shiftDirection)
    {
        foreach (var merge in sheet.MergedRegions)
        {
            if (!MergeIntersectsBand(merge, band))
                continue;

            if (IsMergeFullyInsideBand(merge, band))
                continue;  // fully inside: it moves with the shift, no tear

            if (shiftDirection == ShiftAxis.Row)
            {
                // Shift-down band: constrained by columns [band.StartCol..band.EndCol]
                // A merge is torn if it straddles the column boundary of the band
                // (i.e., partially overlaps the row band but has columns outside the band).
                if (merge.Start.Col < band.StartCol || merge.End.Col > band.EndCol)
                    return true;
                // Merge spans rows that are partially inside and partially outside the shift zone
                // (band.StartRow is the first shifted row; merge straddles that boundary)
                if (merge.Start.Row < band.StartRow && merge.End.Row >= band.StartRow)
                    return true;
            }
            else
            {
                // Shift-right band: constrained by rows [band.StartRow..band.EndRow]
                // A merge is torn if it straddles the row boundary of the band
                if (merge.Start.Row < band.StartRow || merge.End.Row > band.EndRow)
                    return true;
                // Merge spans cols that are partially inside and partially outside the shift zone
                if (merge.Start.Col < band.StartCol && merge.End.Col >= band.StartCol)
                    return true;
            }
        }

        return false;
    }

    private static bool MergeIntersectsBand(GridRange merge, CellShiftRegion band) =>
        merge.Start.Row <= band.EndRow && merge.End.Row >= band.StartRow &&
        merge.Start.Col <= band.EndCol && merge.End.Col >= band.StartCol;

    private static bool IsMergeFullyInsideBand(GridRange merge, CellShiftRegion band) =>
        merge.Start.Row >= band.StartRow && merge.End.Row <= band.EndRow &&
        merge.Start.Col >= band.StartCol && merge.End.Col <= band.EndCol;

    // ── Merge-aware edge-of-sheet guard (R38-commands-insert-delete-shift-2-2) ──

    /// <summary>
    /// Returns the largest End.Col among merged regions that a Shift-Right insert (band rows
    /// [<paramref name="bandStartRow"/>..<paramref name="bandEndRow"/>], insert point
    /// <paramref name="insertBeforeCol"/>) would relocate — i.e. merges fully inside the band's row
    /// span whose End.Col is at/after the insert point, matching exactly the merges
    /// <see cref="AdjustMergesShiftRight"/> would shift or expand — or 0 if none. Used to extend the
    /// value-cell-only overflow guard in <see cref="InsertCellsCommand.Apply"/> so a blank merge (no
    /// Cell entries, invisible to <c>CaptureCellsForMove</c>'s MaxCol) abutting the last column
    /// blocks the insert instead of being silently truncated by AdjustMergesShiftRight's clamp.
    /// </summary>
    private static uint MaxAffectedMergeEndColForShiftRight(
        Sheet sheet, uint bandStartRow, uint bandEndRow, uint insertBeforeCol)
    {
        uint maxEndCol = 0;
        foreach (var merge in sheet.MergedRegions)
        {
            if (merge.Start.Row < bandStartRow || merge.End.Row > bandEndRow)
                continue;
            if (merge.End.Col < insertBeforeCol)
                continue;
            if (merge.End.Col > maxEndCol)
                maxEndCol = merge.End.Col;
        }
        return maxEndCol;
    }

    /// <summary>Shift-Down analogue of <see cref="MaxAffectedMergeEndColForShiftRight"/> for rows.</summary>
    private static uint MaxAffectedMergeEndRowForShiftDown(
        Sheet sheet, uint bandStartCol, uint bandEndCol, uint insertBeforeRow)
    {
        uint maxEndRow = 0;
        foreach (var merge in sheet.MergedRegions)
        {
            if (merge.Start.Col < bandStartCol || merge.End.Col > bandEndCol)
                continue;
            if (merge.End.Row < insertBeforeRow)
                continue;
            if (merge.End.Row > maxEndRow)
                maxEndRow = merge.End.Row;
        }
        return maxEndRow;
    }

    /// <summary>
    /// R71-commands-insert-delete-cells-4-1: returns the maximum anchored column, among
    /// Comments/CommentAuthors/ShownComments/ThreadedComments/Hyperlinks/HyperlinkMetadata/
    /// RichTextRuns and style-only (formatted-but-empty) cells, that
    /// <see cref="ShiftAnnotationsInBandRight{TValue}"/>/<see cref="ShiftStyleOnlyInBandRight"/>
    /// below would actually relocate (row in [bandStartRow..bandEndRow], col &gt;= fromCol) — or 0
    /// if none. These entries carry no Cell object and so are invisible to both
    /// CaptureCellsForMove's MaxCol (value-bearing cells only) and
    /// MaxAffectedMergeEndColForShiftRight (merges only); without this, one anchored at the last
    /// column would sail past the overflow guard in <see cref="InsertCellsCommand.Apply"/> and
    /// then be silently relocated past CellAddress.MaxCol.
    /// </summary>
    private static uint MaxAnnotationColInBandForShiftRight(
        Sheet sheet, uint bandStartRow, uint bandEndRow, uint fromCol)
    {
        uint maxCol = 0;

        void Consider(CellAddress addr)
        {
            if (addr.Row >= bandStartRow && addr.Row <= bandEndRow && addr.Col >= fromCol && addr.Col > maxCol)
                maxCol = addr.Col;
        }

        foreach (var addr in sheet.Comments.Keys) Consider(addr);
        foreach (var addr in sheet.CommentAuthors.Keys) Consider(addr);
        foreach (var addr in sheet.ShownComments) Consider(addr);
        foreach (var addr in sheet.ThreadedComments.Keys) Consider(addr);
        foreach (var addr in sheet.Hyperlinks.Keys) Consider(addr);
        foreach (var addr in sheet.HyperlinkMetadata.Keys) Consider(addr);
        foreach (var addr in sheet.RichTextRuns.Keys) Consider(addr);
        foreach (var addr in sheet.CellPhoneticGuides.Keys) Consider(addr);

        if (sheet.HasStyleOnlyCells)
        {
            foreach (var (key, _) in sheet.GetStyleOnlyEntries())
            {
                if (key.Row >= bandStartRow && key.Row <= bandEndRow && key.Col >= fromCol && key.Col > maxCol)
                    maxCol = key.Col;
            }
        }

        return maxCol;
    }

    /// <summary>Shift-Down analogue of <see cref="MaxAnnotationColInBandForShiftRight"/> for rows.</summary>
    private static uint MaxAnnotationRowInBandForShiftDown(
        Sheet sheet, uint bandStartCol, uint bandEndCol, uint fromRow)
    {
        uint maxRow = 0;

        void Consider(CellAddress addr)
        {
            if (addr.Col >= bandStartCol && addr.Col <= bandEndCol && addr.Row >= fromRow && addr.Row > maxRow)
                maxRow = addr.Row;
        }

        foreach (var addr in sheet.Comments.Keys) Consider(addr);
        foreach (var addr in sheet.CommentAuthors.Keys) Consider(addr);
        foreach (var addr in sheet.ShownComments) Consider(addr);
        foreach (var addr in sheet.ThreadedComments.Keys) Consider(addr);
        foreach (var addr in sheet.Hyperlinks.Keys) Consider(addr);
        foreach (var addr in sheet.HyperlinkMetadata.Keys) Consider(addr);
        foreach (var addr in sheet.RichTextRuns.Keys) Consider(addr);
        foreach (var addr in sheet.CellPhoneticGuides.Keys) Consider(addr);

        if (sheet.HasStyleOnlyCells)
        {
            foreach (var (key, _) in sheet.GetStyleOnlyEntries())
            {
                if (key.Col >= bandStartCol && key.Col <= bandEndCol && key.Row >= fromRow && key.Row > maxRow)
                    maxRow = key.Row;
            }
        }

        return maxRow;
    }

    // ── Merge adjustment for shift-right ─────────────────────────────────────

    private static IReadOnlyList<GridRange> AdjustMergesShiftRight(
        IEnumerable<GridRange> mergedRegions,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count)
    {
        var result = new List<GridRange>();
        foreach (var merge in mergedRegions)
        {
            // Only merges fully within the band rows are affected
            if (merge.Start.Row < bandStartRow || merge.End.Row > bandEndRow)
            {
                result.Add(merge);
                continue;
            }

            // Shift the merge if it starts at or right of the insert point
            GridRange shifted;
            if (merge.Start.Col >= insertBeforeCol)
            {
                shifted = new GridRange(
                    new CellAddress(merge.Start.Sheet, merge.Start.Row, merge.Start.Col + count),
                    new CellAddress(merge.End.Sheet, merge.End.Row, merge.End.Col + count));
            }
            else if (merge.End.Col >= insertBeforeCol)
            {
                // Merge spans the insertion point: expand it
                shifted = new GridRange(
                    merge.Start,
                    new CellAddress(merge.End.Sheet, merge.End.Row, merge.End.Col + count));
            }
            else
            {
                result.Add(merge);
                continue;
            }

            // R17-meta-2: a merged region whose entire shifted position falls past the last
            // column runs off the sheet and is dropped, mirroring
            // RowColumnShiftHelpers.TryInsertColumnsIntoMergedRegion; one whose right edge merely
            // overshoots is clamped back to the last column instead of left out-of-bounds.
            if (shifted.Start.Col > CellAddress.MaxCol)
                continue;

            result.Add(shifted.End.Col > CellAddress.MaxCol
                ? new GridRange(shifted.Start, new CellAddress(shifted.End.Sheet, shifted.End.Row, CellAddress.MaxCol))
                : shifted);
        }

        return result;
    }

    // ── Merge adjustment for shift-down ──────────────────────────────────────

    private static IReadOnlyList<GridRange> AdjustMergesShiftDown(
        IEnumerable<GridRange> mergedRegions,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count)
    {
        var result = new List<GridRange>();
        foreach (var merge in mergedRegions)
        {
            // Only merges fully within the band cols are affected
            if (merge.Start.Col < bandStartCol || merge.End.Col > bandEndCol)
            {
                result.Add(merge);
                continue;
            }

            GridRange shifted;
            if (merge.Start.Row >= insertBeforeRow)
            {
                shifted = new GridRange(
                    new CellAddress(merge.Start.Sheet, merge.Start.Row + count, merge.Start.Col),
                    new CellAddress(merge.End.Sheet, merge.End.Row + count, merge.End.Col));
            }
            else if (merge.End.Row >= insertBeforeRow)
            {
                shifted = new GridRange(
                    merge.Start,
                    new CellAddress(merge.End.Sheet, merge.End.Row + count, merge.End.Col));
            }
            else
            {
                result.Add(merge);
                continue;
            }

            // R17-meta-2: a merged region whose entire shifted position falls past the last
            // row runs off the sheet and is dropped, mirroring
            // RowColumnShiftHelpers.TryInsertColumnsIntoMergedRegion (row analogue); one whose
            // bottom edge merely overshoots is clamped back to the last row instead of left
            // out-of-bounds.
            if (shifted.Start.Row > CellAddress.MaxRow)
                continue;

            result.Add(shifted.End.Row > CellAddress.MaxRow
                ? new GridRange(shifted.Start, new CellAddress(shifted.End.Sheet, CellAddress.MaxRow, shifted.End.Col))
                : shifted);
        }

        return result;
    }

    // ── Band-constrained annotation shift helpers ─────────────────────────────

    /// <summary>Shift-right: move annotations in rows [bandStartRow..bandEndRow] at col >= fromCol rightward by count.</summary>
    private static void ShiftAnnotationsInBandRight<TValue>(
        Dictionary<CellAddress, TValue> dict,
        uint bandStartRow, uint bandEndRow,
        uint fromCol, uint count)
    {
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in dict)
        {
            var addr = pair.Key;
            if (addr.Row >= bandStartRow && addr.Row <= bandEndRow && addr.Col >= fromCol)
                (shifted ??= []).Add(pair);
        }

        if (shifted is null) return;

        foreach (var (addr, _) in shifted)
            dict.Remove(addr);
        foreach (var (addr, value) in shifted)
        {
            // R71-commands-insert-delete-cells-4-1: belt-and-suspenders — the Apply-time overflow
            // guard (MaxAnnotationColInBandForShiftRight) should already have rejected any insert
            // that would push one of these past the last column, but skip rather than store an
            // unreachable/un-saveable entry past CellAddress.MaxCol if that guard is ever bypassed,
            // mirroring AdjustMergesShiftRight's "if (shifted.Start.Col > CellAddress.MaxCol) continue;".
            var shiftedCol = addr.Col + count;
            if (shiftedCol > CellAddress.MaxCol)
                continue;
            dict[new CellAddress(addr.Sheet, addr.Row, shiftedCol)] = value;
        }
    }

    /// <summary>Shift-down: move annotations in cols [bandStartCol..bandEndCol] at row >= fromRow downward by count.</summary>
    private static void ShiftAnnotationsInBandDown<TValue>(
        Dictionary<CellAddress, TValue> dict,
        uint bandStartCol, uint bandEndCol,
        uint fromRow, uint count)
    {
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in dict)
        {
            var addr = pair.Key;
            if (addr.Col >= bandStartCol && addr.Col <= bandEndCol && addr.Row >= fromRow)
                (shifted ??= []).Add(pair);
        }

        if (shifted is null) return;

        foreach (var (addr, _) in shifted)
            dict.Remove(addr);
        foreach (var (addr, value) in shifted)
        {
            // R71-commands-insert-delete-cells-4-1: see ShiftAnnotationsInBandRight above.
            var shiftedRow = addr.Row + count;
            if (shiftedRow > CellAddress.MaxRow)
                continue;
            dict[new CellAddress(addr.Sheet, shiftedRow, addr.Col)] = value;
        }
    }

    // J17: HashSet<CellAddress> counterparts of ShiftAnnotationsInBandRight/Down above, used for
    // Sheet.ShownComments (the address-keyed "pinned note" set), which must shift within the same
    // band as Comments/CommentAuthors or a pinned note's box would render at a stale address.

    /// <summary>Shift-right: move set entries in rows [bandStartRow..bandEndRow] at col >= fromCol rightward by count.</summary>
    private static void ShiftAnnotationsSetInBandRight(
        HashSet<CellAddress> addresses,
        uint bandStartRow, uint bandEndRow,
        uint fromCol, uint count)
    {
        List<CellAddress>? shifted = null;
        foreach (var addr in addresses)
        {
            if (addr.Row >= bandStartRow && addr.Row <= bandEndRow && addr.Col >= fromCol)
                (shifted ??= []).Add(addr);
        }

        if (shifted is null) return;

        foreach (var addr in shifted)
            addresses.Remove(addr);
        foreach (var addr in shifted)
        {
            // R71-commands-insert-delete-cells-4-1: see ShiftAnnotationsInBandRight above.
            var shiftedCol = addr.Col + count;
            if (shiftedCol > CellAddress.MaxCol)
                continue;
            addresses.Add(new CellAddress(addr.Sheet, addr.Row, shiftedCol));
        }
    }

    /// <summary>Shift-down: move set entries in cols [bandStartCol..bandEndCol] at row >= fromRow downward by count.</summary>
    private static void ShiftAnnotationsSetInBandDown(
        HashSet<CellAddress> addresses,
        uint bandStartCol, uint bandEndCol,
        uint fromRow, uint count)
    {
        List<CellAddress>? shifted = null;
        foreach (var addr in addresses)
        {
            if (addr.Col >= bandStartCol && addr.Col <= bandEndCol && addr.Row >= fromRow)
                (shifted ??= []).Add(addr);
        }

        if (shifted is null) return;

        foreach (var addr in shifted)
            addresses.Remove(addr);
        foreach (var addr in shifted)
        {
            // R71-commands-insert-delete-cells-4-1: see ShiftAnnotationsInBandRight above.
            var shiftedRow = addr.Row + count;
            if (shiftedRow > CellAddress.MaxRow)
                continue;
            addresses.Add(new CellAddress(addr.Sheet, shiftedRow, addr.Col));
        }
    }

    // ── Style-only (formatted-but-empty) cell shift helpers (R52-commands-clear-delete-3-1) ──
    // Style-only entries live in Sheet's own row/col-keyed store (Sheet.StyleOnly.cs), entirely
    // separate from sheet.GetOccupiedCellMap() (which CaptureCellsForMove/CaptureCellsForDelete
    // use and which only sees value/formula-bearing Cell entries). Without these helpers, a
    // fill/border applied to an empty cell was invisible to this band-scoped command and got
    // silently dropped (via Sheet.SetCell's ClearStyleOnly side-effect on whatever cell later
    // landed at that address) instead of moving with the rest of the band, unlike the
    // whole-row/whole-column insert/delete family (RowColumnShiftHelpers.AddressState.cs's
    // CaptureStyleOnlyEntries/ApplyShiftedStyleOnlyEntries), which already handles this.

    /// <summary>Captures every style-only entry in the sheet (row/col/style triples) for full restore on undo.</summary>
    internal static List<(uint Row, uint Col, StyleId StyleId)>? CaptureStyleOnlyEntries(Sheet sheet)
    {
        if (!sheet.HasStyleOnlyCells)
            return null;

        var entries = new List<(uint Row, uint Col, StyleId StyleId)>(sheet.StyleOnlyCellCount);
        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
            entries.Add((key.Row, key.Col, styleId));
        return entries;
    }

    /// <summary>Restores a full pre-Apply style-only snapshot captured by <see cref="CaptureStyleOnlyEntries"/>.</summary>
    internal static void RestoreStyleOnlyEntries(Sheet sheet, List<(uint Row, uint Col, StyleId StyleId)>? entries)
    {
        sheet.ClearStyleOnlyEntries();
        if (entries is null)
            return;

        foreach (var (row, col, styleId) in entries)
            sheet.SetStyleOnly(row, col, styleId);
    }

    /// <summary>Shift-right: move style-only entries in rows [bandStartRow..bandEndRow] at col >= fromCol rightward by count.</summary>
    internal static void ShiftStyleOnlyInBandRight(Sheet sheet, uint bandStartRow, uint bandEndRow, uint fromCol, uint count)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        List<(uint Row, uint Col, StyleId StyleId)>? shifted = null;
        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
        {
            if (key.Row >= bandStartRow && key.Row <= bandEndRow && key.Col >= fromCol)
                (shifted ??= []).Add((key.Row, key.Col, styleId));
        }

        if (shifted is null) return;

        foreach (var (row, col, _) in shifted)
            sheet.ClearStyleOnly(row, col);
        foreach (var (row, col, styleId) in shifted)
        {
            // R71-commands-insert-delete-cells-4-1: see ShiftAnnotationsInBandRight above.
            var shiftedCol = col + count;
            if (shiftedCol > CellAddress.MaxCol)
                continue;
            sheet.SetStyleOnly(row, shiftedCol, styleId);
        }
    }

    /// <summary>Shift-down: move style-only entries in cols [bandStartCol..bandEndCol] at row >= fromRow downward by count.</summary>
    internal static void ShiftStyleOnlyInBandDown(Sheet sheet, uint bandStartCol, uint bandEndCol, uint fromRow, uint count)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        List<(uint Row, uint Col, StyleId StyleId)>? shifted = null;
        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
        {
            if (key.Col >= bandStartCol && key.Col <= bandEndCol && key.Row >= fromRow)
                (shifted ??= []).Add((key.Row, key.Col, styleId));
        }

        if (shifted is null) return;

        foreach (var (row, col, _) in shifted)
            sheet.ClearStyleOnly(row, col);
        foreach (var (row, col, styleId) in shifted)
        {
            // R71-commands-insert-delete-cells-4-1: see ShiftAnnotationsInBandRight above.
            var shiftedRow = row + count;
            if (shiftedRow > CellAddress.MaxRow)
                continue;
            sheet.SetStyleOnly(shiftedRow, col, styleId);
        }
    }

    // ── AutoFilter / structured-table guard (finding G3) ───────────────────────

    /// <summary>
    /// Returns true if the worksheet AutoFilter range, any structured table's range, or any
    /// workbook PivotTable's SourceRange overlaps the band-scoped Insert/Delete Cells shift region
    /// at all. Band-scoped cell shifts (unlike whole-row or whole-column insert/delete, which call
    /// RowColumnShiftHelpers.CaptureAddressBearingState / ShiftAddressBearingRows*Up/Down) cannot
    /// safely relocate axis-wide state such as <c>Sheet.AutoFilter.Reference</c>,
    /// <c>Sheet.FilterHiddenRows</c>, or a PivotTable's internal source layout — either the whole
    /// table/filter/pivot range would need to move (which this command has no way to express, since
    /// it only shifts a bounded row/column band, not a whole row or column) or that state goes stale.
    /// Excel itself refuses "Insert/Delete Cells" when it would disturb a table or pivot, so mirror
    /// that instead of silently corrupting filter/pivot state.
    /// R84-commands-clear-delete-5-2: the PivotTable check is workbook-wide (not just
    /// <paramref name="sheet"/>.PivotTables) because a pivot's SourceRange can reference a
    /// *different* sheet than the one it is placed on (e.g. a pivot built from Sheet1!A1:D100 but
    /// placed on Sheet2, Excel's default "New Worksheet" destination) — mirrors the same N33
    /// workbook-wide rationale in RowColumnShiftHelpers.AddressState.cs's CapturePivotTables.
    /// </summary>
    internal static bool AutoFilterOverlapsBand(Workbook workbook, Sheet sheet, CellShiftRegion band)
    {
        if (AutoFilterRangeResolver.TryGetWorksheetAutoFilterRange(sheet, out var autoFilterRange) &&
            RangeIntersectsBand(autoFilterRange, band))
        {
            return true;
        }

        foreach (var table in sheet.StructuredTables)
        {
            if (RangeIntersectsBand(table.Range, band))
                return true;
        }

        foreach (var hostSheet in workbook.Sheets)
        {
            foreach (var pivotTable in hostSheet.PivotTables)
            {
                if (pivotTable.SourceRange.Start.Sheet == sheet.Id && RangeIntersectsBand(pivotTable.SourceRange, band))
                    return true;
            }
        }

        return false;
    }

    private static bool RangeIntersectsBand(GridRange range, CellShiftRegion band) =>
        range.Start.Row <= band.EndRow && range.End.Row >= band.StartRow &&
        range.Start.Col <= band.EndCol && range.End.Col >= band.StartCol;

    // ── Named-range band-scoped shift/delete helpers (R21-defined-name-management-1/-3) ──────
    // Mirrors RowColumnShiftHelpers.Adjust*ShiftRight/Down/Left/Up for CF/DV rules (see
    // RowColumnShiftHelpers.Rules.cs), but for workbook.NamedRanges/ScopedNamedRanges — the plain
    // GridRange-backed defined names that whole-row/whole-column insert/delete already shift via
    // RowColumnShiftHelpers.ShiftNamedRangeRowsUp/Down/ColumnsUp/Down. This band-scoped Insert/Delete
    // Cells path never touched NamedRanges/ScopedNamedRanges at all before this fix, so a name
    // pointing into the shifted band went silently stale.

    /// <summary>
    /// Insert Shift Right: named ranges fully inside [bandStartRow..bandEndRow] that touch or
    /// straddle the insert point are adjusted. A range straddling <paramref name="insertBeforeCol"/>
    /// (Start.Col &lt; insertBeforeCol &lt;= End.Col) GROWS its End.Col by <paramref name="count"/>
    /// while Start.Col stays put (R38-commands-insert-delete-shift-2-1 — matches Excel's own
    /// reference-adjustment behavior, mirroring <see cref="RowColumnShiftHelpers.RewriteRuleFormulas"/>'s
    /// sibling fix for CF/DV rule ranges); a range fully at/right of the insert point shifts both
    /// endpoints right. Ranges outside the band, or entirely left of the insert point, are unchanged.
    /// </summary>
    internal static void ShiftNamedRangesInBandRight(
        Workbook workbook, SheetId sheetId,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow) continue;
            if (range.End.Col < insertBeforeCol) continue;
            var newStartCol = range.Start.Col < insertBeforeCol
                ? range.Start.Col
                : Math.Min(range.Start.Col + count, CellAddress.MaxCol);
            workbook.NamedRanges[name] = new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
                new CellAddress(range.End.Sheet, range.End.Row, Math.Min(range.End.Col + count, CellAddress.MaxCol)));
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow) continue;
            if (range.End.Col < insertBeforeCol) continue;
            var newStartCol = range.Start.Col < insertBeforeCol
                ? range.Start.Col
                : Math.Min(range.Start.Col + count, CellAddress.MaxCol);
            workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
            workbook.DefineNamedRange(name, new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
                new CellAddress(range.End.Sheet, range.End.Row, Math.Min(range.End.Col + count, CellAddress.MaxCol))), metadata, scopeSheet);
        }
    }

    /// <summary>
    /// Insert Shift Down: named ranges fully inside [bandStartCol..bandEndCol] that touch or
    /// straddle the insert point are adjusted, mirroring <see cref="ShiftNamedRangesInBandRight"/>
    /// for rows — a range straddling <paramref name="insertBeforeRow"/> grows its End.Row while
    /// Start.Row stays put (R38-commands-insert-delete-shift-2-1); a range fully at/below the insert
    /// point shifts both endpoints down.
    /// </summary>
    internal static void ShiftNamedRangesInBandDown(
        Workbook workbook, SheetId sheetId,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol) continue;
            if (range.End.Row < insertBeforeRow) continue;
            var newStartRow = range.Start.Row < insertBeforeRow
                ? range.Start.Row
                : Math.Min(range.Start.Row + count, CellAddress.MaxRow);
            workbook.NamedRanges[name] = new GridRange(
                new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
                new CellAddress(range.End.Sheet, Math.Min(range.End.Row + count, CellAddress.MaxRow), range.End.Col));
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol) continue;
            if (range.End.Row < insertBeforeRow) continue;
            var newStartRow = range.Start.Row < insertBeforeRow
                ? range.Start.Row
                : Math.Min(range.Start.Row + count, CellAddress.MaxRow);
            workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
            workbook.DefineNamedRange(name, new GridRange(
                new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
                new CellAddress(range.End.Sheet, Math.Min(range.End.Row + count, CellAddress.MaxRow), range.End.Col)), metadata, scopeSheet);
        }
    }

    /// <summary>
    /// Delete Shift Left: named ranges fully inside [bandStartRow..bandEndRow] are removed when
    /// entirely within the deleted columns — R21-defined-name-management-3: Excel would turn such a
    /// name into #REF!, but GridRange cannot represent that sentinel here (see Workbook.cs's
    /// RemoveNamedRangesForSheet, which drops a dangling global/scoped range for the same reason),
    /// so it is dropped instead of being left pointing at whatever now occupies the old address —
    /// or shifted left by <paramref name="count"/> when entirely right of the deleted columns.
    /// R38-commands-insert-delete-shift-2-1: a range straddling the delete boundary now SHRINKS to
    /// its surviving portion (mirroring RowColumnShiftHelpers.Rules.cs's TranslateRangeDeleteLeft
    /// fix for CF/DV rule ranges) instead of being left stale.
    /// </summary>
    internal static void DeleteNamedRangesInBandLeft(
        Workbook workbook, SheetId sheetId,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow) continue;
            if (range.End.Col < deletedStartCol) continue;
            if (range.Start.Col >= deletedStartCol && range.End.Col <= deletedEndCol)
            {
                workbook.RemoveNamedRange(name);
                continue;
            }
            if (range.Start.Col > deletedEndCol)
            {
                workbook.NamedRanges[name] = new GridRange(
                    new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - count),
                    new CellAddress(range.End.Sheet, range.End.Row, range.End.Col - count));
                continue;
            }
            // Straddles the delete boundary: shrink to the surviving portion.
            var newStartCol = range.Start.Col < deletedStartCol ? range.Start.Col : deletedStartCol;
            var newEndCol = range.End.Col > deletedEndCol ? range.End.Col - count : deletedStartCol - 1;
            workbook.NamedRanges[name] = new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
                new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow) continue;
            if (range.End.Col < deletedStartCol) continue;
            if (range.Start.Col >= deletedStartCol && range.End.Col <= deletedEndCol)
            {
                workbook.RemoveScopedNamedRange(name, scopeSheet);
                continue;
            }
            if (range.Start.Col > deletedEndCol)
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, new GridRange(
                    new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - count),
                    new CellAddress(range.End.Sheet, range.End.Row, range.End.Col - count)), metadata, scopeSheet);
                continue;
            }
            // Straddles the delete boundary: shrink to the surviving portion.
            var newStartCol = range.Start.Col < deletedStartCol ? range.Start.Col : deletedStartCol;
            var newEndCol = range.End.Col > deletedEndCol ? range.End.Col - count : deletedStartCol - 1;
            workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata2);
            workbook.DefineNamedRange(name, new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
                new CellAddress(range.End.Sheet, range.End.Row, newEndCol)), metadata2, scopeSheet);
        }
    }

    /// <summary>Delete Shift Up: analogous to <see cref="DeleteNamedRangesInBandLeft"/> for rows.</summary>
    internal static void DeleteNamedRangesInBandUp(
        Workbook workbook, SheetId sheetId,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol) continue;
            if (range.End.Row < deletedStartRow) continue;
            if (range.Start.Row >= deletedStartRow && range.End.Row <= deletedEndRow)
            {
                workbook.RemoveNamedRange(name);
                continue;
            }
            if (range.Start.Row > deletedEndRow)
            {
                workbook.NamedRanges[name] = new GridRange(
                    new CellAddress(range.Start.Sheet, range.Start.Row - count, range.Start.Col),
                    new CellAddress(range.End.Sheet, range.End.Row - count, range.End.Col));
                continue;
            }
            // Straddles the delete boundary: shrink to the surviving portion.
            var newStartRow = range.Start.Row < deletedStartRow ? range.Start.Row : deletedStartRow;
            var newEndRow = range.End.Row > deletedEndRow ? range.End.Row - count : deletedStartRow - 1;
            workbook.NamedRanges[name] = new GridRange(
                new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
                new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol) continue;
            if (range.End.Row < deletedStartRow) continue;
            if (range.Start.Row >= deletedStartRow && range.End.Row <= deletedEndRow)
            {
                workbook.RemoveScopedNamedRange(name, scopeSheet);
                continue;
            }
            if (range.Start.Row > deletedEndRow)
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, new GridRange(
                    new CellAddress(range.Start.Sheet, range.Start.Row - count, range.Start.Col),
                    new CellAddress(range.End.Sheet, range.End.Row - count, range.End.Col)), metadata, scopeSheet);
                continue;
            }
            // Straddles the delete boundary: shrink to the surviving portion.
            var newStartRow = range.Start.Row < deletedStartRow ? range.Start.Row : deletedStartRow;
            var newEndRow = range.End.Row > deletedEndRow ? range.End.Row - count : deletedStartRow - 1;
            workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata2);
            workbook.DefineNamedRange(name, new GridRange(
                new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
                new CellAddress(range.End.Sheet, newEndRow, range.End.Col)), metadata2, scopeSheet);
        }
    }

    // ── Sparkline band-scoped shift/delete helpers (R84-commands-clear-delete-5-1) ───────────────
    // Mirrors DeleteNamedRangesInBandLeft/Up above, but for sheet.Sparklines. Unlike whole-row/
    // whole-column delete (which routes every sparkline through RowColumnShiftHelpers.AddressState.cs's
    // CaptureSparklines/ShiftSparklines), this band-scoped Delete Cells path never touched
    // sheet.Sparklines at all before this fix, so a sparkline's Location/DataRange (and optional
    // DateAxisRange) went silently stale — plotting a range that no longer matches what the user saw
    // before the delete, with no error and nothing to undo it except reverting the whole command.

    internal readonly record struct SparklineBandSnapshot(
        SparklineModel Sparkline, GridRange DataRange, CellAddress Location, GridRange? DateAxisRange);

    internal static List<SparklineBandSnapshot> CaptureSparklines(Sheet sheet)
    {
        var snapshots = new List<SparklineBandSnapshot>(sheet.Sparklines.Count);
        foreach (var sparkline in sheet.Sparklines)
            snapshots.Add(new SparklineBandSnapshot(sparkline, sparkline.DataRange, sparkline.Location, sparkline.DateAxisRange));
        return snapshots;
    }

    internal static void RestoreSparklines(Sheet sheet, List<SparklineBandSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        sheet.Sparklines.Clear();
        foreach (var entry in snapshot)
        {
            entry.Sparkline.DataRange = entry.DataRange;
            entry.Sparkline.Location = entry.Location;
            entry.Sparkline.DateAxisRange = entry.DateAxisRange;
            sheet.Sparklines.Add(entry.Sparkline);
        }
    }

    private enum RangeBandOutcome { Unaffected, Removed, Translated }

    /// <summary>
    /// Delete Shift Left: a range (DataRange/DateAxisRange) whose rows are fully inside
    /// [bandStartRow..bandEndRow] shrinks/shifts/is dropped exactly like a named range in
    /// <see cref="DeleteNamedRangesInBandLeft"/>; a range whose rows straddle the band boundary is
    /// left untouched (same "fully inside or no-op" scoping as every other band-scoped helper here).
    /// </summary>
    private static RangeBandOutcome ShrinkColRangeForBandLeft(
        GridRange range,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count,
        out GridRange translated)
    {
        translated = range;
        if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow) return RangeBandOutcome.Unaffected;
        if (range.End.Col < deletedStartCol) return RangeBandOutcome.Unaffected;
        if (range.Start.Col >= deletedStartCol && range.End.Col <= deletedEndCol) return RangeBandOutcome.Removed;

        if (range.Start.Col > deletedEndCol)
        {
            translated = new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - count),
                new CellAddress(range.End.Sheet, range.End.Row, range.End.Col - count));
            return RangeBandOutcome.Translated;
        }

        var newStartCol = range.Start.Col < deletedStartCol ? range.Start.Col : deletedStartCol;
        var newEndCol = range.End.Col > deletedEndCol ? range.End.Col - count : deletedStartCol - 1;
        translated = new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
        return RangeBandOutcome.Translated;
    }

    /// <summary>Delete Shift Up: analogous to <see cref="ShrinkColRangeForBandLeft"/> for rows/columns swapped.</summary>
    private static RangeBandOutcome ShrinkRowRangeForBandUp(
        GridRange range,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count,
        out GridRange translated)
    {
        translated = range;
        if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol) return RangeBandOutcome.Unaffected;
        if (range.End.Row < deletedStartRow) return RangeBandOutcome.Unaffected;
        if (range.Start.Row >= deletedStartRow && range.End.Row <= deletedEndRow) return RangeBandOutcome.Removed;

        if (range.Start.Row > deletedEndRow)
        {
            translated = new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row - count, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row - count, range.End.Col));
            return RangeBandOutcome.Translated;
        }

        var newStartRow = range.Start.Row < deletedStartRow ? range.Start.Row : deletedStartRow;
        var newEndRow = range.End.Row > deletedEndRow ? range.End.Row - count : deletedStartRow - 1;
        translated = new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
        return RangeBandOutcome.Translated;
    }

    /// <summary>
    /// Delete Shift Left: sparklines are adjusted like named ranges/CF-DV rules. A sparkline's
    /// Location only moves when it falls inside the row band (like any other single-cell annotation —
    /// see <see cref="DeleteAnnotationsInBandLeft{TValue}"/>); its DataRange/DateAxisRange only move
    /// when fully inside the row band. Either the Location landing in the deleted columns, or the
    /// DataRange being fully consumed by them, drops the sparkline outright — Excel has no way to
    /// render a sparkline whose anchor cell or entire data range no longer exists (mirrors the
    /// #REF!-vs-drop rationale in <see cref="DeleteNamedRangesInBandLeft"/>). Losing just the optional
    /// DateAxisRange only clears that one setting, since a sparkline is still fully renderable without it.
    /// </summary>
    internal static void ShiftSparklinesInBandLeft(
        Sheet sheet,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        for (var i = sheet.Sparklines.Count - 1; i >= 0; i--)
        {
            var sparkline = sheet.Sparklines[i];
            var removed = false;

            var location = sparkline.Location;
            if (location.Row >= bandStartRow && location.Row <= bandEndRow)
            {
                if (location.Col >= deletedStartCol && location.Col <= deletedEndCol)
                    removed = true;
                else if (location.Col > deletedEndCol)
                    sparkline.Location = new CellAddress(location.Sheet, location.Row, location.Col - count);
            }

            if (!removed)
            {
                var outcome = ShrinkColRangeForBandLeft(sparkline.DataRange, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count, out var newDataRange);
                if (outcome == RangeBandOutcome.Removed)
                    removed = true;
                else if (outcome == RangeBandOutcome.Translated)
                    sparkline.DataRange = newDataRange;
            }

            if (!removed && sparkline.DateAxisRange is { } dateAxisRange)
            {
                var outcome = ShrinkColRangeForBandLeft(dateAxisRange, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count, out var newDateAxisRange);
                sparkline.DateAxisRange = outcome switch
                {
                    RangeBandOutcome.Removed => null,
                    RangeBandOutcome.Translated => newDateAxisRange,
                    _ => dateAxisRange,
                };
            }

            if (removed)
                sheet.Sparklines.RemoveAt(i);
        }
    }

    /// <summary>Delete Shift Up: analogous to <see cref="ShiftSparklinesInBandLeft"/> for rows/columns swapped.</summary>
    internal static void ShiftSparklinesInBandUp(
        Sheet sheet,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        for (var i = sheet.Sparklines.Count - 1; i >= 0; i--)
        {
            var sparkline = sheet.Sparklines[i];
            var removed = false;

            var location = sparkline.Location;
            if (location.Col >= bandStartCol && location.Col <= bandEndCol)
            {
                if (location.Row >= deletedStartRow && location.Row <= deletedEndRow)
                    removed = true;
                else if (location.Row > deletedEndRow)
                    sparkline.Location = new CellAddress(location.Sheet, location.Row - count, location.Col);
            }

            if (!removed)
            {
                var outcome = ShrinkRowRangeForBandUp(sparkline.DataRange, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count, out var newDataRange);
                if (outcome == RangeBandOutcome.Removed)
                    removed = true;
                else if (outcome == RangeBandOutcome.Translated)
                    sparkline.DataRange = newDataRange;
            }

            if (!removed && sparkline.DateAxisRange is { } dateAxisRange)
            {
                var outcome = ShrinkRowRangeForBandUp(dateAxisRange, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count, out var newDateAxisRange);
                sparkline.DateAxisRange = outcome switch
                {
                    RangeBandOutcome.Removed => null,
                    RangeBandOutcome.Translated => newDateAxisRange,
                    _ => dateAxisRange,
                };
            }

            if (removed)
                sheet.Sparklines.RemoveAt(i);
        }
    }

    /// <summary>
    /// Insert Shift Right: named-range-style growth mirrored for sheet.Sparklines (R86-meta-3 —
    /// this band-scoped Insert Cells path never touched sheet.Sparklines at all before this fix,
    /// exactly the gap R84-commands-clear-delete-5-1 fixed on the Delete side; see
    /// <see cref="ShiftSparklinesInBandLeft"/>). A sparkline's Location only moves when it falls
    /// inside the row band (like any other single-cell annotation — see
    /// <see cref="ShiftAnnotationsInBandRight{TValue}"/>); its DataRange/DateAxisRange fully inside
    /// the row band GROW their End.Col by <paramref name="count"/> when the insert point straddles
    /// them (Start.Col &lt; insertBeforeCol &lt;= End.Col), matching Excel's own reference-adjustment
    /// (mirrors <see cref="ShiftNamedRangesInBandRight"/>); a range fully at/right of the insert
    /// point shifts both endpoints right.
    /// </summary>
    internal static void ShiftSparklinesInBandRight(
        Sheet sheet,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count)
    {
        foreach (var sparkline in sheet.Sparklines)
        {
            var location = sparkline.Location;
            if (location.Row >= bandStartRow && location.Row <= bandEndRow && location.Col >= insertBeforeCol)
                sparkline.Location = new CellAddress(location.Sheet, location.Row, location.Col + count);

            if (GrowColRangeForBandRight(sparkline.DataRange, bandStartRow, bandEndRow, insertBeforeCol, count, out var newDataRange))
                sparkline.DataRange = newDataRange;

            if (sparkline.DateAxisRange is { } dateAxisRange &&
                GrowColRangeForBandRight(dateAxisRange, bandStartRow, bandEndRow, insertBeforeCol, count, out var newDateAxisRange))
                sparkline.DateAxisRange = newDateAxisRange;
        }
    }

    /// <summary>Insert Shift Down: analogous to <see cref="ShiftSparklinesInBandRight"/> for rows/columns swapped.</summary>
    internal static void ShiftSparklinesInBandDown(
        Sheet sheet,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count)
    {
        foreach (var sparkline in sheet.Sparklines)
        {
            var location = sparkline.Location;
            if (location.Col >= bandStartCol && location.Col <= bandEndCol && location.Row >= insertBeforeRow)
                sparkline.Location = new CellAddress(location.Sheet, location.Row + count, location.Col);

            if (GrowRowRangeForBandDown(sparkline.DataRange, bandStartCol, bandEndCol, insertBeforeRow, count, out var newDataRange))
                sparkline.DataRange = newDataRange;

            if (sparkline.DateAxisRange is { } dateAxisRange &&
                GrowRowRangeForBandDown(dateAxisRange, bandStartCol, bandEndCol, insertBeforeRow, count, out var newDateAxisRange))
                sparkline.DateAxisRange = newDateAxisRange;
        }
    }

    /// <summary>Insert Shift Right: see <see cref="ShiftSparklinesInBandRight"/>. Returns false (no-op) when the range is outside the row band or entirely left of the insert point.</summary>
    private static bool GrowColRangeForBandRight(
        GridRange range,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count,
        out GridRange translated)
    {
        translated = range;
        if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow) return false;
        if (range.End.Col < insertBeforeCol) return false;

        var newStartCol = range.Start.Col < insertBeforeCol
            ? range.Start.Col
            : Math.Min(range.Start.Col + count, CellAddress.MaxCol);
        translated = new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, Math.Min(range.End.Col + count, CellAddress.MaxCol)));
        return true;
    }

    /// <summary>Insert Shift Down: analogous to <see cref="GrowColRangeForBandRight"/> for rows/columns swapped.</summary>
    private static bool GrowRowRangeForBandDown(
        GridRange range,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count,
        out GridRange translated)
    {
        translated = range;
        if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol) return false;
        if (range.End.Row < insertBeforeRow) return false;

        var newStartRow = range.Start.Row < insertBeforeRow
            ? range.Start.Row
            : Math.Min(range.Start.Row + count, CellAddress.MaxRow);
        translated = new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, Math.Min(range.End.Row + count, CellAddress.MaxRow), range.End.Col));
        return true;
    }
}

public sealed class DeleteCellsCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: see InsertCellsCommand's field of the same name/rationale --
    // _capturedCells here covers both the shifted-back survivors and the restored-deleted cells in
    // one unified capture, one of the richest undo snapshot shapes in the codebase.
    private const int BytesPerCell = 400;

    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly DeleteCellsShiftDirection _direction;
    private CellShiftSnapshot? _snapshot;
    private RowColumnMutationSnapshot? _mutationSnapshot;
    // R96-commands-undo-affected-cells-2: see InsertCellsCommand's field of the same name. Here the
    // captured list covers BOTH the shifted-back survivors and the restored-deleted cells (a single
    // unified capture over the whole shift region, unlike DeleteRowsCommand's two separate lists).
    private IReadOnlyList<(CellAddress Address, Cell Cell)>? _capturedCells;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private List<CellAddress>? _movedDestinationCells;
    // R52-commands-clear-delete-3-1: see InsertCellsCommand's field of the same name.
    private List<(uint Row, uint Col, StyleId StyleId)>? _styleOnlySnapshot;
    // R84-commands-clear-delete-5-1: see InsertCellsCommand.CaptureSparklines/RestoreSparklines.
    private List<InsertCellsCommand.SparklineBandSnapshot>? _sparklineSnapshot;

    public string Label => "Delete Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes => _capturedCells is null
        ? 0
        : (int)Math.Min((long)_capturedCells.Count * BytesPerCell, int.MaxValue);

    public DeleteCellsCommand(SheetId sheetId, GridRange range, DeleteCellsShiftDirection direction)
    {
        _sheetId = sheetId;
        _range = range;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Delete range must be on the target sheet.");
        if (!Enum.IsDefined(_direction))
            return new CommandOutcome(false, "Delete shift direction is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _mutationSnapshot = RowColumnMutationSnapshot.Capture(ctx.Workbook, sheet);

        if (_direction == DeleteCellsShiftDirection.Left)
        {
            var shiftRegion = CellShiftRegion.Rightward(_range);

            // R23-array-formula-legacy-cse-1: reject if this band-scoped shift would carry some
            // members of a legacy CSE array / dynamic-array spill along while leaving others behind
            // (Excel's "You cannot change part of an array"). An array whose full extent lies inside
            // the shifted/deleted band still moves (or is deleted) as one atomic unit — see
            // InsertCellsCommand.ArrayMembersWithinShiftRegion.
            if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, shiftRegion)) is { } splitsArrayRejection)
                return splitsArrayRejection;

            if (DeleteMergeWouldBeTorn(sheet, shiftRegion, _range, ShiftAxis.Column))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: see InsertCellsCommand.AutoFilterOverlapsBand — this band-scoped shift cannot
            // safely relocate AutoFilter.Reference/FilterHiddenRows, so refuse rather than corrupt.
            if (InsertCellsCommand.AutoFilterOverlapsBand(ctx.Workbook, sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, shiftRegion,
                orig => orig.Col > _range.End.Col
                    ? new CellAddress(orig.Sheet, orig.Row, orig.Col - _range.ColCount)
                    : (CellAddress?)null);
            _snapshot = capture.Snapshot;
            _capturedCells = capture.Cells;

            uint width = _range.ColCount;

            // Snapshot and shift annotations
            DeleteAnnotationsInBandLeft(sheet.Comments, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must delete/shift in lockstep with it
            // within the same band, or a note's author/pinned box goes stale.
            DeleteAnnotationsInBandLeft(sheet.CommentAuthors, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            DeleteAnnotationsSetInBandLeft(sheet.ShownComments, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            DeleteAnnotationsInBandLeft(sheet.ThreadedComments, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            DeleteAnnotationsInBandLeft(sheet.Hyperlinks, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            DeleteAnnotationsInBandLeft(sheet.HyperlinkMetadata, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            DeleteAnnotationsInBandLeft(sheet.RichTextRuns, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            DeleteAnnotationsInBandLeft(sheet.CellPhoneticGuides, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            // R52-commands-clear-delete-3-1: clear/shift style-only (formatted-but-empty) cells in
            // lockstep with the value cells in the same band — see InsertCellsCommand's Shift-Right
            // branch for the full rationale.
            _styleOnlySnapshot = InsertCellsCommand.CaptureStyleOnlyEntries(sheet);
            DeleteStyleOnlyInBandLeft(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            // Snapshot and update merged regions
            sheet.ReplaceMergedRegions(AdjustMergesDeleteLeft(sheet.MergedRegions, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            RowColumnShiftHelpers.AdjustRulesDeleteShiftLeft(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            // R21-defined-name-management-1/-3: named ranges fully inside the band's row span are
            // shifted left (surviving portion) or removed (fully inside the deleted columns — see
            // InsertCellsCommand.DeleteNamedRangesInBandLeft for the #REF!-vs-drop rationale).
            InsertCellsCommand.DeleteNamedRangesInBandLeft(ctx.Workbook, _sheetId, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            // R84-commands-clear-delete-5-1: a sparkline's Location/DataRange (and optional
            // DateAxisRange) are address-bearing state just like named ranges/CF/DV rules, but this
            // band-scoped shift never touched sheet.Sparklines at all — see
            // InsertCellsCommand.ShiftSparklinesInBandLeft for the move/shrink/drop rationale.
            _sparklineSnapshot = InsertCellsCommand.CaptureSparklines(sheet);
            InsertCellsCommand.ShiftSparklinesInBandLeft(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            DeleteShiftLeft(sheet, capture.Cells);
            // R21-undo-redo-deep-1: record the shifted-to address of every surviving moved cell so a
            // moved dynamic-array anchor whose formula text is unchanged by the shift still gets
            // queued for recalculation — see InsertCellsCommand's Shift-Right branch.
            _movedDestinationCells = capture.Cells
                .Where(c => c.Address.Col > _range.End.Col)
                .Select(c => new CellAddress(c.Address.Sheet, c.Address.Row, c.Address.Col - width))
                .ToList();

            var deleteLeftOp = new DeleteCellsShiftLeftOp(
                sheet.Name,
                _range.Start.Row, _range.End.Row,
                _range.Start.Col, _range.End.Col,
                CellAddress.MaxCol, width);
            // O34: rewrite in-document hyperlink bookmark CONTENT (not just the dictionary key,
            // already shifted/removed above by DeleteAnnotationsInBandLeft) so a surviving "Place in
            // This Document" link whose Bookmark text points at a cell after the deleted band keeps
            // pointing at the same logical cell, matching whole-row/whole-column delete.
            _mutationSnapshot!.RewriteReferences(ctx.Workbook, sheet, deleteLeftOp);
        }
        else
        {
            var shiftRegion = CellShiftRegion.Downward(_range);

            // R23-array-formula-legacy-cse-1: see the Shift-Left branch above.
            if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, shiftRegion)) is { } splitsArrayRejection)
                return splitsArrayRejection;

            if (DeleteMergeWouldBeTorn(sheet, shiftRegion, _range, ShiftAxis.Row))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: see InsertCellsCommand.AutoFilterOverlapsBand — this band-scoped shift cannot
            // safely relocate AutoFilter.Reference/FilterHiddenRows, so refuse rather than corrupt.
            if (InsertCellsCommand.AutoFilterOverlapsBand(ctx.Workbook, sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, shiftRegion,
                orig => orig.Row > _range.End.Row
                    ? new CellAddress(orig.Sheet, orig.Row - _range.RowCount, orig.Col)
                    : (CellAddress?)null);
            _snapshot = capture.Snapshot;
            _capturedCells = capture.Cells;

            uint height = _range.RowCount;

            // Snapshot and shift annotations
            DeleteAnnotationsInBandUp(sheet.Comments, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must delete/shift in lockstep with it
            // within the same band, or a note's author/pinned box goes stale.
            DeleteAnnotationsInBandUp(sheet.CommentAuthors, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            DeleteAnnotationsSetInBandUp(sheet.ShownComments, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            DeleteAnnotationsInBandUp(sheet.ThreadedComments, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            DeleteAnnotationsInBandUp(sheet.Hyperlinks, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            DeleteAnnotationsInBandUp(sheet.HyperlinkMetadata, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            DeleteAnnotationsInBandUp(sheet.RichTextRuns, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            DeleteAnnotationsInBandUp(sheet.CellPhoneticGuides, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            // R52-commands-clear-delete-3-1: see the Delete-Shift-Left branch above.
            _styleOnlySnapshot = InsertCellsCommand.CaptureStyleOnlyEntries(sheet);
            DeleteStyleOnlyInBandUp(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            // Snapshot and update merged regions
            sheet.ReplaceMergedRegions(AdjustMergesDeleteUp(sheet.MergedRegions, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            RowColumnShiftHelpers.AdjustRulesDeleteShiftUp(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            // R21-defined-name-management-1/-3: see the Delete-Shift-Left branch above.
            InsertCellsCommand.DeleteNamedRangesInBandUp(ctx.Workbook, _sheetId, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            // R84-commands-clear-delete-5-1: see the Delete-Shift-Left branch above.
            _sparklineSnapshot = InsertCellsCommand.CaptureSparklines(sheet);
            InsertCellsCommand.ShiftSparklinesInBandUp(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            DeleteShiftUp(sheet, capture.Cells);
            // R21-undo-redo-deep-1: see the Delete-Shift-Left branch above.
            _movedDestinationCells = capture.Cells
                .Where(c => c.Address.Row > _range.End.Row)
                .Select(c => new CellAddress(c.Address.Sheet, c.Address.Row - height, c.Address.Col))
                .ToList();

            var deleteUpOp = new DeleteCellsShiftUpOp(
                sheet.Name,
                _range.Start.Row, _range.End.Row, CellAddress.MaxRow,
                _range.Start.Col, _range.End.Col,
                height);
            // O34: rewrite in-document hyperlink bookmark CONTENT (not just the dictionary key,
            // already shifted/removed above by DeleteAnnotationsInBandUp) so a surviving "Place in
            // This Document" link whose Bookmark text points at a cell after the deleted band keeps
            // pointing at the same logical cell, matching whole-row/whole-column delete.
            _mutationSnapshot!.RewriteReferences(ctx.Workbook, sheet, deleteUpOp);
        }

        // R98-commands-dependency-vacated-2: mirror InsertCellsCommand's Apply-side fix (above).
        // capture.Cells' entries inside _range are permanently deleted (already covered, harmlessly
        // duplicated, by _range.AllCells() below); entries beyond _range (shifted survivors) instead
        // relocate to a new address via DeleteShiftLeft/Up, always leaving their OLD address blank --
        // and that OLD address was never surfaced anywhere (not in _range.AllCells(), not in
        // _movedDestinationCells, which only holds the NEW address). InsertCellsCommand's
        // VacatedAddressesForRelocatedFormulaCells yields every captured formula cell's original
        // Address unconditionally, which covers both cases (duplicates de-duped downstream).
        _affectedCells = _mutationSnapshot!.BuildAffectedCells(
            _range.AllCells()
                .Concat(_movedDestinationCells ?? Enumerable.Empty<CellAddress>())
                .Concat(InsertCellsCommand.VacatedAddressesForRelocatedFormulaCells(_capturedCells ?? [])));
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R96-commands-undo-affected-cells-2: see InsertCellsCommand.Revert above.
        // R98-commands-dependency-vacated-2: capture the CURRENT (post-Apply, pre-Revert) address of
        // every shifted-survivor formula cell now, before _capturedCells is nulled at the end of this
        // method. Unlike InsertCellsCommand, _movedDestinationCells here is a FILTERED subset of
        // _capturedCells (only entries beyond _range, i.e. the surviving shifted cells -- the
        // permanently-deleted ones inside _range never had a "current" address to vacate), so it is
        // recomputed directly from _range/_direction rather than reused index-aligned.
        var vacatedAfterRevert = VacatedAddressesAfterRevert(_capturedCells ?? []).ToList();

        if (_mutationSnapshot is null) return;
        var formulaSnapshotAddressesBeforeRestore = _mutationSnapshot.RestoreRewrittenFormulas(ctx.Workbook);

        _snapshot.Restore(ctx.GetSheet(_sheetId));
        _snapshot = null;

        _mutationSnapshot.RestoreCommonState(ctx.Workbook, sheet, restoreRulesInPlace: false);
        InsertCellsCommand.RestoreStyleOnlyEntries(sheet, _styleOnlySnapshot);
        InsertCellsCommand.RestoreSparklines(sheet, _sparklineSnapshot);

        // R96-commands-undo-affected-cells-2: recompute AffectedCells to reflect where every
        // formula cell ACTUALLY ended up after this Revert -- covers both the shifted-back
        // survivors AND the restored-deleted cells (both are in the single unified _capturedCells
        // list, unlike DeleteRowsCommand's separate _shiftedSnapshot/_deletedSnapshot).
        // CommandBus.Undo reads this live property instead of the frozen forward payload.
        // R98-commands-dependency-vacated-2: also surface the address the Revert's own move-back
        // just vacated for each shifted survivor (vacatedAfterRevert, captured above).
        _affectedCells = _mutationSnapshot.BuildAffectedCells(
            RowColumnShiftHelpers.RelocatedFormulaCellsAtCapturedAddress(_capturedCells ?? [])
                .Concat(formulaSnapshotAddressesBeforeRestore)
                .Concat(vacatedAfterRevert),
            includeRewrittenFormulaAddresses: false);
        _capturedCells = null;
    }

    // R98-commands-dependency-vacated-2: recomputes, for each shifted-survivor entry in
    // capturedCells (address outside _range), the CURRENT (post-Apply) address it lives at just
    // before this Revert moves it back -- exactly the same mapping CaptureCellsForDelete's
    // currentAddressOf delegate used during Apply (orig.Col > _range.End.Col ? Col - width : null /
    // orig.Row > _range.End.Row ? Row - height : null), reproduced here since that delegate is not
    // retained on the command. Entries inside _range were permanently deleted, not shifted, so they
    // have no "current" address to vacate.
    private IEnumerable<CellAddress> VacatedAddressesAfterRevert(
        IReadOnlyList<(CellAddress Address, Cell Cell)> capturedCells)
    {
        foreach (var (address, cell) in capturedCells)
        {
            if (cell.FormulaText is null)
                continue;

            CellAddress? current = _direction == DeleteCellsShiftDirection.Left
                ? (address.Col > _range.End.Col
                    ? new CellAddress(address.Sheet, address.Row, address.Col - _range.ColCount)
                    : (CellAddress?)null)
                : (address.Row > _range.End.Row
                    ? new CellAddress(address.Sheet, address.Row - _range.RowCount, address.Col)
                    : (CellAddress?)null);

            if (current is { } vacated)
                yield return vacated;
        }
    }

    private void DeleteShiftLeft(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var width = _range.ColCount;
        var originalCells = InsertCellsCommand.RentOriginalCells(sheet, captured);
        // R21-undo-redo-deep-1: capture any live spill rooted at each surviving (post-delete) cell
        // BEFORE it is cleared/moved — see InsertCellsCommand.InsertShiftRight above.
        var spillPayloads = new RangeValue?[captured.Count];
        for (var i = 0; i < captured.Count; i++)
        {
            if (captured[i].Address.Col > _range.End.Col)
                spillPayloads[i] = sheet.CaptureSpillForRelocate(captured[i].Address);
        }
        try
        {
            InsertCellsCommand.ClearRange(sheet, _range);
            foreach (var (address, _) in captured)
            {
                if (address.Col > _range.End.Col)
                    sheet.ClearCell(address);
            }

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                if (address.Col > _range.End.Col)
                {
                    var newAddress = new CellAddress(address.Sheet, address.Row, address.Col - width);
                    sheet.SetCell(newAddress, originalCells[i]);
                    if (spillPayloads[i] is { } payload)
                        sheet.SetSpillRange(newAddress, payload);
                }
            }
        }
        finally
        {
            InsertCellsCommand.ReturnOriginalCells(originalCells);
        }
    }

    private void DeleteShiftUp(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var height = _range.RowCount;
        var originalCells = InsertCellsCommand.RentOriginalCells(sheet, captured);
        // R21-undo-redo-deep-1: capture any live spill rooted at each surviving (post-delete) cell
        // BEFORE it is cleared/moved — see InsertCellsCommand.InsertShiftRight above.
        var spillPayloads = new RangeValue?[captured.Count];
        for (var i = 0; i < captured.Count; i++)
        {
            if (captured[i].Address.Row > _range.End.Row)
                spillPayloads[i] = sheet.CaptureSpillForRelocate(captured[i].Address);
        }
        try
        {
            InsertCellsCommand.ClearRange(sheet, _range);
            foreach (var (address, _) in captured)
            {
                if (address.Row > _range.End.Row)
                    sheet.ClearCell(address);
            }

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                if (address.Row > _range.End.Row)
                {
                    var newAddress = new CellAddress(address.Sheet, address.Row - height, address.Col);
                    sheet.SetCell(newAddress, originalCells[i]);
                    if (spillPayloads[i] is { } payload)
                        sheet.SetSpillRange(newAddress, payload);
                }
            }
        }
        finally
        {
            InsertCellsCommand.ReturnOriginalCells(originalCells);
        }
    }

    // ── Merge guard for delete ────────────────────────────────────────────────

    private enum ShiftAxis { Row, Column }

    /// <summary>
    /// Returns true if any merged region partially overlaps the delete shift band.
    /// Merges fully inside the deleted range are removed (not torn). Merges fully outside are untouched.
    /// A partial overlap = would be torn by the shift.
    /// </summary>
    private static bool DeleteMergeWouldBeTorn(Sheet sheet, CellShiftRegion band, GridRange deleteRange, ShiftAxis shiftDirection)
    {
        foreach (var merge in sheet.MergedRegions)
        {
            if (!MergeIntersectsBand(merge, band))
                continue;

            if (shiftDirection == ShiftAxis.Row)
            {
                // Delete-up band: band rows extend to MaxRow, band cols = deleteRange cols
                // Torn if merge straddles the column edge of the band
                if (merge.Start.Col < band.StartCol || merge.End.Col > band.EndCol)
                    return true;
                // Torn if merge straddles the bottom of the deleted range (partially deleted)
                if (merge.Start.Row <= deleteRange.End.Row && merge.End.Row > deleteRange.End.Row)
                    return true;
            }
            else
            {
                // Delete-left band: band cols extend to MaxCol, band rows = deleteRange rows
                // Torn if merge straddles the row edge of the band
                if (merge.Start.Row < band.StartRow || merge.End.Row > band.EndRow)
                    return true;
                // Torn if merge straddles the right edge of the deleted range
                if (merge.Start.Col <= deleteRange.End.Col && merge.End.Col > deleteRange.End.Col)
                    return true;
            }
        }

        return false;
    }

    private static bool MergeIntersectsBand(GridRange merge, CellShiftRegion band) =>
        merge.Start.Row <= band.EndRow && merge.End.Row >= band.StartRow &&
        merge.Start.Col <= band.EndCol && merge.End.Col >= band.StartCol;

    // ── Merge adjustment for delete-left ─────────────────────────────────────

    private static IReadOnlyList<GridRange> AdjustMergesDeleteLeft(
        IEnumerable<GridRange> mergedRegions,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        var result = new List<GridRange>();
        foreach (var merge in mergedRegions)
        {
            // Only merges fully within the band rows
            if (merge.Start.Row < bandStartRow || merge.End.Row > bandEndRow)
            {
                result.Add(merge);
                continue;
            }

            if (merge.End.Col < deletedStartCol)
            {
                result.Add(merge);  // entirely left of deleted range
            }
            else if (merge.Start.Col > deletedEndCol)
            {
                // Entirely right: shift left
                result.Add(new GridRange(
                    new CellAddress(merge.Start.Sheet, merge.Start.Row, merge.Start.Col - count),
                    new CellAddress(merge.End.Sheet, merge.End.Row, merge.End.Col - count)));
            }
            else if (merge.Start.Col < deletedStartCol)
            {
                // Start-edge straddle: merge begins before the deleted range and ends inside it
                // (e.g. merge B2:C2, delete C2). The surviving columns [Start.Col..deletedStartCol-1]
                // are left in place — shrink the merge instead of dropping it.
                result.Add(new GridRange(
                    merge.Start,
                    new CellAddress(merge.End.Sheet, merge.End.Row, deletedStartCol - 1)));
            }
            // else: merge is entirely within the deleted range → drop it
        }

        return result;
    }

    // ── Merge adjustment for delete-up ────────────────────────────────────────

    private static IReadOnlyList<GridRange> AdjustMergesDeleteUp(
        IEnumerable<GridRange> mergedRegions,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        var result = new List<GridRange>();
        foreach (var merge in mergedRegions)
        {
            // Only merges fully within the band cols
            if (merge.Start.Col < bandStartCol || merge.End.Col > bandEndCol)
            {
                result.Add(merge);
                continue;
            }

            if (merge.End.Row < deletedStartRow)
            {
                result.Add(merge);  // entirely above deleted range
            }
            else if (merge.Start.Row > deletedEndRow)
            {
                // Entirely below: shift up
                result.Add(new GridRange(
                    new CellAddress(merge.Start.Sheet, merge.Start.Row - count, merge.Start.Col),
                    new CellAddress(merge.End.Sheet, merge.End.Row - count, merge.End.Col)));
            }
            else if (merge.Start.Row < deletedStartRow)
            {
                // Start-edge straddle: merge begins before the deleted range and ends inside it
                // (e.g. merge A2:A3, delete A3). The surviving rows [Start.Row..deletedStartRow-1]
                // are left in place — shrink the merge instead of dropping it.
                result.Add(new GridRange(
                    merge.Start,
                    new CellAddress(merge.End.Sheet, deletedStartRow - 1, merge.End.Col)));
            }
            // else: merge is entirely within the deleted range → drop it
        }

        return result;
    }

    // ── Band-constrained annotation delete/shift helpers ──────────────────────

    /// <summary>Delete-left: remove annotations at deleted cols, shift annotations at cols > deletedEndCol leftward.</summary>
    private static void DeleteAnnotationsInBandLeft<TValue>(
        Dictionary<CellAddress, TValue> dict,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        List<CellAddress>? removed = null;
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;

        foreach (var pair in dict)
        {
            var addr = pair.Key;
            if (addr.Row < bandStartRow || addr.Row > bandEndRow) continue;

            if (addr.Col >= deletedStartCol && addr.Col <= deletedEndCol)
                (removed ??= []).Add(addr);
            else if (addr.Col > deletedEndCol)
                (shifted ??= []).Add(pair);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                dict.Remove(addr);
        }

        if (shifted is not null)
        {
            foreach (var (addr, _) in shifted)
                dict.Remove(addr);
            foreach (var (addr, value) in shifted)
                dict[new CellAddress(addr.Sheet, addr.Row, addr.Col - count)] = value;
        }
    }

    /// <summary>Delete-up: remove annotations at deleted rows, shift annotations at rows > deletedEndRow upward.</summary>
    private static void DeleteAnnotationsInBandUp<TValue>(
        Dictionary<CellAddress, TValue> dict,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        List<CellAddress>? removed = null;
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;

        foreach (var pair in dict)
        {
            var addr = pair.Key;
            if (addr.Col < bandStartCol || addr.Col > bandEndCol) continue;

            if (addr.Row >= deletedStartRow && addr.Row <= deletedEndRow)
                (removed ??= []).Add(addr);
            else if (addr.Row > deletedEndRow)
                (shifted ??= []).Add(pair);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                dict.Remove(addr);
        }

        if (shifted is not null)
        {
            foreach (var (addr, _) in shifted)
                dict.Remove(addr);
            foreach (var (addr, value) in shifted)
                dict[new CellAddress(addr.Sheet, addr.Row - count, addr.Col)] = value;
        }
    }

    // J17: HashSet<CellAddress> counterparts of DeleteAnnotationsInBandLeft/Up above, used for
    // Sheet.ShownComments (the address-keyed "pinned note" set), which must delete/shift within the
    // same band as Comments/CommentAuthors or a pinned note's box would render at a stale address.

    /// <summary>Delete-left: remove set entries at deleted cols, shift set entries at cols > deletedEndCol leftward.</summary>
    private static void DeleteAnnotationsSetInBandLeft(
        HashSet<CellAddress> addresses,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        List<CellAddress>? removed = null;
        List<CellAddress>? shifted = null;

        foreach (var addr in addresses)
        {
            if (addr.Row < bandStartRow || addr.Row > bandEndRow) continue;

            if (addr.Col >= deletedStartCol && addr.Col <= deletedEndCol)
                (removed ??= []).Add(addr);
            else if (addr.Col > deletedEndCol)
                (shifted ??= []).Add(addr);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                addresses.Remove(addr);
        }

        if (shifted is not null)
        {
            foreach (var addr in shifted)
                addresses.Remove(addr);
            foreach (var addr in shifted)
                addresses.Add(new CellAddress(addr.Sheet, addr.Row, addr.Col - count));
        }
    }

    /// <summary>Delete-up: remove set entries at deleted rows, shift set entries at rows > deletedEndRow upward.</summary>
    private static void DeleteAnnotationsSetInBandUp(
        HashSet<CellAddress> addresses,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        List<CellAddress>? removed = null;
        List<CellAddress>? shifted = null;

        foreach (var addr in addresses)
        {
            if (addr.Col < bandStartCol || addr.Col > bandEndCol) continue;

            if (addr.Row >= deletedStartRow && addr.Row <= deletedEndRow)
                (removed ??= []).Add(addr);
            else if (addr.Row > deletedEndRow)
                (shifted ??= []).Add(addr);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                addresses.Remove(addr);
        }

        if (shifted is not null)
        {
            foreach (var addr in shifted)
                addresses.Remove(addr);
            foreach (var addr in shifted)
                addresses.Add(new CellAddress(addr.Sheet, addr.Row - count, addr.Col));
        }
    }

    // ── Style-only (formatted-but-empty) cell delete/shift helpers (R52-commands-clear-delete-3-1) ──
    // See InsertCellsCommand's equivalent helpers (ShiftStyleOnlyInBandRight/Down) for the full
    // rationale; these are the Delete-Left/Delete-Up analogues, mirroring
    // DeleteAnnotationsInBandLeft/Up above.

    /// <summary>Delete-left: remove style-only entries at deleted cols, shift entries at cols > deletedEndCol leftward.</summary>
    private static void DeleteStyleOnlyInBandLeft(
        Sheet sheet,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        List<(uint Row, uint Col, StyleId StyleId)>? removed = null;
        List<(uint Row, uint Col, StyleId StyleId)>? shifted = null;

        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
        {
            if (key.Row < bandStartRow || key.Row > bandEndRow) continue;

            if (key.Col >= deletedStartCol && key.Col <= deletedEndCol)
                (removed ??= []).Add((key.Row, key.Col, styleId));
            else if (key.Col > deletedEndCol)
                (shifted ??= []).Add((key.Row, key.Col, styleId));
        }

        if (removed is not null)
        {
            foreach (var (row, col, _) in removed)
                sheet.ClearStyleOnly(row, col);
        }

        if (shifted is not null)
        {
            foreach (var (row, col, _) in shifted)
                sheet.ClearStyleOnly(row, col);
            foreach (var (row, col, styleId) in shifted)
                sheet.SetStyleOnly(row, col - count, styleId);
        }
    }

    /// <summary>Delete-up: remove style-only entries at deleted rows, shift entries at rows > deletedEndRow upward.</summary>
    private static void DeleteStyleOnlyInBandUp(
        Sheet sheet,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        if (!sheet.HasStyleOnlyCells)
            return;

        List<(uint Row, uint Col, StyleId StyleId)>? removed = null;
        List<(uint Row, uint Col, StyleId StyleId)>? shifted = null;

        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
        {
            if (key.Col < bandStartCol || key.Col > bandEndCol) continue;

            if (key.Row >= deletedStartRow && key.Row <= deletedEndRow)
                (removed ??= []).Add((key.Row, key.Col, styleId));
            else if (key.Row > deletedEndRow)
                (shifted ??= []).Add((key.Row, key.Col, styleId));
        }

        if (removed is not null)
        {
            foreach (var (row, col, _) in removed)
                sheet.ClearStyleOnly(row, col);
        }

        if (shifted is not null)
        {
            foreach (var (row, col, _) in shifted)
                sheet.ClearStyleOnly(row, col);
            foreach (var (row, col, styleId) in shifted)
                sheet.SetStyleOnly(row - count, col, styleId);
        }
    }
}

internal readonly record struct CellShiftRegion(uint StartRow, uint EndRow, uint StartCol, uint EndCol)
{
    public static CellShiftRegion Rightward(GridRange range) =>
        new(range.Start.Row, range.End.Row, range.Start.Col, CellAddress.MaxCol);

    public static CellShiftRegion Downward(GridRange range) =>
        new(range.Start.Row, CellAddress.MaxRow, range.Start.Col, range.End.Col);

    public bool Contains(CellAddress address) =>
        Contains(address.Row, address.Col);

    public bool Contains(uint row, uint col) =>
        row >= StartRow &&
        row <= EndRow &&
        col >= StartCol &&
        col <= EndCol;
}

internal sealed class CellShiftCapture(
    CellShiftSnapshot snapshot,
    IReadOnlyList<(CellAddress Address, Cell Cell)> cells,
    uint maxRow,
    uint maxCol)
{
    public CellShiftSnapshot Snapshot { get; } = snapshot;
    public IReadOnlyList<(CellAddress Address, Cell Cell)> Cells { get; } = cells;
    public uint MaxRow { get; } = maxRow;
    public uint MaxCol { get; } = maxCol;
}

internal sealed class CellShiftSnapshot(
    CellShiftRegion region,
    IReadOnlyList<(CellAddress Address, Cell Cell)> cells,
    Func<CellAddress, CellAddress?>? currentAddressOf = null)
{
    public void Restore(Sheet sheet)
    {
        var current = ArrayPool<CellAddress>.Shared.Rent(Math.Max(cells.Count, 16));
        var count = 0;
        // R21-undo-redo-deep-1: capture any live spill rooted at each original cell's CURRENT
        // (post-Apply) address before anything is cleared, so undo re-establishes the spill
        // footprint at the restored (pre-Apply) address instead of silently losing it — mirrors the
        // Apply-side fix in InsertShiftRight/Down and DeleteShiftLeft/Up above.
        RangeValue?[]? spillPayloads = null;
        if (currentAddressOf is not null && cells.Count > 0)
        {
            spillPayloads = new RangeValue?[cells.Count];
            for (var i = 0; i < cells.Count; i++)
            {
                if (currentAddressOf(cells[i].Address) is { } liveAddress)
                    spillPayloads[i] = sheet.CaptureSpillForRelocate(liveAddress);
            }
        }
        try
        {
            foreach (var ((row, col), _) in sheet.GetOccupiedCellMap())
            {
                if (!region.Contains(row, col))
                    continue;

                if (count == current.Length)
                {
                    var expanded = ArrayPool<CellAddress>.Shared.Rent(current.Length * 2);
                    current.AsSpan(0, count).CopyTo(expanded);
                    ArrayPool<CellAddress>.Shared.Return(current);
                    current = expanded;
                }

                current[count++] = new CellAddress(sheet.Id, row, col);
            }

            for (var i = 0; i < count; i++)
                sheet.ClearCell(current[i]);

            for (var i = 0; i < cells.Count; i++)
            {
                var (address, cell) = cells[i];
                sheet.SetCell(address, cell);
                if (spillPayloads is not null && spillPayloads[i] is { } payload)
                    sheet.SetSpillRange(address, payload);
            }
        }
        finally
        {
            ArrayPool<CellAddress>.Shared.Return(current);
        }
    }
}
