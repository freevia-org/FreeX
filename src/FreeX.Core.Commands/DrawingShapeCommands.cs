using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        double height = 70,
        CellColor? fillColor = null,
        CellColor? outlineColor = null)
    {
        _sheetId = sheetId;
        _shape = new DrawingShapeModel
        {
            Anchor = anchor,
            Kind = kind,
            Width = width,
            Height = height,
            FillColor = DrawingShapeKindSupport.IsLineLike(kind)
                ? null
                : fillColor ?? DrawingShapeModel.DefaultFillColor,
            OutlineColor = outlineColor ?? DrawingShapeModel.DefaultOutlineColor
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_shape.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Shape anchor must be on the target sheet.");
        if (!Enum.IsDefined(_shape.Kind))
            return new CommandOutcome(false, "Drawing shape kind is not supported.");
        if (DrawingShapeCommandGuards.RejectInvalidSize(_shape.Width, _shape.Height) is { } invalidSize)
            return invalidSize;

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
