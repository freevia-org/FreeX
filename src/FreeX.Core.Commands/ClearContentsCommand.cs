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
    private Dictionary<CellAddress, CellPhoneticGuide>? _phoneticGuideSnapshot;
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

        // R25-spill-dynamic-deep-3 / R112-array-anchor-edit: clearing ONLY a live dynamic-array
        // spill's anchor cell is legitimate -- Excel lets a spilling formula be deleted from its
        // anchor alone, which removes the formula and its entire spill along with it. That
        // anchor-alone carve-out (and its narrower legacy-CSE exclusion) now lives centrally in
        // CommandGuards.RejectIfSplitsArray, so the full range can be passed through unfiltered here.
        if (CommandGuards.RejectIfSplitsArray(sheet, _range.AllCells()) is { } splitsArrayRejection)
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
        _phoneticGuideSnapshot = sheet.CellPhoneticGuides
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
            var hasPhoneticGuide = sheet.CellPhoneticGuides.ContainsKey(address);
            if (oldCell is null &&
                !oldStyleOnly.HasValue &&
                !hasHyperlink &&
                !hasHyperlinkMetadata &&
                !hasRichTextRuns &&
                !hasPhoneticGuide)
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
            // R40-commands-clear-delete-3-1: a plain "Clear Contents" (Delete key / ribbon Clear >
            // Clear Contents) only clears the cell's value/formula in real Excel -- the hyperlink
            // (and its formatting/style, already preserved above) stays attached to the now-blank
            // cell. Only "Clear All" / "Clear Hyperlinks" or a genuine Cut (which relocates the
            // hyperlink to the destination -- see _isCutSource above) actually removes it here.
            if (_isCutSource)
            {
                sheet.Hyperlinks.Remove(address);
                sheet.HyperlinkMetadata.Remove(address);
            }
            sheet.RichTextRuns.Remove(address);
            // R78-selfreg-twin-sweep-4: a phonetic guide (furigana) belongs to the content being
            // cleared -- like RichTextRuns above, it must not survive the clear or a later
            // run-formatting-only edit on brand-new content typed into this cell would re-emit the
            // OLD guide's <rPh> offsets against textually-unrelated text (see SourcePackageSnapshot
            // patch computation).
            sheet.CellPhoneticGuides.Remove(address);
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
            sheet.CellPhoneticGuides.Remove(address);
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
        if (_phoneticGuideSnapshot is not null)
        {
            foreach (var (address, guide) in _phoneticGuideSnapshot)
                sheet.CellPhoneticGuides[address] = guide;
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
