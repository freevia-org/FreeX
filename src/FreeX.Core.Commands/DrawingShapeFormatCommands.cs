using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetDrawingShapeColorsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly CellColor? _fillColor;
    private readonly CellColor? _outlineColor;
    private readonly bool _updateFill;
    private readonly bool _updateOutline;
    private readonly bool? _hasFill;
    private CellColor? _previousFillColor;
    private CellColor? _previousOutlineColor;
    private CellColor? _previousGradientFillEndColor;
    private DrawingShapeGradientDirection _previousGradientFillDirection;
    private WorkbookThemeColorReference? _previousFillThemeColor;
    private WorkbookThemeColorReference? _previousOutlineThemeColor;
    private bool _previousHasFill;
    private bool _previousIsSourceLoaded;
    private bool _applied;

    public string Label => "Shape Colors";

    public SetDrawingShapeColorsCommand(
        SheetId sheetId,
        Guid shapeId,
        CellColor? fillColor,
        CellColor? outlineColor,
        bool updateFill = true,
        bool updateOutline = true,
        bool? hasFill = null)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _fillColor = fillColor;
        _outlineColor = outlineColor;
        _updateFill = updateFill;
        _updateOutline = updateOutline;
        _hasFill = hasFill;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-shape Locked override so an
        // author-unlocked shape's colors stay editable even while the sheet blocks "Edit objects".
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        _previousFillColor = shape.FillColor;
        _previousOutlineColor = shape.OutlineColor;
        _previousGradientFillEndColor = shape.GradientFillEndColor;
        _previousGradientFillDirection = shape.GradientFillDirection;
        _previousFillThemeColor = shape.FillThemeColor;
        _previousOutlineThemeColor = shape.OutlineThemeColor;
        _previousHasFill = shape.HasFill;
        _previousIsSourceLoaded = shape.IsSourceLoaded;
        if (_updateFill)
        {
            shape.HasFill = _hasFill ?? (_fillColor is not null);
            shape.FillColor = _fillColor;
            shape.GradientFillEndColor = null;
            shape.GradientFillDirection = DrawingShapeGradientDirection.DiagonalDown;
            shape.FillThemeColor = null;
        }

        if (_updateOutline)
        {
            shape.OutlineColor = _outlineColor;
            shape.OutlineThemeColor = null;
        }

        // R51-io-picture-fill-shape-3-1: a shape loaded verbatim from an existing .xlsx is skipped
        // by the drawing writer (IsSupportedShape requires !IsSourceLoaded) so its ORIGINAL fill
        // XML is preserved on save via the source-package passthrough — silently discarding this
        // fill edit. Clear the flag so the writer reconstructs the shape from the (now edited)
        // model instead of round-tripping the stale source XML.
        if (_updateFill || _updateOutline)
            shape.IsSourceLoaded = false;

        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.FillColor = _previousFillColor;
        shape.OutlineColor = _previousOutlineColor;
        shape.GradientFillEndColor = _previousGradientFillEndColor;
        shape.GradientFillDirection = _previousGradientFillDirection;
        shape.FillThemeColor = _previousFillThemeColor;
        shape.OutlineThemeColor = _previousOutlineThemeColor;
        shape.HasFill = _previousHasFill;
        shape.IsSourceLoaded = _previousIsSourceLoaded;
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
    private (CellColor? FillColor, CellColor? GradientEndColor, DrawingShapeGradientDirection Direction, WorkbookThemeColorReference? FillThemeColor, bool HasFill, bool IsSourceLoaded) _previous;
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
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-shape Locked override so an
        // author-unlocked shape's gradient stays editable even while the sheet blocks "Edit objects".
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;
        if (shape.Kind == DrawingShapeKind.Line)
            return new CommandOutcome(false, "Line shapes do not support gradient fills.");

        _previous = (shape.FillColor, shape.GradientFillEndColor, shape.GradientFillDirection, shape.FillThemeColor, shape.HasFill, shape.IsSourceLoaded);
        shape.HasFill = true;
        shape.FillColor = _startColor;
        shape.GradientFillEndColor = _endColor;
        shape.GradientFillDirection = _direction;
        shape.FillThemeColor = null;
        // R51-io-picture-fill-shape-3-1: see SetDrawingShapeColorsCommand — a gradient edit on a
        // source-loaded shape must also clear IsSourceLoaded or the writer skips it and the
        // ORIGINAL fill XML silently survives the save.
        shape.IsSourceLoaded = false;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.FillColor = _previous.FillColor;
        shape.GradientFillEndColor = _previous.GradientEndColor;
        shape.GradientFillDirection = _previous.Direction;
        shape.FillThemeColor = _previous.FillThemeColor;
        shape.HasFill = _previous.HasFill;
        shape.IsSourceLoaded = _previous.IsSourceLoaded;
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
    private bool _previousIsSourceLoaded;
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
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-shape Locked override so an
        // author-unlocked shape's effects stay editable even while the sheet blocks "Edit objects".
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        _previousHasShadowEffect = shape.HasShadowEffect;
        _previousEffectPreset = shape.EffectPreset;
        _previousIsSourceLoaded = shape.IsSourceLoaded;
        shape.EffectPreset = _effectPreset;
        shape.HasShadowEffect = _effectPreset == DrawingShapeEffectPreset.Shadow;
        // R51-io-picture-fill-shape-3-1: see SetDrawingShapeColorsCommand — an effect edit on a
        // source-loaded shape must also clear IsSourceLoaded or the writer skips it and the
        // ORIGINAL effect XML silently survives the save.
        shape.IsSourceLoaded = false;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.HasShadowEffect = _previousHasShadowEffect;
        shape.EffectPreset = _previousEffectPreset;
        shape.IsSourceLoaded = _previousIsSourceLoaded;
        _applied = false;
    }
}
