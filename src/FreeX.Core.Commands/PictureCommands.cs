using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class InsertPictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly PictureModel _picture;
    private bool _added;

    public string Label => "Insert Picture";

    public InsertPictureCommand(
        SheetId sheetId,
        CellAddress anchor,
        IReadOnlyCollection<byte> imageBytes,
        string contentType,
        double width = 240,
        double height = 140)
    {
        _sheetId = sheetId;
        _picture = new PictureModel
        {
            Anchor = anchor,
            Kind = PictureKind.Image,
            ImageBytes = imageBytes.ToArray(),
            ContentType = contentType,
            Width = width,
            Height = height
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_picture.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Picture anchor must be on the target sheet.");
        if (_picture.ImageBytes is not { Length: > 0 })
            return new CommandOutcome(false, "Picture data cannot be empty.");
        if (!double.IsFinite(_picture.Width) || !double.IsFinite(_picture.Height) || _picture.Width <= 0 || _picture.Height <= 0)
            return new CommandOutcome(false, "Picture size must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        sheet.Pictures.Add(_picture);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added) return;
        ctx.GetSheet(_sheetId).Pictures.Remove(_picture);
        _added = false;
    }
}

public static class ClipboardPictureService
{
    public static InsertPictureCommand CreateInsertCommand(
        SheetId sheetId,
        CellAddress anchor,
        IReadOnlyCollection<byte> pngBytes,
        int pixelWidth,
        int pixelHeight)
    {
        return new InsertPictureCommand(
            sheetId,
            anchor,
            pngBytes,
            "image/png",
            Math.Max(24, pixelWidth),
            Math.Max(18, pixelHeight));
    }
}
