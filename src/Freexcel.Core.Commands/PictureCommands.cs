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

public sealed class SetPictureLockAspectRatioCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly bool _lockAspectRatio;
    private bool _previousLockAspectRatio;
    private bool _applied;

    public string Label => "Picture Lock Aspect Ratio";

    public SetPictureLockAspectRatioCommand(SheetId sheetId, Guid pictureId, bool lockAspectRatio)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _lockAspectRatio = lockAspectRatio;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var picture = sheet.Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null)
            return new CommandOutcome(false, "Picture was not found.");

        _previousLockAspectRatio = picture.LockAspectRatio;
        picture.LockAspectRatio = _lockAspectRatio;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var picture = ctx.GetSheet(_sheetId).Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null) return;
        picture.LockAspectRatio = _previousLockAspectRatio;
        _applied = false;
    }
}

public sealed class SetPictureCropCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly double _left;
    private readonly double _top;
    private readonly double _right;
    private readonly double _bottom;
    private (double Left, double Top, double Right, double Bottom) _previous;
    private bool _applied;

    public string Label => "Crop Picture";

    public SetPictureCropCommand(SheetId sheetId, Guid pictureId, double left, double top, double right, double bottom)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _left = left;
        _top = top;
        _right = right;
        _bottom = bottom;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!IsValidCrop(_left, _top, _right, _bottom))
            return new CommandOutcome(false, "Picture crop values must be finite percentages between 0 and 100%, with visible width and height remaining.");

        var sheet = ctx.GetSheet(_sheetId);
        var picture = sheet.Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null)
            return new CommandOutcome(false, "Picture was not found.");
        if (picture.Kind != PictureKind.Image)
            return new CommandOutcome(false, "Only inserted image pictures can be cropped.");

        _previous = (picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom);
        picture.CropLeft = _left;
        picture.CropTop = _top;
        picture.CropRight = _right;
        picture.CropBottom = _bottom;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var picture = ctx.GetSheet(_sheetId).Pictures.FirstOrDefault(p => p.Id == _pictureId);
        if (picture is null) return;
        picture.CropLeft = _previous.Left;
        picture.CropTop = _previous.Top;
        picture.CropRight = _previous.Right;
        picture.CropBottom = _previous.Bottom;
        _applied = false;
    }

    private static bool IsValidCrop(double left, double top, double right, double bottom) =>
        double.IsFinite(left) &&
        double.IsFinite(top) &&
        double.IsFinite(right) &&
        double.IsFinite(bottom) &&
        left >= 0 &&
        top >= 0 &&
        right >= 0 &&
        bottom >= 0 &&
        left + right < 1 &&
        top + bottom < 1;
}
