using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum SortOn
{
    CellValues,
    CellColor,
    FontColor
}

public sealed record SortKey(uint ColumnOffset, bool Ascending, SortOn SortOn = SortOn.CellValues, CellColor? TargetColor = null, CustomSortOrder? CustomOrder = null);

public sealed record SortOptions(bool CaseSensitive = false, bool LeftToRight = false);

/// <summary>
/// Sorts the rows of a rectangular range by a specified column, ascending or descending.
/// Stores a snapshot of the original arrangement for undo via Revert.
/// </summary>
public sealed class SortCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly IReadOnlyList<SortKey> _sortKeys;
    private readonly SortOptions _options;

    // Snapshot for undo: list of rows, each row is a list of cell+style+hyperlink+richtext tuples
    private List<List<(CellAddress Address, Cell? Cell, StyleId? StyleOnly, string? Hyperlink, HyperlinkMetadata? HyperlinkMetadata, IReadOnlyList<CellTextRun>? RichTextRuns)>>? _snapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
    // author + pinned/"Show Comment" state) and must travel with a sorted row's comment, or a
    // note's author/pinned box is left behind at the row's old position.
    private Dictionary<CellAddress, string>? _commentAuthorsSnapshot;
    private HashSet<CellAddress>? _shownCommentsSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
    private HashSet<uint>? _hiddenRowsSnapshot;
    private HashSet<uint>? _filterHiddenRowsSnapshot;
    private HashSet<uint>? _valueFilterHiddenRowsSnapshot;
    // R21-autofilter-sort-state-1: per-column ownership of the rows a Top-N/Average/condition/
    // color filter is hiding (sheet.ColumnFilterOwnedRows) must be permuted in lockstep with
    // FilterHiddenRows/ValueFilterHiddenRows, or it keeps naming the pre-sort row positions.
    private Dictionary<uint, HashSet<uint>>? _columnFilterOwnedRowsSnapshot;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    // Undo snapshot for sheet.SortState (R19: Apply must record the sort it just performed so
    // the persisted <sortState> matches the data on disk, and Revert must restore whatever was
    // there before — which may be null, or a stale sortState left over from a prior Excel sort).
    private WorksheetSortStateModel? _priorSortState;
    private bool _sortStateCaptured;

    private sealed record SortPayloadCapture(
        SortCellPayload[][] Rows,
        List<List<(CellAddress Address, Cell? Cell, StyleId? StyleOnly, string? Hyperlink, HyperlinkMetadata? HyperlinkMetadata, IReadOnlyList<CellTextRun>? RichTextRuns)>> CellSnapshot,
        Dictionary<CellAddress, string> CommentSnapshot,
        Dictionary<CellAddress, string> CommentAuthorsSnapshot,
        HashSet<CellAddress> ShownCommentsSnapshot,
        Dictionary<CellAddress, ThreadedComment> ThreadedCommentSnapshot);

    private readonly struct SortCellPayload
    {
        public SortCellPayload(Cell? cell, StyleId? styleOnly, string? comment, string? commentAuthor, bool commentShown, ThreadedComment? threadedComment, string? hyperlink, HyperlinkMetadata? hyperlinkMetadata, IReadOnlyList<CellTextRun>? richTextRuns = null)
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
    }

    public string Label => _sortKeys.Count == 1
        ? $"Sort {(_sortKeys[0].Ascending ? "Ascending" : "Descending")}"
        : "Sort";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

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

        // Excel rejects sorts that contain merged cells, UNLESS every merge overlapping the range
        // is fully contained within it and all such merges are identically sized. In that uniform
        // case (e.g. every row of the range merged the same way — a common "each record spans N
        // cosmetic columns" layout), Excel treats each merge as one sortable row unit and moves it
        // as a whole; the sort below already swaps entire rows intact and never touches
        // MergedRegions, so the merge geometry stays put while the row content moves through it.
        // Merges that only partially overlap the range, or that differ in size/shape from one
        // another, still make the range unsortable — this mirrors Excel's own "This operation
        // requires the merged cells to be identically sized" refusal.
        var overlappingMerges = sheet.MergedRegions.Where(m => _range.Overlaps(m)).ToList();
        if (overlappingMerges.Count > 0)
        {
            var firstRowSpan = overlappingMerges[0].RowCount;
            var firstColSpan = overlappingMerges[0].ColCount;
            bool uniform = overlappingMerges.All(m =>
                _range.Contains(m) && m.RowCount == firstRowSpan && m.ColCount == firstColSpan);

            if (!uniform)
                return new CommandOutcome(false, "Cannot sort a range that contains merged cells.");
        }

        uint startRow = _range.Start.Row;
        uint endRow   = _range.End.Row;
        uint startCol = _range.Start.Col;
        uint endCol   = _range.End.Col;
        uint colCount32 = endCol - startCol + 1;
        var keyLimit = _options.LeftToRight ? endRow - startRow + 1 : colCount32;
        if (_sortKeys.Any(key => key.ColumnOffset >= keyLimit))
            return new CommandOutcome(false, "Sort key offset is outside the sort range.");
        var keyColIndexes = _sortKeys
            .Select(key => ((int)key.ColumnOffset, key.Ascending, key.SortOn, key.TargetColor, key.CustomOrder))
            .ToList();

        int rowCount = (int)(endRow - startRow + 1);
        int colCount = (int)(endCol - startCol + 1);

        // Redo replays Apply after Revert, so the snapshot must capture whatever SortState is
        // on the sheet right now (which is either the pristine pre-sort value, or — after a
        // Revert — back to that same pristine value) each time Apply runs.
        _priorSortState = sheet.SortState;
        _sortStateCaptured = true;

        if (_options.LeftToRight)
            return ApplyLeftToRight(ctx.Workbook, sheet, startRow, endRow, startCol, endCol, keyColIndexes, rowCount, colCount);

        // Read current state and save snapshot. Redo replays Apply after Revert,
        // so the snapshot must describe the current pre-sort state each time.
        _rowHeightSnapshot = CaptureRowHeights(sheet, startRow, rowCount);
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

        rows.Sort((a, b) =>
        {
            foreach (var (index, ascending, sortOn, targetColor, customOrder) in keyColIndexes)
            {
                // Excel always places blank (and error) cells last regardless of sort direction.
                // Guard this BEFORE the ascending/descending negation so the blank-last ordering
                // is never inverted.
                if (sortOn == SortOn.CellValues)
                {
                    bool aBlank = IsBlankOrError(a.Payloads[index].Cell);
                    bool bBlank = IsBlankOrError(b.Payloads[index].Cell);
                    if (aBlank != bBlank)
                        return aBlank ? 1 : -1; // blank always goes last
                    if (aBlank) // both blank — equal on this key, try next
                        continue;
                }

                var cmp = CompareKey(ctx.Workbook, a.Payloads[index].Cell, b.Payloads[index].Cell, sortOn, targetColor, customOrder, _options.CaseSensitive);
                if (cmp != 0)
                    return ascending ? cmp : -cmp;
            }

            return a.OriginalIndex.CompareTo(b.OriginalIndex); // stable tiebreaker
        });

        // Write sorted rows back
        var affected = new List<CellAddress>(rowCount * colCount);
        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            sheet.RowHeights.Remove(row);
            if (rows[ri].HasRowHeight)
                sheet.RowHeights[row] = rows[ri].RowHeight;
            sheet.HiddenRows.Remove(row);
            if (rows[ri].IsHidden)
                sheet.HiddenRows.Add(row);
            sheet.FilterHiddenRows.Remove(row);
            if (rows[ri].IsFilterHidden)
                sheet.FilterHiddenRows.Add(row);
            // sheet.ValueFilterHiddenRows must be permuted in lockstep with FilterHiddenRows — it
            // records exactly which of those rows the value-filter mechanism (sheet.ActiveValueFilterColumns)
            // currently owns, and FilterCommand.RecomputeHiddenRows uses it to decide which rows it may
            // safely un-hide. Left unpermuted, it would name the wrong rows the moment Sort reorders them.
            sheet.ValueFilterHiddenRows.Remove(row);
            if (rows[ri].IsValueFilterHidden)
                sheet.ValueFilterHiddenRows.Add(row);

            // R21-autofilter-sort-state-1: sheet.ColumnFilterOwnedRows must be permuted in
            // lockstep too — it records, per column, exactly which rows a Top-N/Average/condition/
            // color filter is hiding, and FilterHiddenRowUpdater.ClearColumnOwnedRange /
            // IsHiddenByAnyColumnOwnedFilter rely on it to know which rows that mechanism owns.
            // Left unpermuted, it would keep naming the pre-sort row positions after the rows move.
            foreach (var col in _columnFilterOwnedRowsSnapshot.Keys)
                sheet.ColumnFilterOwnedRows[col].Remove(row);
            if (rows[ri].OwnedFilterColumns is { } ownedFilterColumns)
            {
                foreach (var col in ownedFilterColumns)
                    sheet.ColumnFilterOwnedRows[col].Add(row);
            }

            // N37: rows are permuted from OriginalIndex to ri — Excel rewrites each moved
            // formula's relative references the same way a cut/paste to the new row would,
            // so the row delta a cell actually moved must be applied to its formula text.
            int rowDelta = ri - rows[ri].OriginalIndex;

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col  = startCol + (uint)ci;
                var addr  = new CellAddress(_sheetId, row, col);
                WriteCellPayload(sheet, addr, rows[ri].Payloads[ci], rowDelta, 0, sheet.Name);
                affected.Add(addr);
            }
        }

        _affectedCells = affected;
        sheet.SortState = BuildSortState(_range, keyColIndexes, leftToRight: false);
        return new CommandOutcome(true, AffectedCells: affected);
    }

    private CommandOutcome ApplyLeftToRight(
        Workbook workbook,
        Sheet sheet,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol,
        IReadOnlyList<(int RowIndex, bool Ascending, SortOn SortOn, CellColor? TargetColor, CustomSortOrder? CustomOrder)> keyRowIndexes,
        int rowCount,
        int colCount)
    {
        _rowHeightSnapshot = null;
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
            foreach (var (index, ascending, sortOn, targetColor, customOrder) in keyRowIndexes)
            {
                // Excel always places blank (and error) cells last regardless of sort direction.
                // Guard this BEFORE the ascending/descending negation so the blank-last ordering
                // is never inverted.
                if (sortOn == SortOn.CellValues)
                {
                    bool aBlank = IsBlankOrError(a.Payloads[index].Cell);
                    bool bBlank = IsBlankOrError(b.Payloads[index].Cell);
                    if (aBlank != bBlank)
                        return aBlank ? 1 : -1; // blank always goes last
                    if (aBlank) // both blank — equal on this key, try next
                        continue;
                }

                var cmp = CompareKey(workbook, a.Payloads[index].Cell, b.Payloads[index].Cell, sortOn, targetColor, customOrder, _options.CaseSensitive);
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
        sheet.SortState = BuildSortState(_range, keyRowIndexes, leftToRight: true);
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
        IReadOnlyList<(int Index, bool Ascending, SortOn SortOn, CellColor? TargetColor, CustomSortOrder? CustomOrder)> keys,
        bool leftToRight)
    {
        var model = new WorksheetSortStateModel
        {
            Reference = range.ToString(),
            ColumnSort = leftToRight ? true : null,
            CaseSensitive = _options.CaseSensitive ? true : null
        };

        foreach (var (index, ascending, sortOn, _, _) in keys)
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

            model.Conditions.Add(new WorksheetSortConditionModel
            {
                Reference = conditionRange.ToString(),
                Descending = ascending ? null : true,
                SortBy = sortOn switch
                {
                    SortOn.CellColor => "cellColor",
                    SortOn.FontColor => "fontColor",
                    _ => null
                }
            });
        }

        return model;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RestoreCellSnapshot(sheet, _snapshot);
        RestoreCommentSnapshots(sheet);
        RestoreRowHeights(sheet);
        RestoreHiddenRows(sheet);
        RestoreFilterHiddenRows(sheet);
        RestoreValueFilterHiddenRows(sheet);
        RestoreColumnFilterOwnedRows(sheet);
        if (_sortStateCaptured)
            sheet.SortState = _priorSortState;
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
        var cellSnapshot = new List<List<(CellAddress, Cell?, StyleId?, string?, HyperlinkMetadata?, IReadOnlyList<CellTextRun>?)>>(rowCount);
        var commentSnapshot = new Dictionary<CellAddress, string>();
        var commentAuthorsSnapshot = new Dictionary<CellAddress, string>();
        var shownCommentsSnapshot = new HashSet<CellAddress>();
        var threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>();

        for (int ri = 0; ri < rowCount; ri++)
        {
            uint row = startRow + (uint)ri;
            var payloadRow = new SortCellPayload[colCount];
            var snapRow = new List<(CellAddress, Cell?, StyleId?, string?, HyperlinkMetadata?, IReadOnlyList<CellTextRun>?)>(colCount);

            for (int ci = 0; ci < colCount; ci++)
            {
                uint col = startCol + (uint)ci;
                var addr = new CellAddress(sheetId, row, col);
                var payload = CaptureCellPayload(sheet, addr, out var snapshotCell, out var snapshotStyleOnly, out var snapshotHyperlink, out var snapshotHyperlinkMetadata, out var snapshotRichTextRuns);
                payloadRow[ci] = payload;
                snapRow.Add((addr, snapshotCell, snapshotStyleOnly, snapshotHyperlink, snapshotHyperlinkMetadata, snapshotRichTextRuns));
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
        out IReadOnlyList<CellTextRun>? snapshotRichTextRuns)
    {
        var cell = sheet.GetCell(address);
        sheet.Comments.TryGetValue(address, out var comment);
        sheet.CommentAuthors.TryGetValue(address, out var commentAuthor);
        var commentShown = sheet.ShownComments.Contains(address);
        sheet.ThreadedComments.TryGetValue(address, out var threadedComment);
        sheet.Hyperlinks.TryGetValue(address, out var hyperlink);
        sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
        sheet.RichTextRuns.TryGetValue(address, out var richTextRuns);
        var styleOnly = cell is null ? sheet.GetStyleOnly(address.Row, address.Col) : null;

        // The sortable payload and undo snapshot must not share mutable cell instances.
        snapshotCell = cell?.Clone();
        snapshotStyleOnly = styleOnly;
        snapshotHyperlink = hyperlink;
        snapshotHyperlinkMetadata = hyperlinkMetadata;
        snapshotRichTextRuns = richTextRuns;
        return new SortCellPayload(cell?.Clone(), styleOnly, comment, commentAuthor, commentShown, threadedComment, hyperlink, hyperlinkMetadata, richTextRuns);
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

    private static void RestoreCellSnapshot(Sheet sheet, List<List<(CellAddress Address, Cell? Cell, StyleId? StyleOnly, string? Hyperlink, HyperlinkMetadata? HyperlinkMetadata, IReadOnlyList<CellTextRun>? RichTextRuns)>> snapshot)
    {
        foreach (var snapRow in snapshot)
        {
            foreach (var (addr, cell, styleOnly, hyperlink, hyperlinkMetadata, richTextRuns) in snapRow)
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

    private static int CompareKey(Workbook workbook, Cell? a, Cell? b, SortOn sortOn, CellColor? targetColor, CustomSortOrder? customOrder, bool caseSensitive)
    {
        if (targetColor is not null && sortOn is SortOn.CellColor or SortOn.FontColor)
        {
            var aColor = sortOn == SortOn.CellColor ? GetStyle(workbook, a).FillColor : GetStyle(workbook, a).FontColor;
            var bColor = sortOn == SortOn.CellColor ? GetStyle(workbook, b).FillColor : GetStyle(workbook, b).FontColor;
            return CompareTargetColor(aColor, bColor, targetColor.Value);
        }

        return sortOn switch
        {
            SortOn.CellColor => CompareNullableColor(GetStyle(workbook, a).FillColor, GetStyle(workbook, b).FillColor),
            SortOn.FontColor => CompareNullableColor(GetStyle(workbook, a).FontColor, GetStyle(workbook, b).FontColor),
            _ => CompareScalar(a?.Value ?? BlankValue.Instance, b?.Value ?? BlankValue.Instance, customOrder, caseSensitive)
        };
    }

    private static CellStyle GetStyle(Workbook workbook, Cell? cell) =>
        workbook.GetStyle(cell?.StyleId ?? StyleId.Default);

    private static int CompareNullableColor(CellColor? a, CellColor? b)
    {
        if (a is null && b is null)
            return 0;
        if (a is null)
            return 1;
        if (b is null)
            return -1;

        var red = a.Value.R.CompareTo(b.Value.R);
        if (red != 0)
            return red;
        var green = a.Value.G.CompareTo(b.Value.G);
        return green != 0 ? green : a.Value.B.CompareTo(b.Value.B);
    }

    private static int CompareTargetColor(CellColor? a, CellColor? b, CellColor targetColor)
    {
        var aMatches = a == targetColor;
        var bMatches = b == targetColor;
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
        // Custom list ("First key sort order") ranks text by its position in the list.
        if (customOrder is not null && a is TextValue textA && b is TextValue textB)
            return customOrder.Compare(textA.Value, textB.Value, caseSensitive);
        return (a, b) switch
        {
            (TextValue ta,   TextValue tb  ) => string.Compare(ta.Value, tb.Value, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
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
}
