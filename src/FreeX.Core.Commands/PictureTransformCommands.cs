using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ResizePictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly double _width;
    private readonly double _height;
    private readonly bool? _flipHorizontal;
    private readonly bool? _flipVertical;
    private double _previousWidth;
    private double _previousHeight;
    private bool _previousFlipHorizontal;
    private bool _previousFlipVertical;
    private bool _applied;

    public string Label => "Resize Picture";

    public ResizePictureCommand(
        SheetId sheetId,
        Guid pictureId,
        double width,
        double height,
        bool? flipHorizontal = null,
        bool? flipVertical = null)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _width = width;
        _height = height;
        _flipHorizontal = flipHorizontal;
        _flipVertical = flipVertical;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (PictureCommandGuards.RejectInvalidSize(_width, _height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();

        // R111-model-drawing-object-lock-1-1: layer in the per-picture Locked override so an
        // author-unlocked picture stays resizable even while the sheet blocks "Edit objects".
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;

        // r218: Size &amp; Properties pre-fills the current width and height, so tabbing out of the
        // boxes re-submits them. Exact double equality is deliberate and is the SAFE direction to be
        // wrong in: a value that came back through a text box at a different precision compares
        // unequal and takes the real-edit path, which costs an undo entry; a tolerance would risk
        // swallowing a genuine one-hundredth-of-a-point resize.
        if (picture.Width.Equals(_width)
            && picture.Height.Equals(_height)
            && (!_flipHorizontal.HasValue || picture.FlipHorizontal == _flipHorizontal.Value)
            && (!_flipVertical.HasValue || picture.FlipVertical == _flipVertical.Value))
        {
            return new CommandOutcome(true, IsNoOp: true);
        }

        _previousWidth = picture.Width;
        _previousHeight = picture.Height;
        _previousFlipHorizontal = picture.FlipHorizontal;
        _previousFlipVertical = picture.FlipVertical;
        picture.Width = _width;
        picture.Height = _height;
        if (_flipHorizontal.HasValue)
            picture.FlipHorizontal = _flipHorizontal.Value;
        if (_flipVertical.HasValue)
            picture.FlipVertical = _flipVertical.Value;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture)) return;
        picture.Width = _previousWidth;
        picture.Height = _previousHeight;
        picture.FlipHorizontal = _previousFlipHorizontal;
        picture.FlipVertical = _previousFlipVertical;
        _applied = false;
    }
}

public sealed class RepositionPictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly CellAddress _anchor;
    private CellAddress _previousAnchor;
    private bool _applied;

    public string Label => "Move Picture";

    public RepositionPictureCommand(SheetId sheetId, Guid pictureId, CellAddress anchor)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _anchor = anchor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();

        // R111-model-drawing-object-lock-1-1: layer in the per-picture Locked override so an
        // author-unlocked picture stays movable even while the sheet blocks "Edit objects".
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;
        // r218: a drag that ends in the cell it started from -- picked up, moved, put back, or
        // nudged less than one cell -- lands here with the anchor the picture already has.
        if (picture.Anchor == _anchor)
            return new CommandOutcome(true, IsNoOp: true);

        _previousAnchor = picture.Anchor;
        picture.Anchor = _anchor;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [_previousAnchor, _anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture)) return;
        picture.Anchor = _previousAnchor;
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
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-picture Locked override so an
        // author-unlocked picture stays rotatable even while the sheet blocks "Edit objects".
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;

        // r218: compare against what Apply WRITES, not what the caller passed. The rotation is
        // normalised on the way in, so asking for 370 degrees on a picture already at 10 is no
        // change -- comparing against the raw request would call that a real edit.
        var normalizedRotation = ObjectRotationNormalizer.NormalizeDegrees(_rotationDegrees);
        if (picture.RotationDegrees.Equals(normalizedRotation))
            return new CommandOutcome(true, IsNoOp: true);

        _previousRotationDegrees = picture.RotationDegrees;
        picture.RotationDegrees = normalizedRotation;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture)) return;
        picture.RotationDegrees = _previousRotationDegrees;
        _applied = false;
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
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-picture Locked override so an
        // author-unlocked picture's aspect-ratio lock stays editable even while the sheet blocks
        // "Edit objects".
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;

        // r210: an equal-value setter -- the gallery/toggle pre-populates the current value, so
        // re-confirming it pushed an undo entry that changed nothing, and that push clears redo.
        if (picture.LockAspectRatio == _lockAspectRatio)
            return new CommandOutcome(true, IsNoOp: true);

        _previousLockAspectRatio = picture.LockAspectRatio;
        picture.LockAspectRatio = _lockAspectRatio;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture)) return;
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
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-picture Locked override so an
        // author-unlocked picture stays croppable even while the sheet blocks "Edit objects".
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;
        if (picture.Kind != PictureKind.Image)
            return new CommandOutcome(false, "Only inserted image pictures can be cropped.");

        // r211: an equal-value setter. All four crop edges are written, so all four are compared.
        if (picture.CropLeft == _left && picture.CropTop == _top
            && picture.CropRight == _right && picture.CropBottom == _bottom)
        {
            return new CommandOutcome(true, IsNoOp: true);
        }

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
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture)) return;
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
