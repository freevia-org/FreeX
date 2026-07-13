using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ClearContentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    // R38-commands-cut-move-2-3: true only when this ClearContentsCommand is the tail end of a
    // CUT (move) -- e.g. the cross-sheet Cut+Paste fallback in WorkbookSession clears the source
    // range after the destination has already been populated. A plain user-invoked "Clear
    // Contents" (Delete key / ribbon Clear > Clear Contents) on a merged cell leaves the merge in
    // place in real Excel (only the value clears), so merges must NOT be torn down for that case
    // -- only a genuine Cut needs the source's merge removed, matching Excel's "cut unmerges the
    // vacated source, paste re-merges at the destination" move semantics.
    private readonly bool _isCutSource;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRunsSnapshot;
    private List<GridRange>? _mergedRegionsSnapshot;

    public string Label => "Clear Contents";

    public ClearContentsCommand(SheetId sheetId, GridRange range, bool isCutSource = false)
    {
        _sheetId = sheetId;
        _range = range;
        _isCutSource = isCutSource;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var address in _range.AllCells())
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        // R25-spill-dynamic-deep-3: clearing ONLY a live spill's anchor cell (selected range is
        // exactly that one cell) is legitimate -- Excel lets a spilling formula be deleted from its
        // anchor alone, which removes the formula and its entire spill along with it.
        // RejectIfSplitsArray otherwise treats the anchor like any other array member and requires
        // every body cell to already be part of the selection, which would wrongly reject this case,
        // so exclude just the anchor's own address from the check. A selection that includes the
        // anchor alongside only SOME (not all) of the body, or a non-anchor member alone, still fails
        // this narrow condition and falls through to the normal (correctly rejecting) check.
        var guardCells = _range.AllCells();
        if (_range.CellCount == 1 &&
            sheet.TryGetSpillExtent(_range.Start, out var anchorSpillRows, out var anchorSpillCols) &&
            (anchorSpillRows > 1 || anchorSpillCols > 1))
        {
            guardCells = guardCells.Where(address => address != _range.Start);
        }

        if (CommandGuards.RejectIfSplitsArray(sheet, guardCells) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = [];
        _hyperlinkSnapshot = sheet.Hyperlinks
            .Where(pair => _range.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        _hyperlinkMetadataSnapshot = sheet.HyperlinkMetadata
            .Where(pair => _range.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        _richTextRunsSnapshot = sheet.RichTextRuns
            .Where(pair => _range.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (_isCutSource)
        {
            // A merge fully contained in the cut range moved with the cut selection (it was
            // already recreated at the destination by the paste half of the composite command),
            // so remove it here at the source -- otherwise Sheet1 keeps rendering a merged (now
            // blank) block that real Excel would have unmerged.
            var vacatedMerges = sheet.MergedRegions.Where(range => _range.Contains(range)).ToList();
            if (vacatedMerges.Count > 0)
            {
                _mergedRegionsSnapshot = sheet.MergedRegions.ToList();
                sheet.ReplaceMergedRegions(sheet.MergedRegions.Where(range => !vacatedMerges.Contains(range)));
            }
        }

        var affected = new List<CellAddress>();
        foreach (var address in _range.AllCells())
        {
            var oldCell = sheet.GetCell(address)?.Clone();
            var oldStyleOnly = sheet.GetStyleOnly(address.Row, address.Col);
            var hasHyperlink = sheet.Hyperlinks.ContainsKey(address);
            var hasHyperlinkMetadata = sheet.HyperlinkMetadata.ContainsKey(address);
            var hasRichTextRuns = sheet.RichTextRuns.ContainsKey(address);
            if (oldCell is null &&
                !oldStyleOnly.HasValue &&
                !hasHyperlink &&
                !hasHyperlinkMetadata &&
                !hasRichTextRuns)
            {
                continue;
            }

            _snapshot.Add((address, oldCell, oldStyleOnly));

            var cleared = Cell.FromValue(BlankValue.Instance);
            if (oldCell is not null)
                cleared.StyleId = oldCell.StyleId;
            else if (oldStyleOnly.HasValue)
                cleared.StyleId = oldStyleOnly.Value;
            sheet.SetCell(address, cleared);
            sheet.Hyperlinks.Remove(address);
            sheet.HyperlinkMetadata.Remove(address);
            sheet.RichTextRuns.Remove(address);
            affected.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (_mergedRegionsSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergedRegionsSnapshot);

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

        foreach (var (address, _, _) in _snapshot)
        {
            sheet.Hyperlinks.Remove(address);
            sheet.HyperlinkMetadata.Remove(address);
            sheet.RichTextRuns.Remove(address);
        }
        if (_hyperlinkSnapshot is not null)
        {
            foreach (var (address, target) in _hyperlinkSnapshot)
                sheet.Hyperlinks[address] = target;
        }
        if (_hyperlinkMetadataSnapshot is not null)
        {
            foreach (var (address, metadata) in _hyperlinkMetadataSnapshot)
                sheet.HyperlinkMetadata[address] = metadata;
        }
        if (_richTextRunsSnapshot is not null)
        {
            foreach (var (address, runs) in _richTextRunsSnapshot)
                sheet.RichTextRuns[address] = runs;
        }
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }
}
