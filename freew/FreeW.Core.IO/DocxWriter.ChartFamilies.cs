using System.Globalization;
using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

/// <summary>
/// Chart families beyond the single clustered plot: stacked / 100% stacked grouping, radar, bubble,
/// high-low-close stock and column + line combination charts (with an optional secondary value axis).
/// </summary>
public static partial class DocxWriter
{
    private const long SecondaryCatAxisId = 333333333L;
    private const long SecondaryValAxisId = 444444444L;

    /// <summary>
    /// The kind a chart is actually serialised as. A stock chart without exactly High/Low/Close series is
    /// written as a line chart and a combo without both a column and a line series as whichever it has, so
    /// the part is always valid chart XML that Word opens.
    /// </summary>
    private static ChartKind SerializedChartKind(Chart chart) => chart.Kind switch
    {
        ChartKind.Stock when !ChartKindNormalizer.IsStockShaped(chart) => ChartKind.Line,
        ChartKind.Combo when !ChartKindNormalizer.IsComboShaped(chart) =>
            chart.Series.Count > 0 && chart.Series.All(series => series.PlotAsLine) ? ChartKind.Line : ChartKind.Column,
        _ => chart.Kind,
    };

    private static bool ChartUsesSecondaryAxis(Chart chart, ChartKind serializedKind) =>
        serializedKind == ChartKind.Combo
        && chart.Series.Any(series => series.PlotAsLine && series.UseSecondaryAxis);

    private static int[] AllSeriesIndexes(Chart chart) => Enumerable.Range(0, chart.Series.Count).ToArray();

    /// <summary>The chart-type element(s) of the plot area for <paramref name="kind"/>.</summary>
    private static IReadOnlyList<XElement> BuildPlotCharts(Chart chart, ChartKind kind, long catAxisId, long valAxisId) => kind switch
    {
        ChartKind.Pie => [BuildPieChart(chart, doughnut: false)],
        ChartKind.Doughnut => [BuildPieChart(chart, doughnut: true)],
        ChartKind.Scatter => [BuildScatterChart(chart, catAxisId, valAxisId)],
        ChartKind.Radar => [BuildRadarChart(chart, catAxisId, valAxisId)],
        ChartKind.Bubble => [BuildBubbleChart(chart, catAxisId, valAxisId)],
        ChartKind.Stock => [BuildStockChart(chart, catAxisId, valAxisId)],
        ChartKind.Combo => BuildComboCharts(chart, catAxisId, valAxisId),
        _ => [BuildGroupedCartesianChart(
            chart,
            kind,
            kind == chart.Kind ? chart.EffectiveGrouping : ChartGrouping.Standard,
            AllSeriesIndexes(chart),
            catAxisId,
            valAxisId)],
    };

    /// <summary>
    /// Builds a c:barChart (column or bar), c:lineChart or c:areaChart holding the given series, with the
    /// chart's grouping. Stacked bars also carry c:overlap 100 so the series sit on top of each other.
    /// </summary>
    private static XElement BuildGroupedCartesianChart(
        Chart chart,
        ChartKind kind,
        ChartGrouping grouping,
        IReadOnlyList<int> seriesIndexes,
        long catAxisId,
        long valAxisId)
    {
        var stacked = grouping switch
        {
            ChartGrouping.Stacked => "stacked",
            ChartGrouping.PercentStacked => "percentStacked",
            _ => null,
        };

        XElement root;
        if (kind == ChartKind.Line)
        {
            root = new XElement(C + "lineChart",
                new XElement(C + "grouping", new XAttribute("val", stacked ?? "standard")));
        }
        else if (kind == ChartKind.Area)
        {
            root = new XElement(C + "areaChart",
                new XElement(C + "grouping", new XAttribute("val", stacked ?? "standard")));
        }
        else
        {
            root = new XElement(C + "barChart",
                new XElement(C + "barDir", new XAttribute("val", kind == ChartKind.Bar ? "bar" : "col")),
                new XElement(C + "grouping", new XAttribute("val", stacked ?? "clustered")));
        }

        foreach (var index in seriesIndexes)
            root.Add(BuildSeries(chart, chart.Series[index], index));

        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());

        if (stacked is not null && kind is not (ChartKind.Line or ChartKind.Area))
            root.Add(new XElement(C + "overlap", new XAttribute("val", "100")));

        root.Add(new XElement(C + "axId", new XAttribute("val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", valAxisId)));
        return root;
    }

    /// <summary>Builds the c:radarChart: one category/value series per polygon, on a category + value axis pair.</summary>
    private static XElement BuildRadarChart(Chart chart, long catAxisId, long valAxisId)
    {
        var root = new XElement(C + "radarChart",
            new XElement(C + "radarStyle", new XAttribute("val", chart.RadarFilled ? "filled" : "marker")),
            new XElement(C + "varyColors", new XAttribute("val", "0")));
        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildSeries(chart, chart.Series[i], i));
        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());
        root.Add(new XElement(C + "axId", new XAttribute("val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", valAxisId)));
        return root;
    }

    /// <summary>
    /// Builds the c:bubbleChart: each series carries c:xVal / c:yVal like a scatter series plus a literal
    /// c:bubbleSize (a c:numLit, since sizes have no column in the companion workbook).
    /// </summary>
    private static XElement BuildBubbleChart(Chart chart, long xAxisId, long yAxisId)
    {
        var root = new XElement(C + "bubbleChart",
            new XElement(C + "varyColors", new XAttribute("val", "0")));
        for (var i = 0; i < chart.Series.Count; i++)
        {
            var series = chart.Series[i];
            var ser = BuildScatterSeries(chart, series, i);
            var sizes = new XElement(C + "numLit",
                new XElement(C + "formatCode", "General"),
                new XElement(C + "ptCount", new XAttribute("val", series.Values.Count)));
            for (var point = 0; point < series.Values.Count; point++)
            {
                sizes.Add(new XElement(C + "pt", new XAttribute("idx", point),
                    new XElement(C + "v", ChartKindNormalizer.BubbleSizeAt(series, point).ToString(CultureInfo.InvariantCulture))));
            }

            ser.Add(new XElement(C + "bubbleSize", sizes));
            ser.Add(new XElement(C + "bubble3D", new XAttribute("val", "0")));
            root.Add(ser);
        }

        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());
        root.Add(new XElement(C + "bubbleScale", new XAttribute("val", "100")));
        root.Add(new XElement(C + "showNegBubbles", new XAttribute("val", "0")));
        root.Add(new XElement(C + "axId", new XAttribute("val", xAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", yAxisId)));
        return root;
    }

    /// <summary>
    /// Builds a high-low-close c:stockChart: three line series (High, Low, Close) drawn without connecting
    /// lines, joined per category by c:hiLowLines, with a dot marking the close.
    /// </summary>
    private static XElement BuildStockChart(Chart chart, long catAxisId, long valAxisId)
    {
        var root = new XElement(C + "stockChart");
        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildLineSeries(chart, i, connectPoints: false, markerSymbol: i == 2 ? "dot" : "none"));
        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());
        root.Add(new XElement(C + "hiLowLines"));
        root.Add(new XElement(C + "axId", new XAttribute("val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", valAxisId)));
        return root;
    }

    /// <summary>
    /// Builds a column + line combination: a clustered c:barChart for the column series, a c:lineChart for
    /// the line series on the primary axes and, when any line series uses it, a second c:lineChart bound
    /// to the secondary axes. Series keep their model index as c:idx / c:order so the model order survives.
    /// </summary>
    private static IReadOnlyList<XElement> BuildComboCharts(Chart chart, long catAxisId, long valAxisId)
    {
        var columns = new List<int>();
        var primaryLines = new List<int>();
        var secondaryLines = new List<int>();
        for (var i = 0; i < chart.Series.Count; i++)
        {
            var series = chart.Series[i];
            if (!series.PlotAsLine)
                columns.Add(i);
            else if (series.UseSecondaryAxis)
                secondaryLines.Add(i);
            else
                primaryLines.Add(i);
        }

        var charts = new List<XElement>
        {
            BuildGroupedCartesianChart(chart, ChartKind.Column, ChartGrouping.Standard, columns, catAxisId, valAxisId),
        };
        if (primaryLines.Count > 0)
            charts.Add(BuildComboLineChart(chart, primaryLines, catAxisId, valAxisId));
        if (secondaryLines.Count > 0)
            charts.Add(BuildComboLineChart(chart, secondaryLines, SecondaryCatAxisId, SecondaryValAxisId));
        return charts;
    }

    private static XElement BuildComboLineChart(Chart chart, IReadOnlyList<int> seriesIndexes, long catAxisId, long valAxisId)
    {
        var root = new XElement(C + "lineChart",
            new XElement(C + "grouping", new XAttribute("val", "standard")));
        foreach (var index in seriesIndexes)
            root.Add(BuildLineSeries(chart, index, connectPoints: true, markerSymbol: "none"));
        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());
        root.Add(new XElement(C + "axId", new XAttribute("val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", valAxisId)));
        return root;
    }

    /// <summary>
    /// A line-type c:ser (idx, order, tx, spPr, marker, cat, val, smooth) whose stroke is either the
    /// series' palette colour or, for the unconnected stock series, no line at all.
    /// </summary>
    private static XElement BuildLineSeries(Chart chart, int index, bool connectPoints, string markerSymbol)
    {
        var series = chart.Series[index];
        var ser = new XElement(C + "ser",
            new XElement(C + "idx", new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));
        ser.Add(BuildSeriesName(series, index));

        if (!connectPoints)
        {
            ser.Add(new XElement(C + "spPr",
                new XElement(A + "ln", new XAttribute("w", 19050), new XElement(A + "noFill"))));
        }
        else if (ChartSeriesColor(chart, index) is { } color)
        {
            ser.Add(new XElement(C + "spPr",
                new XElement(A + "ln", new XAttribute("w", 28575), new XAttribute("cap", "rnd"),
                    new XElement(A + "solidFill",
                        new XElement(A + "srgbClr", new XAttribute("val", color))))));
        }

        var marker = new XElement(C + "marker", new XElement(C + "symbol", new XAttribute("val", markerSymbol)));
        if (markerSymbol != "none")
            marker.Add(new XElement(C + "size", new XAttribute("val", 3)));
        ser.Add(marker);
        ser.Add(BuildCategoryCache(chart.Categories));
        ser.Add(BuildValueCache(series.Values, index));
        ser.Add(new XElement(C + "smooth", new XAttribute("val", "0")));
        return ser;
    }

    private static string? ChartSeriesColor(Chart chart, int index)
    {
        var schemeId = chart.ColorSchemeId?.Trim();
        var scheme = string.IsNullOrEmpty(schemeId) ? null : ChartColorScheme.FindById(schemeId);
        return scheme is null || scheme.Colors.Count == 0
            ? null
            : scheme.Colors[index % scheme.Colors.Count].Trim().TrimStart('#');
    }

    /// <summary>
    /// The secondary axis pair of a combination chart: a value axis on the right that crosses the (hidden)
    /// secondary category axis at its maximum, as Word and Excel write it.
    /// </summary>
    private static IReadOnlyList<XElement> BuildSecondaryAxes() =>
    [
        new XElement(C + "valAx",
            new XElement(C + "axId", new XAttribute("val", SecondaryValAxisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute("val", "minMax"))),
            new XElement(C + "delete", new XAttribute("val", "0")),
            new XElement(C + "axPos", new XAttribute("val", "r")),
            new XElement(C + "crossAx", new XAttribute("val", SecondaryCatAxisId)),
            new XElement(C + "crosses", new XAttribute("val", "max"))),
        new XElement(C + "catAx",
            new XElement(C + "axId", new XAttribute("val", SecondaryCatAxisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute("val", "minMax"))),
            new XElement(C + "delete", new XAttribute("val", "1")),
            new XElement(C + "axPos", new XAttribute("val", "b")),
            new XElement(C + "crossAx", new XAttribute("val", SecondaryValAxisId))),
    ];
}
