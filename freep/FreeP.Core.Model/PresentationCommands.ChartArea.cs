namespace FreeP.Core.Model;

/// <summary>Undoable chart-area or plot-area fill and outline update.</summary>
public sealed class SetChartAreaOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartAreaOptions _newOptions;
    private ChartAreaOptions? _oldOptions;
    private bool _oldChartExChartAreaEditRequested;
    private bool _oldChartExPlotAreaEditRequested;

    public SetChartAreaOptionsCommand(int slideIndex, uint shapeId, ChartAreaOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Area Options";

    // The captured fill can be ShapeFill.Picture (image fill on the chart/plot area) holding raw
    // bytes, the same risk class as SetChartSeriesOptionsCommand's fill.
    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(new[]
    {
        PresentationCommandSizeEstimator.EstimateBytes(_newOptions.Fill),
        PresentationCommandSizeEstimator.EstimateBytes(_oldOptions?.Fill),
    });

    public void Apply(Presentation presentation)
    {
        if (!TryGetChart(presentation, out var chart)) return;
        if (_oldOptions is null)
        {
            _oldOptions = ReadOptions(chart, _newOptions.Target);
            _oldChartExChartAreaEditRequested = chart.ChartExChartAreaEditRequested;
            _oldChartExPlotAreaEditRequested = chart.ChartExPlotAreaEditRequested;
        }
        Apply(chart, _newOptions);
    }

    public void Revert(Presentation presentation)
    {
        if (!TryGetChart(presentation, out var chart) || _oldOptions is null) return;
        Apply(chart, _oldOptions);
        chart.ChartExChartAreaEditRequested = _oldChartExChartAreaEditRequested;
        chart.ChartExPlotAreaEditRequested = _oldChartExPlotAreaEditRequested;
    }

    private bool TryGetChart(Presentation presentation, out ChartShape chart)
    {
        chart = null!;
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count) return false;
        var found = ShapeHelper.Find(presentation, _slideIndex, _shapeId)?.Chart;
        if (found is null || !ChartHelper.IsFormattingEditable(found)) return false;
        chart = found;
        return true;
    }

    private static ChartAreaOptions ReadOptions(ChartShape chart, ChartAreaFormattingTarget target) =>
        target == ChartAreaFormattingTarget.ChartArea
            ? new(target, chart.ChartAreaFill, chart.ChartAreaOutline)
            : new(target, chart.PlotAreaFill, chart.PlotAreaOutline);

    private static void Apply(ChartShape chart, ChartAreaOptions options)
    {
        if (options.Target == ChartAreaFormattingTarget.ChartArea)
        {
            chart.ChartAreaFill = options.Fill;
            chart.ChartAreaOutline = options.Outline;
            if (chart.IsChartEx)
                chart.ChartExChartAreaEditRequested = true;
        }
        else
        {
            chart.PlotAreaFill = options.Fill;
            chart.PlotAreaOutline = options.Outline;
            if (chart.IsChartEx)
                chart.ChartExPlotAreaEditRequested = true;
        }
    }
}
