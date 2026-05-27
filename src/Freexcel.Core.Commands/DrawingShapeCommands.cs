using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
        double height = 70)
    {
        _sheetId = sheetId;
        _shape = new DrawingShapeModel
        {
            Anchor = anchor,
            Kind = kind,
            Width = width,
            Height = height
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_shape.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Shape anchor must be on the target sheet.");
        if (!Enum.IsDefined(_shape.Kind))
            return new CommandOutcome(false, "Drawing shape kind is not supported.");
        if (!double.IsFinite(_shape.Width) || !double.IsFinite(_shape.Height) || _shape.Width <= 0 || _shape.Height <= 0)
            return new CommandOutcome(false, "Shape size must be positive.");

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
