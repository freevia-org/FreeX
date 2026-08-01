using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class BringDrawingShapeForwardCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private IReadOnlyList<DrawingObjectZOrderEntry>? _previousOrder;
    private bool _hadExplicitOrder;

    public string Label => "Bring Forward";

    public BringDrawingShapeForwardCommand(SheetId sheetId, Guid shapeId)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-shape Locked override so an
        // author-unlocked shape stays reorderable even while the sheet blocks "Edit objects".
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        var outcome = DrawingShapeCommandGuards.TryMoveZOrder(sheet, _shapeId, direction: 1, out _previousOrder, out _hadExplicitOrder);
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousOrder is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        DrawingShapeCommandGuards.RestoreZOrder(sheet, _previousOrder, _hadExplicitOrder);
        _previousOrder = null;
        _hadExplicitOrder = false;
    }
}

public sealed class SendDrawingShapeBackwardCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private IReadOnlyList<DrawingObjectZOrderEntry>? _previousOrder;
    private bool _hadExplicitOrder;

    public string Label => "Send Backward";

    public SendDrawingShapeBackwardCommand(SheetId sheetId, Guid shapeId)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-shape Locked override so an
        // author-unlocked shape stays reorderable even while the sheet blocks "Edit objects".
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        var outcome = DrawingShapeCommandGuards.TryMoveZOrder(sheet, _shapeId, direction: -1, out _previousOrder, out _hadExplicitOrder);
        return outcome;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousOrder is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        DrawingShapeCommandGuards.RestoreZOrder(sheet, _previousOrder, _hadExplicitOrder);
        _previousOrder = null;
        _hadExplicitOrder = false;
    }
}
