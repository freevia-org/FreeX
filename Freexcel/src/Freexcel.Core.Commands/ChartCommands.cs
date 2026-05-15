using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AddChartCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly ChartModel _chart;
    private bool _added;

    public string Label => "Insert Chart";

    public AddChartCommand(
        SheetId sheetId,
        GridRange dataRange,
        ChartType type,
        string? title = null,
        double left = 20,
        double top = 20,
        double width = 400,
        double height = 300)
    {
        _sheetId = sheetId;
        _chart = new ChartModel
        {
            Type = type,
            DataRange = dataRange,
            Title = title,
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_chart.DataRange.Start.Sheet != _sheetId || _chart.DataRange.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Chart data range must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        sheet.Charts.Add(_chart);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).Charts.Remove(_chart);
        _added = false;
    }
}

public sealed record ChartLayoutOptions(
    string? Title = null,
    string? XAxisTitle = null,
    string? YAxisTitle = null,
    ChartLegendPosition? LegendPosition = null,
    bool? ShowLegend = null);

public sealed class SetChartLayoutCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _chartId;
    private readonly ChartLayoutOptions _options;
    private ChartLayoutOptions? _previous;

    public string Label => "Format Chart Layout";

    public SetChartLayoutCommand(SheetId sheetId, Guid chartId, ChartLayoutOptions options)
    {
        _sheetId = sheetId;
        _chartId = chartId;
        _options = options;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is null)
            return new CommandOutcome(false, "Chart was not found.");

        _previous = Capture(chart);
        ApplyOptions(chart, _options);
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null)
            return;

        var chart = ctx.GetSheet(_sheetId).Charts.FirstOrDefault(item => item.Id == _chartId);
        if (chart is not null)
            RestoreLayout(chart, _previous);
    }

    private static ChartLayoutOptions Capture(ChartModel chart) =>
        new(
            chart.Title,
            chart.XAxisTitle,
            chart.YAxisTitle,
            chart.LegendPosition,
            chart.ShowLegend);

    private static void ApplyOptions(ChartModel chart, ChartLayoutOptions options)
    {
        if (options.Title is not null)
            chart.Title = options.Title;
        if (options.XAxisTitle is not null)
            chart.XAxisTitle = options.XAxisTitle;
        if (options.YAxisTitle is not null)
            chart.YAxisTitle = options.YAxisTitle;
        if (options.LegendPosition is not null)
            chart.LegendPosition = options.LegendPosition.Value;
        if (options.ShowLegend is not null)
            chart.ShowLegend = options.ShowLegend.Value;
    }

    private static void RestoreLayout(ChartModel chart, ChartLayoutOptions snapshot)
    {
        chart.Title = snapshot.Title;
        chart.XAxisTitle = snapshot.XAxisTitle;
        chart.YAxisTitle = snapshot.YAxisTitle;
        chart.LegendPosition = snapshot.LegendPosition ?? ChartLegendPosition.Right;
        chart.ShowLegend = snapshot.ShowLegend ?? true;
    }
}
