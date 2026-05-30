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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var target = FindRotatable(sheet, _kind, _objectId);
        if (target is null)
            return new CommandOutcome(false, "Drawing object was not found.");

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
                var picture = sheet.Pictures.FirstOrDefault(item => item.Id == objectId);
                return picture is null
                    ? null
                    : new RotatableObjectRef(picture.Anchor, () => picture.RotationDegrees, value => picture.RotationDegrees = value);
            case SelectionPaneObjectKind.Shape:
                var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == objectId);
                return shape is null
                    ? null
                    : new RotatableObjectRef(shape.Anchor, () => shape.RotationDegrees, value => shape.RotationDegrees = value);
            case SelectionPaneObjectKind.TextBox:
                var textBox = sheet.TextBoxes.FirstOrDefault(item => item.Id == objectId);
                return textBox is null
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
