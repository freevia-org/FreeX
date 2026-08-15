using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum SortOn
{
    CellValues,
    CellColor,
    FontColor,
    // R78-commands-sort-multikey-5-2: Excel's Sort dialog "Sort On" also offers Cell Icon,
    // ordering rows by the conditional-format icon-set bucket a cell falls into (e.g. put every
    // green-up-arrow row on top). See SortKey.TargetIcon / GetEffectiveIcon / CompareTargetIcon.
    CellIcon
}

public sealed record SortKey(uint ColumnOffset, bool Ascending, SortOn SortOn = SortOn.CellValues, CellColor? TargetColor = null, CustomSortOrder? CustomOrder = null, CfIconOverride? TargetIcon = null);

public sealed record SortOptions(bool CaseSensitive = false, bool LeftToRight = false);

/// <summary>
/// Sorts the rows of a rectangular range by a specified column, ascending or descending.
/// Stores a snapshot of the original arrangement for undo via Revert.
/// </summary>
public sealed class SortCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    // R119-commands-undo-byte-budget-1: CapturePayloads snapshots a full SortCellPayload (Cell
    // clone + style + comment + author + threaded comment + hyperlink + rich text + phonetic
    // guide) for EVERY cell in _range, plus several per-row HashSet/Dictionary companions -- the
    // richest per-cell shape in the command set. Without a real estimate here, CommandBus's 50 MB
    // undo byte-budget bills a sort of a huge range at the flat 200-byte IEstimatesMemory default.
    private const int BytesPerCell = 400;

    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly IReadOnlyList<SortKey> _sortKeys;
    private readonly SortOptions _options;

    // Snapshot for undo: list of rows, each row is a list of cell+style+hyperlink+richtext tuples
    private List<List<(CellAddress Address, Cell? Cell, StyleId? StyleOnly, string? Hyperlink, HyperlinkMetadata? HyperlinkMetadata, IReadOnlyList<CellTextRun>? RichTextRuns, CellPhoneticGuide? PhoneticGuide)>>? _snapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
    // author + pinned/"Show Comment" state) and must travel with a sorted row's comment, or a
    // note's author/pinned box is left behind at the row's old position.
    private Dictionary<CellAddress, string>? _commentAuthorsSnapshot;
    private HashSet<CellAddress>? _shownCommentsSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
    // R136: a whole-row DEFAULT style (sheet.RowStyles, the <row s="..." customFormat="1"> banner
    // format that empty cells in that row inherit) belongs to the row's CONTENT, so it has to follow
    // the row to its new position exactly like RowHeights does. Left unpermuted it stays pinned to
    // the physical row number, and after a sort the banner format paints whichever row happens to
    // land there -- visible on screen immediately, since the viewport reads RowStyles directly.
    private Dictionary<uint, StyleId>? _rowStyleSnapshot;
    private HashSet<uint>? _hiddenRowsSnapshot;
    private HashSet<uint>? _filterHiddenRowsSnapshot;
    private HashSet<uint>? _valueFilterHiddenRowsSnapshot;
    // R21-autofilter-sort-state-1: per-column ownership of the rows a Top-N/Average/condition/
    // color filter is hiding (sheet.ColumnFilterOwnedRows) must be permuted in lockstep with
    // FilterHiddenRows/ValueFilterHiddenRows, or it keeps naming the pre-sort row positions.
    private Dictionary<uint, HashSet<uint>>? _columnFilterOwnedRowsSnapshot;
    // R94-commands-sort-partial-reband-1: see RebandOwningTableIfAny -- a table-scoped sort whose
    // _range is only a proper subset of the table's data body (reachable via the quick ribbon Sort
    // buttons on an arbitrary row selection) still calls RebandTable on the table's WHOLE data
    // body, so rows outside _range need their own undo coverage independent of _snapshot.
    private List<(CellAddress Address, Cell? OldCell)>? _tableRebandSnapshot;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    // Undo snapshot for sheet.SortState (R19: Apply must record the sort it just performed so
    // the persisted <sortState> matches the data on disk, and Revert must restore whatever was
    // there before — which may be null, or a stale sortState left over from a prior Excel sort).
    private WorksheetSortStateModel? _priorSortState;
    private bool _sortStateCaptured;
    // R84-io-tables-listobject-5-1: when _range falls entirely inside one of the sheet's own
    // Structured Tables (Insert > Table), the sort's persisted indicator belongs in that table's
    // own <sortState> (xl/tables/tableN.xml), never as a sibling <sortState> under the worksheet
    // root — a table's autoFilter lives entirely inside its own table part, so a worksheet-root
    // sortState for a table-scoped sort is a shape Excel's writer never produces. _structuredTableIndex
    // is -1 when the range isn't table-owned (the classic worksheet-autofilter/plain-range case,
    // which keeps writing sheet.SortState exactly as before).
    private int _structuredTableIndex = -1;
    private StructuredTableModel? _priorStructuredTable;

    private sealed record SortPayloadCapture(
        SortCellPayload[][] Rows,
        List<List<(CellAddress Address, Cell? Cell, StyleId? StyleOnly, string? Hyperlink, HyperlinkMetadata? HyperlinkMetadata, IReadOnlyList<CellTextRun>? RichTextRuns, CellPhoneticGuide? PhoneticGuide)>> CellSnapshot,
        Dictionary<CellAddress, string> CommentSnapshot,
        Dictionary<CellAddress, string> CommentAuthorsSnapshot,
        HashSet<CellAddress> ShownCommentsSnapshot,
        Dictionary<CellAddress, ThreadedComment> ThreadedCommentSnapshot);

    private readonly struct SortCellPayload
    {
        public SortCellPayload(Cell? cell, StyleId? styleOnly, string? comment, string? commentAuthor, bool commentShown, ThreadedComment? threadedComment, string? hyperlink, HyperlinkMetadata? hyperlinkMetadata, IReadOnlyList<CellTextRun>? richTextRuns = null, CellPhoneticGuide? phoneticGuide = null)
        {
            Cell = cell;
            StyleOnly = styleOnly;
            Comment = comment;
            CommentAuthor = commentAuthor;
            CommentShown = commentShown;
            ThreadedComment = threadedComment;
            Hyperlink = hyperlink;
            HyperlinkMetadata = hyperlinkMetadata;
            RichTextRuns = richTextRuns;
            PhoneticGuide = phoneticGuide;
        }

        public Cell? Cell { get; }
        public StyleId? StyleOnly { get; }
        public string? Comment { get; }
        public string? CommentAuthor { get; }
        public bool CommentShown { get; }
        public ThreadedComment? ThreadedComment { get; }
        public string? Hyperlink { get; }
        public HyperlinkMetadata? HyperlinkMetadata { get; }
        public IReadOnlyList<CellTextRun>? RichTextRuns { get; }
        public CellPhoneticGuide? PhoneticGuide { get; }
    }

    public string Label => _sortKeys.Count == 1
        ? $"Sort {(_sortKeys[0].Ascending ? "Ascending" : "Descending")}"
        : "Sort";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_range.CellCount * BytesPerCell, int.MaxValue);

    public SortCommand(SheetId sheetId, GridRange range, uint sortByColOffset, bool ascending)
        : this(sheetId, range, [new SortKey(sortByColOffset, ascending)])
    {
    }

    public SortCommand(SheetId sheetId, GridRange range, IReadOnlyList<SortKey> sortKeys, SortOptions? options = null)
    {
        _sheetId = sheetId;
        _range = range;
        _sortKeys = sortKeys.Count == 0 ? [new SortKey(0, true)] : sortKeys;
        _options = options ?? new SortOptions();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _affectedCells = [];
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.Sort) is { } protectedOutcome)
            return protectedOutcome;

        // Guard against inverted ranges — uint subtraction would wrap to ~4B
        if (_range.End.Row < _range.Start.Row || _range.End.Col < _range.Start.Col)
            return new CommandOutcome(true); // nothing to sort

        // R27-protection-eval-deep-2: Excel documents that Sort is blocked on any range
        // containing locked cells on a protected worksheet "whether or not this element is
        // selected" — the Sort permission checked above only ever matters for a range that is
        // entirely unlocked. Without this, granting Sort alone would let a user rearrange data
        // the sheet owner explicitly locked (the default style for every cell).
        if (sheet.IsProtected)
        {
            for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            {
                for (var col = _range.Start.Col; col <= _range.End.Col; col++)
                {
                    if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, new CellAddress(_sheetId, row, col)))
                        return CommandGuards.RejectSheetProtected();
                }
            }
        }

        // Excel rejects sorts that contain merged cells, UNLESS every merge overlapping the range
        // is fully contained within it, spans exactly one row (a horizontal, cosmetic multi-column
        // merge), and all such merges are identically sized. In that uniform case (e.g. every row
        // of the range merged the same way across the same columns — a common "each record spans
        // N cosmetic columns" layout), Excel treats each merge as one sortable row unit and moves
        // it as a whole; the sort below already swaps entire GRID ROWS intact and never touches
        // MergedRegions, so the merge geometry stays put while the row content moves through it.
        // A vertical (multi-row) merge cannot be relaxed this way: the sort below swaps single grid
        // rows independently, so allowing a RowCount>1 merge through would scramble the row-to-row
        // association within that merged block (e.g. a 2-row merged record's two data rows could
        // land next to a different record's rows) — any such merge must still be rejected below.
        // Merges that only partially overlap the range, or that differ in size/shape from one
        // another, still make the range unsortable — this mirrors Excel's own "This operation
        // requires the merged cells to be identically sized" refusal.
        //
        // R34-commands-sort-custom-deep-1: a LeftToRight (byRows) sort instead swaps whole grid
        // COLUMNS below (ApplyLeftToRight), so the uniform layout that is safe there is the
        // transpose of the row case — every merge must be exactly one COLUMN wide, share the same
        // row-span, and there must be exactly one such merge per column of the range. Approving a
        // row-uniform merge (safe only for a top-to-bottom sort) for a column swap — or vice versa
        // — would leave the merge geometry anchored to its old physical row/column while the swap
        // relocates the data that geometry is supposed to describe, desyncing the two.
        var overlappingMerges = sheet.MergedRegions.Where(m => _range.Overlaps(m)).ToList();
        if (overlappingMerges.Count > 0)
        {
            bool uniform;
            if (_options.LeftToRight)
            {
                var firstRowSpan = overlappingMerges[0].RowCount;
                var firstStartRow = overlappingMerges[0].Start.Row;
                var rangeColCount = (int)(_range.End.Col - _range.Start.Col + 1);
                uniform = overlappingMerges.Count == rangeColCount &&
                    overlappingMerges.All(m =>
                        _range.Contains(m) && m.ColCount == 1 && m.RowCount == firstRowSpan && m.Start.Row == firstStartRow);
            }
            else
            {
                var firstColSpan = overlappingMerges[0].ColCount;
                var firstStartCol = overlappingMerges[0].Start.Col;
                // Merged regions never overlap one another (MergeCellsCommand rejects any merge
                // whose range intersects an existing one), so requiring the merge count to equal
                // the range's row count — on top of each merge being fully contained, exactly one
                // row tall, and identically sized — guarantees every row of the range is covered by
                // an identically-sized merge and none is left partially/un-merged.
                //
                // R100-commands-sort-merge-column-align-4-1: identically SIZED is not enough — the
                // sort below swaps whole grid rows through fixed column indexes (WriteCellPayload
                // writes ci back into the SAME column), so a same-width merge sitting at a different
                // Start.Col than the others would have its anchor value relocated into a covered
                // (non-anchor) cell of whichever row it swaps into, planting a live value where only
                // the top-left cell of a merge may hold one. Requiring every merge to also share the
                // first merge's Start.Col guarantees the merges line up as one vertical column-aligned
                // block, so the row swap moves each merge's anchor to another row's own anchor cell.
                var rangeRowCount = (int)(_range.End.Row - _range.Start.Row + 1);
                uniform = overlappingMerges.Count == rangeRowCount &&
                    overlappingMerges.All(m =>
                        _range.Contains(m) && m.RowCount == 1 && m.ColCount == firstColSpan && m.Start.Col == firstStartCol);
            }

            if (!uniform)
                return new CommandOutcome(false, "Cannot sort a range that contains merged cells.");
        }

        uint startRow = _range.Start.Row;
        uint endRow   = _range.End.Row;
        uint startCol = _range.Start.Col;
        uint endCol   = _range.End.Col;

        // R47-sibling-guard-asymmetry-sweep-3: Sort rewrites the sort range's rows/cells in place,
        // exactly the "clear then rewrite" primitive that ClearContentsCommand/FillCellsCommand/
        // MoveRangeCommand/CopyRangeCommand/PasteCellsCommand/InsertDeleteCellsCommand all guard
        // against a partially-covered CSE array or dynamic-array spill — reject if _range would
        // carry only some members of an array along while leaving others outside the sort range
        // untouched (Excel's "You cannot change part of an array"), matching the merged-cell overlap
        // guard just above.
        var sortShiftRegion = new CellShiftRegion(startRow, endRow, startCol, endCol);
        if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, sortShiftRegion)) is { } splitsArrayRejection)
            return splitsArrayRejection;

        uint colCount32 = endCol - startCol + 1;
        var keyLimit = _options.LeftToRight ? endRow - startRow + 1 : colCount32;
        if (_sortKeys.Any(key => key.ColumnOffset >= keyLimit))
            return new CommandOutcome(false, "Sort key offset is outside the sort range.");
        var keyColIndexes = _sortKeys
            .Select(key => ((int)key.ColumnOffset, key.Ascending, key.SortOn, key.TargetColor, key.CustomOrder, key.TargetIcon))
            .ToList();

        int rowCount = (int)(endRow - startRow + 1);
        int colCount = (int)(endCol - startCol + 1);

        // Redo replays Apply after Revert, so the snapshot must capture whatever SortState is
        // on the sheet right now (which is either the pristine pre-sort value, or — after a
        // Revert — back to that same pristine value) each time Apply runs.
        _priorSortState = sheet.SortState;
        _sortStateCaptured = true;

        // R84-io-tables-listobject-5-1: capture the owning Structured Table (if any) the same
        // way — Redo replays Apply after Revert, so this must re-resolve against the sheet's
        // current StructuredTables each time, exactly like _priorSortState above.
        _structuredTableIndex = FindOwningStructuredTableIndex(sheet, _range);
        _priorStructuredTable = _structuredTableIndex >= 0 ? sheet.StructuredTables[_structuredTableIndex] : null;

        if (_options.LeftToRight)
            return ApplyLeftToRight(ctx.Workbook, sheet, startRow, endRow, startCol, endCol, keyColIndexes, rowCount, colCount);

        // Read current state and save snapshot. Redo replays Apply after Revert,
        // so the snapshot must describe the current pre-sort state each time.
        _rowHeightSnapshot = CaptureRowHeights(sheet, startRow, rowCount);
        _rowStyleSnapshot = CaptureRowStyles(sheet, startRow, rowCount);
        _hiddenRowsSnapshot = CaptureHiddenRows(sheet, startRow, rowCount);
        _filterHiddenRowsSnapshot = CaptureFilterHiddenRows(sheet, startRow, rowCount);
        _valueFilterHiddenRowsSnapshot = CaptureValueFilterHiddenRows(sheet, startRow, rowCount);
        _columnFilterOwnedRowsSnapshot = CaptureColumnFilterOwnedRows(sheet, startRow, rowCount);
        var payloadCapture = CapturePayloads(sheet, _sheetId, startRow, startCol, rowCount, colCount);
        _snapshot = payloadCapture.CellSnapshot;
        _commentSnapshot = payloadCapture.CommentSnapshot;
        _commentAuthorsSnapshot = payloadCapture.CommentAuthorsSnapshot;
        _shownCommentsSnapshot = payloadCapture.ShownCommentsSnapshot;
        _threadedCommentSnapshot = payloadCapture.ThreadedCommentSnapshot;

        var rows = new List<(SortCellPayload[] Payloads, bool HasRowHeight, double RowHeight, bool IsHidden, bool IsFilterHidden, bool IsValueFilterHidden, List<uint>? OwnedFilterColumns, int OriginalIndex)>(rowCount);

        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            var hasRowHeight = sheet.RowHeights.TryGetValue(row, out var rowHeight);
            var isHidden = sheet.HiddenRows.Contains(row);
            var isFilterHidden = sheet.FilterHiddenRows.Contains(row);
            var isValueFilterHidden = sheet.ValueFilterHiddenRows.Contains(row);
            // R21-autofilter-sort-state-1: carry each row's column-owned-filter membership
            // along with it so it lands on the row's new post-sort position below.
            List<uint>? ownedFilterColumns = null;
            foreach (var (col, ownedRows) in _columnFilterOwnedRowsSnapshot)
            {
                if (ownedRows.Contains(row))
                    (ownedFilterColumns ??= []).Add(col);
            }

            rows.Add((payloadCapture.Rows[ri], hasRowHeight, rowHeight, isHidden, isFilterHidden, isValueFilterHidden, ownedFilterColumns, ri));
        }

        // R45-commands-sort-filter-interaction-3-1: Excel's Sort never moves a row that the
        // active AutoFilter (or one of its Top-N/Average/condition/color column filters) is
        // currently hiding — per Microsoft's own Sort documentation, hidden rows in a filtered
        // range are not sorted. Such a row must stay pinned at its own physical row with its
        // content completely untouched; only the VISIBLE rows among the range are reordered,
        // and only into the physical slots that were themselves visible before the sort. Filter-
        // hidden rows are therefore excluded from the sort worklist entirely here so they never
        // enter the comparator and never receive another row's content.
        var visibleSlots = new List<int>(rowCount);
        var sortable = new List<(SortCellPayload[] Payloads, bool HasRowHeight, double RowHeight, bool IsHidden, bool IsFilterHidden, bool IsValueFilterHidden, List<uint>? OwnedFilterColumns, int OriginalIndex)>(rowCount);
        for (int ri = 0; ri < rowCount; ri++)
        {
            if (rows[ri].IsFilterHidden || rows[ri].IsValueFilterHidden)
                continue;
            visibleSlots.Add(ri);
            sortable.Add(rows[ri]);
        }

        sortable.Sort((a, b) =>
        {
            foreach (var (index, ascending, sortOn, targetColor, customOrder, targetIcon) in keyColIndexes)
            {
                // Excel always places blank (and error) cells last regardless of sort direction.
                // Guard this BEFORE the ascending/descending negation so the blank-last ordering
                // is never inverted.
                if (sortOn == SortOn.CellValues)
                {
                    bool aBlank = IsBlankOrError(a.Payloads[index].Cell);
                    bool bBlank = IsBlankOrError(b.Payloads[index].Cell);
                    if (aBlank != bBlank)
                        return aBlank ? 1 : -1; // blank/error always goes last
                    if (aBlank)
                    {
                        // R27-sort-deep-2: within the "goes last" bucket, Excel's fixed
                        // precedence puts errors above blanks — independent of direction, same
                        // as the blank-last rule itself.
                        bool aError = a.Payloads[index].Cell?.Value is ErrorValue;
                        bool bError = b.Payloads[index].Cell?.Value is ErrorValue;
                        if (aError != bError)
                            return aError ? -1 : 1;
                        continue; // both blank, or both error — equal on this key, try next
                    }

                    // R91-commands-sort-customlist-5-1: a custom list's "member always precedes
                    // non-member" rule is a fixed property of the list itself, not of the sort
                    // direction — Excel's Sort dialog doesn't even let a user combine Descending
                    // with a custom list (choosing "Custom List..." replaces the A-to-Z/Z-to-A
                    // choice entirely). FreeX's Ascending toggle and custom-list picker are two
                    // independent controls that both land on the same SortKey, so guard the
                    // membership precedence here, before the shared ascending/descending negation
                    // below, so Descending only reverses the WITHIN-list order (e.g. Wed, Tue,
                    // Mon) and never lets a non-member value jump ahead of every list member.
                    if (customOrder is not null)
                    {
                        bool aMember = a.Payloads[index].Cell?.Value is TextValue taMember && customOrder.IndexOf(taMember.Value) >= 0;
                        bool bMember = b.Payloads[index].Cell?.Value is TextValue tbMember && customOrder.IndexOf(tbMember.Value) >= 0;
                        if (aMember != bMember)
                            return aMember ? -1 : 1; // list members precede non-members, regardless of direction
                    }
                }
                else if ((sortOn == SortOn.CellColor || sortOn == SortOn.FontColor) && targetColor is null)
                {
                    // R27-sort-deep-3: with no specific target color chosen, "no fill"/"no font
                    // color" must always sort last, direction-independent — same fixed-last rule
                    // as blanks above, so it must not be run through the ascending/descending
                    // negation below. R65-commands-sort-6-2: the remaining two-colors-present case
                    // still goes through CompareKey/negation unchanged, but CompareKey now treats
                    // it as a no-op (0) rather than inventing an R/G/B ordering — Excel has no
                    // ordering between two different colors when no target color was chosen.
                    // R39-commands-sort-custom-2-2: resolve the EFFECTIVE color (static style
                    // overlaid with any matching conditional-formatting rule's color), not just
                    // the cell's stored style, so a CF-only-colored cell isn't wrongly treated
                    // as "no fill".
                    var addrA = new CellAddress(_sheetId, startRow + (uint)a.OriginalIndex, startCol + (uint)index);
                    var addrB = new CellAddress(_sheetId, startRow + (uint)b.OriginalIndex, startCol + (uint)index);
                    var aColor = GetEffectiveColor(ctx.Workbook, sheet, addrA, a.Payloads[index].Cell, wantFill: sortOn == SortOn.CellColor);
                    var bColor = GetEffectiveColor(ctx.Workbook, sheet, addrB, b.Payloads[index].Cell, wantFill: sortOn == SortOn.CellColor);
                    bool aNoFill = aColor is null;
                    bool bNoFill = bColor is null;
                    if (aNoFill != bNoFill)
                        return aNoFill ? 1 : -1; // no fill/font color always goes last
                    if (aNoFill) // both no-fill — equal on this key, try next
                        continue;
                }
                else if (sortOn == SortOn.CellIcon && targetIcon is null)
                {
                    // R78-commands-sort-multikey-5-2: mirrors the no-target-color guard above —
                    // with no specific icon chosen, a cell with no matching icon-set rule (no
                    // icon at all) always sorts last, direction-independent; two DIFFERENT icons
                    // with no target chosen fall through to CompareKey below as a no-op (0),
                    // same rationale as R65-commands-sort-6-2 for color.
                    var addrA = new CellAddress(_sheetId, startRow + (uint)a.OriginalIndex, startCol + (uint)index);
                    var addrB = new CellAddress(_sheetId, startRow + (uint)b.OriginalIndex, startCol + (uint)index);
                    var aIcon = GetEffectiveIcon(ctx.Workbook, sheet, addrA, a.Payloads[index].Cell);
                    var bIcon = GetEffectiveIcon(ctx.Workbook, sheet, addrB, b.Payloads[index].Cell);
                    bool aNoIcon = aIcon is null;
                    bool bNoIcon = bIcon is null;
                    if (aNoIcon != bNoIcon)
                        return aNoIcon ? 1 : -1; // no icon always goes last
                    if (aNoIcon) // both no-icon — equal on this key, try next
                        continue;
                }

                var keyAddrA = new CellAddress(_sheetId, startRow + (uint)a.OriginalIndex, startCol + (uint)index);
                var keyAddrB = new CellAddress(_sheetId, startRow + (uint)b.OriginalIndex, startCol + (uint)index);
                var cmp = CompareKey(ctx.Workbook, sheet, keyAddrA, a.Payloads[index].Cell, keyAddrB, b.Payloads[index].Cell, sortOn, targetColor, targetIcon, customOrder, _options.CaseSensitive);
                if (cmp != 0)
                    return ascending ? cmp : -cmp;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex); // stable tiebreaker
        });

        // Reassemble the full per-slot row list: filter-hidden rows default to themselves
        // (OriginalIndex == ri, so no permutation/formula-rewrite happens for them below), and
        // the sorted visible rows are dropped back into exactly the physical slots that were
        // visible before the sort, in their new order.
        var finalRows = new (SortCellPayload[] Payloads, bool HasRowHeight, double RowHeight, bool IsHidden, bool IsFilterHidden, bool IsValueFilterHidden, List<uint>? OwnedFilterColumns, int OriginalIndex)[rowCount];
        for (int ri = 0; ri < rowCount; ri++)
            finalRows[ri] = rows[ri];
        for (int k = 0; k < visibleSlots.Count; k++)
            finalRows[visibleSlots[k]] = sortable[k];

        // Write sorted rows back
        var affected = new List<CellAddress>(rowCount * colCount);
        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            sheet.RowHeights.Remove(row);
            if (finalRows[ri].HasRowHeight)
                sheet.RowHeights[row] = finalRows[ri].RowHeight;
            // Permuted from the pre-sort snapshot via OriginalIndex rather than carried in the row
            // tuple: the whole-row default style is keyed by row number in sheet.RowStyles, so the
            // row that MOVED here must bring its own banner format with it. _rowStyleSnapshot is
            // read-only here (it is also the Revert baseline), so it stays correct across the loop
            // even as sheet.RowStyles is being rewritten.
            sheet.RowStyles.Remove(row);
            if (_rowStyleSnapshot is { } rowStyles
                && rowStyles.TryGetValue(startRow + (uint)finalRows[ri].OriginalIndex, out var movedRowStyle))
            {
                sheet.RowStyles[row] = movedRowStyle;
            }
            sheet.HiddenRows.Remove(row);
            if (finalRows[ri].IsHidden)
                sheet.HiddenRows.Add(row);
            sheet.FilterHiddenRows.Remove(row);
            if (finalRows[ri].IsFilterHidden)
                sheet.FilterHiddenRows.Add(row);
            // sheet.ValueFilterHiddenRows must be permuted in lockstep with FilterHiddenRows — it
            // records exactly which of those rows the value-filter mechanism (sheet.ActiveValueFilterColumns)
            // currently owns, and FilterCommand.RecomputeHiddenRows uses it to decide which rows it may
            // safely un-hide. Left unpermuted, it would name the wrong rows the moment Sort reorders them.
            sheet.ValueFilterHiddenRows.Remove(row);
            if (finalRows[ri].IsValueFilterHidden)
                sheet.ValueFilterHiddenRows.Add(row);

            // R21-autofilter-sort-state-1: sheet.ColumnFilterOwnedRows must be permuted in
            // lockstep too — it records, per column, exactly which rows a Top-N/Average/condition/
            // color filter is hiding, and FilterHiddenRowUpdater.ClearColumnOwnedRange /
            // IsHiddenByAnyColumnOwnedFilter rely on it to know which rows that mechanism owns.
            // Left unpermuted, it would keep naming the pre-sort row positions after the rows move.
            foreach (var col in _columnFilterOwnedRowsSnapshot.Keys)
                sheet.ColumnFilterOwnedRows[col].Remove(row);
            if (finalRows[ri].OwnedFilterColumns is { } ownedFilterColumns)
            {
                foreach (var col in ownedFilterColumns)
                    sheet.ColumnFilterOwnedRows[col].Add(row);
            }

            // N37: rows are permuted from OriginalIndex to ri — Excel rewrites each moved
            // formula's relative references the same way a cut/paste to the new row would,
            // so the row delta a cell actually moved must be applied to its formula text. A
            // filter-hidden row's finalRows[ri].OriginalIndex is always ri itself (see above),
            // so rowDelta is always 0 for it — its formula text (and everything else) is
            // written back completely unchanged.
            int rowDelta = ri - finalRows[ri].OriginalIndex;

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col  = startCol + (uint)ci;
                var addr  = new CellAddress(_sheetId, row, col);
                WriteCellPayload(sheet, addr, finalRows[ri].Payloads[ci], rowDelta, 0, sheet.Name);
                affected.Add(addr);
            }
        }

        _affectedCells = affected;
        ApplySortStateResult(sheet, BuildSortState(_range, keyColIndexes, leftToRight: false));
        RebandOwningTableIfAny(ctx.Workbook, sheet);
        return new CommandOutcome(true, AffectedCells: affected);
    }

    // R90-io-table-style-banding-5-3: real Excel's table banding is purely positional and
    // continuously re-flows after a sort — the rows just moved, but StructuredTableStyleService's
    // load-time bake travels WITH each Cell (StyleId included) rather than recomputing, so without
    // this the alternating fill would reflect the PRE-sort row order instead of the new one.
    //
    // R94-commands-sort-partial-reband-1: RebandTable always repaints the table's ENTIRE data body
    // with forceFill:true (MergeStyleOntoCell's keepExistingFill is unconditionally false under
    // forceFill), not just _range. FindOwningStructuredTableIndex only requires _range's row span
    // to be CONTAINED WITHIN the table (table.Range.Contains(range)), not equal to its full data
    // body -- the quick ribbon Sort Ascending/Descending buttons pass an arbitrary user selection
    // straight through, so _range can be a genuine proper subset of the table. _snapshot only
    // covers _range, so without this wider capture a row's explicit FillColor outside _range could
    // be silently overwritten by the reband with no undo coverage at all.
    private void RebandOwningTableIfAny(Workbook workbook, Sheet sheet)
    {
        if (_structuredTableIndex < 0 || _structuredTableIndex >= sheet.StructuredTables.Count)
            return;

        var table = sheet.StructuredTables[_structuredTableIndex];
        var captured = new List<(CellAddress Address, Cell? OldCell)>();
        var (firstDataRow, lastDataRow) = StructuredTableEditEffects.GetDataBodyRowBounds(table);
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
            {
                var address = new CellAddress(_sheetId, row, col);
                captured.Add((address, sheet.GetCell(address)?.Clone()));
            }
        }

        _tableRebandSnapshot = captured;
        StructuredTableStyleService.RebandTable(workbook, sheet, table);
    }

    private CommandOutcome ApplyLeftToRight(
        Workbook workbook,
        Sheet sheet,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol,
        IReadOnlyList<(int RowIndex, bool Ascending, SortOn SortOn, CellColor? TargetColor, CustomSortOrder? CustomOrder, CfIconOverride? TargetIcon)> keyRowIndexes,
        int rowCount,
        int colCount)
    {
        _rowHeightSnapshot = null;
        // Left-to-right sort permutes COLUMNS, so per-ROW state (heights, banner styles, hidden
        // flags) is untouched by it -- null here means Revert skips restoring what Apply never moved.
        _rowStyleSnapshot = null;
        _hiddenRowsSnapshot = null;
        _filterHiddenRowsSnapshot = null;
        _valueFilterHiddenRowsSnapshot = null;
        _columnFilterOwnedRowsSnapshot = null;
        var payloadCapture = CapturePayloads(sheet, _sheetId, startRow, startCol, rowCount, colCount);
        _snapshot = payloadCapture.CellSnapshot;
        _commentSnapshot = payloadCapture.CommentSnapshot;
        _commentAuthorsSnapshot = payloadCapture.CommentAuthorsSnapshot;
        _shownCommentsSnapshot = payloadCapture.ShownCommentsSnapshot;
        _threadedCommentSnapshot = payloadCapture.ThreadedCommentSnapshot;

        var columns = new List<(SortCellPayload[] Payloads, int OriginalIndex)>(colCount);

        for (int ci = 0; ci < colCount; ci++)
        {
            columns.Add((CopyColumnPayloads(payloadCapture.Rows, ci, rowCount), ci));
        }

        columns.Sort((a, b) =>
        {
            foreach (var (index, ascending, sortOn, targetColor, customOrder, targetIcon) in keyRowIndexes)
            {
                // Excel always places blank (and error) cells last regardless of sort direction.
                // Guard this BEFORE the ascending/descending negation so the blank-last ordering
                // is never inverted.
                if (sortOn == SortOn.CellValues)
                {
                    bool aBlank = IsBlankOrError(a.Payloads[index].Cell);
                    bool bBlank = IsBlankOrError(b.Payloads[index].Cell);
                    if (aBlank != bBlank)
                        return aBlank ? 1 : -1; // blank/error always goes last
                    if (aBlank)
                    {
                        // R27-sort-deep-2: within the "goes last" bucket, Excel's fixed
                        // precedence puts errors above blanks — independent of direction, same
                        // as the blank-last rule itself.
                        bool aError = a.Payloads[index].Cell?.Value is ErrorValue;
                        bool bError = b.Payloads[index].Cell?.Value is ErrorValue;
                        if (aError != bError)
                            return aError ? -1 : 1;
                        continue; // both blank, or both error — equal on this key, try next
                    }

                    // R91-commands-sort-customlist-5-1: mirrors the guard in Apply's top-to-bottom
                    // comparator above — a custom list's "member always precedes non-member" rule
                    // must not be inverted by the Descending direction toggle.
                    if (customOrder is not null)
                    {
                        bool aMember = a.Payloads[index].Cell?.Value is TextValue taMember && customOrder.IndexOf(taMember.Value) >= 0;
                        bool bMember = b.Payloads[index].Cell?.Value is TextValue tbMember && customOrder.IndexOf(tbMember.Value) >= 0;
                        if (aMember != bMember)
                            return aMember ? -1 : 1; // list members precede non-members, regardless of direction
                    }
                }
                else if ((sortOn == SortOn.CellColor || sortOn == SortOn.FontColor) && targetColor is null)
                {
                    // R27-sort-deep-3: with no specific target color chosen, "no fill"/"no font
                    // color" must always sort last, direction-independent — mirrors the guard in
                    // Apply's top-to-bottom comparator above.
                    // R39-commands-sort-custom-2-2: resolve the EFFECTIVE color (static style
                    // overlaid with any matching conditional-formatting rule's color) here too.
                    var addrA = new CellAddress(_sheetId, startRow + (uint)index, startCol + (uint)a.OriginalIndex);
                    var addrB = new CellAddress(_sheetId, startRow + (uint)index, startCol + (uint)b.OriginalIndex);
                    var aColor = GetEffectiveColor(workbook, sheet, addrA, a.Payloads[index].Cell, wantFill: sortOn == SortOn.CellColor);
                    var bColor = GetEffectiveColor(workbook, sheet, addrB, b.Payloads[index].Cell, wantFill: sortOn == SortOn.CellColor);
                    bool aNoFill = aColor is null;
                    bool bNoFill = bColor is null;
                    if (aNoFill != bNoFill)
                        return aNoFill ? 1 : -1; // no fill/font color always goes last
                    if (aNoFill) // both no-fill — equal on this key, try next
                        continue;
                }
                else if (sortOn == SortOn.CellIcon && targetIcon is null)
                {
                    // R78-commands-sort-multikey-5-2: mirrors the no-target-color guard above,
                    // and the top-to-bottom no-target-icon guard in Apply's comparator.
                    var addrA = new CellAddress(_sheetId, startRow + (uint)index, startCol + (uint)a.OriginalIndex);
                    var addrB = new CellAddress(_sheetId, startRow + (uint)index, startCol + (uint)b.OriginalIndex);
                    var aIcon = GetEffectiveIcon(workbook, sheet, addrA, a.Payloads[index].Cell);
                    var bIcon = GetEffectiveIcon(workbook, sheet, addrB, b.Payloads[index].Cell);
                    bool aNoIcon = aIcon is null;
                    bool bNoIcon = bIcon is null;
                    if (aNoIcon != bNoIcon)
                        return aNoIcon ? 1 : -1; // no icon always goes last
                    if (aNoIcon) // both no-icon — equal on this key, try next
                        continue;
                }

                var keyAddrA = new CellAddress(_sheetId, startRow + (uint)index, startCol + (uint)a.OriginalIndex);
                var keyAddrB = new CellAddress(_sheetId, startRow + (uint)index, startCol + (uint)b.OriginalIndex);
                var cmp = CompareKey(workbook, sheet, keyAddrA, a.Payloads[index].Cell, keyAddrB, b.Payloads[index].Cell, sortOn, targetColor, targetIcon, customOrder, _options.CaseSensitive);
                if (cmp != 0)
                    return ascending ? cmp : -cmp;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex);
        });

        var affected = new List<CellAddress>(rowCount * colCount);
        for (int ci = 0; ci < colCount; ci++)
        {
            uint col = startCol + (uint)ci;
            // N37: columns are permuted from OriginalIndex to ci for a left-to-right sort —
            // rewrite each moved formula's relative references by the column delta it moved,
            // mirroring the row-delta rewrite the top-to-bottom sort applies below.
            int colDelta = ci - columns[ci].OriginalIndex;
            for (int ri = 0; ri < rowCount; ri++)
            {
                uint row = startRow + (uint)ri;
                var addr = new CellAddress(_sheetId, row, col);
                WriteCellPayload(sheet, addr, columns[ci].Payloads[ri], 0, colDelta, sheet.Name);
                affected.Add(addr);
            }
        }

        _affectedCells = affected;
        ApplySortStateResult(sheet, BuildSortState(_range, keyRowIndexes, leftToRight: true));
        RebandOwningTableIfAny(workbook, sheet);
        return new CommandOutcome(true, AffectedCells: affected);
    }

    /// <summary>
    /// Builds the sheet-level sortState metadata (ref + per-key sortCondition) describing the
    /// sort that was just applied, matching what Excel itself writes after a Data > Sort.
    /// R19: previously SortCommand never touched sheet.SortState at all, so the saved file's
    /// persisted sort metadata was either missing entirely or — worse — left stale from whatever
    /// sort (if any) had been recorded before this command ran.
    /// </summary>
    private WorksheetSortStateModel BuildSortState(
        GridRange range,
        IReadOnlyList<(int Index, bool Ascending, SortOn SortOn, CellColor? TargetColor, CustomSortOrder? CustomOrder, CfIconOverride? TargetIcon)> keys,
        bool leftToRight)
    {
        var model = new WorksheetSortStateModel
        {
            Reference = range.ToString(),
            ColumnSort = leftToRight ? true : null,
            CaseSensitive = _options.CaseSensitive ? true : null
        };

        foreach (var (index, ascending, sortOn, _, customOrder, _) in keys)
        {
            // Top-to-bottom: each key is a column, and its condition ref spans the full sorted
            // row range within that single column. Left-to-right: each key is a row, and its
            // condition ref spans the full sorted column range within that single row.
            var conditionRange = leftToRight
                ? new GridRange(
                    new CellAddress(_sheetId, range.Start.Row + (uint)index, range.Start.Col),
                    new CellAddress(_sheetId, range.Start.Row + (uint)index, range.End.Col))
                : new GridRange(
                    new CellAddress(_sheetId, range.Start.Row, range.Start.Col + (uint)index),
                    new CellAddress(_sheetId, range.End.Row, range.Start.Col + (uint)index));

            // R34-commands-sort-custom-deep-3: a custom-list ("First key sort order") key must
            // round-trip its list through the persisted customList attribute, or reopening the
            // saved file shows "Normal" instead of the custom order that was actually applied.
            // Note: TargetColor/TargetIcon (cellColor/fontColor/icon sorts) have no corresponding
            // fix here — a real dxfId must reference an entry in the workbook's <dxfs>
            // differential-format list, and Workbook/Sheet have no such registry to allocate one
            // from; stamping an arbitrary dxfId would point Excel at an unrelated (or
            // out-of-range) format, which is worse than omitting the attribute.
            model.Conditions.Add(new WorksheetSortConditionModel
            {
                Reference = conditionRange.ToString(),
                Descending = ascending ? null : true,
                SortBy = sortOn switch
                {
                    SortOn.CellColor => "cellColor",
                    SortOn.FontColor => "fontColor",
                    SortOn.CellIcon => "icon",
                    _ => null
                },
                CustomList = customOrder is not null ? string.Join(",", customOrder.Tokens) : null
            });
        }

        return model;
    }

    /// <summary>
    /// R84-io-tables-listobject-5-1: finds the index of the Structured Table on <paramref
    /// name="sheet"/> that owns <paramref name="range"/> — i.e. a table whose column span exactly
    /// matches the range's and whose row span fully contains it (true whether or not the caller
    /// already stripped the header row, since callers differ on that — see
    /// MainWindow.DataFilterCommands.ExcludeHeaderRowForAutoFilterSort vs the ribbon's quick Sort
    /// buttons). Returns -1 when no table owns the range, meaning this is a plain worksheet range/
    /// autofilter sort and sheet.SortState is still the right place to persist the result.
    /// </summary>
    private static int FindOwningStructuredTableIndex(Sheet sheet, GridRange range)
    {
        for (var i = 0; i < sheet.StructuredTables.Count; i++)
        {
            var table = sheet.StructuredTables[i];
            if (table.Range.Start.Col == range.Start.Col &&
                table.Range.End.Col == range.End.Col &&
                table.Range.Contains(range))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// R84-io-tables-listobject-5-1: records the sort just performed in whichever place Excel
    /// itself would persist it. When _range belongs to a Structured Table, that's the table's own
    /// &lt;sortState&gt; inside xl/tables/tableN.xml (via StructuredTableModel.NativeSortStateXml)
    /// — sheet.SortState is intentionally left untouched, since a table-scoped sort never gets a
    /// worksheet-root &lt;sortState&gt; sibling from Excel's own writer. Otherwise this is the
    /// classic plain-range/worksheet-autofilter sort, which keeps writing sheet.SortState exactly
    /// as before R84.
    /// </summary>
    private void ApplySortStateResult(Sheet sheet, WorksheetSortStateModel model)
    {
        if (_structuredTableIndex >= 0 && _structuredTableIndex < sheet.StructuredTables.Count)
        {
            var table = sheet.StructuredTables[_structuredTableIndex];
            sheet.StructuredTables[_structuredTableIndex] =
                CopyTableWithNativeSortState(table, BuildTableNativeSortStateXml(model));
            return;
        }

        sheet.SortState = model;
    }

    /// <summary>
    /// Serializes <paramref name="model"/> into the raw &lt;sortState&gt; XML that
    /// StructuredTableModel.NativeSortStateXml expects (see XlsxStructuredTableWriter.
    /// TryCreateNativeSortState, which re-parses this string and requires the root element name to
    /// be the spreadsheetml "sortState" element) — the table-part shape omits columnSort (a
    /// Structured Table's Sort dialog never offers "left to right", so Excel's own table sortState
    /// never carries that attribute).
    /// </summary>
    private static string BuildTableNativeSortStateXml(WorksheetSortStateModel model)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var element = new XElement(ns + "sortState");
        if (!string.IsNullOrWhiteSpace(model.Reference))
            element.SetAttributeValue("ref", model.Reference);
        if (model.CaseSensitive == true)
            element.SetAttributeValue("caseSensitive", "1");

        foreach (var condition in model.Conditions)
        {
            var conditionElement = new XElement(ns + "sortCondition");
            if (!string.IsNullOrWhiteSpace(condition.Reference))
                conditionElement.SetAttributeValue("ref", condition.Reference);
            if (condition.Descending == true)
                conditionElement.SetAttributeValue("descending", "1");
            if (!string.IsNullOrWhiteSpace(condition.SortBy))
                conditionElement.SetAttributeValue("sortBy", condition.SortBy);
            if (!string.IsNullOrWhiteSpace(condition.CustomList))
                conditionElement.SetAttributeValue("customList", condition.CustomList);
            element.Add(conditionElement);
        }

        return element.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Full-field copy of <paramref name="table"/> with only NativeSortStateXml replaced, matching
    /// the copy-then-replace-in-list convention every other StructuredTableModel-mutating command
    /// uses (e.g. StructuredTableCommand.CopyWithStyleOptions) — StructuredTableModel's properties
    /// are init-only and _priorStructuredTable (captured for Revert) must keep pointing at the
    /// original, untouched instance.
    /// </summary>
    private static StructuredTableModel CopyTableWithNativeSortState(StructuredTableModel table, string? nativeSortStateXml)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = table.Range,
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
            NativeSortStateXml = nativeSortStateXml,
            NativeAttributes = table.NativeAttributes,
            NativeChildXmls = table.NativeChildXmls,
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
        };

        copy.Columns.AddRange(table.Columns);
        copy.FilterColumns.AddRange(table.FilterColumns);
        return copy;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        // R94-commands-sort-partial-reband-1: undo the reband repaint FIRST -- it was the very
        // last effect Apply performed (see RebandOwningTableIfAny). A row outside _range that
        // RebandTable touched has no other undo coverage; a row inside _range is also captured
        // here but is harmlessly re-overwritten (with the same value) by RestoreCellSnapshot below.
        if (_tableRebandSnapshot is not null)
        {
            foreach (var (address, oldCell) in _tableRebandSnapshot)
            {
                if (oldCell is null)
                    sheet.ClearCell(address);
                else
                    sheet.SetCell(address, oldCell);
            }
        }

        RestoreCellSnapshot(sheet, _snapshot);
        RestoreCommentSnapshots(sheet);
        RestoreRowHeights(sheet);
        RestoreRowStyles(sheet);
        RestoreHiddenRows(sheet);
        RestoreFilterHiddenRows(sheet);
        RestoreValueFilterHiddenRows(sheet);
        RestoreColumnFilterOwnedRows(sheet);
        if (_sortStateCaptured)
            sheet.SortState = _priorSortState;
        // R84-io-tables-listobject-5-1: undo a table-scoped sort's NativeSortStateXml change by
        // putting the original StructuredTableModel instance back, mirroring how sheet.SortState
        // is restored just above.
        if (_structuredTableIndex >= 0 && _structuredTableIndex < sheet.StructuredTables.Count)
            sheet.StructuredTables[_structuredTableIndex] = _priorStructuredTable!;
    }

    private static SortPayloadCapture CapturePayloads(
        Sheet sheet,
        SheetId sheetId,
        uint startRow,
        uint startCol,
        int rowCount,
        int colCount)
    {
        var rows = new SortCellPayload[rowCount][];
        var cellSnapshot = new List<List<(CellAddress, Cell?, StyleId?, string?, HyperlinkMetadata?, IReadOnlyList<CellTextRun>?, CellPhoneticGuide?)>>(rowCount);
        var commentSnapshot = new Dictionary<CellAddress, string>();
        var commentAuthorsSnapshot = new Dictionary<CellAddress, string>();
        var shownCommentsSnapshot = new HashSet<CellAddress>();
        var threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>();

        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            var payloadRow = new SortCellPayload[colCount];
            var snapRow = new List<(CellAddress, Cell?, StyleId?, string?, HyperlinkMetadata?, IReadOnlyList<CellTextRun>?, CellPhoneticGuide?)>(colCount);

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col = startCol + (uint)ci;
                var addr = new CellAddress(sheetId, row, col);
                var payload = CaptureCellPayload(sheet, addr, out var snapshotCell, out var snapshotStyleOnly, out var snapshotHyperlink, out var snapshotHyperlinkMetadata, out var snapshotRichTextRuns, out var snapshotPhoneticGuide);
                payloadRow[ci] = payload;
                snapRow.Add((addr, snapshotCell, snapshotStyleOnly, snapshotHyperlink, snapshotHyperlinkMetadata, snapshotRichTextRuns, snapshotPhoneticGuide));
                if (payload.Comment is not null)
                    commentSnapshot[addr] = payload.Comment;
                if (payload.CommentAuthor is not null)
                    commentAuthorsSnapshot[addr] = payload.CommentAuthor;
                if (payload.CommentShown)
                    shownCommentsSnapshot.Add(addr);
                if (payload.ThreadedComment is not null)
                    threadedCommentSnapshot[addr] = payload.ThreadedComment;
            }

            rows[ri] = payloadRow;
            cellSnapshot.Add(snapRow);
        }

        return new SortPayloadCapture(rows, cellSnapshot, commentSnapshot, commentAuthorsSnapshot, shownCommentsSnapshot, threadedCommentSnapshot);
    }

    private static Dictionary<uint, StyleId> CaptureRowStyles(Sheet sheet, uint startRow, int rowCount)
    {
        var snapshot = new Dictionary<uint, StyleId>();
        for (int ri = 0; ri < rowCount; ri++)
        {
            var row = startRow + (uint)ri;
            if (sheet.RowStyles.TryGetValue(row, out var styleId))
                snapshot[row] = styleId;
        }

        return snapshot;
    }

    private static Dictionary<uint, double> CaptureRowHeights(Sheet sheet, uint startRow, int rowCount)
    {
        var snapshot = new Dictionary<uint, double>();
        for (int ri = 0; ri < rowCount; ri++)
        {
            var row = startRow + (uint)ri;
            if (sheet.RowHeights.TryGetValue(row, out var height))
                snapshot[row] = height;
        }

        return snapshot;
    }

    private static HashSet<uint> CaptureHiddenRows(Sheet sheet, uint startRow, int rowCount)
    {
        var snapshot = new HashSet<uint>();
        for (int ri = 0; ri < rowCount; ri++)
        {
            var row = startRow + (uint)ri;
            if (sheet.HiddenRows.Contains(row))
                snapshot.Add(row);
        }

        return snapshot;
    }

    private static HashSet<uint> CaptureFilterHiddenRows(Sheet sheet, uint startRow, int rowCount)
    {
        var snapshot = new HashSet<uint>();
        for (int ri = 0; ri < rowCount; ri++)
        {
            var row = startRow + (uint)ri;
            if (sheet.FilterHiddenRows.Contains(row))
                snapshot.Add(row);
        }

        return snapshot;
    }

    private static HashSet<uint> CaptureValueFilterHiddenRows(Sheet sheet, uint startRow, int rowCount)
    {
        var snapshot = new HashSet<uint>();
        for (int ri = 0; ri < rowCount; ri++)
        {
            var row = startRow + (uint)ri;
            if (sheet.ValueFilterHiddenRows.Contains(row))
                snapshot.Add(row);
        }

        return snapshot;
    }

    // R21-autofilter-sort-state-1: captures, per column, which rows within the sort range are
    // currently owned by a Top-N/Average/condition/color filter (sheet.ColumnFilterOwnedRows),
    // mirroring CaptureFilterHiddenRows/CaptureValueFilterHiddenRows above so the ownership can
    // be permuted (and restored on Revert) in lockstep with the rows themselves.
    private static Dictionary<uint, HashSet<uint>> CaptureColumnFilterOwnedRows(Sheet sheet, uint startRow, int rowCount)
    {
        var snapshot = new Dictionary<uint, HashSet<uint>>();
        foreach (var (col, ownedRows) in sheet.ColumnFilterOwnedRows)
        {
            for (int ri = 0; ri < rowCount; ri++)
            {
                var row = startRow + (uint)ri;
                if (ownedRows.Contains(row))
                {
                    if (!snapshot.TryGetValue(col, out var set))
                        snapshot[col] = set = [];
                    set.Add(row);
                }
            }
        }

        return snapshot;
    }

    private static SortCellPayload CaptureCellPayload(
        Sheet sheet,
        CellAddress address,
        out Cell? snapshotCell,
        out StyleId? snapshotStyleOnly,
        out string? snapshotHyperlink,
        out HyperlinkMetadata? snapshotHyperlinkMetadata,
        out IReadOnlyList<CellTextRun>? snapshotRichTextRuns,
        out CellPhoneticGuide? snapshotPhoneticGuide)
    {
        var cell = sheet.GetCell(address);
        sheet.Comments.TryGetValue(address, out var comment);
        sheet.CommentAuthors.TryGetValue(address, out var commentAuthor);
        var commentShown = sheet.ShownComments.Contains(address);
        sheet.ThreadedComments.TryGetValue(address, out var threadedComment);
        sheet.Hyperlinks.TryGetValue(address, out var hyperlink);
        sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
        sheet.RichTextRuns.TryGetValue(address, out var richTextRuns);
        sheet.CellPhoneticGuides.TryGetValue(address, out var phoneticGuide);
        var styleOnly = cell is null ? sheet.GetStyleOnly(address.Row, address.Col) : null;

        // The sortable payload and undo snapshot must not share mutable cell instances.
        snapshotCell = cell?.Clone();
        snapshotStyleOnly = styleOnly;
        snapshotHyperlink = hyperlink;
        snapshotHyperlinkMetadata = hyperlinkMetadata;
        snapshotRichTextRuns = richTextRuns;
        snapshotPhoneticGuide = phoneticGuide;
        return new SortCellPayload(cell?.Clone(), styleOnly, comment, commentAuthor, commentShown, threadedComment, hyperlink, hyperlinkMetadata, richTextRuns, phoneticGuide);
    }

    private static SortCellPayload[] CopyColumnPayloads(SortCellPayload[][] rows, int columnIndex, int rowCount)
    {
        var column = new SortCellPayload[rowCount];
        for (int ri = 0; ri < rowCount; ri++)
            column[ri] = rows[ri][columnIndex];

        return column;
    }

    private static void WriteCellPayload(
        Sheet sheet, CellAddress address, SortCellPayload payload,
        int rowDelta = 0, int colDelta = 0, string? sheetName = null)
    {
        WriteCellClone(sheet, address, payload.Cell, payload.StyleOnly, rowDelta, colDelta, sheetName);

        sheet.Comments.Remove(address);
        if (payload.Comment is not null)
            sheet.Comments[address] = payload.Comment;

        sheet.CommentAuthors.Remove(address);
        if (payload.CommentAuthor is not null)
            sheet.CommentAuthors[address] = payload.CommentAuthor;

        sheet.ShownComments.Remove(address);
        if (payload.CommentShown)
            sheet.ShownComments.Add(address);

        sheet.ThreadedComments.Remove(address);
        if (payload.ThreadedComment is not null)
            sheet.ThreadedComments[address] = payload.ThreadedComment;

        sheet.Hyperlinks.Remove(address);
        if (payload.Hyperlink is not null)
            sheet.Hyperlinks[address] = payload.Hyperlink;

        sheet.HyperlinkMetadata.Remove(address);
        if (payload.HyperlinkMetadata is not null)
            sheet.HyperlinkMetadata[address] = payload.HyperlinkMetadata;

        sheet.RichTextRuns.Remove(address);
        if (payload.RichTextRuns is not null)
            sheet.RichTextRuns[address] = payload.RichTextRuns;

        sheet.CellPhoneticGuides.Remove(address);
        if (payload.PhoneticGuide is not null)
            sheet.CellPhoneticGuides[address] = payload.PhoneticGuide;
    }

    private static void WriteCellClone(
        Sheet sheet, CellAddress address, Cell? cell, StyleId? styleOnly = null,
        int rowDelta = 0, int colDelta = 0, string? sheetName = null)
    {
        if (cell is null)
        {
            sheet.ClearCell(address);
            if (styleOnly.HasValue)
                sheet.SetStyleOnly(address.Row, address.Col, styleOnly.Value);
            else
                sheet.ClearStyleOnly(address.Row, address.Col);
        }
        else
        {
            var clone = cell.Clone();
            // N37: a sort permutes each cell to a new row (or column, for a left-to-right sort) —
            // Excel treats that exactly like a per-cell cut/paste and rewrites the formula's
            // relative references by the distance it moved (absolute $ references are unaffected,
            // same as FillCellsCommand.CloneForTarget's PasteOffsetOp usage). Only rewrite when a
            // delta and host sheet name were actually supplied (undo/restore always passes the
            // defaults, since cells there return to their exact original address/text).
            if ((rowDelta != 0 || colDelta != 0) && sheetName is not null &&
                clone.HasFormula && clone.FormulaText is { } formula)
            {
                clone.FormulaText = FormulaRewriter.Rewrite(
                    formula, new PasteOffsetOp(rowDelta, colDelta), sheetName) ?? formula;
            }
            sheet.SetCell(address, clone);
        }
    }

    private static void RestoreCellSnapshot(Sheet sheet, List<List<(CellAddress Address, Cell? Cell, StyleId? StyleOnly, string? Hyperlink, HyperlinkMetadata? HyperlinkMetadata, IReadOnlyList<CellTextRun>? RichTextRuns, CellPhoneticGuide? PhoneticGuide)>> snapshot)
    {
        foreach (var snapRow in snapshot)
        {
            foreach (var (addr, cell, styleOnly, hyperlink, hyperlinkMetadata, richTextRuns, phoneticGuide) in snapRow)
            {
                WriteCellClone(sheet, addr, cell, styleOnly);

                sheet.Hyperlinks.Remove(addr);
                if (hyperlink is not null)
                    sheet.Hyperlinks[addr] = hyperlink;

                sheet.HyperlinkMetadata.Remove(addr);
                if (hyperlinkMetadata is not null)
                    sheet.HyperlinkMetadata[addr] = hyperlinkMetadata;

                sheet.RichTextRuns.Remove(addr);
                if (richTextRuns is not null)
                    sheet.RichTextRuns[addr] = richTextRuns;

                sheet.CellPhoneticGuides.Remove(addr);
                if (phoneticGuide is not null)
                    sheet.CellPhoneticGuides[addr] = phoneticGuide;
            }
        }
    }

    private void RestoreCommentSnapshots(Sheet sheet)
    {
        foreach (var addr in _affectedCells)
        {
            sheet.Comments.Remove(addr);
            sheet.CommentAuthors.Remove(addr);
            sheet.ShownComments.Remove(addr);
            sheet.ThreadedComments.Remove(addr);
        }

        if (_commentSnapshot is not null)
        {
            foreach (var (addr, comment) in _commentSnapshot)
                sheet.Comments[addr] = comment;
        }

        if (_commentAuthorsSnapshot is not null)
        {
            foreach (var (addr, author) in _commentAuthorsSnapshot)
                sheet.CommentAuthors[addr] = author;
        }

        if (_shownCommentsSnapshot is not null)
        {
            foreach (var addr in _shownCommentsSnapshot)
                sheet.ShownComments.Add(addr);
        }

        if (_threadedCommentSnapshot is not null)
        {
            foreach (var (addr, comment) in _threadedCommentSnapshot)
                sheet.ThreadedComments[addr] = comment;
        }
    }

    private void RestoreRowHeights(Sheet sheet)
    {
        if (_rowHeightSnapshot is null)
            return;

        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            sheet.RowHeights.Remove(row);

        foreach (var (row, height) in _rowHeightSnapshot)
            sheet.RowHeights[row] = height;
    }

    private void RestoreRowStyles(Sheet sheet)
    {
        if (_rowStyleSnapshot is null)
            return;

        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            sheet.RowStyles.Remove(row);

        foreach (var (row, styleId) in _rowStyleSnapshot)
            sheet.RowStyles[row] = styleId;
    }

    private void RestoreHiddenRows(Sheet sheet)
    {
        if (_hiddenRowsSnapshot is null)
            return;

        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            sheet.HiddenRows.Remove(row);

        foreach (var row in _hiddenRowsSnapshot)
            sheet.HiddenRows.Add(row);
    }

    private void RestoreFilterHiddenRows(Sheet sheet)
    {
        if (_filterHiddenRowsSnapshot is null)
            return;

        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            sheet.FilterHiddenRows.Remove(row);

        foreach (var row in _filterHiddenRowsSnapshot)
            sheet.FilterHiddenRows.Add(row);
    }

    private void RestoreValueFilterHiddenRows(Sheet sheet)
    {
        if (_valueFilterHiddenRowsSnapshot is null)
            return;

        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            sheet.ValueFilterHiddenRows.Remove(row);

        foreach (var row in _valueFilterHiddenRowsSnapshot)
            sheet.ValueFilterHiddenRows.Add(row);
    }

    // R21-autofilter-sort-state-1: undo the ColumnFilterOwnedRows permutation in lockstep with
    // the sibling FilterHiddenRows/ValueFilterHiddenRows restores above.
    private void RestoreColumnFilterOwnedRows(Sheet sheet)
    {
        if (_columnFilterOwnedRowsSnapshot is null)
            return;

        foreach (var col in _columnFilterOwnedRowsSnapshot.Keys)
        {
            if (sheet.ColumnFilterOwnedRows.TryGetValue(col, out var ownedRows))
            {
                for (var row = _range.Start.Row; row <= _range.End.Row; row++)
                    ownedRows.Remove(row);
            }
        }

        foreach (var (col, ownedRows) in _columnFilterOwnedRowsSnapshot)
        {
            if (!sheet.ColumnFilterOwnedRows.TryGetValue(col, out var targetSet))
                sheet.ColumnFilterOwnedRows[col] = targetSet = [];

            foreach (var row in ownedRows)
                targetSet.Add(row);
        }
    }

    private static int CompareKey(Workbook workbook, Sheet sheet, CellAddress addressA, Cell? a, CellAddress addressB, Cell? b, SortOn sortOn, CellColor? targetColor, CfIconOverride? targetIcon, CustomSortOrder? customOrder, bool caseSensitive)
    {
        if (targetColor is not null && sortOn is SortOn.CellColor or SortOn.FontColor)
        {
            var aColor = GetEffectiveColor(workbook, sheet, addressA, a, wantFill: sortOn == SortOn.CellColor);
            var bColor = GetEffectiveColor(workbook, sheet, addressB, b, wantFill: sortOn == SortOn.CellColor);
            return CompareTargetColor(aColor, bColor, targetColor.Value);
        }

        // R78-commands-sort-multikey-5-2: mirrors the CellColor/FontColor target-match branch
        // above for Sort On: Cell Icon — a chosen target icon pulls matching cells to the front
        // (or back, via the caller's ascending/descending negation), same as a target color.
        if (targetIcon is not null && sortOn == SortOn.CellIcon)
        {
            var aIcon = GetEffectiveIcon(workbook, sheet, addressA, a);
            var bIcon = GetEffectiveIcon(workbook, sheet, addressB, b);
            return CompareTargetIcon(aIcon, bIcon, targetIcon);
        }

        return sortOn switch
        {
            // R65-commands-sort-6-2: with no target color chosen for this color-sort level,
            // Excel has no basis to order distinctly-colored cells against each other — the UI
            // always requires a specific target color per color-sort level. This is therefore a
            // no-op (the null-vs-non-null "no fill/font color goes last" rule is already enforced
            // by the caller, before CompareKey is ever reached for this key). Previously this fell
            // through to CompareNullableColor, which fabricated an R/G/B byte-value ordering Excel
            // never produces.
            // R78-commands-sort-multikey-5-2: same reasoning applies to Cell Icon with no target
            // icon chosen — two different icons have no inherent ordering without a pinned icon.
            SortOn.CellColor or SortOn.FontColor or SortOn.CellIcon => 0,
            _ => CompareScalar(a?.Value ?? BlankValue.Instance, b?.Value ?? BlankValue.Instance, customOrder, caseSensitive)
        };
    }

    /// <summary>
    /// R39-commands-sort-custom-2-2 / filter-by-color-cf: resolves the color Excel would actually
    /// show for a cell when sorting/filtering by Cell Color or Font Color — the cell's static
    /// stored style overlaid with the color contributed by the highest-precedence matching
    /// conditional-formatting rule, mirroring how Sort On/Filter by Cell Color/Font Color includes
    /// CF-driven colors in real Excel. Shared by <see cref="SortCommand"/> (Sort On: Cell/Font
    /// Color) and by FilterCommand's cell-color/font-color/no-fill filter commands and
    /// AutoFilterDropdownMenuPlanner's color-options list, so a CF-red cell is treated as red by
    /// both the offered color swatches and the actual match — never as "no fill".
    /// <para>
    /// This intentionally only evaluates CF rule shapes that can be judged purely from the cell's
    /// own value (literal CellValue comparisons, Blanks/NoBlanks/Errors/NoErrors, and the simple
    /// text-match rules) — rule types that need cross-cell aggregation or arbitrary formula
    /// evaluation (AboveAverage, Top10, Duplicate/UniqueValues, ColorScale, DataBar, IconSet,
    /// Formula) are left unresolved here and fall through to the cell's static style. A full
    /// formula/aggregate CF evaluator already exists for viewport rendering in
    /// FreeX.Core.Calc.ViewportConditionalFormatEvaluator, but that project is not referenced by
    /// FreeX.Core.Commands, so reusing it here is out of scope for this fix.
    /// </para>
    /// </summary>
    public static CellColor? GetEffectiveColor(Workbook workbook, Sheet sheet, CellAddress address, Cell? cell, bool wantFill)
    {
        // Resolve the cell's static style the same way callers used to before this helper was
        // shared: prefer the live Cell's own StyleId, then fall back to a style-only (no Cell
        // object, e.g. an empty formatted cell) entry recorded against this address, then Default.
        var styleId = cell?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
        var style = workbook.GetStyle(styleId);
        CellColor? effective = wantFill ? style.FillColor : style.FontColor;
        if (sheet.ConditionalFormats.Count == 0)
            return effective;

        foreach (var rule in sheet.ConditionalFormats.OrderBy(r => r.Priority))
        {
            var applies = false;
            foreach (var range in rule.AllRanges)
            {
                if (range.Contains(address))
                {
                    applies = true;
                    break;
                }
            }
            if (!applies)
                continue;

            if (!TryEvaluateSimpleConditionalFormatRule(rule, cell, out var matches) || !matches)
                continue;

            // FontColor is a non-nullable CellStyle member defaulting to Black, so a plain
            // (non-dxf) FontColor value can't by itself distinguish "this rule sets font color to
            // black" from "this rule doesn't override font color". ViewportConditionalFormatEvaluator
            // resolves that ambiguity via the tri-state CellStyle.DxfFontColor field, which the xlsx
            // dxf reader populates with the rule's actual explicit color (including an explicit
            // black) — mirror that same resolution here (ViewportConditionalFormatEvaluator.
            // EffectiveFontColor) so Sort/Filter On Font Color agrees with what the grid renders:
            // DxfFontColor wins when present (even black), otherwise fall back to the legacy
            // "non-black plain FontColor means explicitly set" heuristic for CF styles built
            // without going through the dxf reader (tests, UI/paste-built rules).
            CellColor? ruleColor = wantFill
                ? rule.FormatIfTrue?.FillColor
                : rule.FormatIfTrue is { } fmt
                    ? fmt.DxfFontColor ?? (fmt.FontColor != CellColor.Black ? fmt.FontColor : null)
                    : null;
            if (ruleColor is not null)
            {
                effective = ruleColor;
                break; // highest-precedence (lowest Priority number) matching rule wins for this aspect
            }
            if (rule.StopIfTrue)
                break;
        }

        return effective;
    }

    /// <summary>
    /// R78-commands-sort-multikey-5-2: resolves the icon-set icon (icon-set style name + bucket
    /// index, lowest to highest) Excel would display for a cell, mirroring
    /// <see cref="GetEffectiveColor"/> above but for Sort On: Cell Icon. Only iconSet rules whose
    /// thresholds are the common Number/Percent/Percentile shapes are resolved here — Formula and
    /// the data-bar-only Auto* threshold types are left unresolved (treated as "no icon"), the
    /// same narrowed scope <see cref="GetEffectiveColor"/> documents for aggregate-requiring CF
    /// rule types: a full evaluator already exists in FreeX.Core.Calc.
    /// ViewportConditionalFormatEvaluator, but that project is not referenced by
    /// FreeX.Core.Commands.
    /// </summary>
    public static CfIconOverride? GetEffectiveIcon(Workbook workbook, Sheet sheet, CellAddress address, Cell? cell)
    {
        if (sheet.ConditionalFormats.Count == 0)
            return null;

        var value = cell?.Value ?? BlankValue.Instance;
        if (!TryGetNumber(value, out var cellNumber))
            return null;

        foreach (var rule in sheet.ConditionalFormats.OrderBy(r => r.Priority))
        {
            if (rule.RuleType != CfRuleType.IconSet)
                continue;

            var applies = false;
            foreach (var range in rule.AllRanges)
            {
                if (range.Contains(address))
                {
                    applies = true;
                    break;
                }
            }
            if (!applies)
                continue;

            if (!TryResolveIconSetBucket(sheet, rule, cellNumber, out var bucket, out var iconCount))
            {
                if (rule.StopIfTrue)
                    break;
                continue;
            }

            var displayIndex = rule.IconSetReverse ? iconCount - 1 - bucket : bucket;
            if (rule.IconOverrides.Count == iconCount)
                return rule.IconOverrides[displayIndex];

            return new CfIconOverride(rule.IconSetStyle ?? "3TrafficLights1", displayIndex);
        }

        return null;
    }

    /// <summary>
    /// Determines which icon-set bucket (0 = lowest icon) a cell's numeric value falls into for
    /// an IconSet rule, evaluating its thresholds in ascending order. Percent/Percentile
    /// thresholds are normalized against the rule's own applied range. Returns false if the
    /// threshold shapes aren't fully resolvable (Formula/Min/Max/AutoMin/AutoMax, or malformed
    /// threshold data) — see <see cref="GetEffectiveIcon"/> for the rationale.
    /// </summary>
    private static bool TryResolveIconSetBucket(Sheet sheet, ConditionalFormat rule, double cellValue, out int bucket, out int iconCount)
    {
        bucket = 0;
        iconCount = GetIconSetCount(rule.IconSetStyle);
        var thresholdCount = iconCount - 1;
        var thresholdStartIndex = rule.IconSetThresholds.Count >= iconCount ? 1 : 0;
        if (rule.IconSetThresholds.Count - thresholdStartIndex < thresholdCount)
            return false;

        List<double>? rangeValues = null;
        for (var i = 0; i < thresholdCount; i++)
        {
            var threshold = rule.IconSetThresholds[thresholdStartIndex + i];
            double thresholdValue;
            switch (threshold.Type)
            {
                case CfThresholdType.Number:
                    if (!double.TryParse(threshold.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out thresholdValue))
                        return false;
                    break;
                case CfThresholdType.Percent:
                {
                    rangeValues ??= CollectIconSetRangeNumbers(sheet, rule);
                    if (rangeValues.Count == 0 || !double.TryParse(threshold.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                        return false;
                    var min = rangeValues.Min();
                    var max = rangeValues.Max();
                    thresholdValue = min + (max - min) * (pct / 100.0);
                    break;
                }
                case CfThresholdType.Percentile:
                {
                    rangeValues ??= CollectIconSetRangeNumbers(sheet, rule);
                    if (rangeValues.Count == 0 || !double.TryParse(threshold.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pctile))
                        return false;
                    thresholdValue = Percentile(rangeValues, pctile / 100.0);
                    break;
                }
                default:
                    // Formula/Min/Max/AutoMin/AutoMax: not resolvable from the cell's value alone.
                    return false;
            }

            var greaterThanOrEqual = threshold.GreaterThanOrEqual ?? true;
            var matches = greaterThanOrEqual ? cellValue >= thresholdValue : cellValue > thresholdValue;
            if (matches)
                bucket = i + 1;
            else
                break; // thresholds are ascending — once one fails, every higher one fails too
        }

        return true;
    }

    /// <summary>Icon count for an icon-set style name, mirroring ViewportConditionalFormatEvaluator.GetIconSetCount (duplicated here since FreeX.Core.Commands does not reference FreeX.Core.Calc).</summary>
    private static int GetIconSetCount(string? style) =>
        !string.IsNullOrWhiteSpace(style) && char.IsDigit(style[0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;

    private static List<double> CollectIconSetRangeNumbers(Sheet sheet, ConditionalFormat rule)
    {
        var values = new List<double>();
        foreach (var range in rule.AllRanges)
        {
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            {
                for (var col = range.Start.Col; col <= range.End.Col; col++)
                {
                    if (TryGetNumber(sheet.GetValue(row, col), out var num))
                        values.Add(num);
                }
            }
        }
        return values;
    }

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 1)
            return sorted[0];

        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi)
            return sorted[lo];

        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    /// <summary>
    /// Evaluates the subset of conditional-format rule types that can be judged from the cell's
    /// own value alone. Returns false (rule not evaluated, treated as non-matching for color
    /// resolution purposes) for rule types this minimal resolver does not support.
    /// </summary>
    private static bool TryEvaluateSimpleConditionalFormatRule(ConditionalFormat rule, Cell? cell, out bool matches)
    {
        matches = false;
        var value = cell?.Value ?? BlankValue.Instance;
        switch (rule.RuleType)
        {
            case CfRuleType.Blanks:
                matches = value is BlankValue || (value is TextValue tv && tv.Value.Length == 0);
                return true;
            case CfRuleType.NoBlanks:
                matches = !(value is BlankValue) && !(value is TextValue tvNoBlank && tvNoBlank.Value.Length == 0);
                return true;
            case CfRuleType.Errors:
                matches = value is ErrorValue;
                return true;
            case CfRuleType.NoErrors:
                matches = value is not ErrorValue;
                return true;
            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                return TryEvaluateTextRule(rule, value, out matches);
            case CfRuleType.CellValue:
                return TryEvaluateCellValueRule(rule, value, out matches);
            default:
                return false;
        }
    }

    private static bool TryEvaluateTextRule(ConditionalFormat rule, ScalarValue value, out bool matches)
    {
        matches = false;
        if (value is not TextValue textValue || rule.TextRuleText is not { Length: > 0 } needle)
            return false;

        var haystack = textValue.Value;
        matches = rule.RuleType switch
        {
            CfRuleType.ContainsText => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase),
            CfRuleType.NotContainsText => !haystack.Contains(needle, StringComparison.OrdinalIgnoreCase),
            CfRuleType.BeginsWith => haystack.StartsWith(needle, StringComparison.OrdinalIgnoreCase),
            CfRuleType.EndsWith => haystack.EndsWith(needle, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        return true;
    }

    private static bool TryEvaluateCellValueRule(ConditionalFormat rule, ScalarValue value, out bool matches)
    {
        matches = false;
        if (!TryParseLiteralThreshold(rule.Value1, out var v1Num, out var v1Text))
            return false;

        double? v2Num = null;
        if (rule.Operator is CfOperator.Between or CfOperator.NotBetween)
        {
            if (!TryParseLiteralThreshold(rule.Value2, out var v2n, out _) || v2n is null)
                return false;
            v2Num = v2n;
        }

        if (TryGetNumber(value, out var cellNumber) && v1Num.HasValue)
        {
            var lo = v2Num.HasValue ? Math.Min(v1Num.Value, v2Num.Value) : 0;
            var hi = v2Num.HasValue ? Math.Max(v1Num.Value, v2Num.Value) : 0;
            matches = rule.Operator switch
            {
                CfOperator.Equal => cellNumber == v1Num.Value,
                CfOperator.NotEqual => cellNumber != v1Num.Value,
                CfOperator.GreaterThan => cellNumber > v1Num.Value,
                CfOperator.GreaterThanOrEqual => cellNumber >= v1Num.Value,
                CfOperator.LessThan => cellNumber < v1Num.Value,
                CfOperator.LessThanOrEqual => cellNumber <= v1Num.Value,
                CfOperator.Between => cellNumber >= lo && cellNumber <= hi,
                CfOperator.NotBetween => cellNumber < lo || cellNumber > hi,
                _ => false
            };
            return true;
        }

        if (value is TextValue textValue && v1Text is not null)
        {
            var cmp = string.Compare(textValue.Value, v1Text, StringComparison.OrdinalIgnoreCase);
            matches = rule.Operator switch
            {
                CfOperator.Equal => cmp == 0,
                CfOperator.NotEqual => cmp != 0,
                _ => false // ordering operators against a literal text threshold aren't handled here
            };
            return true;
        }

        return false;
    }

    private static bool TryGetNumber(ScalarValue value, out double number)
    {
        switch (value)
        {
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            case BoolValue b:
                number = b.Value ? 1 : 0;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>
    /// Parses a cfRule Value1/Value2 attribute that is a plain numeric or quoted-string literal
    /// (e.g. "100" or "&quot;Done&quot;"). Anything else — a cell reference, a function call, or
    /// any other formula-shaped text — is a threshold this minimal, non-formula-evaluating
    /// resolver intentionally does not support, and returns false for.
    /// </summary>
    private static bool TryParseLiteralThreshold(string? text, out double? number, out string? quotedText)
    {
        number = null;
        quotedText = null;
        if (string.IsNullOrEmpty(text))
            return false;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            number = parsed;
            return true;
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            quotedText = text.Substring(1, text.Length - 2).Replace("\"\"", "\"");
            return true;
        }

        return false;
    }

    private static int CompareTargetColor(CellColor? a, CellColor? b, CellColor targetColor)
    {
        var aMatches = a == targetColor;
        var bMatches = b == targetColor;
        if (aMatches == bMatches)
            return 0;

        return aMatches ? -1 : 1;
    }

    private static int CompareTargetIcon(CfIconOverride? a, CfIconOverride? b, CfIconOverride targetIcon)
    {
        var aMatches = a == targetIcon;
        var bMatches = b == targetIcon;
        if (aMatches == bMatches)
            return 0;

        return aMatches ? -1 : 1;
    }

    /// <summary>
    /// Returns true if the cell is blank (null or BlankValue) or contains an error.
    /// Excel places both blank and error cells at the bottom regardless of sort direction.
    /// </summary>
    private static bool IsBlankOrError(Cell? cell) =>
        cell is null || cell.Value is BlankValue or ErrorValue;

    /// <summary>
    /// Sort comparison mirroring Excel's order: numbers/dates, text, booleans, blanks/errors last.
    /// </summary>
    private static int CompareScalar(ScalarValue a, ScalarValue b, CustomSortOrder? customOrder, bool caseSensitive)
    {
        // R65-commands-sort-6-1: a custom list ("First key sort order") overrides the default
        // numbers-before-text type hierarchy entirely — in Excel, EVERY custom-list member (in
        // list order) comes first, before anything not in the list, including numbers. So custom-
        // list membership must be checked BEFORE the numeric short-circuit below, not after it
        // (which used to let numbers slip ahead of custom-list text members).
        if (customOrder is not null)
        {
            bool aMember = a is TextValue textA0 && customOrder.IndexOf(textA0.Value) >= 0;
            bool bMember = b is TextValue textB0 && customOrder.IndexOf(textB0.Value) >= 0;
            if (aMember && bMember)
                return customOrder.Compare(((TextValue)a).Value, ((TextValue)b).Value, caseSensitive);
            if (aMember) return -1; // list member before any non-member (including numbers)
            if (bMember) return 1;
            // Neither is a list member — fall through to the normal type hierarchy below.
        }

        bool aNum = a is NumberValue or DateTimeValue;
        bool bNum = b is NumberValue or DateTimeValue;
        if (aNum && bNum)
        {
            double av = a is DateTimeValue da ? da.Value : ((NumberValue)a).Value;
            double bv = b is DateTimeValue db ? db.Value : ((NumberValue)b).Value;
            return av.CompareTo(bv);
        }
        if (aNum) return -1;  // numbers/dates before text/bool/blank
        if (bNum) return  1;
        return (a, b) switch
        {
            (TextValue ta,   TextValue tb  ) => caseSensitive
                ? CompareCaseSensitiveText(ta.Value, tb.Value)
                : string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase),
            (TextValue,      _             ) => -1,  // text before bool/blank
            (_,              TextValue     ) =>  1,
            (BoolValue ba,   BoolValue bb  ) => ba.Value.CompareTo(bb.Value),
            (BoolValue,      _             ) => -1,  // bools before blank/error
            (_,              BoolValue     ) =>  1,
            (BlankValue,     BlankValue    ) =>  0,
            (BlankValue,     _             ) =>  1,  // blanks last
            (_,              BlankValue    ) => -1,
            _                               =>  0,
        };
    }

    /// <summary>
    /// R39-commands-sort-custom-2-1: Excel's "Case Sensitive" sort option does NOT switch to raw
    /// ordinal/codepoint ordering (which would clump all uppercase-leading words ahead of all
    /// lowercase-leading ones, e.g. "Mango","Zebra","apple","banana"). It still sorts
    /// alphabetically first — case only breaks a tie between strings that are otherwise
    /// letter-for-letter identical, and in that tiebreak lowercase sorts before uppercase (e.g.
    /// "apple" before "Apple"). This compares case-insensitively first, then falls back to a
    /// per-character lowercase-before-uppercase tiebreak only when the case-insensitive compare
    /// found the strings equal.
    /// </summary>
    private static int CompareCaseSensitiveText(string a, string b)
    {
        var primary = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        if (primary != 0)
            return primary;

        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (ca == cb)
                continue;

            var aLower = char.IsLower(ca);
            var bLower = char.IsLower(cb);
            if (aLower != bLower)
                return aLower ? -1 : 1; // lowercase before uppercase, as a same-letter tiebreak only

            return ca.CompareTo(cb);
        }

        return a.Length.CompareTo(b.Length);
    }
}
