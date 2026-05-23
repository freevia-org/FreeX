using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _oldCell = sheet.GetCell(_address)?.Clone();
        _hadOldTarget = sheet.Hyperlinks.TryGetValue(_address, out _oldTarget);
        _hadOldMetadata = sheet.HyperlinkMetadata.TryGetValue(_address, out _oldMetadata);

        var newCell = Cell.FromValue(new TextValue(_displayText));
        if (_oldCell is not null)
            newCell.StyleId = _oldCell.StyleId;
        sheet.SetCell(_address, newCell);
        sheet.Hyperlinks[_address] = _target;
        sheet.HyperlinkMetadata[_address] = _metadata;
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
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = sheet.Hyperlinks
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);
        _metadataSnapshot = sheet.HyperlinkMetadata
            .Where(p => _range.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value);

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
