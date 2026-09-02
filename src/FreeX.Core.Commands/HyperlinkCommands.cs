using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets a cell hyperlink and display text with undo support.</summary>
public sealed class SetHyperlinkCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly string _target;
    private readonly string _displayText;
    private readonly HyperlinkMetadata _metadata;
    private Cell? _oldCell;
    private string? _oldTarget;
    private HyperlinkMetadata? _oldMetadata;
    private bool _hadOldTarget;
    private bool _hadOldMetadata;
    private bool _hadOldRichTextRuns;
    private IReadOnlyList<CellTextRun>? _oldRichTextRuns;
    private bool _hadOldPhoneticGuide;
    private CellPhoneticGuide? _oldPhoneticGuide;

    public string Label => "Insert Hyperlink";

    public SetHyperlinkCommand(
        SheetId sheetId,
        CellAddress address,
        string target,
        string displayText,
        HyperlinkMetadata? metadata = null)
    {
        _sheetId = sheetId;
        _address = address;
        _target = target;
        _displayText = displayText;
        _metadata = metadata ?? new HyperlinkMetadata();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.InsertHyperlinks) is { } protectedOutcome)
            return protectedOutcome;

        _oldCell = sheet.GetCell(_address)?.Clone();
        _hadOldTarget = sheet.Hyperlinks.TryGetValue(_address, out _oldTarget);
        _hadOldMetadata = sheet.HyperlinkMetadata.TryGetValue(_address, out _oldMetadata);
        _hadOldRichTextRuns = sheet.RichTextRuns.TryGetValue(_address, out _oldRichTextRuns);
        _hadOldPhoneticGuide = sheet.CellPhoneticGuides.TryGetValue(_address, out _oldPhoneticGuide);

        var newCell = Cell.FromValue(new TextValue(_displayText));
        if (_oldCell is not null)
            newCell.StyleId = _oldCell.StyleId;
        var hyperlinkStyle = ctx.Workbook.GetStyle(newCell.StyleId).Clone();
        hyperlinkStyle.Underline = true;
        hyperlinkStyle.FontColor = ctx.Workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Hyperlink);
        newCell.StyleId = ctx.Workbook.RegisterStyle(hyperlinkStyle);
        sheet.SetCell(_address, newCell);
        sheet.Hyperlinks[_address] = _target;
        sheet.HyperlinkMetadata[_address] = _metadata;

        // The display text is being replaced wholesale, so any rich-text runs and phonetic guide
        // that belonged to the old content are stale (their character offsets/reading no longer
        // line up with the new text) and must not carry over onto the hyperlink text (matching
        // GroupedEditCellsCommand's handling).
        sheet.RichTextRuns.Remove(_address);
        sheet.CellPhoneticGuides.Remove(_address);
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (_oldCell is null)
            sheet.ClearCell(_address);
        else
            sheet.SetCell(_address, _oldCell.Clone());

        if (_hadOldTarget && _oldTarget is not null)
            sheet.Hyperlinks[_address] = _oldTarget;
        else
            sheet.Hyperlinks.Remove(_address);
        if (_hadOldMetadata && _oldMetadata is not null)
            sheet.HyperlinkMetadata[_address] = _oldMetadata;
        else
            sheet.HyperlinkMetadata.Remove(_address);
        if (_hadOldRichTextRuns && _oldRichTextRuns is not null)
            sheet.RichTextRuns[_address] = _oldRichTextRuns;
        else
            sheet.RichTextRuns.Remove(_address);
        if (_hadOldPhoneticGuide && _oldPhoneticGuide is not null)
            sheet.CellPhoneticGuides[_address] = _oldPhoneticGuide;
        else
            sheet.CellPhoneticGuides.Remove(_address);
    }
}

/// <summary>Clears hyperlinks in a range without changing display text.</summary>
public sealed class ClearHyperlinksCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private Dictionary<CellAddress, string>? _snapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _metadataSnapshot;

    public string Label => "Clear Hyperlinks";

    public ClearHyperlinksCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _snapshot = sheet.Hyperlinks
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        _metadataSnapshot = sheet.HyperlinkMetadata
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        if (_snapshot.Keys.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return CommandGuards.RejectSheetProtected();

        // r220: Clear > Hyperlinks over a selection that holds none. Placed after the protection
        // check rather than before it only for symmetry -- with both snapshots empty that check
        // cannot fail, so the two orderings are equivalent.
        if (_snapshot.Count == 0 && _metadataSnapshot.Count == 0)
            return new CommandOutcome(true, IsNoOp: true);

        foreach (var addr in _snapshot.Keys)
            sheet.Hyperlinks.Remove(addr);
        foreach (var addr in _metadataSnapshot.Keys)
            sheet.HyperlinkMetadata.Remove(addr);

        return new CommandOutcome(true, AffectedCells: _snapshot.Keys.ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, target) in _snapshot)
            sheet.Hyperlinks[addr] = target;
        if (_metadataSnapshot is not null)
        {
            foreach (var (addr, metadata) in _metadataSnapshot)
                sheet.HyperlinkMetadata[addr] = metadata;
        }
    }
}

/// <summary>Removes hyperlinks in a range and resets visible hyperlink styling.</summary>
public sealed class RemoveHyperlinksCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private Dictionary<CellAddress, string>? _snapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _metadataSnapshot;
    private Dictionary<CellAddress, Cell>? _cellSnapshot;

    public string Label => "Remove Hyperlinks";

    public RemoveHyperlinksCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _snapshot = sheet.Hyperlinks
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        _metadataSnapshot = sheet.HyperlinkMetadata
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        if (_snapshot.Keys.Any(address => !CommandGuards.CanEditCell(ctx.Workbook, sheet, address)))
            return CommandGuards.RejectSheetProtected();

        // r224: the twin of r220's ClearHyperlinksCommand guard, in the command next door. Remove
        // Hyperlinks over a selection that carries none removes nothing AND restyles nothing -- the
        // underline/colour reset below only runs over _snapshot's keys, so an empty snapshot means
        // the whole method is inert.
        if (_snapshot.Count == 0 && _metadataSnapshot.Count == 0)
            return new CommandOutcome(true, IsNoOp: true);

        _cellSnapshot = [];
        foreach (var addr in _snapshot.Keys)
        {
            if (sheet.GetCell(addr) is { } cell)
            {
                _cellSnapshot[addr] = cell.Clone();
                var style = ctx.Workbook.GetStyle(cell.StyleId).Clone();
                style.Underline = false;
                style.DoubleUnderline = false;
                style.FontColor = CellColor.Black;
                cell.StyleId = ctx.Workbook.RegisterStyle(style);
            }

            sheet.Hyperlinks.Remove(addr);
            sheet.HyperlinkMetadata.Remove(addr);
        }

        foreach (var addr in _metadataSnapshot.Keys)
            sheet.HyperlinkMetadata.Remove(addr);

        return new CommandOutcome(true, AffectedCells: _snapshot.Keys.ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, cell) in _cellSnapshot ?? [])
            sheet.SetCell(addr, cell.Clone());

        foreach (var (addr, target) in _snapshot)
            sheet.Hyperlinks[addr] = target;
        if (_metadataSnapshot is not null)
        {
            foreach (var (addr, metadata) in _metadataSnapshot)
                sheet.HyperlinkMetadata[addr] = metadata;
        }
    }
}
