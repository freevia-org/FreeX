using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Replaces a shape's plain text and supports undo.</summary>
public sealed class SetDrawingShapeTextCommand(SheetId sheetId, Guid shapeId, string? text) : IWorkbookCommand
{
    private string? _previousText;
    private DrawingShapeTextDocument? _previousDocument;
    private bool _applied;

    public string Label => "Edit Shape Text";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } blocked)
            return blocked;
        if (shape.IsDrawingConnector && !string.IsNullOrEmpty(text))
            return new CommandOutcome(false, "Connector shapes cannot contain text.");
        if (string.Equals(shape.ShapeText ?? "", text ?? "", StringComparison.Ordinal))
            return new CommandOutcome(true, IsNoOp: true);
        _previousText = shape.ShapeText;
        _previousDocument = shape.ShapeTextDocument;
        shape.ShapeText = text;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || !DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(sheetId), shapeId, out var shape)) return;
        shape.ShapeText = _previousText;
        shape.ShapeTextDocument = _previousDocument;
        _applied = false;
    }
}
