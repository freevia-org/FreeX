using FreeX.Core.Model;

namespace FreeX.Core.Commands;

// R129-model-drawing-nudge-1: keyboard arrow-key nudge for a genuinely selected picture/shape/text
// box/chart, matching Excel ("with an object selected, arrow keys move it; Ctrl+arrow moves it by a
// smaller increment"). Deliberately does NOT touch the object's anchor cell -- pictures/shapes/text
// boxes already render at `column.LeftOffset + AnchorOffsetX` / `row.TopOffset + AnchorOffsetY` with
// no clamp to the anchor cell's own width/height (see DrawingObjectViewportPlanner
// .TryCreateAnchoredObjectRect), so simply accumulating the pixel delta onto the existing offset is
// sufficient and keeps every guard/undo path identical to the existing Reposition*Command family.
// Charts have no anchor/offset pair at all -- they're positioned by absolute Left/Top -- so
// NudgeChartCommand adds the delta directly to those.

public sealed class NudgePictureCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly double _deltaX;
    private readonly double _deltaY;
    private double _previousOffsetX;
    private double _previousOffsetY;
    private bool _applied;

    public string Label => "Move Picture";

    public NudgePictureCommand(SheetId sheetId, Guid pictureId, double deltaX, double deltaY)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _deltaX = deltaX;
        _deltaY = deltaY;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;

        _previousOffsetX = picture.AnchorOffsetX;
        _previousOffsetY = picture.AnchorOffsetY;
        picture.AnchorOffsetX += _deltaX;
        picture.AnchorOffsetY += _deltaY;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!PictureCommandGuards.TryFindPicture(ctx.GetSheet(_sheetId), _pictureId, out var picture)) return;
        picture.AnchorOffsetX = _previousOffsetX;
        picture.AnchorOffsetY = _previousOffsetY;
        _applied = false;
    }
}

public sealed class NudgeDrawingShapeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly double _deltaX;
    private readonly double _deltaY;
    private double _previousOffsetX;
    private double _previousOffsetY;
    private bool _applied;

    public string Label => "Move Shape";

    public NudgeDrawingShapeCommand(SheetId sheetId, Guid shapeId, double deltaX, double deltaY)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _deltaX = deltaX;
        _deltaY = deltaY;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return new CommandOutcome(false, "Shape was not found.");
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        _previousOffsetX = shape.AnchorOffsetX;
        _previousOffsetY = shape.AnchorOffsetY;
        shape.AnchorOffsetX += _deltaX;
        shape.AnchorOffsetY += _deltaY;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.AnchorOffsetX = _previousOffsetX;
        shape.AnchorOffsetY = _previousOffsetY;
        _applied = false;
    }
}

public sealed class NudgeTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly double _deltaX;
    private readonly double _deltaY;
    private double _previousOffsetX;
    private double _previousOffsetY;
    private bool _applied;

    public string Label => "Move Text Box";

    public NudgeTextBoxCommand(SheetId sheetId, Guid textBoxId, double deltaX, double deltaY)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _deltaX = deltaX;
        _deltaY = deltaY;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        _previousOffsetX = textBox.AnchorOffsetX;
        _previousOffsetY = textBox.AnchorOffsetY;
        textBox.AnchorOffsetX += _deltaX;
        textBox.AnchorOffsetY += _deltaY;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!TextBoxCommandGuards.TryFindTextBox(ctx.GetSheet(_sheetId), _textBoxId, out var textBox)) return;
        textBox.AnchorOffsetX = _previousOffsetX;
        textBox.AnchorOffsetY = _previousOffsetY;
        _applied = false;
    }
}

public sealed class NudgeChartCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly double _deltaX;
    private readonly double _deltaY;
    private double _previousLeft;
    private double _previousTop;
    private bool _applied;

    public string Label => "Chart Bounds";

    public NudgeChartCommand(SheetId sheetId, Guid chartId, double deltaX, double deltaY)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _deltaX = deltaX;
        _deltaY = deltaY;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;

        _previousLeft = chart.Left;
        _previousTop = chart.Top;
        chart.Left = Math.Max(0, chart.Left + _deltaX);
        chart.Top = Math.Max(0, chart.Top + _deltaY);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: []);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart)) return;
        chart.Left = _previousLeft;
        chart.Top = _previousTop;
        _applied = false;
    }
}
