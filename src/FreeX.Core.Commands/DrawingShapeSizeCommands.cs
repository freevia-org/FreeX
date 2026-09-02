using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class ResizeDrawingShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly double _width;
    private readonly double _height;
    private readonly bool? _flipHorizontal;
    private readonly bool? _flipVertical;
    private double _previousWidth;
    private double _previousHeight;
    private bool _previousFlipHorizontal;
    private bool _previousFlipVertical;
    private bool _applied;

    public string Label => "Resize Shape";

    public ResizeDrawingShapeCommand(
        SheetId sheetId,
        Guid shapeId,
        double width,
        double height,
        bool? flipHorizontal = null,
        bool? flipVertical = null)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _width = width;
        _height = height;
        _flipHorizontal = flipHorizontal;
        _flipVertical = flipVertical;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (DrawingShapeCommandGuards.RejectInvalidSize(_width, _height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        // r218: the picture twin of this guard, for the same gesture -- see ResizePictureCommand for
        // why exact double equality is the safe direction here.
        if (shape.Width.Equals(_width)
            && shape.Height.Equals(_height)
            && (!_flipHorizontal.HasValue || shape.FlipHorizontal == _flipHorizontal.Value)
            && (!_flipVertical.HasValue || shape.FlipVertical == _flipVertical.Value))
        {
            return new CommandOutcome(true, IsNoOp: true);
        }

        _previousWidth = shape.Width;
        _previousHeight = shape.Height;
        _previousFlipHorizontal = shape.FlipHorizontal;
        _previousFlipVertical = shape.FlipVertical;
        shape.Width = _width;
        shape.Height = _height;
        if (_flipHorizontal.HasValue)
            shape.FlipHorizontal = _flipHorizontal.Value;
        if (_flipVertical.HasValue)
            shape.FlipVertical = _flipVertical.Value;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.Width = _previousWidth;
        shape.Height = _previousHeight;
        shape.FlipHorizontal = _previousFlipHorizontal;
        shape.FlipVertical = _previousFlipVertical;
        _applied = false;
    }
}

public sealed class RotateDrawingShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly double _rotationDegrees;
    private double _previousRotationDegrees;
    private bool _applied;

    public string Label => "Rotate Shape";

    public RotateDrawingShapeCommand(SheetId sheetId, Guid shapeId, double rotationDegrees)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _rotationDegrees = rotationDegrees;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_rotationDegrees))
            return new CommandOutcome(false, "Shape rotation must be a finite number.");

        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        // r218: compared against the NORMALISED angle, as RotatePictureCommand explains.
        var normalizedRotation = ObjectRotationNormalizer.NormalizeDegrees(_rotationDegrees);
        if (shape.RotationDegrees.Equals(normalizedRotation))
            return new CommandOutcome(true, IsNoOp: true);

        _previousRotationDegrees = shape.RotationDegrees;
        shape.RotationDegrees = normalizedRotation;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.RotationDegrees = _previousRotationDegrees;
        _applied = false;
    }

}
