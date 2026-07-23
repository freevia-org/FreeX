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

public sealed class InsertCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly InsertCellsShiftDirection _direction;
    private CellShiftSnapshot? _snapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentAuthorsSnapshot;
    private List<CellAddress>? _shownCommentsSnapshot;
    private List<KeyValuePair<CellAddress, ThreadedComment>>? _threadedCommentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _hyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, HyperlinkMetadata>>? _hyperlinkMetadataSnapshot;
    private List<RowColumnShiftHelpers.HyperlinkOtherSheetChange>? _otherSheetHyperlinkBookmarkSnapshot;
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<KeyValuePair<CellAddress, CellPhoneticGuide>>? _phoneticGuideSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dvRuleSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _cfRuleSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<CellAddress>? _movedDestinationCells;
    // R52-commands-clear-delete-3-1: style-only (formatted-but-empty) cells are invisible to
    // sheet.GetOccupiedCellMap() (which only sees value/formula-bearing Cell entries), so the
    // band-scoped shift below never moved or cleared them, silently destroying or misplacing
    // format-only cells such as a fill color applied to an empty cell.
    private List<(uint Row, uint Col, StyleId StyleId)>? _styleOnlySnapshot;

    public string Label => "Insert Cells";

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
            if (AutoFilterOverlapsBand(sheet, shiftRegion))
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

            // Snapshot and shift annotations (comments, hyperlinks, style-only) in the band
            _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
            ShiftAnnotationsInBandRight(sheet.Comments, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must shift in lockstep with it within
            // the same band, or a note's author/pinned box goes stale at its old address.
            _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
            ShiftAnnotationsInBandRight(sheet.CommentAuthors, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
            ShiftAnnotationsSetInBandRight(sheet.ShownComments, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
            ShiftAnnotationsInBandRight(sheet.ThreadedComments, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
            ShiftAnnotationsInBandRight(sheet.Hyperlinks, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
            ShiftAnnotationsInBandRight(sheet.HyperlinkMetadata, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
            ShiftAnnotationsInBandRight(sheet.RichTextRuns, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
            ShiftAnnotationsInBandRight(sheet.CellPhoneticGuides, _range.Start.Row, _range.End.Row, _range.Start.Col, width);
            // R52-commands-clear-delete-3-1: shift style-only (formatted-but-empty) cells in lockstep
            // with the value cells in the same band, so a fill/border applied to an empty cell moves
            // (or is displaced) exactly the way the same formatting on a value-bearing cell would.
            _styleOnlySnapshot = CaptureStyleOnlyEntries(sheet);
            ShiftStyleOnlyInBandRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesShiftRight(sheet.MergedRegions, _range.Start.Row, _range.End.Row, _range.Start.Col, width));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesInsertShiftRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            // R21-defined-name-management-1: plain (GridRange-backed) named ranges fully inside the
            // band's row span are shifted right in lockstep with the cells, the same way whole-row/
            // whole-column insert already does via RowColumnShiftHelpers.ShiftNamedRangeRowsUp/Down/
            // ColumnsUp/Down — this band-scoped path never touched NamedRanges/ScopedNamedRanges at all.
            _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
            _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
            ShiftNamedRangesInBandRight(ctx.Workbook, _sheetId, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

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
            _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
                ctx.Workbook, sheet, insertRightOp, sheet.Name);
            _formulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, insertRightOp, _formulaSnapshot);
            _namedFormulaSnapshot.Clear();
            _scopedNamedFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, insertRightOp, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
            _cfFormulaSnapshot.Clear();
            _cfThresholdSnapshot.Clear();
            _dvFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteRuleFormulas(sheet, insertRightOp, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
            _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, insertRightOp);
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
            if (AutoFilterOverlapsBand(sheet, shiftRegion))
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

            // Snapshot and shift annotations in the band
            _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
            ShiftAnnotationsInBandDown(sheet.Comments, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must shift in lockstep with it within
            // the same band, or a note's author/pinned box goes stale at its old address.
            _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
            ShiftAnnotationsInBandDown(sheet.CommentAuthors, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
            ShiftAnnotationsSetInBandDown(sheet.ShownComments, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
            ShiftAnnotationsInBandDown(sheet.ThreadedComments, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
            ShiftAnnotationsInBandDown(sheet.Hyperlinks, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
            ShiftAnnotationsInBandDown(sheet.HyperlinkMetadata, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
            ShiftAnnotationsInBandDown(sheet.RichTextRuns, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
            ShiftAnnotationsInBandDown(sheet.CellPhoneticGuides, _range.Start.Col, _range.End.Col, _range.Start.Row, height);
            // R52-commands-clear-delete-3-1: see the Shift-Right branch above.
            _styleOnlySnapshot = CaptureStyleOnlyEntries(sheet);
            ShiftStyleOnlyInBandDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesShiftDown(sheet.MergedRegions, _range.Start.Col, _range.End.Col, _range.Start.Row, height));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesInsertShiftDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            // R21-defined-name-management-1: see the Shift-Right branch above.
            _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
            _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
            ShiftNamedRangesInBandDown(ctx.Workbook, _sheetId, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

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
            _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
                ctx.Workbook, sheet, insertDownOp, sheet.Name);
            _formulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, insertDownOp, _formulaSnapshot);
            _namedFormulaSnapshot.Clear();
            _scopedNamedFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, insertDownOp, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
            _cfFormulaSnapshot.Clear();
            _cfThresholdSnapshot.Clear();
            _dvFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteRuleFormulas(sheet, insertDownOp, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
            _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, insertDownOp);
        }

        return new CommandOutcome(
            true,
            AffectedCells: RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
                _range.AllCells().Concat(_movedDestinationCells ?? Enumerable.Empty<CellAddress>()),
                _formulaSnapshot));
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

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
        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(sheet, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);

        _snapshot.Restore(ctx.GetSheet(_sheetId));
        _snapshot = null;

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RowColumnShiftHelpers.RestoreRuleRangesInPlace(sheet, _dvRuleSnapshot, _cfRuleSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreHyperlinkBookmarks(ctx.Workbook, _otherSheetHyperlinkBookmarkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CellPhoneticGuides, _phoneticGuideSnapshot);
        RestoreStyleOnlyEntries(sheet, _styleOnlySnapshot);
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
    /// Returns true if the worksheet AutoFilter range, or any structured table's range, overlaps the
    /// band-scoped Insert/Delete Cells shift region at all. Band-scoped cell shifts (unlike whole-row
    /// or whole-column insert/delete, which call RowColumnShiftHelpers.CaptureAddressBearingState /
    /// ShiftAddressBearingRows*Up/Down) cannot safely relocate axis-wide state such as
    /// <c>Sheet.AutoFilter.Reference</c> or <c>Sheet.FilterHiddenRows</c> — either the whole
    /// table/filter range would need to move (which this command has no way to express, since it only
    /// shifts a bounded row/column band, not a whole row or column) or that state goes stale. Excel
    /// itself refuses "Insert/Delete Cells" when it would disturb a table, so mirror that instead of
    /// silently corrupting filter state.
    /// </summary>
    internal static bool AutoFilterOverlapsBand(Sheet sheet, CellShiftRegion band)
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
}

public sealed class DeleteCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly DeleteCellsShiftDirection _direction;
    private CellShiftSnapshot? _snapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _commentAuthorsSnapshot;
    private List<CellAddress>? _shownCommentsSnapshot;
    private List<KeyValuePair<CellAddress, ThreadedComment>>? _threadedCommentSnapshot;
    private List<KeyValuePair<CellAddress, string>>? _hyperlinkSnapshot;
    private List<KeyValuePair<CellAddress, HyperlinkMetadata>>? _hyperlinkMetadataSnapshot;
    private List<RowColumnShiftHelpers.HyperlinkOtherSheetChange>? _otherSheetHyperlinkBookmarkSnapshot;
    private List<KeyValuePair<CellAddress, IReadOnlyList<CellTextRun>>>? _richTextRunsSnapshot;
    private List<KeyValuePair<CellAddress, CellPhoneticGuide>>? _phoneticGuideSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _dvRuleSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? _cfRuleSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private readonly Dictionary<string, string> _namedFormulaSnapshot = [];
    private readonly Dictionary<(string Name, SheetId Sheet), string> _scopedNamedFormulaSnapshot = [];
    private readonly Dictionary<Guid, string?> _cfFormulaSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _cfThresholdSnapshot = [];
    private readonly Dictionary<(Guid Id, int Slot), string?> _dvFormulaSnapshot = [];
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimSnapshot;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private List<CellAddress>? _movedDestinationCells;
    // R52-commands-clear-delete-3-1: see InsertCellsCommand's field of the same name.
    private List<(uint Row, uint Col, StyleId StyleId)>? _styleOnlySnapshot;

    public string Label => "Delete Cells";

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
            if (InsertCellsCommand.AutoFilterOverlapsBand(sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, shiftRegion,
                orig => orig.Col > _range.End.Col
                    ? new CellAddress(orig.Sheet, orig.Row, orig.Col - _range.ColCount)
                    : (CellAddress?)null);
            _snapshot = capture.Snapshot;

            uint width = _range.ColCount;

            // Snapshot and shift annotations
            _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
            DeleteAnnotationsInBandLeft(sheet.Comments, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must delete/shift in lockstep with it
            // within the same band, or a note's author/pinned box goes stale.
            _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
            DeleteAnnotationsInBandLeft(sheet.CommentAuthors, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
            DeleteAnnotationsSetInBandLeft(sheet.ShownComments, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
            DeleteAnnotationsInBandLeft(sheet.ThreadedComments, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
            DeleteAnnotationsInBandLeft(sheet.Hyperlinks, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
            DeleteAnnotationsInBandLeft(sheet.HyperlinkMetadata, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
            DeleteAnnotationsInBandLeft(sheet.RichTextRuns, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
            DeleteAnnotationsInBandLeft(sheet.CellPhoneticGuides, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);
            // R52-commands-clear-delete-3-1: clear/shift style-only (formatted-but-empty) cells in
            // lockstep with the value cells in the same band — see InsertCellsCommand's Shift-Right
            // branch for the full rationale.
            _styleOnlySnapshot = InsertCellsCommand.CaptureStyleOnlyEntries(sheet);
            DeleteStyleOnlyInBandLeft(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesDeleteLeft(sheet.MergedRegions, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesDeleteShiftLeft(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            // R21-defined-name-management-1/-3: named ranges fully inside the band's row span are
            // shifted left (surviving portion) or removed (fully inside the deleted columns — see
            // InsertCellsCommand.DeleteNamedRangesInBandLeft for the #REF!-vs-drop rationale).
            _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
            _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
            InsertCellsCommand.DeleteNamedRangesInBandLeft(ctx.Workbook, _sheetId, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

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
            _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
                ctx.Workbook, sheet, deleteLeftOp, sheet.Name);
            _formulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, deleteLeftOp, _formulaSnapshot);
            _namedFormulaSnapshot.Clear();
            _scopedNamedFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, deleteLeftOp, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
            _cfFormulaSnapshot.Clear();
            _cfThresholdSnapshot.Clear();
            _dvFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteRuleFormulas(sheet, deleteLeftOp, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
            _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, deleteLeftOp);
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
            if (InsertCellsCommand.AutoFilterOverlapsBand(sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, shiftRegion,
                orig => orig.Row > _range.End.Row
                    ? new CellAddress(orig.Sheet, orig.Row - _range.RowCount, orig.Col)
                    : (CellAddress?)null);
            _snapshot = capture.Snapshot;

            uint height = _range.RowCount;

            // Snapshot and shift annotations
            _commentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Comments);
            DeleteAnnotationsInBandUp(sheet.Comments, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy
            // note author + pinned/"Show Comment" state) and must delete/shift in lockstep with it
            // within the same band, or a note's author/pinned box goes stale.
            _commentAuthorsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CommentAuthors);
            DeleteAnnotationsInBandUp(sheet.CommentAuthors, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            _shownCommentsSnapshot = RowColumnShiftHelpers.CaptureAddressSet(sheet.ShownComments);
            DeleteAnnotationsSetInBandUp(sheet.ShownComments, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            _threadedCommentSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.ThreadedComments);
            DeleteAnnotationsInBandUp(sheet.ThreadedComments, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            _hyperlinkSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.Hyperlinks);
            DeleteAnnotationsInBandUp(sheet.Hyperlinks, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            _hyperlinkMetadataSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.HyperlinkMetadata);
            DeleteAnnotationsInBandUp(sheet.HyperlinkMetadata, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            _richTextRunsSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.RichTextRuns);
            DeleteAnnotationsInBandUp(sheet.RichTextRuns, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            _phoneticGuideSnapshot = RowColumnShiftHelpers.CaptureDictionary(sheet.CellPhoneticGuides);
            DeleteAnnotationsInBandUp(sheet.CellPhoneticGuides, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);
            // R52-commands-clear-delete-3-1: see the Delete-Shift-Left branch above.
            _styleOnlySnapshot = InsertCellsCommand.CaptureStyleOnlyEntries(sheet);
            DeleteStyleOnlyInBandUp(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesDeleteUp(sheet.MergedRegions, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesDeleteShiftUp(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            // R21-defined-name-management-1/-3: see the Delete-Shift-Left branch above.
            _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
            _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
            InsertCellsCommand.DeleteNamedRangesInBandUp(ctx.Workbook, _sheetId, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

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
            _otherSheetHyperlinkBookmarkSnapshot = RowColumnShiftHelpers.ShiftHyperlinkBookmarks(
                ctx.Workbook, sheet, deleteUpOp, sheet.Name);
            _formulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteAllFormulas(ctx.Workbook, deleteUpOp, _formulaSnapshot);
            _namedFormulaSnapshot.Clear();
            _scopedNamedFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteNamedFormulas(ctx.Workbook, deleteUpOp, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
            _cfFormulaSnapshot.Clear();
            _cfThresholdSnapshot.Clear();
            _dvFormulaSnapshot.Clear();
            RowColumnShiftHelpers.RewriteRuleFormulas(sheet, deleteUpOp, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
            _chartVerbatimSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, deleteUpOp);
        }

        return new CommandOutcome(
            true,
            AffectedCells: RowColumnShiftHelpers.BuildAffectedCellsForFormulaRewrite(
                _range.AllCells().Concat(_movedDestinationCells ?? Enumerable.Empty<CellAddress>()),
                _formulaSnapshot));
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
        RowColumnShiftHelpers.RestoreRuleFormulas(sheet, _cfFormulaSnapshot, _cfThresholdSnapshot, _dvFormulaSnapshot);
        RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimSnapshot);

        _snapshot.Restore(ctx.GetSheet(_sheetId));
        _snapshot = null;

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        // Full rebuild because delete operations may have removed rules from the collection.
        RowColumnShiftHelpers.RestoreRuleRanges(sheet, _dvRuleSnapshot, _cfRuleSnapshot);
        RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);

        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreHyperlinkBookmarks(ctx.Workbook, _otherSheetHyperlinkBookmarkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CellPhoneticGuides, _phoneticGuideSnapshot);
        InsertCellsCommand.RestoreStyleOnlyEntries(sheet, _styleOnlySnapshot);
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
