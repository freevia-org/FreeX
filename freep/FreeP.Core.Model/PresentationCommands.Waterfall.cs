namespace FreeP.Core.Model;

/// <summary>Changes one waterfall point between increment and total semantics.</summary>
public sealed class SetWaterfallTotalPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pointIndex;
    private readonly bool _setAsTotal;
    private List<int>? _previousTotals;
    private bool _applied;

    public SetWaterfallTotalPointCommand(int slideIndex, uint shapeId, int pointIndex, bool setAsTotal)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pointIndex = pointIndex;
        _setAsTotal = setAsTotal;
    }

    public string Label => _setAsTotal ? "Set Waterfall Point as Total" : "Clear Waterfall Total";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null || chart.ChartType != ChartType.Waterfall ||
            chart.Series.Count == 0 || _pointIndex < 0 || _pointIndex >= chart.Categories.Count)
            return;

        _previousTotals = chart.WaterfallTotalPointIndices?.ToList();
        var totals = chart.WaterfallTotalPointIndices is { } authored
            ? authored.ToHashSet()
            : [];
        if (_setAsTotal)
            totals.Add(_pointIndex);
        else
            totals.Remove(_pointIndex);
        chart.WaterfallTotalPointIndices = totals.OrderBy(index => index).ToList();
        chart.RegenerateWorkbookOnSave = true;
        _applied = true;
    }

    public void Revert(Presentation p)
    {
        if (!_applied)
            return;
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null)
            return;
        chart.WaterfallTotalPointIndices = _previousTotals?.ToList();
        chart.RegenerateWorkbookOnSave = true;
    }
}
