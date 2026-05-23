using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AddDrawingShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly DrawingShapeModel _shape;
    private bool _added;

    public string Label => "Insert Shape";

    public AddDrawingShapeCommand(
        SheetId sheetId,
        CellAddress anchor,
        DrawingShapeKind kind,
        double width = 120,
        double height = 70)
    {
        _sheetId = sheetId;
        _shape = new DrawingShapeModel
        {
            Anchor = anchor,
            Kind = kind,
            Width = width,
            Height = height
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_shape.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Shape anchor must be on the target sheet.");
        if (!Enum.IsDefined(_shape.Kind))
            return new CommandOutcome(false, "Drawing shape kind is not supported.");
        if (!double.IsFinite(_shape.Width) || !double.IsFinite(_shape.Height) || _shape.Width <= 0 || _shape.Height <= 0)
            return new CommandOutcome(false, "Shape size must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        sheet.DrawingShapes.Add(_shape);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).DrawingShapes.Remove(_shape);
        _added = false;
    }
}

public sealed class BringDrawingShapeForwardCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private int _fromIndex = -1;
    private int _toIndex = -1;

    public string Label => "Bring Forward";

    public BringDrawingShapeForwardCommand(SheetId sheetId, Guid shapeId)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var index = sheet.DrawingShapes.FindIndex(shape => shape.Id == _shapeId);
        if (index < 0)
            return new CommandOutcome(false, "Drawing shape was not found.");

        if (index >= sheet.DrawingShapes.Count - 1)
            return new CommandOutcome(true);

        _fromIndex = index;
        _toIndex = index + 1;
        (sheet.DrawingShapes[_fromIndex], sheet.DrawingShapes[_toIndex]) =
            (sheet.DrawingShapes[_toIndex], sheet.DrawingShapes[_fromIndex]);

        return new CommandOutcome(true, AffectedCells: [sheet.DrawingShapes[_toIndex].Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_fromIndex < 0 || _toIndex < 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        (sheet.DrawingShapes[_fromIndex], sheet.DrawingShapes[_toIndex]) =
            (sheet.DrawingShapes[_toIndex], sheet.DrawingShapes[_fromIndex]);
        _fromIndex = -1;
        _toIndex = -1;
    }
}

public sealed class SendDrawingShapeBackwardCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private int _fromIndex = -1;
    private int _toIndex = -1;

    public string Label => "Send Backward";

    public SendDrawingShapeBackwardCommand(SheetId sheetId, Guid shapeId)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var index = sheet.DrawingShapes.FindIndex(shape => shape.Id == _shapeId);
        if (index < 0)
            return new CommandOutcome(false, "Drawing shape was not found.");

        if (index == 0)
            return new CommandOutcome(true);

        _fromIndex = index;
        _toIndex = index - 1;
        (sheet.DrawingShapes[_fromIndex], sheet.DrawingShapes[_toIndex]) =
            (sheet.DrawingShapes[_toIndex], sheet.DrawingShapes[_fromIndex]);

        return new CommandOutcome(true, AffectedCells: [sheet.DrawingShapes[_toIndex].Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_fromIndex < 0 || _toIndex < 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        (sheet.DrawingShapes[_fromIndex], sheet.DrawingShapes[_toIndex]) =
            (sheet.DrawingShapes[_toIndex], sheet.DrawingShapes[_fromIndex]);
        _fromIndex = -1;
        _toIndex = -1;
    }
}

public sealed class ResizeDrawingShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly double _width;
    private readonly double _height;
    private double _previousWidth;
    private double _previousHeight;
    private bool _applied;

    public string Label => "Resize Shape";

    public ResizeDrawingShapeCommand(SheetId sheetId, Guid shapeId, double width, double height)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _width = width;
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_width) || !double.IsFinite(_height) || _width <= 0 || _height <= 0)
            return new CommandOutcome(false, "Shape size must be positive.");

        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        _previousWidth = shape.Width;
        _previousHeight = shape.Height;
        shape.Width = _width;
        shape.Height = _height;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.Width = _previousWidth;
        shape.Height = _previousHeight;
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
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        _previousRotationDegrees = shape.RotationDegrees;
        shape.RotationDegrees = Normalize(_rotationDegrees);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.RotationDegrees = _previousRotationDegrees;
        _applied = false;
    }

    private static double Normalize(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}

public sealed class SetDrawingShapeColorsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly CellColor? _fillColor;
    private readonly CellColor? _outlineColor;
    private CellColor? _previousFillColor;
    private CellColor? _previousOutlineColor;
    private CellColor? _previousGradientFillEndColor;
    private WorkbookThemeColorReference? _previousFillThemeColor;
    private WorkbookThemeColorReference? _previousOutlineThemeColor;
    private bool _applied;

    public string Label => "Shape Colors";

    public SetDrawingShapeColorsCommand(
        SheetId sheetId,
        Guid shapeId,
        CellColor? fillColor,
        CellColor? outlineColor)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _fillColor = fillColor;
        _outlineColor = outlineColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        _previousFillColor = shape.FillColor;
        _previousOutlineColor = shape.OutlineColor;
        _previousGradientFillEndColor = shape.GradientFillEndColor;
        _previousFillThemeColor = shape.FillThemeColor;
        _previousOutlineThemeColor = shape.OutlineThemeColor;
        shape.FillColor = _fillColor;
        shape.OutlineColor = _outlineColor;
        shape.GradientFillEndColor = null;
        shape.FillThemeColor = null;
        shape.OutlineThemeColor = null;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.FillColor = _previousFillColor;
        shape.OutlineColor = _previousOutlineColor;
        shape.GradientFillEndColor = _previousGradientFillEndColor;
        shape.FillThemeColor = _previousFillThemeColor;
        shape.OutlineThemeColor = _previousOutlineThemeColor;
        _applied = false;
    }
}

public sealed class SetDrawingShapeGradientCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly CellColor _startColor;
    private readonly CellColor _endColor;
    private (CellColor? FillColor, CellColor? GradientEndColor, WorkbookThemeColorReference? FillThemeColor) _previous;
    private bool _applied;

    public string Label => "Shape Gradient";

    public SetDrawingShapeGradientCommand(SheetId sheetId, Guid shapeId, CellColor startColor, CellColor endColor)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _startColor = startColor;
        _endColor = endColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");
        if (shape.Kind == DrawingShapeKind.Line)
            return new CommandOutcome(false, "Line shapes do not support gradient fills.");

        _previous = (shape.FillColor, shape.GradientFillEndColor, shape.FillThemeColor);
        shape.FillColor = _startColor;
        shape.GradientFillEndColor = _endColor;
        shape.FillThemeColor = null;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.FillColor = _previous.FillColor;
        shape.GradientFillEndColor = _previous.GradientEndColor;
        shape.FillThemeColor = _previous.FillThemeColor;
        _applied = false;
    }
}

public sealed class SetDrawingShapeEffectCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly bool _hasShadowEffect;
    private bool _previous;
    private bool _applied;

    public string Label => "Shape Effects";

    public SetDrawingShapeEffectCommand(SheetId sheetId, Guid shapeId, bool hasShadowEffect)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _hasShadowEffect = hasShadowEffect;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        _previous = shape.HasShadowEffect;
        shape.HasShadowEffect = _hasShadowEffect;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.HasShadowEffect = _previous;
        _applied = false;
    }
}

internal static class DrawingShapeCommandGuards
{
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);
}
