using System.Globalization;
using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

/// <summary>
/// Maps a chart part's plot area onto the FreeW chart model: one chart-type element (with its grouping)
/// or a column + line combination. Anything the model cannot represent faithfully returns null, so the
/// caller keeps the whole chart verbatim instead of loading a lossy approximation.
/// </summary>
public static partial class DocxReader
{
    /// <summary>One chart-type element of a plot area and how its series map onto the model.</summary>
    private sealed record ChartPlot(XElement Element, bool SeriesAreLines, bool UsesSecondaryAxis);

    private sealed record ChartLayout(
        ChartKind Kind,
        ChartGrouping Grouping,
        bool RadarFilled,
        IReadOnlyList<ChartPlot> Plots)
    {
        /// <summary>The element chart-level settings (data labels, scatter style) are read from.</summary>
        public XElement Primary => Plots[0].Element;
    }

    private static ChartLayout? ResolveChartLayout(XElement plotArea)
    {
        var types = plotArea.Elements()
            .Where(element => element.Name.Namespace == C
                && element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal))
            .ToList();
        if (types.Count == 0)
            return null;
        if (types.Count > 1)
            return ResolveComboLayout(types);

        var type = types[0];
        ChartLayout Single(ChartKind kind, ChartGrouping grouping = ChartGrouping.Standard, bool radarFilled = false) =>
            new(kind, grouping, radarFilled, [new ChartPlot(type, SeriesAreLines: false, UsesSecondaryAxis: false)]);

        switch (type.Name.LocalName)
        {
            case "barChart":
                var direction = type.Element(C + "barDir")?.Attribute("val")?.Value;
                return Single(direction == "bar" ? ChartKind.Bar : ChartKind.Column, ReadChartGrouping(type));
            case "lineChart":
                return Single(ChartKind.Line, ReadChartGrouping(type));
            case "areaChart":
                return Single(ChartKind.Area, ReadChartGrouping(type));
            case "pieChart":
                return Single(ChartKind.Pie);
            case "doughnutChart":
                return Single(ChartKind.Doughnut);
            case "scatterChart":
                return Single(ChartKind.Scatter);
            case "radarChart":
                return Single(
                    ChartKind.Radar,
                    radarFilled: string.Equals(
                        type.Element(C + "radarStyle")?.Attribute("val")?.Value, "filled", StringComparison.Ordinal));
            case "bubbleChart":
                return Single(ChartKind.Bubble);
            case "stockChart":
                // Only high-low-close: an open-high-low-close chart (four series, c:upDownBars) and the
                // volume variants have no model here and stay preserved verbatim.
                return type.Elements(C + "ser").Count() == 3 && type.Element(C + "upDownBars") is null
                    ? Single(ChartKind.Stock)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// A plot area with several chart-type elements is modelled only as the column + line combination:
    /// exactly one clustered vertical c:barChart plus one or more unstacked c:lineChart elements. A line
    /// chart bound to different axes than the columns is on the secondary axis.
    /// </summary>
    private static ChartLayout? ResolveComboLayout(IReadOnlyList<XElement> types)
    {
        XElement? bar = null;
        var lines = new List<XElement>();
        foreach (var type in types)
        {
            switch (type.Name.LocalName)
            {
                case "barChart" when bar is null
                    && type.Element(C + "barDir")?.Attribute("val")?.Value is null or "col"
                    && ReadChartGrouping(type) == ChartGrouping.Standard:
                    bar = type;
                    break;
                case "lineChart" when ReadChartGrouping(type) == ChartGrouping.Standard:
                    lines.Add(type);
                    break;
                default:
                    return null;
            }
        }

        if (bar is null || lines.Count == 0)
            return null;

        static HashSet<string> AxisIds(XElement type) =>
            type.Elements(C + "axId")
                .Select(axis => axis.Attribute("val")?.Value ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal);

        var barAxes = AxisIds(bar);
        var plots = new List<ChartPlot> { new(bar, SeriesAreLines: false, UsesSecondaryAxis: false) };
        foreach (var line in lines)
            plots.Add(new ChartPlot(line, SeriesAreLines: true, UsesSecondaryAxis: !AxisIds(line).SetEquals(barAxes)));
        return new ChartLayout(ChartKind.Combo, ChartGrouping.Standard, RadarFilled: false, plots);
    }

    private static ChartGrouping ReadChartGrouping(XElement type) =>
        type.Element(C + "grouping")?.Attribute("val")?.Value switch
        {
            "stacked" => ChartGrouping.Stacked,
            "percentStacked" => ChartGrouping.PercentStacked,
            _ => ChartGrouping.Standard,
        };

    /// <summary>
    /// Reads the shared categories and every series of <paramref name="layout"/> into the chart. Scatter
    /// and bubble take their categories from c:xVal; combination series are merged back into c:order.
    /// </summary>
    private static void ReadChartSeries(ChartLayout layout, Chart chart)
    {
        var numericX = layout.Kind is ChartKind.Scatter or ChartKind.Bubble;
        var firstSeries = layout.Primary.Elements(C + "ser").FirstOrDefault();
        if (firstSeries is not null)
        {
            if (numericX)
            {
                foreach (var x in ReadNumberCache(firstSeries.Element(C + "xVal")))
                    chart.Categories.Add(x.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                var categories = ReadStringCache(firstSeries.Element(C + "cat"));
                // Stock and radar categories are often numeric (dates, angles) and so sit in a number cache.
                if (categories.Count == 0 && layout.Kind is ChartKind.Stock or ChartKind.Radar or ChartKind.Combo)
                {
                    categories = ReadNumberCache(firstSeries.Element(C + "cat"))
                        .Select(value => value.ToString(CultureInfo.InvariantCulture))
                        .ToList();
                }

                chart.Categories.AddRange(categories);
            }
        }

        var valueElementName = numericX ? "yVal" : "val";
        var read = new List<(int Order, int Sequence, ChartSeries Series)>();
        foreach (var plot in layout.Plots)
        {
            foreach (var ser in plot.Element.Elements(C + "ser"))
            {
                var name = ReadStringCache(ser.Element(C + "tx")).FirstOrDefault();
                var series = new ChartSeries
                {
                    Name = string.IsNullOrEmpty(name) ? null : name,
                    PlotAsLine = plot.SeriesAreLines,
                    UseSecondaryAxis = plot.SeriesAreLines && plot.UsesSecondaryAxis,
                };
                series.Values.AddRange(ReadNumberCache(ser.Element(C + valueElementName)));
                if (layout.Kind == ChartKind.Bubble)
                    series.BubbleSizes.AddRange(ReadBubbleSizes(ser.Element(C + "bubbleSize"), series.Values.Count));

                var order = int.TryParse(
                    ser.Element(C + "order")?.Attribute("val")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedOrder)
                    ? parsedOrder
                    : int.MaxValue;
                read.Add((order, read.Count, series));
            }
        }

        // Only a combination needs re-ordering (its series are split across chart-type elements); every
        // other kind keeps document order exactly as before.
        var ordered = layout.Kind == ChartKind.Combo
            ? read.OrderBy(entry => entry.Order).ThenBy(entry => entry.Sequence)
            : read.AsEnumerable();
        chart.Series.AddRange(ordered.Select(entry => entry.Series));
    }

    /// <summary>Bubble sizes come from a number cache or a literal list; missing points default to 1.</summary>
    private static List<double> ReadBubbleSizes(XElement? bubbleSize, int pointCount)
    {
        var source = bubbleSize?.Descendants(C + "numCache").FirstOrDefault()
            ?? bubbleSize?.Descendants(C + "numLit").FirstOrDefault();
        var sizes = ReadCachePoints(source)
            .Select(point => double.TryParse(point.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var size)
                // Accept range: a bubble size is a positive, bounded magnitude.
                && size >= 0 && size <= 1e15
                ? size
                : 0)
            .Take(pointCount)
            .ToList();
        while (sizes.Count < pointCount)
            sizes.Add(1);
        return sizes;
    }
}
