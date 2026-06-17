using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>Builders for constructing layout requests in tests with sensible defaults.</summary>
internal static class ChartLayoutTestData
{
    public static readonly PlotRect StandardPlot = new(0, 0, 400, 300);

    public static ChartSeriesData Series(int index, string? name, params double?[] values) =>
        new() { SeriesIndex = index, Name = name, Values = values };

    public static ChartSeriesData ScatterSeries(int index, string? name, double[] xValues, params double?[] values) =>
        new() { SeriesIndex = index, Name = name, Values = values, XValues = xValues };

    public static ChartSeriesData BubbleSeries(int index, string? name, double[] xValues, double?[] values, double?[] sizes) =>
        new() { SeriesIndex = index, Name = name, Values = values, XValues = xValues, SizeValues = sizes };

    public static ChartSeriesData StockSeries(
        int index,
        double?[] high,
        double?[] low,
        double?[] close,
        double?[]? open = null) =>
        new()
        {
            SeriesIndex = index,
            Name = "Stock",
            Values = close,
            HighValues = high,
            LowValues = low,
            OpenValues = open,
        };

    public static ChartLayoutRequest Request(
        ChartModel chart,
        IReadOnlyList<string> categories,
        IReadOnlyList<ChartSeriesData> series,
        PlotRect? plot = null,
        double widthFactor = 0.6) =>
        new()
        {
            Chart = chart,
            Categories = categories,
            Series = series,
            PlotArea = plot ?? StandardPlot,
            TextMeasurer = new FakeTextMeasurer(widthFactor),
        };

    public static ChartModel Chart(ChartType type, Action<ChartModel>? configure = null)
    {
        var chart = new ChartModel
        {
            Type = type,
            ShowLegend = false, // Tests opt into legends explicitly so the plot rect is predictable.
        };
        configure?.Invoke(chart);
        return chart;
    }
}
