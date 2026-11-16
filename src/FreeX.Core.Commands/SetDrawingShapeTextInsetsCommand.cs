using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets authored text insets on a drawing shape with undo support.</summary>
public sealed class SetDrawingShapeTextInsetsCommand(SheetId sheetId, Guid shapeId, DrawingShapeTextInsets? insets) : IWorkbookCommand
{
    private DrawingShapeTextInsets? _previous;
    private bool _applied;
    public string Label => "Set Shape Text Insets";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, shapeId, out var shape)) return DrawingShapeCommandGuards.DrawingShapeNotFound();
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } blocked) return blocked;
        var normalized = insets is { Left: null, Right: null, Top: null, Bottom: null } ? null : insets;
        if (shape.IsDrawingConnector && normalized is not null)
            return new CommandOutcome(false, "Connector shapes cannot contain text insets.");
        if (Equals(shape.ShapeTextInsets, normalized)) return new CommandOutcome(true, IsNoOp: true);
        _previous = shape.ShapeTextInsets;
        shape.ShapeTextInsets = normalized;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || !DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(sheetId), shapeId, out var shape)) return;
        shape.ShapeTextInsets = _previous;
        _applied = false;
    }
}
