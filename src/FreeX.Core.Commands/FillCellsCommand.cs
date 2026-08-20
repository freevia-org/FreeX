using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum FillCellsDirection
{
    Down,
    Right,
    Up,
    Left
}

public sealed class FillCellsCommand : IWorkbookCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: the undo snapshot holds a Cell clone plus style, hyperlink/
    // metadata, rich-text runs and phonetic guide PER FILL TARGET (see Apply below) -- this is
    // exactly the "fill a whole column" scenario the R119 fix (PasteCellsCommand et al.) was meant
    // to cover but missed. Without this, CommandBus's 50 MB undo byte-budget bills every fill at the
    // 200-byte IEstimatesMemory default regardless of size, so a large Fill Down never trips the
    // budget and only the 100-entry depth cap bounds the undo stack.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly FillCellsDirection _direction;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;
    private List<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>? _hyperlinkSnapshot;
    private List<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>? _richTextRunsSnapshot;
    private List<(CellAddress Address, bool HadPhoneticGuide, CellPhoneticGuide? PhoneticGuide)>? _phoneticGuideSnapshot;
    // R142-comments-notes-1: mirrors AutofillCommand -- Ctrl+D/Ctrl+R/Fill Down/Right/Up/Left must
    // carry a source cell's legacy note (Comments/CommentAuthors/ShownComments) and threaded
    // comment to each fill target, and undo must restore exactly what was there before.
    private List<(CellAddress Address, bool HadComment, string? Comment, bool HadCommentAuthor, string? CommentAuthor, bool HadShown, bool HadThreadedComment, ThreadedComment? ThreadedComment)>? _commentSnapshot;

    public string Label => _direction switch
    {
        FillCellsDirection.Down => "Fill Down",
        FillCellsDirection.Right => "Fill Right",
        FillCellsDirection.Up => "Fill Up",
        FillCellsDirection.Left => "Fill Left",
        _ => "Fill"
    };

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_range.CellCount * BytesPerCell, int.MaxValue);

    public FillCellsCommand(SheetId sheetId, GridRange range, FillCellsDirection direction)
    {
        _sheetId = sheetId;
        _range = range;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var targets = GetTargetAddresses().ToList();
        if (targets.Count == 0)
            return new CommandOutcome(false, "The fill range must include at least one target cell.");
        if (targets.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return CommandGuards.RejectSheetProtected();
        if (CommandGuards.RejectIfSplitsArray(sheet, targets, allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
            return splitsArrayRejection;
        // Excel refuses to fill (Ctrl+D/Ctrl+R) across a merged region: the merge's non-anchor
        // cells must never receive independent content, and a fill that only partially covers a
        // merge would leave the merge's data model out of sync (mirrors AutofillCommand/MoveRangeCommand's merge guard).
        // The one shape Excel DOES allow through -- mirroring AutofillCommand's own
        // TryGetUniformMergeTileSize/ApplyMergeTiledFill carve-out -- is when the merges
        // overlapping the selection are ALL the same size and exactly tile the selection with
        // no gaps/partial overlaps (e.g. a "Q1" header merged across A1:B1 stacked over a
        // second, identically-sized A2:B2 merge): that retiles the merged anchor content
        // instead of refusing outright.
        var overlappingMerges = sheet.MergedRegions.Where(region => _range.Overlaps(region)).ToList();
        if (overlappingMerges.Count > 0)
        {
            var tileSpan = TryGetUniformMergeTileSpan(overlappingMerges);
            if (tileSpan is null)
                return new CommandOutcome(false, "Cannot fill a range that intersects merged cells.");

            return ApplyMergeTiledFill(ctx, sheet, overlappingMerges, tileSpan.Value);
        }

        _snapshot = [];
        _hyperlinkSnapshot = [];
        _richTextRunsSnapshot = [];
        _phoneticGuideSnapshot = [];
        _commentSnapshot = [];
        foreach (var target in targets)
        {
            _snapshot.Add((target, sheet.GetCell(target)?.Clone(), sheet.GetStyleOnly(target.Row, target.Col)));
            _hyperlinkSnapshot.Add((
                target,
                sheet.Hyperlinks.TryGetValue(target, out var oldTarget),
                oldTarget,
                sheet.HyperlinkMetadata.TryGetValue(target, out var oldMetadata),
                oldMetadata));
            _richTextRunsSnapshot.Add((
                target,
                sheet.RichTextRuns.TryGetValue(target, out var oldRuns),
                oldRuns));
            _phoneticGuideSnapshot.Add((
                target,
                sheet.CellPhoneticGuides.TryGetValue(target, out var oldPhoneticGuide),
                oldPhoneticGuide));
            SnapshotComments(sheet, target);

            var source = GetSourceAddress(target);
            var sourceCell = sheet.GetCell(source);
            if (sourceCell is null)
            {
                sheet.ClearCell(target);
                if (sheet.GetStyleOnly(source.Row, source.Col) is { } sourceStyleOnly)
                    sheet.SetStyleOnly(target.Row, target.Col, sourceStyleOnly);
                else
                    sheet.ClearStyleOnly(target.Row, target.Col);
                sheet.Hyperlinks.Remove(target);
                sheet.HyperlinkMetadata.Remove(target);
                sheet.RichTextRuns.Remove(target);
                sheet.CellPhoneticGuides.Remove(target);
                ClearComments(sheet, target);
                continue;
            }

            sheet.SetCell(target, CloneForTarget(sourceCell, source, target, sheet.Name));
            if (sheet.Hyperlinks.TryGetValue(source, out var sourceTarget))
                sheet.Hyperlinks[target] = sourceTarget;
            else
                sheet.Hyperlinks.Remove(target);

            if (sheet.HyperlinkMetadata.TryGetValue(source, out var sourceMetadata))
                sheet.HyperlinkMetadata[target] = sourceMetadata;
            else
                sheet.HyperlinkMetadata.Remove(target);

            if (sheet.RichTextRuns.TryGetValue(source, out var sourceRuns))
                sheet.RichTextRuns[target] = sourceRuns;
            else
                sheet.RichTextRuns.Remove(target);

            if (sheet.CellPhoneticGuides.TryGetValue(source, out var sourcePhoneticGuide))
                sheet.CellPhoneticGuides[target] = sourcePhoneticGuide;
            else
                sheet.CellPhoneticGuides.Remove(target);

            CopyComments(sheet, source, target);
        }

        return new CommandOutcome(true, AffectedCells: targets);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(address);
                RestoreStyleOnly(sheet, address, oldStyleOnly);
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }
        }

        if (_hyperlinkSnapshot is null)
            return;

        foreach (var (address, hadTarget, target, hadMetadata, metadata) in _hyperlinkSnapshot)
        {
            if (hadTarget && target is not null)
                sheet.Hyperlinks[address] = target;
            else
                sheet.Hyperlinks.Remove(address);

            if (hadMetadata && metadata is not null)
                sheet.HyperlinkMetadata[address] = metadata;
            else
                sheet.HyperlinkMetadata.Remove(address);
        }

        if (_richTextRunsSnapshot is null)
            return;

        foreach (var (address, hadRuns, runs) in _richTextRunsSnapshot)
        {
            if (hadRuns && runs is not null)
                sheet.RichTextRuns[address] = runs;
            else
                sheet.RichTextRuns.Remove(address);
        }

        if (_phoneticGuideSnapshot is not null)
        {
            foreach (var (address, hadPhoneticGuide, phoneticGuide) in _phoneticGuideSnapshot)
            {
                if (hadPhoneticGuide && phoneticGuide is not null)
                    sheet.CellPhoneticGuides[address] = phoneticGuide;
                else
                    sheet.CellPhoneticGuides.Remove(address);
            }
        }

        if (_commentSnapshot is null)
            return;

        foreach (var (address, hadComment, comment, hadCommentAuthor, commentAuthor, hadShown, hadThreadedComment, threadedComment) in _commentSnapshot)
        {
            if (hadComment && comment is not null)
                sheet.Comments[address] = comment;
            else
                sheet.Comments.Remove(address);

            if (hadCommentAuthor && commentAuthor is not null)
                sheet.CommentAuthors[address] = commentAuthor;
            else
                sheet.CommentAuthors.Remove(address);

            if (hadShown)
                sheet.ShownComments.Add(address);
            else
                sheet.ShownComments.Remove(address);

            if (hadThreadedComment && threadedComment is not null)
                sheet.ThreadedComments[address] = CloneThreadedComment(threadedComment);
            else
                sheet.ThreadedComments.Remove(address);
        }
    }

    /// <summary>Snapshots a fill target's legacy note (Comments/CommentAuthors/ShownComments) and threaded comment before overwriting it, for undo.</summary>
    private void SnapshotComments(Sheet sheet, CellAddress addr)
    {
        _commentSnapshot!.Add((
            addr,
            sheet.Comments.TryGetValue(addr, out var oldComment),
            oldComment,
            sheet.CommentAuthors.TryGetValue(addr, out var oldCommentAuthor),
            oldCommentAuthor,
            sheet.ShownComments.Contains(addr),
            sheet.ThreadedComments.TryGetValue(addr, out var oldThreadedComment),
            oldThreadedComment is null ? null : CloneThreadedComment(oldThreadedComment)));
    }

    /// <summary>
    /// Copies (or removes) a fill target's legacy note/threaded comment to match the source cell
    /// (R142-comments-notes-1) -- Excel carries a cell's note/comment along with Ctrl+D/Ctrl+R/
    /// Fill Down/Right/Up/Left exactly like it does the hyperlink/rich-text runs already handled
    /// here. A fresh, independent threaded-comment thread is minted for the target (Id cleared) so
    /// multiple fill targets sharing one source note don't collide on the same persisted thread id
    /// on save (mirrors CopyRangeCommand.ClonedThreadedCommentForNewAddress).
    /// </summary>
    private static void CopyComments(Sheet sheet, CellAddress source, CellAddress target)
    {
        if (sheet.Comments.TryGetValue(source, out var sourceComment))
            sheet.Comments[target] = sourceComment;
        else
            sheet.Comments.Remove(target);

        if (sheet.CommentAuthors.TryGetValue(source, out var sourceCommentAuthor))
            sheet.CommentAuthors[target] = sourceCommentAuthor;
        else
            sheet.CommentAuthors.Remove(target);

        if (sheet.ShownComments.Contains(source))
            sheet.ShownComments.Add(target);
        else
            sheet.ShownComments.Remove(target);

        if (sheet.ThreadedComments.TryGetValue(source, out var sourceThreadedComment))
            sheet.ThreadedComments[target] = ClonedThreadedCommentForNewAddress(sourceThreadedComment);
        else
            sheet.ThreadedComments.Remove(target);
    }

    /// <summary>Drops a fill target's legacy note/threaded comment (used when the fill's source cell is empty).</summary>
    private static void ClearComments(Sheet sheet, CellAddress addr)
    {
        sheet.Comments.Remove(addr);
        sheet.CommentAuthors.Remove(addr);
        sheet.ShownComments.Remove(addr);
        sheet.ThreadedComments.Remove(addr);
    }

    /// <summary>Deep-clones a threaded comment (including its reply list) for a snapshot, preserving its Id. Mirrors CopyRangeCommand.CloneThreadedComment.</summary>
    private static ThreadedComment CloneThreadedComment(ThreadedComment comment) =>
        comment with { Replies = comment.Replies.Select(reply => reply with { }).ToList() };

    /// <summary>
    /// Clones a threaded comment for a NEW destination address, clearing its Id (and each reply's
    /// Id) so the copy mints its own independent, address-derived thread id on save instead of
    /// colliding with the source's persisted <c>&lt;threadedComment id="..."&gt;</c>. Mirrors
    /// CopyRangeCommand.ClonedThreadedCommentForNewAddress.
    /// </summary>
    private static ThreadedComment ClonedThreadedCommentForNewAddress(ThreadedComment comment) =>
        comment with
        {
            Id = null,
            Replies = comment.Replies.Select(reply => reply with { Id = null }).ToList(),
        };

    private IEnumerable<CellAddress> GetTargetAddresses()
    {
        switch (_direction)
        {
            case FillCellsDirection.Down:
                for (uint row = _range.Start.Row + 1; row <= _range.End.Row; row++)
                for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
            case FillCellsDirection.Right:
                for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
                for (uint col = _range.Start.Col + 1; col <= _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
            case FillCellsDirection.Up:
                for (uint row = _range.Start.Row; row < _range.End.Row; row++)
                for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
            case FillCellsDirection.Left:
                for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
                for (uint col = _range.Start.Col; col < _range.End.Col; col++)
                    yield return new CellAddress(_sheetId, row, col);
                break;
        }
    }

    private CellAddress GetSourceAddress(CellAddress target) => _direction switch
    {
        FillCellsDirection.Down => new CellAddress(_sheetId, _range.Start.Row, target.Col),
        FillCellsDirection.Right => new CellAddress(_sheetId, target.Row, _range.Start.Col),
        FillCellsDirection.Up => new CellAddress(_sheetId, _range.End.Row, target.Col),
        FillCellsDirection.Left => new CellAddress(_sheetId, target.Row, _range.End.Col),
        _ => target
    };

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }

    /// <summary>
    /// The uniform-merge-tile shape that lets a fill go through instead of being refused: every
    /// merged region overlapping <see cref="_range"/> must be the same size, and together they
    /// must exactly tile the whole selection (no gaps, no partial-overlap merges, no merge that
    /// straddles a tile boundary). Mirrors AutofillCommand's TryGetUniformMergeTileSize, adapted
    /// to FillCellsCommand's model where the whole selection (not just a separate source range)
    /// is pre-populated with equal-size merges.
    /// </summary>
    private (uint RowSpan, uint ColSpan)? TryGetUniformMergeTileSpan(IReadOnlyList<GridRange> overlappingMerges)
    {
        var rowSpan = overlappingMerges[0].RowCount;
        var colSpan = overlappingMerges[0].ColCount;
        if (overlappingMerges.Any(merge => merge.RowCount != rowSpan || merge.ColCount != colSpan))
            return null;
        if (_range.RowCount % rowSpan != 0 || _range.ColCount % colSpan != 0)
            return null;

        var expectedTileCount = (_range.RowCount / rowSpan) * (_range.ColCount / colSpan);
        if (overlappingMerges.Count != expectedTileCount)
            return null;

        foreach (var merge in overlappingMerges)
        {
            if (!_range.Contains(merge))
                return null;
            if ((merge.Start.Row - _range.Start.Row) % rowSpan != 0 || (merge.Start.Col - _range.Start.Col) % colSpan != 0)
                return null;
        }

        return (rowSpan, colSpan);
    }

    /// <summary>
    /// Handles the merged-cell fill shape <see cref="TryGetUniformMergeTileSpan"/> allows
    /// through: for each tile NOT on the source edge (row 0 for Down, last row for Up, col 0 for
    /// Right, last col for Left), copies the same-tile-column/row source tile's anchor content
    /// into the target tile's anchor, exactly like the plain per-cell fill path but at merge-tile
    /// granularity. A tile's non-anchor cells are never touched, matching the invariant that only
    /// a merge's top-left anchor cell holds a value.
    /// </summary>
    private CommandOutcome ApplyMergeTiledFill(ICommandContext ctx, Sheet sheet, IReadOnlyList<GridRange> overlappingMerges, (uint RowSpan, uint ColSpan) tileSpan)
    {
        var tileRows = (int)(_range.RowCount / tileSpan.RowSpan);
        var tileCols = (int)(_range.ColCount / tileSpan.ColSpan);
        var tilesByPosition = overlappingMerges.ToDictionary(merge => (
            (int)((merge.Start.Row - _range.Start.Row) / tileSpan.RowSpan),
            (int)((merge.Start.Col - _range.Start.Col) / tileSpan.ColSpan)));

        var sourceForTargetAnchor = new Dictionary<CellAddress, CellAddress>();
        for (var tr = 0; tr < tileRows; tr++)
        {
            for (var tc = 0; tc < tileCols; tc++)
            {
                var isSourceTile = _direction switch
                {
                    FillCellsDirection.Down => tr == 0,
                    FillCellsDirection.Up => tr == tileRows - 1,
                    FillCellsDirection.Right => tc == 0,
                    FillCellsDirection.Left => tc == tileCols - 1,
                    _ => false
                };
                if (isSourceTile)
                    continue;

                var sourceKey = _direction switch
                {
                    FillCellsDirection.Down => (0, tc),
                    FillCellsDirection.Up => (tileRows - 1, tc),
                    FillCellsDirection.Right => (tr, 0),
                    FillCellsDirection.Left => (tr, tileCols - 1),
                    _ => (tr, tc)
                };

                sourceForTargetAnchor[tilesByPosition[(tr, tc)].Start] = tilesByPosition[sourceKey].Start;
            }
        }

        var targetAnchors = sourceForTargetAnchor.Keys.ToList();
        if (targetAnchors.Count == 0)
            return new CommandOutcome(false, "The fill range must include at least one target cell.");
        if (targetAnchors.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return CommandGuards.RejectSheetProtected();
        if (CommandGuards.RejectIfSplitsArray(sheet, targetAnchors, allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = [];
        _hyperlinkSnapshot = [];
        _richTextRunsSnapshot = [];
        _phoneticGuideSnapshot = [];
        _commentSnapshot = [];
        var writtenCells = new List<CellAddress>(targetAnchors.Count);

        foreach (var target in targetAnchors)
        {
            var source = sourceForTargetAnchor[target];
            _snapshot.Add((target, sheet.GetCell(target)?.Clone(), sheet.GetStyleOnly(target.Row, target.Col)));
            _hyperlinkSnapshot.Add((
                target,
                sheet.Hyperlinks.TryGetValue(target, out var oldTarget),
                oldTarget,
                sheet.HyperlinkMetadata.TryGetValue(target, out var oldMetadata),
                oldMetadata));
            _richTextRunsSnapshot.Add((
                target,
                sheet.RichTextRuns.TryGetValue(target, out var oldRuns),
                oldRuns));
            _phoneticGuideSnapshot.Add((
                target,
                sheet.CellPhoneticGuides.TryGetValue(target, out var oldPhoneticGuide),
                oldPhoneticGuide));
            SnapshotComments(sheet, target);

            var sourceCell = sheet.GetCell(source);
            if (sourceCell is null)
            {
                sheet.ClearCell(target);
                if (sheet.GetStyleOnly(source.Row, source.Col) is { } sourceStyleOnly)
                    sheet.SetStyleOnly(target.Row, target.Col, sourceStyleOnly);
                else
                    sheet.ClearStyleOnly(target.Row, target.Col);
                sheet.Hyperlinks.Remove(target);
                sheet.HyperlinkMetadata.Remove(target);
                sheet.RichTextRuns.Remove(target);
                sheet.CellPhoneticGuides.Remove(target);
                ClearComments(sheet, target);
                writtenCells.Add(target);
                continue;
            }

            sheet.SetCell(target, CloneForTarget(sourceCell, source, target, sheet.Name));
            if (sheet.Hyperlinks.TryGetValue(source, out var sourceTarget))
                sheet.Hyperlinks[target] = sourceTarget;
            else
                sheet.Hyperlinks.Remove(target);

            if (sheet.HyperlinkMetadata.TryGetValue(source, out var sourceMetadata))
                sheet.HyperlinkMetadata[target] = sourceMetadata;
            else
                sheet.HyperlinkMetadata.Remove(target);

            if (sheet.RichTextRuns.TryGetValue(source, out var sourceRuns))
                sheet.RichTextRuns[target] = sourceRuns;
            else
                sheet.RichTextRuns.Remove(target);

            if (sheet.CellPhoneticGuides.TryGetValue(source, out var sourcePhoneticGuide))
                sheet.CellPhoneticGuides[target] = sourcePhoneticGuide;
            else
                sheet.CellPhoneticGuides.Remove(target);

            CopyComments(sheet, source, target);

            writtenCells.Add(target);
        }

        return new CommandOutcome(true, AffectedCells: writtenCells);
    }

    private static Cell CloneForTarget(Cell sourceCell, CellAddress source, CellAddress target, string sheetName)
    {
        var result = sourceCell.Clone();
        if (sourceCell.HasFormula && sourceCell.FormulaText is { } formula)
        {
            var rowOffset = (int)target.Row - (int)source.Row;
            var colOffset = (int)target.Col - (int)source.Col;
            RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(
                result,
                FormulaRewriter.Rewrite(formula, new PasteOffsetOp(rowOffset, colOffset), sheetName) ?? formula);
        }

        return result;
    }
}
