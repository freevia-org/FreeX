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

            var capture = CaptureCellsForMove(sheet, shiftRegion);
            if (capture.MaxCol > 0 && capture.MaxCol + width > CellAddress.MaxCol)
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

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesShiftRight(sheet.MergedRegions, _range.Start.Row, _range.End.Row, _range.Start.Col, width));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesInsertShiftRight(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, width);

            InsertShiftRight(sheet, capture.Cells);

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

            if (MergeWouldBeTorn(sheet, shiftRegion, shiftDirection: ShiftAxis.Row))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: see the Shift-Right branch above for why this band-scoped operation must refuse
            // rather than silently shift AutoFilter/table state it cannot safely relocate.
            if (AutoFilterOverlapsBand(sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = CaptureCellsForMove(sheet, shiftRegion);
            if (capture.MaxRow > 0 && capture.MaxRow + height > CellAddress.MaxRow)
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

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesShiftDown(sheet.MergedRegions, _range.Start.Col, _range.End.Col, _range.Start.Row, height));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesInsertShiftDown(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, height);

            InsertShiftDown(sheet, capture.Cells);

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
                _range.AllCells(),
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

        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreHyperlinkBookmarks(ctx.Workbook, _otherSheetHyperlinkBookmarkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
    }

    private void InsertShiftRight(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var width = _range.ColCount;
        var originalCells = RentOriginalCells(sheet, captured);
        try
        {
            foreach (var (address, _) in captured)
                sheet.ClearCell(address);

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                sheet.SetCell(new CellAddress(address.Sheet, address.Row, address.Col + width), originalCells[i]);
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
        try
        {
            foreach (var (address, _) in captured)
                sheet.ClearCell(address);

            for (var i = 0; i < captured.Count; i++)
            {
                var address = captured[i].Address;
                sheet.SetCell(new CellAddress(address.Sheet, address.Row + height, address.Col), originalCells[i]);
            }
        }
        finally
        {
            ReturnOriginalCells(originalCells);
        }
    }

    internal static CellShiftSnapshot CaptureCells(Sheet sheet, CellShiftRegion region)
        => CaptureCellsForMove(sheet, region).Snapshot;

    internal static CellShiftCapture CaptureCellsForDelete(Sheet sheet, CellShiftRegion region)
        => CaptureCellsForMove(sheet, region);

    private static CellShiftCapture CaptureCellsForMove(Sheet sheet, CellShiftRegion region)
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
            new CellShiftSnapshot(region, snapshotCells),
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
            if (merge.Start.Col >= insertBeforeCol)
            {
                result.Add(new GridRange(
                    new CellAddress(merge.Start.Sheet, merge.Start.Row, merge.Start.Col + count),
                    new CellAddress(merge.End.Sheet, merge.End.Row, merge.End.Col + count)));
            }
            else if (merge.End.Col >= insertBeforeCol)
            {
                // Merge spans the insertion point: expand it
                result.Add(new GridRange(
                    merge.Start,
                    new CellAddress(merge.End.Sheet, merge.End.Row, merge.End.Col + count)));
            }
            else
            {
                result.Add(merge);
            }
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

            if (merge.Start.Row >= insertBeforeRow)
            {
                result.Add(new GridRange(
                    new CellAddress(merge.Start.Sheet, merge.Start.Row + count, merge.Start.Col),
                    new CellAddress(merge.End.Sheet, merge.End.Row + count, merge.End.Col)));
            }
            else if (merge.End.Row >= insertBeforeRow)
            {
                result.Add(new GridRange(
                    merge.Start,
                    new CellAddress(merge.End.Sheet, merge.End.Row + count, merge.End.Col)));
            }
            else
            {
                result.Add(merge);
            }
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
            dict[new CellAddress(addr.Sheet, addr.Row, addr.Col + count)] = value;
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
            dict[new CellAddress(addr.Sheet, addr.Row + count, addr.Col)] = value;
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
            addresses.Add(new CellAddress(addr.Sheet, addr.Row, addr.Col + count));
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
            addresses.Add(new CellAddress(addr.Sheet, addr.Row + count, addr.Col));
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

            if (DeleteMergeWouldBeTorn(sheet, shiftRegion, _range, ShiftAxis.Column))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: see InsertCellsCommand.AutoFilterOverlapsBand — this band-scoped shift cannot
            // safely relocate AutoFilter.Reference/FilterHiddenRows, so refuse rather than corrupt.
            if (InsertCellsCommand.AutoFilterOverlapsBand(sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, shiftRegion);
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

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesDeleteLeft(sheet.MergedRegions, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesDeleteShiftLeft(sheet, _range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col, width);

            DeleteShiftLeft(sheet, capture.Cells);

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

            if (DeleteMergeWouldBeTorn(sheet, shiftRegion, _range, ShiftAxis.Row))
                return new CommandOutcome(false,
                    "This operation will cause some merged cells to unmerge. To do this, first unmerge the affected cells.");

            // G3: see InsertCellsCommand.AutoFilterOverlapsBand — this band-scoped shift cannot
            // safely relocate AutoFilter.Reference/FilterHiddenRows, so refuse rather than corrupt.
            if (InsertCellsCommand.AutoFilterOverlapsBand(sheet, shiftRegion))
                return new CommandOutcome(false,
                    "This operation is not allowed. The operation is attempting to shift cells in a table or AutoFilter range on your worksheet.");

            var capture = InsertCellsCommand.CaptureCellsForDelete(sheet, shiftRegion);
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

            // Snapshot and update merged regions
            _mergeSnapshot = sheet.MergedRegions.ToList();
            sheet.ReplaceMergedRegions(AdjustMergesDeleteUp(sheet.MergedRegions, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height));

            // Snapshot and adjust CF/DV rule ranges that are fully inside the band
            (_dvRuleSnapshot, _cfRuleSnapshot) = RowColumnShiftHelpers.CaptureRuleRanges(sheet);
            RowColumnShiftHelpers.AdjustRulesDeleteShiftUp(sheet, _range.Start.Col, _range.End.Col, _range.Start.Row, _range.End.Row, height);

            DeleteShiftUp(sheet, capture.Cells);

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
                _range.AllCells(),
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

        RowColumnShiftHelpers.RestoreDictionary(sheet.Comments, _commentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot);
        RowColumnShiftHelpers.RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RowColumnShiftHelpers.RestoreHyperlinkBookmarks(ctx.Workbook, _otherSheetHyperlinkBookmarkSnapshot);
        RowColumnShiftHelpers.RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot);
    }

    private void DeleteShiftLeft(Sheet sheet, IReadOnlyList<(CellAddress Address, Cell Cell)> captured)
    {
        var width = _range.ColCount;
        var originalCells = InsertCellsCommand.RentOriginalCells(sheet, captured);
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
                    sheet.SetCell(new CellAddress(address.Sheet, address.Row, address.Col - width), originalCells[i]);
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
                    sheet.SetCell(new CellAddress(address.Sheet, address.Row - height, address.Col), originalCells[i]);
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
    IReadOnlyList<(CellAddress Address, Cell Cell)> cells)
{
    public void Restore(Sheet sheet)
    {
        var current = ArrayPool<CellAddress>.Shared.Rent(Math.Max(cells.Count, 16));
        var count = 0;
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

            foreach (var (address, cell) in cells)
                sheet.SetCell(address, cell);
        }
        finally
        {
            ArrayPool<CellAddress>.Shared.Return(current);
        }
    }
}
