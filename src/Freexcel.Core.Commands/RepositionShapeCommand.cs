using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class RepositionShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly CellAddress _anchor;
    private CellAddress _previousAnchor;
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
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        var shape = sheet.DrawingShapes.FirstOrDefault(s => s.Id == _shapeId);
        if (shape is null) return new CommandOutcome(false, "Shape was not found.");
        _previousAnchor = shape.Anchor;
        shape.Anchor = _anchor;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [_previousAnchor, _anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(s => s.Id == _shapeId);
        if (shape is null) return;
        shape.Anchor = _previousAnchor;
        _applied = false;
    }
}
