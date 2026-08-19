using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Removes duplicate rows in a range as one undoable command.</summary>
/// <remarks>
/// Only cells within the selected range columns are written or cleared — data in columns
/// outside the range is never touched, and no sheet-wide row deletion occurs.
/// </remarks>
public sealed class RemoveDuplicateRowsCommand : IWorkbookCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _snapshot below captures a (Address, Cell?, StyleId?) per
    // in-range cell -- the same 3-field shape MoveRangeCommand/InsertDeleteCellsCommand use 400
    // bytes/cell for -- PLUS several additional per-cell dictionaries (comments, hyperlinks, rich
    // text, phonetic guides, filter-hidden rows) below that are only populated for cells that
    // actually carry that metadata. 400 bytes/cell already covers a comparably (or more) richly-
    // shaped capture elsewhere (CopyRangeCommand's CellSnapshot record has 8 MORE fields than
    // this one and still uses 400), so reuse that same constant here rather than inventing a new
    // one. Removing duplicates from a large range should count proportionally, not the flat
    // 200-byte default.
    private const int BytesPerCell = 400;

    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly IReadOnlyList<uint>? _columnOffsets;

    // Snapshot of every in-range cell before Apply, used by Revert.
    private List<CellSnapshot>? _snapshot;

    public int EstimatedBytes => (int)Math.Min((long)(_snapshot?.Count ?? _range.CellCount) * BytesPerCell, int.MaxValue);
    private Dictionary<CellAddress, string>? _commentSnapshot;
    // J17: CommentAuthors/ShownComments are address-keyed companions of Comments (legacy note
    // author + pinned/"Show Comment" state) and must be captured/compacted/restored in lockstep
    // with it, or a surviving row's note author/pinned box is left behind at its old row.
    private Dictionary<CellAddress, string>? _commentAuthorsSnapshot;
    private HashSet<CellAddress>? _shownCommentsSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRunsSnapshot;
    private Dictionary<CellAddress, CellPhoneticGuide>? _phoneticGuideSnapshot;
    private HashSet<uint>? _filterHiddenRowsSnapshot;
    private HashSet<uint>? _valueFilterHiddenRowsSnapshot;
    // Snapshot of sheet.MergedRegions before Apply, used by Revert. A merge entirely contained
    // within the operated range must travel with its surviving row(s) as they compact upward
    // (or be dropped if every row it covered was removed as a duplicate), otherwise the merge
    // is left anchored over vacated rows while the data it belonged to moves elsewhere — see
    // R20-merged-cells-deep-2.
    private List<GridRange>? _mergeSnapshot;
    // Snapshot of the structured table (if any) whose Range this command shrank, so Revert can
    // restore its original extent — see R25-remove-duplicates-consolidate-3.
    private StructuredTableModel? _previousStructuredTable;

    public int RemovedRowCount { get; private set; }

    public string Label => "Remove Duplicates";

    public RemoveDuplicateRowsCommand(SheetId sheetId, GridRange range)
        : this(sheetId, range, null)
    {
    }

    public RemoveDuplicateRowsCommand(SheetId sheetId, GridRange range, IReadOnlyList<uint>? columnOffsets)
    {
        _sheetId = sheetId;
        _range = range;
        _columnOffsets = columnOffsets;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        // R47-sibling-guard-asymmetry-sweep-5: mirror ClearContentsCommand's CSE-array/dynamic-spill
        // split guard for this command's identical "clear then possibly rewrite" primitive (see step
        // 3 below) — reject if _range would only partially cover an array/spill (some members inside
        // the operated range, others outside it left behind), matching Excel's "You cannot change
        // part of an array".
        var removeDuplicatesShiftRegion = new CellShiftRegion(_range.Start.Row, _range.End.Row, _range.Start.Col, _range.End.Col);
        if (CommandGuards.RejectIfSplitsArray(sheet, InsertCellsCommand.ArrayMembersWithinShiftRegion(sheet, removeDuplicatesShiftRegion)) is { } splitsArrayRejection)
            return splitsArrayRejection;

        // R146-merged-structural-F1: a merge that only PARTIALLY overlaps the operated range (some
        // of the rows it covers fall inside the range, others outside) cannot be handled safely by
        // this command: step 4 below rewrites the full row content of every row inside the range
        // regardless of merge geometry, but step 5's remap only relocates merges FULLY CONTAINED in
        // the range (see `!_range.Contains(merge)` below) and otherwise leaves the merge's geometry
        // untouched. That combination let a surviving row's real value get written into a cell a
        // straddling merge still marks as a covered, blank non-anchor cell -- an invisible, silently
        // desynced value (merged-cell rendering only ever shows the anchor's content). Reject up
        // front, mirroring FillCellsCommand's identical "cannot partially cover a merge" refusal,
        // rather than letting Apply proceed and corrupt the merge/value invariant.
        if (sheet.MergedRegions.Any(m => _range.Overlaps(m) && !_range.Contains(m)))
            return new CommandOutcome(false, "Cannot remove duplicates in a range that partially overlaps a merged cell.");

        RemovedRowCount = 0;
        _previousStructuredTable = null;

        // ── 1. Identify surviving rows (de-duplicate) ──────────────────────
        // Excel's Remove Duplicates treats text case-insensitively (e.g. "MAY"/"may" are
        // duplicates), so row keys must be compared with OrdinalIgnoreCase, not Ordinal.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var survivingRows = new List<uint>();
        for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
        {
            var key = BuildKey(sheet, row);
            if (seen.Add(key))
                survivingRows.Add(row);
        }

        RemovedRowCount = (int)(_range.RowCount - survivingRows.Count);
        if (RemovedRowCount == 0)
        {
            // Nothing to do — take an empty snapshot so Revert is a no-op.
            _snapshot = [];
            _commentSnapshot = [];
            _commentAuthorsSnapshot = [];
            _shownCommentsSnapshot = [];
            _threadedCommentSnapshot = [];
            _hyperlinkSnapshot = [];
            _hyperlinkMetadataSnapshot = [];
            _richTextRunsSnapshot = [];
            _phoneticGuideSnapshot = [];
            _filterHiddenRowsSnapshot = [];
            _valueFilterHiddenRowsSnapshot = [];
            // Nothing was moved/cleared, so no merge snapshot is taken — Revert must leave every
            // merge on the sheet (in-range or not) exactly as it is. An empty list here would make
            // Revert's `sheet.ReplaceMergedRegions(_mergeSnapshot)` wipe every merge on the entire
            // sheet, not just the operated range (see R48-commands-undo-redo-inverse-3-1).
            _mergeSnapshot = null;
            return new CommandOutcome(true);
        }

        // ── 2. Snapshot the entire in-range area ───────────────────────────
        var allInRangeAddresses = _range.AllCells().ToList();
        _snapshot = CaptureCellSnapshots(sheet, allInRangeAddresses);
        _commentSnapshot = CaptureDictionary(sheet.Comments, allInRangeAddresses);
        _commentAuthorsSnapshot = CaptureDictionary(sheet.CommentAuthors, allInRangeAddresses);
        _shownCommentsSnapshot = CaptureAddressSet(sheet.ShownComments, allInRangeAddresses);
        _threadedCommentSnapshot = CaptureDictionary(sheet.ThreadedComments, allInRangeAddresses);
        _hyperlinkSnapshot = CaptureDictionary(sheet.Hyperlinks, allInRangeAddresses);
        _hyperlinkMetadataSnapshot = CaptureDictionary(sheet.HyperlinkMetadata, allInRangeAddresses);
        _richTextRunsSnapshot = CaptureDictionary(sheet.RichTextRuns, allInRangeAddresses);
        _phoneticGuideSnapshot = CaptureDictionary(sheet.CellPhoneticGuides, allInRangeAddresses);
        _filterHiddenRowsSnapshot = CaptureHiddenRowSet(sheet.FilterHiddenRows, _range.Start.Row, _range.End.Row);
        _valueFilterHiddenRowsSnapshot = CaptureHiddenRowSet(sheet.ValueFilterHiddenRows, _range.Start.Row, _range.End.Row);

        // ── 3. Clear the entire in-range area ─────────────────────────────
        foreach (var address in allInRangeAddresses)
            ClearAddress(sheet, address);

        // sheet.FilterHiddenRows/ValueFilterHiddenRows must be permuted in lockstep with the row
        // content below (mirroring SortCommand's per-row IsFilterHidden/IsValueFilterHidden carry):
        // each surviving row's hidden-by-filter flags move with it to its new compacted row index,
        // and vacated trailing rows are unhidden since they no longer hold any content.
        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
        {
            sheet.FilterHiddenRows.Remove(row);
            sheet.ValueFilterHiddenRows.Remove(row);
        }

        // Build a fast lookup from the snapshot list.
        var snapshotMap = _snapshot.ToDictionary(s => s.Address);

        // ── 4. Write surviving rows compacted upward into the range ────────
        uint targetRow = _range.Start.Row;
        foreach (var sourceRow in survivingRows)
        {
            if (_filterHiddenRowsSnapshot.Contains(sourceRow))
                sheet.FilterHiddenRows.Add(targetRow);
            if (_valueFilterHiddenRowsSnapshot.Contains(sourceRow))
                sheet.ValueFilterHiddenRows.Add(targetRow);

            for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
            {
                var source = new CellAddress(_sheetId, sourceRow, col);
                var target = new CellAddress(_sheetId, targetRow, col);

                // Cell value / formula / style
                if (snapshotMap.TryGetValue(source, out var snap))
                {
                    if (snap.Cell is not null)
                    {
                        sheet.SetCell(target, snap.Cell.Clone());
                    }
                    else if (snap.StyleOnly.HasValue)
                    {
                        sheet.SetStyleOnly(target.Row, target.Col, snap.StyleOnly.Value);
                    }
                }

                // Comments
                if (_commentSnapshot.TryGetValue(source, out var comment))
                    sheet.Comments[target] = comment;

                if (_commentAuthorsSnapshot.TryGetValue(source, out var commentAuthor))
                    sheet.CommentAuthors[target] = commentAuthor;

                if (_shownCommentsSnapshot.Contains(source))
                    sheet.ShownComments.Add(target);

                if (_threadedCommentSnapshot.TryGetValue(source, out var threadedComment))
                    sheet.ThreadedComments[target] = CloneThreadedComment(threadedComment);

                // Hyperlinks
                if (_hyperlinkSnapshot.TryGetValue(source, out var hyperlink))
                    sheet.Hyperlinks[target] = hyperlink;

                if (_hyperlinkMetadataSnapshot.TryGetValue(source, out var hyperlinkMetadata))
                    sheet.HyperlinkMetadata[target] = hyperlinkMetadata;

                // Rich text runs
                if (_richTextRunsSnapshot.TryGetValue(source, out var richRuns))
                    sheet.RichTextRuns[target] = richRuns;

                // Phonetic guide (furigana)
                if (_phoneticGuideSnapshot.TryGetValue(source, out var phoneticGuide))
                    sheet.CellPhoneticGuides[target] = phoneticGuide;
            }

            targetRow++;
        }

        // Vacated trailing rows are already cleared (step 3).

        // ── 5. Adjust merged regions so merges travel with their compacted rows ────────────
        // A merge entirely inside the operated range must be remapped onto the new (compacted)
        // row positions of whichever of its rows survived, or dropped entirely if every row it
        // covered was removed as a duplicate. Merges entirely outside the range are left untouched
        // (their data was never moved by this command); merges that only partially overlap the
        // range are rejected up front, above, before any mutation happens.
        _mergeSnapshot = sheet.MergedRegions.ToList();
        var survivorTargetRow = new Dictionary<uint, uint>();
        for (var i = 0; i < survivingRows.Count; i++)
            survivorTargetRow[survivingRows[i]] = _range.Start.Row + (uint)i;

        var adjustedMerges = new List<GridRange>(_mergeSnapshot.Count);
        foreach (var merge in _mergeSnapshot)
        {
            if (!_range.Contains(merge))
            {
                adjustedMerges.Add(merge);
                continue;
            }

            // Collect the new target rows for whichever of the merge's original rows survived,
            // preserving order (survivingRows/target rows are contiguous ascending, and the
            // merge's row span is itself contiguous, so any survivors within it map to a
            // contiguous run of target rows).
            uint? newStartRow = null;
            uint newEndRow = 0;
            for (var row = merge.Start.Row; row <= merge.End.Row; row++)
            {
                if (!survivorTargetRow.TryGetValue(row, out var target))
                    continue;

                newStartRow ??= target;
                newEndRow = target;
            }

            if (newStartRow is null)
                continue; // Every row this merge covered was removed as a duplicate — drop it.

            // A merge must span at least two cells; don't keep a degenerate 1x1 "merge".
            if (newStartRow.Value == newEndRow && merge.Start.Col == merge.End.Col)
                continue;

            adjustedMerges.Add(new GridRange(
                new CellAddress(merge.Start.Sheet, newStartRow.Value, merge.Start.Col),
                new CellAddress(merge.End.Sheet, newEndRow, merge.End.Col)));
        }

        sheet.ReplaceMergedRegions(adjustedMerges);

        // ── 6. Shrink a structured table's Range so it doesn't keep pointing at rows this
        // command just vacated (banding/AutoFilter/structured refs would otherwise stay stale
        // over now-blank trailing rows) — see R25-remove-duplicates-consolidate-3. Only applies
        // when the operated range is exactly a table's data body: same column span, reaching to
        // the table's own last row, with the table starting above the range (i.e. its header
        // row(s) sit above _range, matching how RemoveDuplicatesPlanner.ExcludeHeaderRow already
        // trims the header off before this command ever runs). A dedup over an unrelated or only
        // partially-overlapping range never touches any table's Range.
        for (var i = 0; i < sheet.StructuredTables.Count; i++)
        {
            var table = sheet.StructuredTables[i];
            if (table.Range.Start.Col != _range.Start.Col || table.Range.End.Col != _range.End.Col)
                continue;
            if (table.Range.End.Row != _range.End.Row || table.Range.Start.Row >= _range.Start.Row)
                continue;

            _previousStructuredTable = table;
            var newEndRow = _range.Start.Row + (uint)survivingRows.Count - 1;
            var shrunkRange = new GridRange(
                table.Range.Start,
                new CellAddress(table.Range.End.Sheet, newEndRow, table.Range.End.Col));
            sheet.StructuredTables[i] = StructuredTableDesignCommandHelpers.CopyTable(table, range: shrunkRange);
            break;
        }

        var affectedCells = allInRangeAddresses;
        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);

        // Clear everything that Apply may have written in-range.
        foreach (var snapshot in _snapshot)
            ClearAddress(sheet, snapshot.Address);

        // Restore cells and style-only entries.
        foreach (var snapshot in _snapshot)
            RestoreCellSnapshot(sheet, snapshot);

        // Restore metadata dictionaries.
        var allInRangeAddresses = _snapshot.Select(s => s.Address).ToList();
        RestoreDictionary(sheet.Comments, _commentSnapshot, allInRangeAddresses);
        RestoreDictionary(sheet.CommentAuthors, _commentAuthorsSnapshot, allInRangeAddresses);
        RestoreAddressSet(sheet.ShownComments, _shownCommentsSnapshot, allInRangeAddresses);
        RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot, allInRangeAddresses);
        RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot, allInRangeAddresses);
        RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot, allInRangeAddresses);
        RestoreDictionary(sheet.RichTextRuns, _richTextRunsSnapshot, allInRangeAddresses);
        RestoreDictionary(sheet.CellPhoneticGuides, _phoneticGuideSnapshot, allInRangeAddresses);

        // Restore FilterHiddenRows/ValueFilterHiddenRows to their pre-Apply state (undoing the
        // lockstep permutation performed in Apply).
        if (_filterHiddenRowsSnapshot is not null && _valueFilterHiddenRowsSnapshot is not null)
        {
            for (var row = _range.Start.Row; row <= _range.End.Row; row++)
            {
                sheet.FilterHiddenRows.Remove(row);
                sheet.ValueFilterHiddenRows.Remove(row);
            }

            foreach (var row in _filterHiddenRowsSnapshot)
                sheet.FilterHiddenRows.Add(row);
            foreach (var row in _valueFilterHiddenRowsSnapshot)
                sheet.ValueFilterHiddenRows.Add(row);
        }

        // Restore merged regions to their pre-Apply state (undoing the row remap/drop above).
        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        // Restore the structured table's Range to its pre-Apply extent (undoing the shrink above).
        if (_previousStructuredTable is not null &&
            CommandGuards.TryFindStructuredTableIndex(sheet, _previousStructuredTable.Id, out var tableIndex))
        {
            sheet.StructuredTables[tableIndex] = _previousStructuredTable;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a collision-proof key for a row by length-prefixing each cell value before
    /// joining, so that tab characters inside values cannot merge across column boundaries.
    /// E.g. columns ("a\tb", "c") and ("a", "b\tc") produce distinct keys.
    /// </summary>
    private string BuildKey(Sheet sheet, uint row)
    {
        var parts = new System.Text.StringBuilder();
        var first = true;
        foreach (var col in DuplicateKeyColumns())
        {
            if (!first) parts.Append('\t');
            first = false;
            var part = ScalarKeyPart(sheet.GetValue(row, col));
            parts.Append(part.Length);
            parts.Append(':');
            parts.Append(part);
        }

        return parts.ToString();
    }

    /// <summary>
    /// Returns the per-cell text used inside <see cref="BuildKey"/>'s length-prefixed row key.
    /// Excel's Remove Duplicates compares the underlying value: a date literal and a plain number
    /// holding the identical serial value are the same value (a date IS a number — only its display
    /// format differs), so <see cref="NumberValue"/> and <see cref="DateTimeValue"/> must produce
    /// the same key part for equal <c>Value</c>s, rather than the type-name-embedding default
    /// record ToString() ("NumberValue { Value = ... }" vs "DateTimeValue { Value = ... }"), which
    /// never compares equal even for numerically identical values. Every other scalar kind keeps
    /// using its own default record ToString(), unchanged. Mirrors
    /// ViewportConditionalFormatEvaluator.Aggregates.GetDuplicateValueKey's Number/DateTime bucket.
    /// </summary>
    private static string ScalarKeyPart(ScalarValue value) => value switch
    {
        NumberValue n => "N:" + n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => "N:" + d.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private IEnumerable<uint> DuplicateKeyColumns()
    {
        if (_columnOffsets is null || _columnOffsets.Count == 0)
        {
            for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
                yield return col;
            yield break;
        }

        foreach (var offset in _columnOffsets.Distinct().Order())
            if (offset < _range.ColCount)
                yield return _range.Start.Col + offset;
    }

    private static HashSet<uint> CaptureHiddenRowSet(HashSet<uint> source, uint startRow, uint endRow)
    {
        var snapshot = new HashSet<uint>();
        for (var row = startRow; row <= endRow; row++)
        {
            if (source.Contains(row))
                snapshot.Add(row);
        }

        return snapshot;
    }

    private static List<CellSnapshot> CaptureCellSnapshots(Sheet sheet, IReadOnlyList<CellAddress> addresses)
    {
        var snapshots = new List<CellSnapshot>(addresses.Count);
        foreach (var address in addresses)
        {
            snapshots.Add(new CellSnapshot(
                address,
                sheet.GetCell(address)?.Clone(),
                sheet.GetStyleOnly(address.Row, address.Col)));
        }

        return snapshots;
    }

    private static Dictionary<CellAddress, TValue> CaptureDictionary<TValue>(
        Dictionary<CellAddress, TValue> source,
        IReadOnlyList<CellAddress> addresses)
    {
        var snapshot = new Dictionary<CellAddress, TValue>();
        foreach (var address in addresses)
        {
            if (source.TryGetValue(address, out var value))
                snapshot[address] = value;
        }

        return snapshot;
    }

    private static HashSet<CellAddress> CaptureAddressSet(
        HashSet<CellAddress> source,
        IReadOnlyList<CellAddress> addresses)
    {
        var snapshot = new HashSet<CellAddress>();
        foreach (var address in addresses)
        {
            if (source.Contains(address))
                snapshot.Add(address);
        }

        return snapshot;
    }

    private static void ClearAddress(Sheet sheet, CellAddress address)
    {
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
    }

    private static void RestoreCellSnapshot(Sheet sheet, CellSnapshot snapshot)
    {
        if (snapshot.Cell is null)
        {
            if (snapshot.StyleOnly.HasValue)
                sheet.SetStyleOnly(snapshot.Address.Row, snapshot.Address.Col, snapshot.StyleOnly.Value);
        }
        else
        {
            sheet.SetCell(snapshot.Address, snapshot.Cell.Clone());
        }
    }

    private static void RestoreDictionary<TValue>(
        Dictionary<CellAddress, TValue> target,
        Dictionary<CellAddress, TValue>? snapshot,
        IReadOnlyList<CellAddress> affected)
    {
        foreach (var address in affected)
            target.Remove(address);

        if (snapshot is null)
            return;

        foreach (var (address, value) in snapshot)
            target[address] = value;
    }

    private static void RestoreAddressSet(
        HashSet<CellAddress> target,
        HashSet<CellAddress>? snapshot,
        IReadOnlyList<CellAddress> affected)
    {
        foreach (var address in affected)
            target.Remove(address);

        if (snapshot is null)
            return;

        foreach (var address in snapshot)
            target.Add(address);
    }

    private static ThreadedComment CloneThreadedComment(ThreadedComment comment) =>
        comment with { Replies = comment.Replies.Select(reply => reply with { }).ToList() };

    private sealed record CellSnapshot(CellAddress Address, Cell? Cell, StyleId? StyleOnly);
}
