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
    /// <summary>A series' trendline (<see cref="ChartElementRef.SeriesIndex"/> = the fitted series).</summary>
    Trendline,
    /// <summary>A series' error bars.</summary>
    ErrorBars,
    /// <summary>One data point of a series (<see cref="ChartElementRef.PointIndex"/>); reached by a second click on the selected series.</summary>
    DataPoint,
    /// <summary>One legend entry (series, or category for pie charts); reached by a second click on the selected legend.</summary>
    LegendEntry,
    SecondaryAxisTitle,
}

/// <summary>A selected chart element: its kind plus the series it belongs to (-1 when not series-scoped).</summary>
public readonly record struct ChartElementRef(ChartElementKind Kind, int SeriesIndex = -1)
{
    /// <summary>The data point for <see cref="ChartElementKind.DataPoint"/> (-1 otherwise).</summary>
    public int PointIndex { get; init; } = -1;

    public static ChartElementRef ChartArea => new(ChartElementKind.ChartArea);

    public static ChartElementRef ForSeries(int seriesIndex) => new(ChartElementKind.Series, seriesIndex);

    public static ChartElementRef ForDataLabels(int seriesIndex) => new(ChartElementKind.DataLabels, seriesIndex);

    public static ChartElementRef ForTrendline(int seriesIndex) => new(ChartElementKind.Trendline, seriesIndex);

    public static ChartElementRef ForErrorBars(int seriesIndex) => new(ChartElementKind.ErrorBars, seriesIndex);

    public static ChartElementRef ForDataPoint(int seriesIndex, int pointIndex) =>
        new(ChartElementKind.DataPoint, seriesIndex) { PointIndex = pointIndex };

    /// <summary>A legend entry; <paramref name="entryIndex"/> is the series index (the point index for pie charts).</summary>
    public static ChartElementRef ForLegendEntry(int entryIndex) => new(ChartElementKind.LegendEntry, entryIndex);

    /// <summary>True for the whole-chart selection (no inner element picked).</summary>
    public bool IsChartArea => Kind == ChartElementKind.ChartArea;
}
