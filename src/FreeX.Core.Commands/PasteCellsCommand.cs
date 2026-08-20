using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Pastes complete cell payloads, including values/formulas and formatting.
/// </summary>
public sealed class PasteCellsCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _cells;
    private readonly IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRuns;
    private readonly IReadOnlyDictionary<CellAddress, string>? _hyperlinks;
    private readonly IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadata;
    private readonly IReadOnlyDictionary<CellAddress, CellPhoneticGuide>? _phoneticGuides;
    // freex-cell-styles-F1: each pasted Cell's StyleId (set by PasteCommandFactory from the SOURCE
    // sheet's Cell.StyleId) is a raw index into the SOURCE workbook's private style table. For an
    // ordinary same-window paste that index is also valid in ctx.Workbook (they're the same
    // workbook), so leaving it untranslated has always looked correct. But when copy and paste
    // happen in two different open FreeX windows (WorkbookClipboardSession is a DI-wide singleton
    // shared by every window, see its own doc comment), ctx.Workbook is a DIFFERENT, independently
    // built Workbook whose own style table has whatever CellStyle happens to occupy that same
    // numeric slot -- writing the raw index in silently swaps in an unrelated style. When the
    // caller can supply the ACTUAL source CellStyle content per pasted address (PasteCommandFactory
    // resolves it from the source sheet's owning workbook), Apply() re-registers that content into
    // ctx.Workbook via RegisterStyle -- which is workbook-relative dedup-by-structural-equality, so
    // for the ordinary same-workbook case this simply resolves back to the identical index the old
    // code used, and for the cross-workbook case it resolves (or creates) the correct index in the
    // DESTINATION workbook instead of trusting the source's. Null (the default) preserves the prior
    // untranslated behavior exactly, which every pre-existing caller that doesn't supply this still
    // gets.
    private readonly IReadOnlyDictionary<CellAddress, CellStyle>? _sourceStyles;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata, bool HadPhoneticGuide, CellPhoneticGuide? OldPhoneticGuide)>? _snapshot;

    // R119-commands-undo-byte-budget-1: the undo snapshot holds a full Cell clone plus style,
    // hyperlink/metadata, rich-text runs and phonetic guide PER PASTED CELL (see Apply below), so
    // its footprint scales with _cells.Count, not with a flat per-command constant. Without this,
    // CommandBus's 50 MB undo byte-budget (CommandBus.MaxUndoByteBudget) bills every paste at the
    // 200-byte IEstimatesMemory default regardless of size, so a 100k-cell paste never trips the
    // budget and only the 100-entry depth cap bounds the undo stack.
    private const int BytesPerCell = 300;

    public string Label => _cells.Count == 1 ? "Paste Cell" : $"Paste {_cells.Count} Cells";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min((long)_cells.Count * BytesPerCell, int.MaxValue);

    public PasteCellsCommand(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, Cell Cell)> cells,
        IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? richTextRuns = null,
        IReadOnlyDictionary<CellAddress, string>? hyperlinks = null,
        IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? hyperlinkMetadata = null,
        IReadOnlyDictionary<CellAddress, CellPhoneticGuide>? phoneticGuides = null,
        IReadOnlyDictionary<CellAddress, CellStyle>? sourceStyles = null)
    {
        _sheetId = sheetId;
        _cells = cells;
        _richTextRuns = richTextRuns;
        _hyperlinks = hyperlinks;
        _hyperlinkMetadata = hyperlinkMetadata;
        _phoneticGuides = phoneticGuides;
        _sourceStyles = sourceStyles;
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

            // This command always carries the source cell's full formatting (StyleId) into the
            // destination -- it is only ever constructed for PasteCellsMode.All (see
            // PasteCommandFactory), never for a values-only paste, which instead builds an
            // EditCellsCommand that leaves the destination's own style untouched. So even when every
            // destination cell is individually unlocked (CanEditCell above passes), a protected sheet
            // must still require the FormatCells permission before this formatting change is allowed,
            // matching every other formatting-capable command (ApplyStyleCommand, PasteFormatsCommand,
            // MergeCellsCommand, GroupedApplyStyleCommand, PasteConditionalFormatsCommand).
            if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } formatRejection)
                return formatRejection;
        }

        if (CommandGuards.RejectIfSplitsArray(sheet, _cells.Select(c => c.Address), allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = [];
        var affected = new List<CellAddress>(_cells.Count);

        foreach (var (addr, cell) in _cells)
        {
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(addr, out var oldRuns);
            var hadHyperlink = sheet.Hyperlinks.TryGetValue(addr, out var oldHyperlink);
            var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(addr, out var oldHyperlinkMetadata);
            var hadPhoneticGuide = sheet.CellPhoneticGuides.TryGetValue(addr, out var oldPhoneticGuide);
            _snapshot.Add((
                addr,
                sheet.GetCell(addr)?.Clone(),
                sheet.GetStyleOnly(addr.Row, addr.Col),
                hadRichTextRuns,
                oldRuns,
                hadHyperlink,
                oldHyperlink,
                hadHyperlinkMetadata,
                oldHyperlinkMetadata,
                hadPhoneticGuide,
                oldPhoneticGuide));

            // A destination cell that is a non-anchor (hidden/covered) member of an existing merged
            // region must stay empty, matching Excel: only the merge's top-left anchor cell ever
            // carries a value. Writing into a covered cell would silently plant a live value that the
            // grid never displays (the merge only renders the anchor), yet formulas like =SUM or
            // unmerging later would suddenly surface it. So skip the mutation entirely for those cells.
            var mergeRegion = sheet.GetMergeRegion(addr);
            if (mergeRegion is { } region && !region.Start.Equals(addr))
                continue;

            var pastedCell = cell.Clone();
            // freex-cell-styles-F1: translate the source's raw StyleId into a valid destination
            // index when the caller supplied the actual source style content (cross-workbook
            // paste). See the field doc comment on _sourceStyles above.
            if (_sourceStyles is not null && _sourceStyles.TryGetValue(addr, out var sourceStyle))
                pastedCell.StyleId = ctx.Workbook.RegisterStyle(sourceStyle);
            sheet.SetCell(addr, pastedCell);

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

            // R78-selfreg-twin-sweep-5: carry the phonetic guide (furigana) alongside its
            // RichTextRuns companion at the pasted target, matching that dictionary's handling.
            if (_phoneticGuides is not null && _phoneticGuides.TryGetValue(addr, out var newPhoneticGuide))
                sheet.CellPhoneticGuides[addr] = newPhoneticGuide;
            else
                sheet.CellPhoneticGuides.Remove(addr);

            affected.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata, hadPhoneticGuide, oldPhoneticGuide) in _snapshot)
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

            if (hadPhoneticGuide && oldPhoneticGuide is not null)
                sheet.CellPhoneticGuides[addr] = oldPhoneticGuide;
            else
                sheet.CellPhoneticGuides.Remove(addr);
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
