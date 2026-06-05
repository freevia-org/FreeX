using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class MoveRangeCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private IReadOnlyList<CellAddress> _affectedCells = [];
    private IReadOnlyList<CellAddress> _payloadAffectedCells = [];
    private List<CellSnapshot>? _snapshot;
    private Dictionary<CellAddress, string>? _formulaSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;

    public string Label => "Move Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public MoveRangeCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sheetId ||
            _sourceRange.End.Sheet != _sheetId ||
            _destination.Sheet != _sheetId)
        {
            return new CommandOutcome(false, "Move source and destination must be on the target sheet.");
        }

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
        if (targetRange == _sourceRange)
        {
            _affectedCells = [];
            _payloadAffectedCells = [];
            _snapshot = [];
            _formulaSnapshot = [];
            return new CommandOutcome(true, AffectedCells: _affectedCells);
        }

        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.MergedRegions.Any(range => _sourceRange.Overlaps(range) || targetRange.Overlaps(range)))
            return new CommandOutcome(false, "Cannot move a range that intersects merged cells.");

        var affected = CreateAffectedCellList(_sourceRange, targetRange);
        if (sheet.IsProtected)
        {
            foreach (var address in affected)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return new CommandOutcome(false, "The sheet is protected.");
            }

            if (HasComments(sheet, affected) &&
                !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditObjects))
            {
                return new CommandOutcome(false, "The sheet is protected.");
            }
        }

        _snapshot = CaptureCellSnapshots(sheet, affected);
        _commentSnapshot = CaptureDictionary(sheet.Comments, affected);
        _threadedCommentSnapshot = CaptureDictionary(sheet.ThreadedComments, affected);
        _hyperlinkSnapshot = CaptureDictionary(sheet.Hyperlinks, affected);
        _hyperlinkMetadataSnapshot = CaptureDictionary(sheet.HyperlinkMetadata, affected);
        _payloadAffectedCells = affected;

        _formulaSnapshot = [];
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook,
            CreateMoveRangeOp(sheet, _sourceRange, _destination),
            _formulaSnapshot);

        var payloads = CaptureSourcePayloads(sheet, _sourceRange, _destination);

        foreach (var address in affected)
            ClearAddress(sheet, address);

        foreach (var payload in payloads)
            WritePayload(sheet, payload);

        _affectedCells = MergeAffectedCells(affected, _formulaSnapshot.Keys);
        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (_formulaSnapshot is not null)
            RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var snapshot in _snapshot)
            RestoreCellSnapshot(sheet, snapshot);

        RestoreDictionary(sheet.Comments, _commentSnapshot, _payloadAffectedCells);
        RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot, _payloadAffectedCells);
        RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot, _payloadAffectedCells);
        RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot, _payloadAffectedCells);
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

        foreach (var source in sourceRange.AllCells())
        {
            var target = new CellAddress(
                destination.Sheet,
                checked((uint)(source.Row + rowDelta)),
                checked((uint)(source.Col + colDelta)));
            var cell = sheet.GetCell(source)?.Clone();
            if (cell?.FormulaText is { } formulaText)
                cell.FormulaText = formulaText;

            payloads.Add(new MovePayload(
                target,
                cell,
                sheet.GetStyleOnly(source.Row, source.Col),
                sheet.Comments.TryGetValue(source, out var comment) ? comment : null,
                sheet.ThreadedComments.TryGetValue(source, out var threadedComment)
                    ? CloneThreadedComment(threadedComment)
                    : null,
                sheet.Hyperlinks.TryGetValue(source, out var hyperlink) ? hyperlink : null,
                sheet.HyperlinkMetadata.TryGetValue(source, out var metadata) ? metadata : null));
        }

        return payloads;
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

    private static void ClearAddress(Sheet sheet, CellAddress address)
    {
        sheet.ClearCell(address);
        sheet.ClearStyleOnly(address.Row, address.Col);
        sheet.Comments.Remove(address);
        sheet.ThreadedComments.Remove(address);
        sheet.Hyperlinks.Remove(address);
        sheet.HyperlinkMetadata.Remove(address);
    }

    private static void WritePayload(Sheet sheet, MovePayload payload)
    {
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
        if (payload.ThreadedComment is not null)
            sheet.ThreadedComments[payload.Target] = CloneThreadedComment(payload.ThreadedComment);
        if (payload.Hyperlink is not null)
            sheet.Hyperlinks[payload.Target] = payload.Hyperlink;
        if (payload.HyperlinkMetadata is not null)
            sheet.HyperlinkMetadata[payload.Target] = payload.HyperlinkMetadata;
    }

    private static void RestoreCellSnapshot(Sheet sheet, CellSnapshot snapshot)
    {
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

    private static int GetSafeListCapacity(long cellCount) =>
        cellCount is > 0 and <= 1_000_000 ? (int)cellCount : 0;

    private sealed record CellSnapshot(CellAddress Address, Cell? Cell, StyleId? StyleOnly);

    private sealed record MovePayload(
        CellAddress Target,
        Cell? Cell,
        StyleId? StyleOnly,
        string? Comment,
        ThreadedComment? ThreadedComment,
        string? Hyperlink,
        HyperlinkMetadata? HyperlinkMetadata);
}
