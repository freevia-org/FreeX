using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static (XElement Element, ChartType Type)? FindDeferredAdvancedChart(XElement? plotArea)
    {
        if (plotArea is null)
            return null;

        foreach (var element in plotArea.Descendants())
        {
            var chartType = element.Name.LocalName switch
            {
                "surfaceChart" => ChartType.Surface,
                "surface3DChart" => ChartType.ThreeDSurface,
                "treemapChart" => ChartType.Treemap,
                "sunburstChart" => ChartType.Sunburst,
                "histogramChart" => XlsxChartSeriesRangeReader.HasDescendant(element, "paretoLine") ? ChartType.Pareto : ChartType.Histogram,
                "boxWhiskerChart" => ChartType.BoxAndWhisker,
                "waterfallChart" => ChartType.Waterfall,
                "funnelChart" => ChartType.Funnel,
                _ => (ChartType?)null
            };
            if (chartType is { } type)
                return (element, type);
        }

        var chartExSeries = plotArea
            .Descendants()
            .Where(element => element.Name.LocalName == "series")
            .ToList();
        if (chartExSeries.Count > 0)
        {
            var primarySeries = FindPrimaryChartExSeries(chartExSeries, out var hasParetoLine);
            if (primarySeries is not null && ToChartExChartType(primarySeries.Attribute("layoutId")?.Value, hasParetoLine) is { } chartType)
                return (primarySeries.Parent ?? primarySeries, chartType);
        }

        var mapChart = FindMapChartElement(plotArea);
        return mapChart is null ? null : (mapChart, ChartType.Map);
    }

    private static XElement? FindPrimaryChartExSeries(IEnumerable<XElement> chartExSeries, out bool hasParetoLine)
    {
        hasParetoLine = false;
        XElement? primarySeries = null;
        foreach (var series in chartExSeries)
        {
            if (string.Equals(series.Attribute("layoutId")?.Value, "paretoLine", StringComparison.OrdinalIgnoreCase))
            {
                hasParetoLine = true;
                continue;
            }

            primarySeries ??= series;
        }

        return primarySeries;
    }

    private static XElement? FindMapChartElement(XElement plotArea)
    {
        foreach (var element in plotArea.Descendants())
        {
            if (IsMapChartElement(element))
                return element;
        }

        return null;
    }

    private static ChartType? ToChartExChartType(string? layoutId, bool hasParetoLine) =>
        layoutId?.ToLowerInvariant() switch
        {
            "treemap" => ChartType.Treemap,
            "sunburst" => ChartType.Sunburst,
            "clusteredcolumn" => hasParetoLine ? ChartType.Pareto : ChartType.Histogram,
            "boxwhisker" => ChartType.BoxAndWhisker,
            "waterfall" => ChartType.Waterfall,
            "funnel" => ChartType.Funnel,
            _ => null
        };

    private static bool HasDirectSupportedChart(XElement? plotArea) =>
        plotArea?.Elements().Any(element => element.Name.LocalName is
            "areaChart" or
            "area3DChart" or
            "barChart" or
            "bubbleChart" or
            "doughnutChart" or
            "line3DChart" or
            "lineChart" or
            "ofPieChart" or
            "pie3DChart" or
            "pieChart" or
            "radarChart" or
            "scatterChart" or
            "stockChart") == true;

    private static bool TryReadDeferredAdvancedChart(
        XDocument chartXml,
        XElement plotChart,
        SheetId sheetId,
        ChartType chartType,
        out ChartModel chart)
    {
        return TryReadDeferredAdvancedChart(chartXml, plotChart, sheetId, chartType, fallbackDataRange: null, sheetNameResolver: null, out chart);
    }

    private static bool TryReadDeferredAdvancedChart(
        XDocument chartXml,
        XElement plotChart,
        SheetId sheetId,
        ChartType chartType,
        GridRange? fallbackDataRange,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = chartType,
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };

        var chartExSeries = plotChart.Name.LocalName == "series"
            ? [plotChart]
            : plotChart.Descendants()
                .Where(element =>
                    element.Name.LocalName == "series" &&
                    !string.Equals(element.Attribute("layoutId")?.Value, "paretoLine", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var seriesElements = (chartExSeries.Length > 0
            ? chartExSeries
            : plotChart.Descendants().Where(element => element.Name.LocalName == "ser"))
            .ToList();
        foreach (var series in seriesElements)
        {
            if (series.Name.LocalName == "series")
            {
                var data = FindChartExData(chartXml, series);
                hasTitleRange |= data?.Elements().Any(element =>
                    element.Name.LocalName == "numDim" &&
                    element.Elements().Any(child => child.Name.LocalName == "nf" && !string.IsNullOrWhiteSpace(child.Value))) == true;
                hasCategoryRange |= data?.Elements().Any(element =>
                    element.Name.LocalName == "strDim" &&
                    string.Equals(element.Attribute("type")?.Value, "cat", StringComparison.OrdinalIgnoreCase) &&
                    element.Elements().Any(child => child.Name.LocalName == "f" && !string.IsNullOrWhiteSpace(child.Value))) == true;

                ReadChartExSeriesLayout(series, result);
            }
            else
            {
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
            }

            foreach (var formula in ReadDeferredAdvancedSeriesRangeFormulas(chartXml, series))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                    ranges.Add(range);
            }
        }

        if (ranges.Count == 0)
        {
            // R111-io-chart-deferred-named-range-1: this reader (TryReadDeferredAdvancedChart) is
            // the eighth reader for named-range chart series that r110's numCache/strCache fallback
            // missed — it serves every chartEx-family chart (Waterfall, Histogram, Box & Whisker,
            // Treemap, Sunburst, Funnel, Pareto, Surface/3D-Surface — see FindDeferredAdvancedChart)
            // AND the ordinary bar3DChart (3D-Column/3D-Bar, via TryReadThreeDBarChart below, which
            // always passes fallbackDataRange:null). When every series' val/cat (classic <ser>) or
            // numDim/strDim (true chartEx <cx:series>) formula is a defined name that
            // TryParseFormulaRange cannot resolve (e.g. the OFFSET-based "auto-expanding chart"
            // pattern), ranges stays empty and this used to unconditionally drop the whole chart.
            // Reuse the same embedded-cache reader the seven fixed families use for the classic
            // <ser>-shaped members of this family, and a chartEx-native numDim/strDim <lvl>/<pt>
            // cache reader (TryReadChartExEmbeddedSeriesData) for the true <cx:series>-shaped ones.
            var embeddedData = chartExSeries.Length > 0
                ? TryReadChartExEmbeddedSeriesData(chartXml, chartExSeries)
                : XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(seriesElements, sheetId)
                  ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(seriesElements, sheetId, sheetNameResolver);

            if (embeddedData is not null)
            {
                // No series formula parsed to a real cell range (that is precisely why we are in
                // this branch), so there is no live DataRange to union — use the same 1x1
                // placeholder the seven fixed readers fall back to (e.g. Bar.cs) purely so
                // DataRange.Start.Sheet carries the chart's own sheet for downstream sheet-ID
                // matching; the actual series values come from EmbeddedSeriesData.
                result.DataRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1));
                result.FirstRowIsHeader = hasTitleRange;
                result.FirstColIsCategories = hasCategoryRange;
                result.EmbeddedSeriesData = embeddedData;
                // DetectDeferredSeriesInRows would also report false here: every formula it could
                // parse already failed above (that's why ranges.Count == 0), so there is nothing
                // left to detect a multi-column strip from.
                result.SeriesInRows = false;
                XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
                XlsxChartSanitizer.SanitizeLoadedChart(result);
                chart = result;
                return true;
            }

            if (fallbackDataRange is not { } fallbackRange || !HasExcelInternalChartExDataReference(chartXml))
            {
                chart = new ChartModel();
                return false;
            }

            result.DataRange = fallbackRange;
            result.FirstRowIsHeader = true;
            result.FirstColIsCategories = fallbackRange.End.Col > fallbackRange.Start.Col;
            XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
            XlsxChartSanitizer.SanitizeLoadedChart(result);
            chart = result;
            return true;
        }

        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = DetectDeferredSeriesInRows(chartXml, seriesElements, sheetId, sheetNameResolver);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    /// <summary>
    /// Detects Excel's "Switch Row/Column" orientation for deferred/chartEx charts from their
    /// value-dimension formulas (<c>cx:numDim/cx:f</c> for chartEx series, <c>c:val</c> for classic
    /// <c>ser</c> elements). Same single-row-strip rule as
    /// <see cref="XlsxChartSeriesRangeReader.DetectSeriesInRows"/>.
    /// </summary>
    private static bool DetectDeferredSeriesInRows(
        XDocument chartXml,
        IEnumerable<XElement> seriesElements,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver)
    {
        var anyMultiColumn = false;
        foreach (var series in seriesElements)
        {
            var formula = series.Name.LocalName == "series"
                ? FindChartExData(chartXml, series)?.Elements()
                    .Where(element => element.Name.LocalName == "numDim")
                    .SelectMany(element => element.Elements())
                    .FirstOrDefault(child => child.Name.LocalName == "f" && !string.IsNullOrWhiteSpace(child.Value))?
                    .Value
                : XlsxChartSeriesRangeReader.ReadFirstFormula(series, "val");
            if (string.IsNullOrWhiteSpace(formula))
                continue;
            if (!XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                continue;

            if (range.Start.Row != range.End.Row)
                return false;
            if (range.End.Col > range.Start.Col)
                anyMultiColumn = true;
        }

        return anyMultiColumn;
    }

    private static bool HasExcelInternalChartExDataReference(XDocument chartXml) =>
        chartXml.Root?
            .Descendants()
            .Any(element =>
                element.Name.LocalName == "f" &&
                element.Value.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Reads chartEx per-series <c>layoutPr</c> settings (histogram <c>binning</c>, box-and-whisker
    /// <c>statistics/@quartileMethod</c>, waterfall <c>subtotals</c>) into the model so they round-trip
    /// through XLSX.
    /// </summary>
    private static void ReadChartExSeriesLayout(XElement series, ChartModel chart)
    {
        var layoutPr = FirstChildElementByLocalName(series, "layoutPr");
        if (layoutPr is null)
            return;

        var binning = FirstChildElementByLocalName(layoutPr, "binning");
        if (binning is not null && ParseChartExBinning(binning) is { } binningModel)
            chart.HistogramBinning = binningModel;

        var statistics = FirstChildElementByLocalName(layoutPr, "statistics");
        var quartileMethod = statistics?.Attribute("quartileMethod")?.Value;
        if (!string.IsNullOrWhiteSpace(quartileMethod))
            chart.QuartileMethod = quartileMethod;

        var subtotals = FirstChildElementByLocalName(layoutPr, "subtotals");
        if (subtotals is not null)
        {
            var indices = subtotals.Elements()
                .Where(element => element.Name.LocalName == "idx")
                .Select(element => int.TryParse(element.Attribute("val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : -1)
                .Where(i => i >= 0)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            chart.WaterfallTotalPointIndices = indices;
        }
    }

    private static HistogramBinningModel? ParseChartExBinning(XElement binning)
    {
        var binSize = FirstChildElementByLocalName(binning, "binSize")?.Value;
        var binCount = FirstChildElementByLocalName(binning, "binCount")?.Value;
        var underflow = ParseChartExThreshold(binning.Attribute("underflow")?.Value);
        var overflow = ParseChartExThreshold(binning.Attribute("overflow")?.Value);

        if (!string.IsNullOrWhiteSpace(binSize) &&
            double.TryParse(binSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && width > 0)
            return new HistogramBinningModel(HistogramBinningMode.BinWidth, BinWidth: width, OverflowThreshold: overflow, UnderflowThreshold: underflow);

        if (!string.IsNullOrWhiteSpace(binCount) &&
            int.TryParse(binCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            return new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: count, OverflowThreshold: overflow, UnderflowThreshold: underflow);

        if (underflow is not null || overflow is not null)
            return new HistogramBinningModel(HistogramBinningMode.Automatic, OverflowThreshold: overflow, UnderflowThreshold: underflow);

        return null;
    }

    private static double? ParseChartExThreshold(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static IEnumerable<string> ReadDeferredAdvancedSeriesRangeFormulas(XDocument chartXml, XElement series)
    {
        if (series.Name.LocalName != "series")
            return XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series);

        var dataId = ReadChartExSeriesDataId(series);
        if (string.IsNullOrWhiteSpace(dataId))
            return [];

        var data = FindChartExData(chartXml, dataId);
        return data is null
            ? []
            : data.Descendants()
                .Where(element => element.Name.LocalName is "f" or "nf")
                .Select(element => element.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// R111-io-chart-deferred-named-range-1: chartEx analogue of
    /// <see cref="XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData"/> for the true
    /// &lt;cx:series&gt;-shaped members of the advanced-chart family (dataId/data/numDim/strDim,
    /// not the classic &lt;ser&gt; shape). Reads each series' cached values straight from the
    /// numDim/strDim &lt;cx:lvl&gt;/&lt;cx:pt&gt; cache elements — the chartEx equivalent of a
    /// classic &lt;c:numCache&gt;/&lt;c:strCache&gt; — so a named-range series (whose &lt;cx:f&gt;
    /// formula TryParseFormulaRange could not resolve, which is why the caller is in this branch
    /// at all) can still be rendered. Returns null when no series has any cached numeric points
    /// (e.g. the numDim only carries an &lt;cx:f&gt; formula with no cache at all).
    /// </summary>
    private static List<ChartEmbeddedSeriesData>? TryReadChartExEmbeddedSeriesData(
        XDocument chartXml,
        IReadOnlyList<XElement> chartExSeriesElements)
    {
        var result = new List<ChartEmbeddedSeriesData>(chartExSeriesElements.Count);
        for (var i = 0; i < chartExSeriesElements.Count; i++)
        {
            var series = chartExSeriesElements[i];
            var data = FindChartExData(chartXml, series);
            var catDimension = FindChartExDimension(data, "strDim", "cat");
            var valDimension = FindChartExDimension(data, "numDim", "val");
            var seriesName = ReadChartExSeriesNameCache(series);
            var categories = ReadChartExStringCacheValues(catDimension);
            var values = ReadChartExNumericCacheValues(valDimension);
            result.Add(new ChartEmbeddedSeriesData(i, seriesName, categories, values));
        }

        return result.Any(s => s.Values.Count > 0) ? result : null;
    }

    /// <summary>Finds a &lt;cx:numDim&gt;/&lt;cx:strDim&gt; child of a chartEx &lt;cx:data&gt; element by its @type attribute.</summary>
    private static XElement? FindChartExDimension(XElement? data, string dimensionLocalName, string dimensionType) =>
        data?.Elements().FirstOrDefault(element =>
            element.Name.LocalName == dimensionLocalName &&
            string.Equals(element.Attribute("type")?.Value, dimensionType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads a chartEx series' own cached name from &lt;cx:tx&gt;/&lt;cx:txData&gt;/&lt;cx:v&gt;.</summary>
    private static string? ReadChartExSeriesNameCache(XElement series) =>
        FirstChildElementByLocalName(series, "tx")?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "v")?
            .Value;

    /// <summary>Reads all cached string points from a &lt;cx:strDim&gt;'s &lt;cx:lvl&gt;/&lt;cx:pt&gt; cache.</summary>
    private static IReadOnlyList<string> ReadChartExStringCacheValues(XElement? dimension)
    {
        if (dimension is null || FirstChildElementByLocalName(dimension, "lvl") is not { } lvl)
            return [];

        var values = new string?[ChartExCachePointCapacity(lvl)];
        foreach (var pt in lvl.Elements().Where(element => element.Name.LocalName == "pt"))
        {
            if (!TryReadChartExPointIndex(pt, ref values, out var idx))
                continue;
            values[idx] = pt.Value ?? "";
        }

        return values.Select(v => v ?? "").ToList();
    }

    /// <summary>Reads all cached numeric points from a &lt;cx:numDim&gt;'s &lt;cx:lvl&gt;/&lt;cx:pt&gt; cache.</summary>
    private static IReadOnlyList<double?> ReadChartExNumericCacheValues(XElement? dimension)
    {
        if (dimension is null || FirstChildElementByLocalName(dimension, "lvl") is not { } lvl)
            return [];

        var values = new double?[ChartExCachePointCapacity(lvl)];
        foreach (var pt in lvl.Elements().Where(element => element.Name.LocalName == "pt"))
        {
            if (!TryReadChartExPointIndex(pt, ref values, out var idx))
                continue;
            values[idx] = double.TryParse(pt.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        return values;
    }

    private static int ChartExCachePointCapacity(XElement lvl) =>
        int.TryParse(lvl.Attribute("ptCount")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0
            ? count
            : lvl.Elements()
                .Where(element => element.Name.LocalName == "pt")
                .Select(element => int.TryParse(element.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) ? idx + 1 : 0)
                .DefaultIfEmpty(0)
                .Max();

    private static bool TryReadChartExPointIndex<T>(XElement pt, ref T[] values, out int idx)
    {
        if (!int.TryParse(pt.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) || idx < 0)
            return false;

        if (idx >= values.Length)
            Array.Resize(ref values, idx + 1);

        return true;
    }

    private static XElement? FindChartExData(XDocument chartXml, XElement series)
    {
        var dataId = ReadChartExSeriesDataId(series);
        return string.IsNullOrWhiteSpace(dataId) ? null : FindChartExData(chartXml, dataId);
    }

    private static XElement? FindChartExData(XDocument chartXml, string dataId)
    {
        if (chartXml.Root is not { } root)
            return null;

        foreach (var element in root.Descendants())
        {
            if (IsChartExDataElement(element, dataId))
                return element;
        }

        return null;
    }

    private static XElement? FirstChildElementByLocalName(XElement element, string localName)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == localName)
                return child;
        }

        return null;
    }

    private static string? ReadChartExSeriesDataId(XElement series) =>
        FirstChildElementByLocalName(series, "dataId")?
            .Attribute("val")?
            .Value;

    private static bool IsChartExDataElement(XElement element, string dataId) =>
        element.Name.LocalName == "data" &&
        string.Equals(element.Attribute("id")?.Value, dataId, StringComparison.Ordinal);

    private static bool IsMapChartElement(XElement element) =>
        element.Name.LocalName is "geoChart" or "mapChart" or "regionMapChart";

    private static XElement? FindFirstMapChartElement(XElement plotArea)
    {
        foreach (var element in plotArea.Descendants())
        {
            if (IsMapChartElement(element))
                return element;
        }

        return null;
    }

    private static bool TryReadThreeDBarChart(
        XDocument chartXml,
        XElement plotChart,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var barDirection = FirstChildElementByLocalName(plotChart, "barDir")?
            .Attribute("val")?
            .Value;
        var chartType = string.Equals(barDirection, "bar", StringComparison.OrdinalIgnoreCase)
            ? ChartType.ThreeDBar
            : ChartType.ThreeDColumn;

        return TryReadDeferredAdvancedChart(chartXml, plotChart, sheetId, chartType, fallbackDataRange: null, sheetNameResolver, out chart);
    }
}
