using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Removes duplicate rows in a range as one undoable command.</summary>
/// <remarks>
/// Only cells within the selected range columns are written or cleared — data in columns
/// outside the range is never touched, and no sheet-wide row deletion occurs.
/// </remarks>
public sealed class RemoveDuplicateRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly IReadOnlyList<uint>? _columnOffsets;

    // Snapshot of every in-range cell before Apply, used by Revert.
    private List<CellSnapshot>? _snapshot;
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
    private HashSet<uint>? _filterHiddenRowsSnapshot;
    private HashSet<uint>? _valueFilterHiddenRowsSnapshot;

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

        RemovedRowCount = 0;

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
            _filterHiddenRowsSnapshot = [];
            _valueFilterHiddenRowsSnapshot = [];
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
            }

            targetRow++;
        }

        // Vacated trailing rows are already cleared (step 3).

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
            var part = sheet.GetValue(row, col).ToString() ?? string.Empty;
            parts.Append(part.Length);
            parts.Append(':');
            parts.Append(part);
        }

        return parts.ToString();
    }

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
