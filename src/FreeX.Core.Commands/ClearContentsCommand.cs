using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ClearContentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? _richTextRunsSnapshot;

    public string Label => "Clear Contents";

    public ClearContentsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
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
