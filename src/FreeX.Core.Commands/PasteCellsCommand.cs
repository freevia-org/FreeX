using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Pastes complete cell payloads, including values/formulas and formatting.
/// </summary>
public sealed class PasteCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _cells;
    private readonly IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRuns;
    private readonly IReadOnlyDictionary<CellAddress, string>? _hyperlinks;
    private readonly IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadata;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata)>? _snapshot;

    public string Label => _cells.Count == 1 ? "Paste Cell" : $"Paste {_cells.Count} Cells";

    public PasteCellsCommand(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, Cell Cell)> cells,
        IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? richTextRuns = null,
        IReadOnlyDictionary<CellAddress, string>? hyperlinks = null,
        IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? hyperlinkMetadata = null)
    {
        _sheetId = sheetId;
        _cells = cells;
        _richTextRuns = richTextRuns;
        _hyperlinks = hyperlinks;
        _hyperlinkMetadata = hyperlinkMetadata;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var (addr, _) in _cells)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, addr))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>(_cells.Count);

        foreach (var (addr, cell) in _cells)
        {
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(addr, out var oldRuns);
            var hadHyperlink = sheet.Hyperlinks.TryGetValue(addr, out var oldHyperlink);
            var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(addr, out var oldHyperlinkMetadata);
            _snapshot.Add((
                addr,
                sheet.GetCell(addr)?.Clone(),
                sheet.GetStyleOnly(addr.Row, addr.Col),
                hadRichTextRuns,
                oldRuns,
                hadHyperlink,
                oldHyperlink,
                hadHyperlinkMetadata,
                oldHyperlinkMetadata));
            sheet.SetCell(addr, cell.Clone());

            if (_richTextRuns is not null && _richTextRuns.TryGetValue(addr, out var newRuns))
                sheet.RichTextRuns[addr] = newRuns;
            else
                sheet.RichTextRuns.Remove(addr);

            if (_hyperlinks is not null && _hyperlinks.TryGetValue(addr, out var newHyperlink))
                sheet.Hyperlinks[addr] = newHyperlink;
            else
                sheet.Hyperlinks.Remove(addr);

            if (_hyperlinkMetadata is not null && _hyperlinkMetadata.TryGetValue(addr, out var newHyperlinkMetadata))
                sheet.HyperlinkMetadata[addr] = newHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(addr);

            affected.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(addr);
                RestoreStyleOnly(sheet, addr, oldStyleOnly);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }

            if (hadRichTextRuns && oldRichTextRuns is not null)
                sheet.RichTextRuns[addr] = oldRichTextRuns;
            else
                sheet.RichTextRuns.Remove(addr);

            if (hadHyperlink && oldHyperlink is not null)
                sheet.Hyperlinks[addr] = oldHyperlink;
            else
                sheet.Hyperlinks.Remove(addr);

            if (hadHyperlinkMetadata && oldHyperlinkMetadata is not null)
                sheet.HyperlinkMetadata[addr] = oldHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(addr);
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

/// <summary>
/// Recreates merged cell regions at the paste destination for any source merged region that
/// overlapped the copied range, matching Excel's behavior of preserving a copied cell's merge
/// when pasted elsewhere. Mirrors PasteConditionalFormatsCommand's clip-then-remap approach.
/// </summary>
public sealed class PasteMergedRegionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly bool _transpose;
    private List<GridRange>? _addedRegions;

    public string Label => "Paste Merged Regions";

    public PasteMergedRegionsCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste merged regions source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(targetSheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        _addedRegions = [];
        // Snapshot the overlapping source regions first: on a same-sheet paste, sourceSheet and
        // targetSheet are the SAME sheet, so AddMergedRegion below would otherwise mutate the very
        // collection being enumerated ("Collection was modified" mid-paste).
        var overlappingRegions = sourceSheet.MergedRegions
            .Where(region => region.Overlaps(_sourceRange))
            .ToList();
        foreach (var region in overlappingRegions)
        {
            var clipped = GridRange.TryIntersect(region, _sourceRange, out var intersection) ? intersection : region;
            var mapped = new GridRange(MapDestination(clipped.Start), MapDestination(clipped.End));

            // A destination that already overlaps an existing merge is left alone rather than
            // rejecting the whole paste; this mirrors Excel silently skipping the recreation of a
            // merge that would collide with one already present at the destination.
            if (targetSheet.MergedRegions.Any(existing => existing.Overlaps(mapped)))
                continue;

            targetSheet.AddMergedRegion(mapped);
            _addedRegions.Add(mapped);
        }

        return new CommandOutcome(true, AffectedCells: _addedRegions.SelectMany(r => r.AllCells()).Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedRegions is null)
            return;

        var targetSheet = ctx.GetSheet(_sheetId);
        foreach (var region in _addedRegions)
            targetSheet.RemoveMergedRegion(region);
        _addedRegions = null;
    }

    private CellAddress MapDestination(CellAddress source)
    {
        var rowOffset = source.Row - _sourceRange.Start.Row;
        var colOffset = source.Col - _sourceRange.Start.Col;
        return _transpose
            ? new CellAddress(_sheetId, _destination.Row + colOffset, _destination.Col + rowOffset)
            : new CellAddress(_sheetId, _destination.Row + rowOffset, _destination.Col + colOffset);
    }
}
