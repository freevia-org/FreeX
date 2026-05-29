using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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

        var outcome = DrawingShapeCommandGuards.TryMoveZOrder(sheet, _shapeId, direction: 1, out _fromIndex, out _toIndex);
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_fromIndex < 0 || _toIndex < 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        DrawingShapeCommandGuards.SwapZOrder(sheet, _fromIndex, _toIndex);
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

        var outcome = DrawingShapeCommandGuards.TryMoveZOrder(sheet, _shapeId, direction: -1, out _fromIndex, out _toIndex);
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_fromIndex < 0 || _toIndex < 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        DrawingShapeCommandGuards.SwapZOrder(sheet, _fromIndex, _toIndex);
        _fromIndex = -1;
        _toIndex = -1;
    }
}
