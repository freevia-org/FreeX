namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>
/// The individually selectable parts of a chart, mirroring the elements Excel lets a user click
/// (Chart Area, Plot Area, Chart Title, Legend, axes, axis titles, gridlines, data series, data
/// labels). Only parts the <see cref="FreeX.Core.Model.ChartModel"/> can actually represent appear
/// here.
/// </summary>
public enum ChartElementKind
{
    /// <summary>The whole chart object (the default selection; Delete removes the chart itself).</summary>
    ChartArea = 0,
    PlotArea,
    ChartTitle,
    Legend,
    CategoryAxis,
    ValueAxis,
    SecondaryValueAxis,
    CategoryAxisTitle,
    ValueAxisTitle,
    CategoryGridlines,
    ValueGridlines,
    Series,
    DataLabels,
}

/// <summary>A selected chart element: its kind plus the series it belongs to (-1 when not series-scoped).</summary>
public readonly record struct ChartElementRef(ChartElementKind Kind, int SeriesIndex = -1)
{
    public static ChartElementRef ChartArea => new(ChartElementKind.ChartArea);

    public static ChartElementRef ForSeries(int seriesIndex) => new(ChartElementKind.Series, seriesIndex);

    public static ChartElementRef ForDataLabels(int seriesIndex) => new(ChartElementKind.DataLabels, seriesIndex);

    /// <summary>True for the whole-chart selection (no inner element picked).</summary>
    public bool IsChartArea => Kind == ChartElementKind.ChartArea;
}
