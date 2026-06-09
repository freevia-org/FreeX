using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteRangeAsPictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly PictureModel _picture;
    private bool _added;

    public string Label => "Paste Picture";

    public PasteRangeAsPictureCommand(
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Address, string Text)> sourceCells,
        CellAddress destination,
        bool isLinkedToSourceRange = false,
        string? sourceSheetName = null)
        : this(
            sheetId,
            sourceRange,
            sourceCells.Select(cell => (
                cell.Address,
                new PictureCellSnapshot(
                    cell.Address.Row - sourceRange.Start.Row,
                    cell.Address.Col - sourceRange.Start.Col,
                    cell.Text))).ToList(),
            destination,
            isLinkedToSourceRange,
            sourceSheetName)
    {
    }

    public PasteRangeAsPictureCommand(
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Address, PictureCellSnapshot Snapshot)> sourceCells,
        CellAddress destination,
        bool isLinkedToSourceRange = false,
        string? sourceSheetName = null)
    {
        _sheetId = sheetId;
        _picture = new PictureModel
        {
            Anchor = destination,
            SourceRowCount = sourceRange.RowCount,
            SourceColumnCount = sourceRange.ColCount,
            IsLinkedToSourceRange = isLinkedToSourceRange,
            LinkedSourceRange = isLinkedToSourceRange ? sourceRange : null,
            LinkedSourceSheetName = isLinkedToSourceRange ? sourceSheetName : null,
            Width = Math.Max(80, sourceRange.ColCount * 80),
            Height = Math.Max(40, sourceRange.RowCount * 20)
        };

        foreach (var (address, snapshot) in sourceCells)
        {
            if (address.Row < sourceRange.Start.Row ||
                address.Row > sourceRange.End.Row ||
                address.Col < sourceRange.Start.Col ||
                address.Col > sourceRange.End.Col)
                continue;

            _picture.Cells.Add(new PictureCellSnapshot(
                address.Row - sourceRange.Start.Row,
                address.Col - sourceRange.Start.Col,
                snapshot.Text,
                snapshot.Style?.Clone(),
                snapshot.IsNumericOrDate));
        }
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_picture.Anchor.Sheet != _sheetId)
            return PictureCommandGuards.PictureAnchorOnTargetSheet();

        var sheet = ctx.GetSheet(_sheetId);
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        sheet.Pictures.Add(_picture);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).Pictures.Remove(_picture);
        _added = false;
    }
}
