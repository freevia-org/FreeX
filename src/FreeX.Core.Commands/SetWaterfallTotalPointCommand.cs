using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetWaterfallTotalPointCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly int _pointIndex;
    private readonly bool _setAsTotal;
    private List<int>? _previousTotals;
    private bool _applied;

    public string Label => "Set Waterfall Total";

    public SetWaterfallTotalPointCommand(
        SheetId sheetId,
        Guid chartId,
        int pointIndex,
        bool setAsTotal)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _pointIndex = pointIndex;
        _setAsTotal = setAsTotal;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ChartCommandGuards.TryFindChart(sheet, _chartId, out var chart))
            return ChartCommandGuards.ChartNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-chart Locked override so
        // an author-unlocked waterfall chart's total-point flags stay editable even while the sheet
        // blocks "Edit objects".
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;

        if (chart.Type != ChartType.Waterfall)
            return new CommandOutcome(false, "Set as Total is only available for waterfall charts.");

        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (_pointIndex < 0 || _pointIndex >= pointCount)
            return new CommandOutcome(false, "Waterfall point was not found.");

        _previousTotals = chart.WaterfallTotalPointIndices?.ToList();
        var totals = ResolveEffectiveTotals(chart, pointCount);
        if (_setAsTotal)
            totals.Add(_pointIndex);
        else
            totals.Remove(_pointIndex);

        chart.WaterfallTotalPointIndices = totals.ToList();
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        if (!ChartCommandGuards.TryFindChart(ctx.GetSheet(_sheetId), _chartId, out var chart))
            return;

        chart.WaterfallTotalPointIndices = _previousTotals?.ToList();
    }

    private static SortedSet<int> ResolveEffectiveTotals(ChartModel chart, int pointCount)
    {
        var totals = new SortedSet<int>();
        if (chart.WaterfallTotalPointIndices is { } authoredTotals)
        {
            foreach (var index in authoredTotals)
                if (index >= 0 && index < pointCount)
                    totals.Add(index);
        }
        else if (pointCount > 0)
        {
            totals.Add(pointCount - 1);
        }

        return totals;
    }
}
