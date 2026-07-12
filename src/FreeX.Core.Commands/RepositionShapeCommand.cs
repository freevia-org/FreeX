using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RepositionShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly CellAddress _anchor;
    private CellAddress _previousAnchor;
    private double _previousAnchorOffsetX;
    private double _previousAnchorOffsetY;
    private bool _applied;

    public string Label => "Move Shape";

    public RepositionShapeCommand(SheetId sheetId, Guid shapeId, CellAddress anchor)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _anchor = anchor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return new CommandOutcome(false, "Shape was not found.");
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;
        _previousAnchor = shape.Anchor;
        _previousAnchorOffsetX = shape.AnchorOffsetX;
        _previousAnchorOffsetY = shape.AnchorOffsetY;
        shape.Anchor = _anchor;
        // Moving to a different anchor cell invalidates the old sub-cell pixel offset (it was
        // measured from the previous cell's origin). This command only receives the whole-cell
        // target, so snap to that cell's origin — matching Excel, which repositions the shape to
        // the new cell with no leftover fractional offset from the cell it came from.
        if (_anchor != _previousAnchor)
        {
            shape.AnchorOffsetX = 0;
            shape.AnchorOffsetY = 0;
        }
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [_previousAnchor, _anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.Anchor = _previousAnchor;
        shape.AnchorOffsetX = _previousAnchorOffsetX;
        shape.AnchorOffsetY = _previousAnchorOffsetY;
        _applied = false;
    }
}
