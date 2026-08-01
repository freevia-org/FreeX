using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";

    public static string GetContentType(ChartModel chart) =>
        ChartTypeSupport.IsChartExFamily(chart.Type) ? ChartExContentType : ChartContentType;

    public static string GetRelationshipType(ChartModel chart) =>
        ChartTypeSupport.IsChartExFamily(chart.Type) ? ChartExRelationshipType : ChartRelationshipType;

    private static XDocument ToChartExXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var chartData = BuildChartExData(chart, sheet, chartExNs).ToList();

        return new XDocument(
            new XElement(chartExNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XElement(chartExNs + "chartData", chartData),
                new XElement(chartExNs + "chart",
                    // chartEx (cx:) has its own title/legend shapes — distinct from the classic c:
                    // schema — so it must not reuse the classic builders.
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartExTitleXml(chart, chartExNs, drawingNs),
                    new XElement(chartExNs + "plotArea",
                        new XElement(chartExNs + "plotAreaRegion",
                            BuildChartExSeries(chart, sheet, chartExNs, chartData.Count)),
                        BuildChartExPlotAreaAxes(chart, chartExNs)),
                    ToChartExLegendXml(chart, chartExNs))));
    }

    // CT_Title (chartEx): a tx with a rich text body (a:bodyPr must come first per CT_TextBody).
    private static XElement ToChartExTitleXml(ChartModel chart, XNamespace chartExNs, XNamespace drawingNs) =>
        new(chartExNs + "title",
            new XElement(chartExNs + "tx",
                new XElement(chartExNs + "rich",
                    new XElement(drawingNs + "bodyPr"),
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            new XElement(drawingNs + "t", chart.Title))))));

    // CT_Legend (chartEx): position is the "pos" attribute (t/b/l/r), not a legendPos child element.
    private static XElement? ToChartExLegendXml(ChartModel chart, XNamespace chartExNs)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return null;

        return new XElement(chartExNs + "legend",
            new XAttribute("pos", ToChartExLegendPosition(chart.LegendPosition)),
            new XAttribute("align", "ctr"),
            new XAttribute("overlay", chart.LegendOverlay ? "1" : "0"));
    }

    private static string ToChartExLegendPosition(ChartLegendPosition position) =>
        position switch
        {
            ChartLegendPosition.Top => "t",
            ChartLegendPosition.Bottom => "b",
            ChartLegendPosition.Left => "l",
            ChartLegendPosition.Right => "r",
            _ => "b",
        };

    private static IEnumerable<XElement> BuildChartExData(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartExNs)
    {
        // R112-io-chartex-verbatim-series-3: when every series in this chart was loaded through
        // XlsxChartPartReader.Deferred.cs's "every series' formula is an unresolvable named range"
        // fallback (see BuildChartExVerbatimSeriesFormulas there), chart.DataRange is a synthetic
        // 1x1 placeholder that carries no real strip span at all. Falling through to the
        // GetSeriesStripLayout-driven loop below would collapse to a single (or otherwise wrong)
        // count of <cx:data> elements cut from that placeholder, silently rewriting or dropping
        // the user's named-range series on save — the exact defect this branch exists to prevent.
        // TryReadDeferredAdvancedChart is the ONLY reader that ever produces a chartEx-family
        // ChartModel, so any VerbatimSeriesFormulas present on one is guaranteed to be this
        // whole-chart chartEx-native capture (never the classic per-series partial capture used by
        // the non-chartEx families), making this check safe and exhaustive.
        if (chart.VerbatimSeriesFormulas is { Count: > 0 } verbatimSeries && ChartTypeSupport.IsChartExFamily(chart.Type))
        {
            foreach (var entry in verbatimSeries.OrderBy(e => e.SeriesIndex))
            {
                var categoryCache = entry.CatCacheXml is { Length: > 0 } catCacheXml ? TryParseChartXml(catCacheXml) : null;
                var valueCache = entry.ValCacheXml is { Length: > 0 } valCacheXml ? TryParseChartXml(valCacheXml) : null;

                yield return new XElement(chartExNs + "data",
                    new XAttribute("id", ToChartExDataId(entry.SeriesIndex)),
                    string.IsNullOrEmpty(entry.CatFormula)
                        ? null
                        : new XElement(chartExNs + "strDim",
                            new XAttribute("type", "cat"),
                            new XElement(chartExNs + "f", entry.CatFormula),
                            categoryCache),
                    new XElement(chartExNs + "numDim",
                        new XAttribute("type", ToChartExNumericDimensionType(chart.Type)),
                        new XElement(chartExNs + "f", entry.ValFormula),
                        string.IsNullOrEmpty(entry.TxFormula)
                            ? null
                            : new XElement(chartExNs + "nf", entry.TxFormula),
                        valueCache));
            }

            yield break;
        }

        var layout = GetSeriesStripLayout(chart);
        var hasCategoryStrip = chart.FirstColIsCategories && layout.LastStrip > layout.CategoryStrip;
        var firstValueStrip = hasCategoryStrip ? layout.CategoryStrip + 1 : layout.CategoryStrip;
        var categoryRange = hasCategoryStrip
            ? FormatStripRange(layout, sheet.Name, layout.CategoryStrip)
            : null;

        var seriesIndex = 0;
        for (var strip = firstValueStrip; strip <= layout.LastStrip; strip++)
        {
            var valueRange = FormatStripRange(layout, sheet.Name, strip);
            yield return new XElement(chartExNs + "data",
                new XAttribute("id", ToChartExDataId(seriesIndex)),
                string.IsNullOrWhiteSpace(categoryRange)
                    ? null
                    : new XElement(chartExNs + "strDim",
                        new XAttribute("type", "cat"),
                        new XElement(chartExNs + "f", categoryRange)),
                new XElement(chartExNs + "numDim",
                    new XAttribute("type", ToChartExNumericDimensionType(chart.Type)),
                    new XElement(chartExNs + "f", valueRange),
                    chart.FirstRowIsHeader
                        ? new XElement(chartExNs + "nf",
                            FormatStripHeaderCell(layout, sheet.Name, strip))
                        : null));
            seriesIndex++;
        }
    }

    private static IEnumerable<XElement> BuildChartExSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartExNs,
        int dataCount)
    {
        for (var seriesIndex = 0; seriesIndex < dataCount; seriesIndex++)
        {
            var dataId = ToChartExDataId(seriesIndex);
            // Per CT_Series the optional layoutPr (binning / subtotals) follows dataId.
            yield return new XElement(chartExNs + "series",
                new XAttribute("layoutId", ToChartExSeriesLayoutId(chart.Type)),
                ToChartExSeriesUniqueIdAttribute(chart, seriesIndex),
                ToChartExSeriesTitleXml(chart, sheet, seriesIndex, chartExNs),
                new XElement(chartExNs + "dataId", new XAttribute("val", dataId)),
                BuildChartExSeriesLayoutPr(chart, chartExNs));

            if (chart.Type == ChartType.Pareto)
            {
                yield return new XElement(chartExNs + "series",
                    new XAttribute("layoutId", "paretoLine"),
                    new XAttribute("ownerIdx", seriesIndex.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    /// <summary>
    /// Optional per-series layout properties for chartEx families. Histogram emits Excel's default
    /// empty binning element so desktop Excel treats the data as bins, and Pareto emits Excel's
    /// aggregation marker so the primary column series is sorted/grouped by value. BoxAndWhisker
    /// emits <c>cx:statistics/@quartileMethod</c> from <see cref="ChartModel.QuartileMethod"/>,
    /// defaulting to "exclusive" when unset. Custom
    /// <c>cx:binCount</c> and <c>cx:binSize</c> values remain intentionally suppressed because Excel
    /// rejects otherwise valid chartEx packages that contain them.
    /// </summary>
    private static XElement? BuildChartExSeriesLayoutPr(ChartModel chart, XNamespace chartExNs)
    {
        if (chart.Type == ChartType.Histogram)
        {
            return new XElement(chartExNs + "layoutPr",
                new XElement(chartExNs + "binning", new XAttribute("intervalClosed", "r")));
        }

        if (chart.Type == ChartType.Pareto)
            return new XElement(chartExNs + "layoutPr", new XElement(chartExNs + "aggregation"));

        if (chart.Type == ChartType.BoxAndWhisker)
        {
            var quartileMethod = chart.QuartileMethod ?? "exclusive";
            return new XElement(chartExNs + "layoutPr",
                new XElement(chartExNs + "statistics", new XAttribute("quartileMethod", quartileMethod)));
        }

        if (chart.Type == ChartType.Waterfall)
        {
            // Mirrors the WPF renderer's own gate (ChartRenderer.WaterfallHistogram.cs:
            // `chart.ShowSeriesLines ? CreateSeriesLineConnectorSeries(...) : null`), so a chart with
            // connector lines turned off doesn't come back with them turned on after a save/reopen.
            var connectorLines = chart.ShowSeriesLines ? "1" : "0";
            return new XElement(chartExNs + "layoutPr",
                new XElement(chartExNs + "visibility", new XAttribute("connectorLines", connectorLines)),
                BuildChartExSubtotals(chart, chartExNs));
        }

        var subtotals = BuildChartExSubtotals(chart, chartExNs);
        return subtotals is null
            ? null
            : new XElement(chartExNs + "layoutPr", subtotals);
    }

    private static IEnumerable<XElement> BuildChartExPlotAreaAxes(ChartModel chart, XNamespace chartExNs)
    {
        // Treemap and Sunburst use hierarchical layout with no explicit axes in the chartEx schema.
        if (chart.Type is ChartType.Treemap or ChartType.Sunburst)
            yield break;

        // Funnel has a category (id=0) and value (id=1) axis.
        // Histogram, Pareto, BoxAndWhisker, Waterfall all share the same two-axis base structure.
        yield return new XElement(chartExNs + "axis",
            new XAttribute("id", "0"),
            new XElement(chartExNs + "catScaling", new XAttribute("gapWidth", "2.19000006")),
            new XElement(chartExNs + "tickLabels"));

        // Build the value-axis scaling element respecting YAxisMinimum/Maximum/LogScale.
        var valScaling = BuildChartExValScalingElement(chart, chartExNs);
        yield return new XElement(chartExNs + "axis",
            new XAttribute("id", "1"),
            valScaling,
            new XElement(chartExNs + "majorGridlines"),
            new XElement(chartExNs + "tickLabels"));

        if (chart.Type != ChartType.Pareto)
            yield break;

        yield return new XElement(chartExNs + "axis",
            new XAttribute("id", "2"),
            new XElement(chartExNs + "valScaling",
                new XAttribute("max", "1"),
                new XAttribute("min", "0")),
            new XElement(chartExNs + "units", new XAttribute("unit", "percentage")),
            new XElement(chartExNs + "tickLabels"));
    }

    private static XElement BuildChartExValScalingElement(ChartModel chart, XNamespace chartExNs)
    {
        var scaling = new XElement(chartExNs + "valScaling");
        if (chart.YAxisMaximum is { } max && double.IsFinite(max))
            scaling.SetAttributeValue("max", max.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (chart.YAxisMinimum is { } min && double.IsFinite(min))
            scaling.SetAttributeValue("min", min.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (chart.YAxisMajorUnit is { } majorUnit && double.IsFinite(majorUnit) && majorUnit > 0)
            scaling.SetAttributeValue("majorUnit", majorUnit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return scaling;
    }

    private static XElement? BuildChartExSubtotals(ChartModel chart, XNamespace chartExNs)
    {
        if (chart.WaterfallTotalPointIndices is not { Count: > 0 } totals)
            return null;

        return new XElement(chartExNs + "subtotals",
            totals.Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .Select(index => new XElement(chartExNs + "idx", new XAttribute("val", index))));
    }

    // cx:data/@id and cx:dataId/@val are xsd:unsignedInt — a bare numeric id, not "data{n}".
    private static string ToChartExDataId(int seriesIndex) =>
        seriesIndex.ToString(CultureInfo.InvariantCulture);

    private static XAttribute? ToChartExSeriesUniqueIdAttribute(ChartModel chart, int seriesIndex) =>
        chart.Type == ChartType.BoxAndWhisker
            ? new XAttribute("uniqueId", ToChartExSeriesUniqueId(chart, seriesIndex))
            : null;

    private static string ToChartExSeriesUniqueId(ChartModel chart, int seriesIndex)
    {
        var chartId = chart.Id.ToString("N").ToUpperInvariant();
        var seriesSuffix = (seriesIndex + 1).ToString("X8", CultureInfo.InvariantCulture);
        var id = chartId[..24] + seriesSuffix;
        return $"{{{id[..8]}-{id[8..12]}-{id[12..16]}-{id[16..20]}-{id[20..32]}}}";
    }

    private static XElement? ToChartExSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        int seriesIndex,
        XNamespace chartExNs)
    {
        if (chart.Type != ChartType.BoxAndWhisker || !chart.FirstRowIsHeader)
            return null;

        // R112-io-chartex-verbatim-series-4: chart.DataRange is a synthetic 1x1 placeholder for a
        // BoxAndWhisker chart loaded through the named-range verbatim fallback (see
        // BuildChartExData above) — GetSeriesStripLayout/GetChartExSeriesValueStrip below would
        // compute a meaningless header-cell reference from it. Use the series' own captured name
        // formula (the numDim <cx:nf>, repurposed as TxFormula) and cached name (EmbeddedSeriesData,
        // captured from the series' own <cx:tx>/<cx:txData>/<cx:v> at load time) instead.
        var verbatim = chart.VerbatimSeriesFormulas?.FirstOrDefault(f => f.SeriesIndex == seriesIndex);
        if (verbatim is not null)
        {
            if (string.IsNullOrEmpty(verbatim.TxFormula))
                return null;

            var cachedName = chart.EmbeddedSeriesData?.FirstOrDefault(d => d.SeriesIndex == seriesIndex)?.SeriesName;
            if (string.IsNullOrEmpty(cachedName))
                return null;

            return new XElement(chartExNs + "tx",
                new XElement(chartExNs + "txData",
                    new XElement(chartExNs + "f", verbatim.TxFormula),
                    new XElement(chartExNs + "v", cachedName)));
        }

        var layout = GetSeriesStripLayout(chart);
        var seriesStrip = GetChartExSeriesValueStrip(chart, layout, seriesIndex);
        var (headerRow, headerCol) = layout.SeriesInRows
            ? (seriesStrip, layout.HeaderPoint)
            : (layout.HeaderPoint, seriesStrip);
        return new XElement(chartExNs + "tx",
            new XElement(chartExNs + "txData",
                new XElement(chartExNs + "f",
                    FormatStripHeaderCell(layout, sheet.Name, seriesStrip)),
                new XElement(chartExNs + "v",
                    ToChartExSeriesTitleText(sheet.GetCell(headerRow, headerCol)?.Value))));
    }

    private static uint GetChartExSeriesValueStrip(ChartModel chart, ChartSeriesStripLayout layout, int seriesIndex)
    {
        var hasCategoryStrip = chart.FirstColIsCategories && layout.LastStrip > layout.CategoryStrip;
        var firstValueStrip = hasCategoryStrip ? layout.CategoryStrip + 1 : layout.CategoryStrip;
        return firstValueStrip + (uint)seriesIndex;
    }

    private static string ToChartExSeriesTitleText(ScalarValue? value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static string ToChartExNumericDimensionType(ChartType chartType) =>
        chartType is ChartType.Treemap or ChartType.Sunburst ? "size" : "val";

    private static string ToChartExSeriesLayoutId(ChartType chartType) =>
        chartType switch
        {
            ChartType.Treemap => "treemap",
            ChartType.Sunburst => "sunburst",
            ChartType.Histogram or ChartType.Pareto => "clusteredColumn",
            ChartType.BoxAndWhisker => "boxWhisker",
            ChartType.Waterfall => "waterfall",
            ChartType.Funnel => "funnel",
            _ => throw new ArgumentOutOfRangeException(nameof(chartType), chartType, null)
        };
}
