namespace FreeP.Core.Model;

/// <summary>Atomically updates the modeled Scatter and Radar plot-style values.</summary>
public sealed class SetChartPlotStyleOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartPlotStyleOptions _newOptions;
    private ChartPlotStyleOptions? _oldOptions;

    public SetChartPlotStyleOptionsCommand(int slideIndex, uint shapeId, ChartPlotStyleOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Plot Style";

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (!Supports(chart))
            return;

        _oldOptions = ReadOptions(chart!);
        ApplyOptions(chart!, _newOptions);
        ChartHelper.MarkWorkbookDirty(chart!);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.Find(p, _slideIndex, _shapeId);
        if (!Supports(chart) || _oldOptions is null)
            return;

        ApplyOptions(chart!, _oldOptions);
        ChartHelper.MarkWorkbookDirty(chart!);
    }

    private static bool Supports(ChartShape? chart) =>
        chart is not null && chart.ChartType is ChartType.Scatter or ChartType.Radar;

    private static ChartPlotStyleOptions ReadOptions(ChartShape chart) => new(
        chart.ScatterStyle,
        chart.RadarStyle);

    private static void ApplyOptions(ChartShape chart, ChartPlotStyleOptions options)
    {
        chart.ScatterStyle = options.ScatterStyle;
        chart.RadarStyle = options.RadarStyle;
    }
}
