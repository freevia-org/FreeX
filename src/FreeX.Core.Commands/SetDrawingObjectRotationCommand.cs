using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Sets the rotation (in degrees) of a single drawing object (picture, shape, or text box),
/// dispatching by <see cref="SelectionPaneObjectKind"/>. Used by on-canvas rotation-grip drags.
/// </summary>
public sealed class SetDrawingObjectRotationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _objectId;
    private readonly double _rotationDegrees;
    private double _previousRotationDegrees;
    private bool _applied;

    public string Label => "Rotate Object";

    public SetDrawingObjectRotationCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        double rotationDegrees)
    {
        _sheetId = sheetId;
        _kind = kind;
        _objectId = objectId;
        _rotationDegrees = rotationDegrees;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_rotationDegrees))
            return new CommandOutcome(false, "Object rotation must be a finite number.");

        var sheet = ctx.GetSheet(_sheetId);
        var target = FindRotatable(sheet, _kind, _objectId);
        if (target is null)
            return new CommandOutcome(false, "Drawing object was not found.");

        // R113-model-drawing-object-lock-1-1: honour the object's own Locked flag, not just the
        // sheet's "Edit objects" permission -- an author-unlocked picture/shape/text box stays
        // rotatable on a protected sheet, matching the R111/R112 per-object guard overloads.
        if (SelectionPaneObjectAccess.Find(sheet, _kind, _objectId) is { } objectRef &&
            SelectionPaneObjectAccess.RejectIfEditObjectsBlocked(sheet, objectRef) is { } protectedOutcome)
            return protectedOutcome;

        _previousRotationDegrees = target.RotationDegrees;
        target.RotationDegrees = ObjectRotationNormalizer.NormalizeDegrees(_rotationDegrees);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [target.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var target = FindRotatable(ctx.GetSheet(_sheetId), _kind, _objectId);
        if (target is null)
            return;

        target.RotationDegrees = _previousRotationDegrees;
        _applied = false;
    }

    private static RotatableObjectRef? FindRotatable(Sheet sheet, SelectionPaneObjectKind kind, Guid objectId)
    {
        switch (kind)
        {
            case SelectionPaneObjectKind.Picture:
                return !PictureCommandGuards.TryFindPicture(sheet, objectId, out var picture)
                    ? null
                    : new RotatableObjectRef(picture.Anchor, () => picture.RotationDegrees, value => picture.RotationDegrees = value);
            case SelectionPaneObjectKind.Shape:
                return !DrawingShapeCommandGuards.TryFindShape(sheet, objectId, out var shape)
                    ? null
                    : new RotatableObjectRef(shape.Anchor, () => shape.RotationDegrees, value => shape.RotationDegrees = value);
            case SelectionPaneObjectKind.TextBox:
                return !TextBoxCommandGuards.TryFindTextBox(sheet, objectId, out var textBox)
                    ? null
                    : new RotatableObjectRef(textBox.Anchor, () => textBox.RotationDegrees, value => textBox.RotationDegrees = value);
            default:
                return null;
        }
    }

    private sealed record RotatableObjectRef(CellAddress Anchor, Func<double> GetRotation, Action<double> SetRotation)
    {
        public double RotationDegrees
        {
            get => GetRotation();
            set => SetRotation(value);
        }
    }
}
