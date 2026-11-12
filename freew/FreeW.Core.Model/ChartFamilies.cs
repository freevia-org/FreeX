namespace FreeW.Core.Model;

/// <summary>
/// Brings a chart's data into the shape its <see cref="Chart.Kind"/> needs, so a kind chosen from the
/// ribbon or the Insert Chart dialog always yields a chart that renders and serialises as that kind.
/// Idempotent, and a no-op for a chart that already has the right shape.
/// </summary>
public static class ChartKindNormalizer
{
    public const string HighSeriesName = "High";
    public const string LowSeriesName = "Low";
    public const string CloseSeriesName = "Close";
    public const string TrendSeriesName = "Trend";

    public static void Normalize(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        if (!chart.SupportsGrouping)
            chart.Grouping = ChartGrouping.Standard;
        if (chart.Kind != ChartKind.Radar)
            chart.RadarFilled = false;

        if (chart.Kind != ChartKind.Combo)
        {
            foreach (var series in chart.Series)
            {
                series.PlotAsLine = false;
                series.UseSecondaryAxis = false;
            }
        }

        if (chart.Kind != ChartKind.Bubble)
        {
            foreach (var series in chart.Series)
                series.BubbleSizes.Clear();
        }

        switch (chart.Kind)
        {
            case ChartKind.Stock:
                NormalizeStock(chart);
                break;
            case ChartKind.Combo:
                NormalizeCombo(chart);
                break;
            case ChartKind.Bubble:
                NormalizeBubble(chart);
                break;
        }
    }

    /// <summary>True when the chart can be written and drawn as a high-low-close stock chart.</summary>
    public static bool IsStockShaped(Chart chart) =>
        chart.Kind == ChartKind.Stock && chart.Series.Count == 3;

    /// <summary>True when the chart has both a column series and a line series.</summary>
    public static bool IsComboShaped(Chart chart) =>
        chart.Kind == ChartKind.Combo
        && chart.Series.Any(series => series.PlotAsLine)
        && chart.Series.Any(series => !series.PlotAsLine);

    /// <summary>The size drawn for a bubble: the stored size, or the value's magnitude when none is stored.</summary>
    public static double BubbleSizeAt(ChartSeries series, int index)
    {
        ArgumentNullException.ThrowIfNull(series);
        var size = index >= 0 && index < series.BubbleSizes.Count
            ? series.BubbleSizes[index]
            : index >= 0 && index < series.Values.Count ? Math.Abs(series.Values[index]) : 0;
        return double.IsFinite(size) && size > 0 ? size : 0;
    }

    private static void NormalizeStock(Chart chart)
    {
        if (chart.Series.Count == 3)
            return;

        if (chart.Series.Count > 3)
        {
            chart.Series.RemoveRange(3, chart.Series.Count - 3);
            return;
        }

        // Fewer than three series: derive a plausible High / Low / Close band around what is there, so the
        // user gets a working stock chart to edit rather than an empty frame.
        var basis = chart.Series.Count > 0 ? chart.Series[0].Values.ToArray() : [];
        var second = chart.Series.Count > 1 ? chart.Series[1].Values.ToArray() : null;
        var high = new ChartSeries(HighSeriesName, basis.Select((value, index) =>
            second is not null && index < second.Length ? Math.Max(value, second[index]) : value + Spread(value)));
        var low = new ChartSeries(LowSeriesName, basis.Select((value, index) =>
            second is not null && index < second.Length ? Math.Min(value, second[index]) : value - Spread(value)));
        var close = new ChartSeries(CloseSeriesName, basis.Select((value, index) =>
            second is not null && index < second.Length ? (value + second[index]) / 2 : value));
        chart.Series.Clear();
        chart.Series.Add(high);
        chart.Series.Add(low);
        chart.Series.Add(close);
    }

    private static double Spread(double value) =>
        double.IsFinite(value) && value != 0 ? Math.Abs(value) * 0.1 : 1;

    private static void NormalizeCombo(Chart chart)
    {
        if (chart.Series.Count == 0)
            return;
        // A single series becomes columns plus a line over the same data, so the chart is a real combination.
        if (chart.Series.Count == 1)
            chart.Series.Add(new ChartSeries(TrendSeriesName, chart.Series[0].Values) { PlotAsLine = true });
        if (chart.Series.All(series => series.PlotAsLine))
            chart.Series[0].PlotAsLine = false;
        if (!chart.Series.Any(series => series.PlotAsLine))
            chart.Series[^1].PlotAsLine = true;
        foreach (var series in chart.Series)
        {
            if (!series.PlotAsLine)
                series.UseSecondaryAxis = false;
        }
    }

    private static void NormalizeBubble(Chart chart)
    {
        foreach (var series in chart.Series)
        {
            if (series.BubbleSizes.Count > series.Values.Count)
                series.BubbleSizes.RemoveRange(series.Values.Count, series.BubbleSizes.Count - series.Values.Count);
            for (var index = series.BubbleSizes.Count; index < series.Values.Count; index++)
                series.BubbleSizes.Add(Math.Max(1, Math.Abs(series.Values[index])));
        }
    }
}

/// <summary>
/// The kind-dependent state of a chart (kind, grouping, radar style and the series list with each
/// series' own state), captured at Apply so a command can restore exactly what was there.
/// </summary>
internal sealed class ChartDataShapeSnapshot
{
    private ChartKind _kind;
    private ChartGrouping _grouping;
    private bool _radarFilled;
    private (ChartSeries Series, ChartSeries State)[] _series = [];

    public static ChartDataShapeSnapshot Capture(Chart chart) => new()
    {
        _kind = chart.Kind,
        _grouping = chart.Grouping,
        _radarFilled = chart.RadarFilled,
        _series = chart.Series.Select(series => (series, series.Clone())).ToArray(),
    };

    public void Restore(Chart chart)
    {
        chart.Kind = _kind;
        chart.Grouping = _grouping;
        chart.RadarFilled = _radarFilled;
        chart.Series.Clear();
        foreach (var (series, state) in _series)
        {
            series.CopyFrom(state);
            chart.Series.Add(series);
        }
    }
}
