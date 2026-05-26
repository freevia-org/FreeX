using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
