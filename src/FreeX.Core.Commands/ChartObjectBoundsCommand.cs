using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetChartBoundsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly double _left;
    private readonly double _top;
    private readonly double _width;
    private readonly double _height;
    private (double Left, double Top, double Width, double Height) _previousBounds;
    private bool _applied;

    public string Label => "Chart Bounds";

    public SetChartBoundsCommand(
        SheetId sheetId,
        Guid chartId,
        double left,
        double top,
        double width,
        double height)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _left = left;
        _top = top;
        _width = width;
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_left) || !double.IsFinite(_top))
            return new CommandOutcome(false, "Chart position must be finite.");
        if (ChartCommandGuards.RejectInvalidSize(_width, _height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R111-model-drawing-object-lock-1-1: layer in the per-chart Locked override so an
        // author-unlocked chart stays movable/resizable even while the sheet blocks "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;

        _previousBounds = (chart.Left, chart.Top, chart.Width, chart.Height);
        chart.Left = _left;
        chart.Top = _top;
        chart.Width = _width;
        chart.Height = _height;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.Left = _previousBounds.Left;
        chart.Top = _previousBounds.Top;
        chart.Width = _previousBounds.Width;
        chart.Height = _previousBounds.Height;
        _applied = false;
    }
}
