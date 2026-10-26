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
        return new CommandOutcome(true, AffectedCells: [_address], IsNoOp: NothingChanged(sheet));
    }

    /// <summary>
    /// r260: re-applying the same hyperlink -- re-confirming the Insert Hyperlink dialog on a cell
    /// that already carries that link, with the same display text and screen tip -- writes back
    /// exactly what is there. Without this the command still pushed an undo entry, and
    /// UndoRedoStack.Push clears the redo stack, destroying a real edit the user could have redone.
    ///
    /// <para>The decision is POST-HOC over the command's whole undo record: Revert restores the cell,
    /// the hyperlink target, the metadata, the rich-text runs and the phonetic guide, and nothing
    /// else, so all five are compared. The two "had" flags matter as much as the values -- Apply
    /// REMOVES the rich-text runs and the phonetic guide, so a cell that had either and no longer
    /// does has changed even though every other field matches.</para>
    /// </summary>
    private bool NothingChanged(Sheet sheet) =>
        CellEditCompanionSnapshot.SameCellOrAbsent(sheet, _address, _oldCell)
        && SameOptionalEntry(sheet.Hyperlinks, _hadOldTarget, _oldTarget)
        && SameOptionalEntry(sheet.HyperlinkMetadata, _hadOldMetadata, _oldMetadata)
        && SameOptionalRuns(sheet, _hadOldRichTextRuns, _oldRichTextRuns)
        && SameOptionalEntry(sheet.CellPhoneticGuides, _hadOldPhoneticGuide, _oldPhoneticGuide);

    /// <summary>
    /// Present-with-this-value versus absent, for the per-address companion maps. The captured value
    /// is the sheet's own instance rather than a copy, so for these types equality is content
    /// equality: <c>HyperlinkMetadata</c> is a record of three scalars, and a phonetic guide taken
    /// out of the map and put back is the same object.
    /// </summary>
    private bool SameOptionalEntry<T>(IDictionary<CellAddress, T> map, bool hadValue, T? captured)
    {
        var present = map.TryGetValue(_address, out var current);
        if (present != hadValue)
            return false;

        return !present || EqualityComparer<T?>.Default.Equals(current, captured);
    }

    /// <summary>
    /// Rich-text runs need element-wise comparison rather than the list's own equality: the list is
    /// an <c>IReadOnlyList</c>, compared by reference, while <c>CellTextRun</c> is a record of
    /// scalars whose equality is content equality.
    /// </summary>
    private bool SameOptionalRuns(Sheet sheet, bool hadRuns, IReadOnlyList<CellTextRun>? captured)
    {
        var present = sheet.RichTextRuns.TryGetValue(_address, out var current);
        if (present != hadRuns)
            return false;
        if (!present)
            return true;
        if (current is null || captured is null)
            return ReferenceEquals(current, captured);
        if (current.Count != captured.Count)
            return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (current[i] != captured[i])
                return false;
        }

        return true;
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
