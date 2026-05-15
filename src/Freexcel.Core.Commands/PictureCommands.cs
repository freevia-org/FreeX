using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

public sealed class ResizePictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly double _width;
    private readonly double _height;
    private double _previousWidth;
    private double _previousHeight;
    private bool _applied;

    public string Label => "Resize Picture";

    public ResizePictureCommand(SheetId sheetId, Guid pictureId, double width, double height)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _width = width;
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_width) || !double.IsFinite(_height) || _width <= 0 || _height <= 0)
            return new CommandOutcome(false, "Picture size must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        var picture = sheet.Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null)
            return new CommandOutcome(false, "Picture was not found.");

        _previousWidth = picture.Width;
        _previousHeight = picture.Height;
        picture.Width = _width;
        picture.Height = _height;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var picture = ctx.GetSheet(_sheetId).Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null) return;
        picture.Width = _previousWidth;
        picture.Height = _previousHeight;
        _applied = false;
    }
}

public sealed class RotatePictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly double _rotationDegrees;
    private double _previousRotationDegrees;
    private bool _applied;

    public string Label => "Rotate Picture";

    public RotatePictureCommand(SheetId sheetId, Guid pictureId, double rotationDegrees)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _rotationDegrees = rotationDegrees;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_rotationDegrees))
            return new CommandOutcome(false, "Picture rotation must be a finite number.");

        var sheet = ctx.GetSheet(_sheetId);
        var picture = sheet.Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null)
            return new CommandOutcome(false, "Picture was not found.");

        _previousRotationDegrees = picture.RotationDegrees;
        picture.RotationDegrees = Normalize(_rotationDegrees);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var picture = ctx.GetSheet(_sheetId).Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null) return;
        picture.RotationDegrees = _previousRotationDegrees;
        _applied = false;
    }

    private static double Normalize(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
