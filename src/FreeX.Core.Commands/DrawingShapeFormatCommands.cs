using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetDrawingShapeColorsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly CellColor? _fillColor;
    private readonly CellColor? _outlineColor;
    private CellColor? _previousFillColor;
    private CellColor? _previousOutlineColor;
    private CellColor? _previousGradientFillEndColor;
    private DrawingShapeGradientDirection _previousGradientFillDirection;
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
        _previousGradientFillDirection = shape.GradientFillDirection;
        _previousFillThemeColor = shape.FillThemeColor;
        _previousOutlineThemeColor = shape.OutlineThemeColor;
        shape.FillColor = _fillColor;
        shape.OutlineColor = _outlineColor;
        shape.GradientFillEndColor = null;
        shape.GradientFillDirection = DrawingShapeGradientDirection.DiagonalDown;
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
        shape.GradientFillDirection = _previousGradientFillDirection;
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
    private readonly DrawingShapeGradientDirection _direction;
    private (CellColor? FillColor, CellColor? GradientEndColor, DrawingShapeGradientDirection Direction, WorkbookThemeColorReference? FillThemeColor) _previous;
    private bool _applied;

    public string Label => "Shape Gradient";

    public SetDrawingShapeGradientCommand(SheetId sheetId, Guid shapeId, CellColor startColor, CellColor endColor)
        : this(sheetId, shapeId, startColor, endColor, DrawingShapeGradientDirection.DiagonalDown)
    {
    }

    public SetDrawingShapeGradientCommand(
        SheetId sheetId,
        Guid shapeId,
        CellColor startColor,
        CellColor endColor,
        DrawingShapeGradientDirection direction)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _startColor = startColor;
        _endColor = endColor;
        _direction = direction;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_direction))
            return new CommandOutcome(false, "Drawing shape gradient direction is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");
        if (shape.Kind == DrawingShapeKind.Line)
            return new CommandOutcome(false, "Line shapes do not support gradient fills.");

        _previous = (shape.FillColor, shape.GradientFillEndColor, shape.GradientFillDirection, shape.FillThemeColor);
        shape.FillColor = _startColor;
        shape.GradientFillEndColor = _endColor;
        shape.GradientFillDirection = _direction;
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
        shape.GradientFillDirection = _previous.Direction;
        shape.FillThemeColor = _previous.FillThemeColor;
        _applied = false;
    }
}

public sealed class SetDrawingShapeEffectCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly DrawingShapeEffectPreset _effectPreset;
    private bool _previousHasShadowEffect;
    private DrawingShapeEffectPreset _previousEffectPreset;
    private bool _applied;

    public string Label => "Shape Effects";

    public SetDrawingShapeEffectCommand(SheetId sheetId, Guid shapeId, bool hasShadowEffect)
        : this(
            sheetId,
            shapeId,
            hasShadowEffect ? DrawingShapeEffectPreset.Shadow : DrawingShapeEffectPreset.None)
    {
    }

    public SetDrawingShapeEffectCommand(
        SheetId sheetId,
        Guid shapeId,
        DrawingShapeEffectPreset effectPreset)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _effectPreset = effectPreset;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_effectPreset))
            return new CommandOutcome(false, "Drawing shape effect preset is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        _previousHasShadowEffect = shape.HasShadowEffect;
        _previousEffectPreset = shape.EffectPreset;
        shape.EffectPreset = _effectPreset;
        shape.HasShadowEffect = _effectPreset == DrawingShapeEffectPreset.Shadow;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.HasShadowEffect = _previousHasShadowEffect;
        shape.EffectPreset = _previousEffectPreset;
        _applied = false;
    }
}
