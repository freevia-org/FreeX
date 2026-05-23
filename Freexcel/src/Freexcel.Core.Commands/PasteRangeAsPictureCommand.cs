using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

        foreach (var (address, text) in sourceCells)
        {
            if (address.Row < sourceRange.Start.Row ||
                address.Row > sourceRange.End.Row ||
                address.Col < sourceRange.Start.Col ||
                address.Col > sourceRange.End.Col)
                continue;

            _picture.Cells.Add(new PictureCellSnapshot(
                address.Row - sourceRange.Start.Row,
                address.Col - sourceRange.Start.Col,
                text));
        }
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_picture.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Picture anchor must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
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
