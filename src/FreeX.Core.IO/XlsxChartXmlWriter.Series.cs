using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static readonly XNamespace Chart2012Ns = "http://schemas.microsoft.com/office/drawing/2012/chart";
    private const string DataLabelsRangeExtUri = "{02D57815-91ED-43cb-92C2-25804820EDAC}";

    /// <summary>
    /// Re-emits a series' "Value From Cells" data labels (<c>c15:datalabelsRange</c>) under the
    /// series <c>c:extLst</c>, round-tripping the source formula and cached point strings. Returns
    /// null when the series has no captured definition or the definition is empty.
    /// </summary>
    private static XElement? ToRangeDataLabelsExtXml(ChartModel chart, int seriesIndex, XNamespace chartNs)
    {
        var definition = chart.SeriesRangeDataLabels?.LastOrDefault(item => item.SeriesIndex == seriesIndex);
        if (definition is null)
            return null;

        var points = (definition.Points ?? [])
            .Where(point => point.PointIndex >= 0)
            .OrderBy(point => point.PointIndex)
            .ToList();
        if (string.IsNullOrEmpty(definition.Formula) && definition.PointCount is null && points.Count == 0)
            return null;

        var pointCount = definition.PointCount ?? points.Count;

        var dlblRangeCache = new XElement(Chart2012Ns + "dlblRangeCache",
            new XElement(Chart2012Ns + "ptCount", new XAttribute("val", pointCount)));
        foreach (var point in points)
        {
            dlblRangeCache.Add(new XElement(Chart2012Ns + "pt",
                new XAttribute("idx", point.PointIndex),
                new XElement(Chart2012Ns + "v", point.Text)));
        }

        var dataLabelsRange = new XElement(Chart2012Ns + "datalabelsRange");
        if (!string.IsNullOrEmpty(definition.Formula))
            dataLabelsRange.Add(new XElement(Chart2012Ns + "f", definition.Formula));
        dataLabelsRange.Add(dlblRangeCache);

        return new XElement(chartNs + "extLst",
            new XElement(chartNs + "ext",
                new XAttribute("uri", DataLabelsRangeExtUri),
                new XAttribute(XNamespace.Xmlns + "c15", Chart2012Ns.NamespaceName),
                dataLabelsRange));
    }

    /// <summary>
    /// R41-io-chart-errorbars-trendline-3-2: re-emits any extra &lt;c:errBars&gt; captured for this
    /// series beyond the one modeled by the scalar ErrorBar* properties (e.g. the paired X/Y set
    /// Excel writes when both horizontal and vertical error bars are configured), so they survive
    /// an open/save round-trip verbatim. See <see cref="ChartModel.AdditionalSeriesErrorBarsXml"/>.
    /// </summary>
    private static IEnumerable<XElement> ToAdditionalErrorBarsXml(ChartModel chart, int seriesIndex) =>
        chart.AdditionalSeriesErrorBarsXml
            .Where(entry => entry.SeriesIndex == seriesIndex)
            .Select(entry => TryParseChartXml(entry.RawXml))
            .OfType<XElement>();

    /// <summary>
    /// R41-io-chart-errorbars-trendline-3-3: re-emits any extra &lt;c:trendline&gt; captured for
    /// this series beyond the one modeled by the scalar Trendline* properties (a second trendline
    /// on this series, or the first trendline on any series other than
    /// <see cref="ChartModel.TrendlineSeriesIndex"/>), so it survives an open/save round-trip
    /// verbatim instead of being silently dropped. See
    /// <see cref="ChartModel.AdditionalSeriesTrendlinesXml"/>.
    /// </summary>
    private static IEnumerable<XElement> ToAdditionalTrendlinesXml(ChartModel chart, int seriesIndex) =>
        chart.AdditionalSeriesTrendlinesXml
            .Where(entry => entry.SeriesIndex == seriesIndex)
            .Select(entry => TryParseChartXml(entry.RawXml))
            .OfType<XElement>();

    /// <summary>
    /// Orientation-aware geometry for positional series-range computation. A "strip" is one series
    /// line of <see cref="ChartModel.DataRange"/> — a column by default, a row when
    /// <see cref="ChartModel.SeriesInRows"/> (Excel's "Switch Row/Column") — and the point axis runs
    /// perpendicular to it. Series names come from the strip's cell at <see cref="HeaderPoint"/>;
    /// categories come from the strip at <see cref="CategoryStrip"/>.
    /// </summary>
    private readonly struct ChartSeriesStripLayout
    {
        public required bool SeriesInRows { get; init; }
        public required uint FirstValueStrip { get; init; }
        public required uint LastStrip { get; init; }
        public required uint CategoryStrip { get; init; }
        public required uint FirstDataPoint { get; init; }
        public required uint LastPoint { get; init; }
        public required uint HeaderPoint { get; init; }
    }

    private static ChartSeriesStripLayout GetSeriesStripLayout(ChartModel chart) =>
        chart.SeriesInRows
            ? new ChartSeriesStripLayout
            {
                SeriesInRows = true,
                FirstValueStrip = chart.FirstColIsCategories ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row,
                LastStrip = chart.DataRange.End.Row,
                CategoryStrip = chart.DataRange.Start.Row,
                FirstDataPoint = chart.FirstRowIsHeader ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col,
                LastPoint = chart.DataRange.End.Col,
                HeaderPoint = chart.DataRange.Start.Col,
            }
            : new ChartSeriesStripLayout
            {
                SeriesInRows = false,
                FirstValueStrip = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col,
                LastStrip = chart.DataRange.End.Col,
                CategoryStrip = chart.DataRange.Start.Col,
                FirstDataPoint = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row,
                LastPoint = chart.DataRange.End.Row,
                HeaderPoint = chart.DataRange.Start.Row,
            };

    /// <summary>Formula for one series strip's data points (a column strip or a row strip).</summary>
    private static string FormatStripRange(ChartSeriesStripLayout layout, string sheetName, uint strip) =>
        layout.SeriesInRows
            ? FormatSheetRange(sheetName, strip, layout.FirstDataPoint, strip, layout.LastPoint)
            : FormatSheetRange(sheetName, layout.FirstDataPoint, strip, layout.LastPoint, strip);

    /// <summary>Formula for a strip's series-name (header) cell.</summary>
    private static string FormatStripHeaderCell(ChartSeriesStripLayout layout, string sheetName, uint strip) =>
        layout.SeriesInRows
            ? FormatSheetRange(sheetName, strip, layout.HeaderPoint, strip, layout.HeaderPoint)
            : FormatSheetRange(sheetName, layout.HeaderPoint, strip, layout.HeaderPoint, strip);

    private static IEnumerable<XElement> BuildChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null,
        bool forceLineShapeProperties = false)
    {
        var layout = GetSeriesStripLayout(chart);
        var categoryRange = chart.FirstColIsCategories
            ? FormatStripRange(layout, sheet.Name, layout.CategoryStrip)
            : null;
        var categoryIsNumeric = chart.FirstColIsCategories &&
            IsCategoryRangeNumeric(sheet, layout);
        var categoryStripValues = chart.FirstColIsCategories
            ? GetStripPointValues(sheet, layout, layout.CategoryStrip)
            : null;

        foreach (var (strip, seriesIndex) in GetChartSeriesStripSequence(chart, layout))
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
                continue;

            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var valueRange = verbatim?.ValFormula
                ?? FormatStripRange(layout, sheet.Name, strip);
            var effectiveCategoryRange = verbatim?.CatFormula ?? categoryRange;
            var effectiveCategoryIsNumeric = verbatim?.CatFormula is null && categoryIsNumeric;
            var valueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, strip), chartNs)
                : null;
            var categoryCache = verbatim?.CatFormula is null && categoryStripValues is not null
                ? (effectiveCategoryIsNumeric
                    ? BuildNumCacheXml(categoryStripValues, chartNs)
                    : BuildStrCacheXml(categoryStripValues, chartNs))
                : null;

            XElement? txElement = null;
            if (verbatim?.TxFormula is { } txFormula)
            {
                txElement = new XElement(chartNs + "tx",
                    new XElement(chartNs + "strRef",
                        new XElement(chartNs + "f", txFormula)));
            }
            else
            {
                txElement = ToSeriesTitleXml(chart, sheet, layout, strip, chartNs);
            }

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs)
                    : ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs)
                    : null,
                ToSeriesInvertIfNegativeXml(chart, seriesIndex, chartNs),
                ToDataPointsXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToAdditionalTrendlinesXml(chart, seriesIndex),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                ToAdditionalErrorBarsXml(chart, seriesIndex),
                ToCategoryRangeXml(effectiveCategoryRange, effectiveCategoryIsNumeric, chartNs, categoryCache),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange),
                        valueCache)),
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesSmoothXml(chart, seriesIndex, chartNs)
                    : null,
                ToRangeDataLabelsExtXml(chart, seriesIndex, chartNs));
        }
    }

    /// <summary>
    /// R16-chart-datasource-editing-3: yields the (column, chart-XML series index) pairs to emit as
    /// <c>&lt;c:ser&gt;</c> elements. When <see cref="ChartModel.SeriesColumnMappings"/> is
    /// authoritative (populated and every mapped column lies within the strip span — mirrors
    /// ChartRenderer.SeriesFormatting.HasAuthoritativeSeriesColumns) it is the source of truth: a
    /// worksheet column inside <see cref="ChartModel.DataRange"/> that was deselected from the chart
    /// (and so has no entry) is skipped instead of being re-emitted as a phantom series, and kept
    /// columns use their original chart-XML idx (the key <see cref="ChartModel.SeriesFormats"/> and
    /// friends are indexed by) instead of a freshly recomputed position. Otherwise falls back to the
    /// legacy positional scan of every column in the strip span.
    /// </summary>
    private static IEnumerable<(uint Strip, int SeriesIndex)> GetChartSeriesStripSequence(
        ChartModel chart,
        ChartSeriesStripLayout layout)
    {
        if (HasAuthoritativeSeriesColumnMappings(chart, layout))
        {
            foreach (var mapping in chart.SeriesColumnMappings.OrderBy(m => m.SeriesXmlIndex))
                yield return (mapping.ValueColumn, mapping.SeriesXmlIndex);
            yield break;
        }

        var seriesIndex = 0;
        for (var strip = layout.FirstValueStrip; strip <= layout.LastStrip; strip++)
        {
            yield return (strip, seriesIndex);
            seriesIndex++;
        }
    }

    /// <summary>
    /// True when <see cref="ChartModel.SeriesColumnMappings"/> can be trusted as the exact set of
    /// series to emit. Column-based mappings cannot describe row-major series (mirrors the renderer's
    /// same guard), and a mapping referencing a column outside the strip span is stale/ambiguous.
    /// </summary>
    private static bool HasAuthoritativeSeriesColumnMappings(ChartModel chart, ChartSeriesStripLayout layout)
    {
        if (chart.SeriesInRows)
            return false;

        var mappings = chart.SeriesColumnMappings;
        if (mappings.Count == 0)
            return false;

        foreach (var mapping in mappings)
        {
            if (mapping.ValueColumn < layout.FirstValueStrip || mapping.ValueColumn > layout.LastStrip)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true when every non-blank cell in the category strip's data points contains a
    /// numeric value. An all-blank strip returns false (fall back to strRef).
    /// </summary>
    private static bool IsCategoryRangeNumeric(Sheet sheet, ChartSeriesStripLayout layout)
    {
        var hasAnyValue = false;
        for (var point = layout.FirstDataPoint; point <= layout.LastPoint; point++)
        {
            var value = layout.SeriesInRows
                ? sheet.GetValue(layout.CategoryStrip, point)
                : sheet.GetValue(point, layout.CategoryStrip);
            if (value is BlankValue)
                continue;
            if (value is not NumberValue)
                return false;
            hasAnyValue = true;
        }

        return hasAnyValue;
    }

    private static ChartSeriesVerbatimFormulas? GetVerbatimFormulas(ChartModel chart, int seriesIndex) =>
        chart.VerbatimSeriesFormulas?.FirstOrDefault(f => f.SeriesIndex == seriesIndex);

    /// <summary>
    /// R53-io-chart-series-order-3-2: reads a strip's current worksheet values so numRef/strRef
    /// elements can carry a real &lt;c:numCache&gt;/&lt;c:strCache&gt; (real Excel always pairs a
    /// series formula with its cached values, so the chart still displays last-known data when the
    /// referenced range/sheet is unavailable — external link broken, manual calc not yet
    /// recalculated, or a non-recalculating OOXML consumer). Only meaningful for a strip whose range
    /// is known positionally; verbatim (multi-area) formulas have no single strip and are left
    /// without a cache, unchanged from prior behavior.
    /// </summary>
    private static ScalarValue[] GetStripPointValues(Sheet sheet, ChartSeriesStripLayout layout, uint strip)
    {
        var count = layout.LastPoint >= layout.FirstDataPoint
            ? (int)(layout.LastPoint - layout.FirstDataPoint) + 1
            : 0;
        if (count <= 0)
            return [];

        var values = new ScalarValue[count];
        for (var i = 0; i < count; i++)
        {
            var point = layout.FirstDataPoint + (uint)i;
            values[i] = layout.SeriesInRows ? sheet.GetValue(strip, point) : sheet.GetValue(point, strip);
        }

        return values;
    }

    private static XElement BuildNumCacheXml(IReadOnlyList<ScalarValue> values, XNamespace chartNs)
    {
        var cache = new XElement(chartNs + "numCache",
            new XElement(chartNs + "formatCode", "General"),
            new XElement(chartNs + "ptCount", new XAttribute("val", values.Count)));
        for (var i = 0; i < values.Count; i++)
        {
            var text = values[i] switch
            {
                NumberValue number => number.Value.ToString("G15", CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString("G15", CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "1" : "0",
                _ => null,
            };
            if (text is null)
                continue;

            cache.Add(new XElement(chartNs + "pt", new XAttribute("idx", i), new XElement(chartNs + "v", text)));
        }

        return cache;
    }

    private static XElement BuildStrCacheXml(IReadOnlyList<ScalarValue> values, XNamespace chartNs)
    {
        var cache = new XElement(chartNs + "strCache",
            new XElement(chartNs + "ptCount", new XAttribute("val", values.Count)));
        for (var i = 0; i < values.Count; i++)
        {
            var text = values[i] switch
            {
                NumberValue number => number.Value.ToString("G15", CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString("G15", CultureInfo.InvariantCulture),
                TextValue textValue => textValue.Value,
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                _ => null,
            };
            if (text is null)
                continue;

            cache.Add(new XElement(chartNs + "pt", new XAttribute("idx", i), new XElement(chartNs + "v", text)));
        }

        return cache;
    }

    private static XElement? ToCategoryRangeXml(
        string? categoryRange,
        bool numericCategory,
        XNamespace chartNs,
        XElement? cacheElement = null)
    {
        if (string.IsNullOrWhiteSpace(categoryRange))
            return null;

        var refElement = numericCategory
            ? new XElement(chartNs + "numRef", new XElement(chartNs + "f", categoryRange), cacheElement)
            : new XElement(chartNs + "strRef", new XElement(chartNs + "f", categoryRange), cacheElement);

        return new XElement(chartNs + "cat", refElement);
    }

    private static XElement? ToSeriesSmoothXml(ChartModel chart, int seriesIndex, XNamespace chartNs) =>
        GetSeriesFormat(chart, seriesIndex)?.Smooth is { } smooth
            ? new XElement(chartNs + "smooth", new XAttribute("val", smooth ? "1" : "0"))
            : null;

    private static XElement? ToSeriesInvertIfNegativeXml(ChartModel chart, int seriesIndex, XNamespace chartNs)
    {
        if (!ChartTypeSupport.SupportsInvertIfNegative(chart.Type) ||
            GetSeriesFormat(chart, seriesIndex)?.InvertIfNegative is not { } invertIfNegative)
        {
            return null;
        }

        return new XElement(chartNs + "invertIfNegative", new XAttribute("val", invertIfNegative ? "1" : "0"));
    }

    private static IEnumerable<XElement> BuildScatterChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null)
    {
        var layout = GetSeriesStripLayout(chart);
        var xValueStrip = chart.SeriesInRows ? chart.DataRange.Start.Row : chart.DataRange.Start.Col;
        var xValueRange = FormatStripRange(layout, sheet.Name, xValueStrip);

        var seriesIndex = 0;
        for (var strip = xValueStrip + 1; strip <= layout.LastStrip; strip++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var effectiveXValueRange = verbatim?.CatFormula ?? xValueRange;
            var yValueRange = verbatim?.ValFormula
                ?? FormatStripRange(layout, sheet.Name, strip);
            var xValueCache = verbatim?.CatFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, xValueStrip), chartNs)
                : null;
            var yValueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, strip), chartNs)
                : null;

            XElement? txElement = verbatim?.TxFormula is { } txFormula
                ? new XElement(chartNs + "tx", new XElement(chartNs + "strRef", new XElement(chartNs + "f", txFormula)))
                : ToSeriesTitleXml(chart, sheet, layout, strip, chartNs);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                ToScatterSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs),
                ToDataPointsXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToAdditionalTrendlinesXml(chart, seriesIndex),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                ToAdditionalErrorBarsXml(chart, seriesIndex),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", effectiveXValueRange),
                        xValueCache)),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange),
                        yValueCache)),
                ToSeriesSmoothXml(chart, seriesIndex, chartNs),
                ToRangeDataLabelsExtXml(chart, seriesIndex, chartNs));
            seriesIndex++;
        }
    }

    private static XElement? ToScatterSeriesLineShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (ScatterSeriesRequestsLine(chart, seriesIndex))
            return ToSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs);

        return new XElement(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XElement(drawingNs + "noFill")));
    }

    private static bool ScatterSeriesRequestsLine(ChartModel chart, int seriesIndex)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        return format is not null &&
            (format.StrokeColor is not null ||
             format.StrokeThemeColor is not null ||
             format.StrokeThickness is not null ||
             format.DashStyle is not null ||
             format.Smooth == true);
    }

    private static HashSet<int> GetSecondaryAxisSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type) || seriesCount < 2)
            return [];

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return Enumerable.Range(1, seriesCount - 1).ToHashSet();

        // Keep index 0: an explicit secondary-axis assignment is valid for ANY series, including the
        // first (R25-chart-axis-series-deep-1), so a series-0 assignment round-trips back out to XLSX
        // instead of being silently dropped on save. Mirrors GetComboLineSeriesIndexes below.
        return chart.SecondaryAxisSeriesIndexes
            .Where(index => index >= 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static HashSet<int> GetComboLineSeriesIndexes(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || !ChartTypeSupport.SupportsComboLineOverlay(chart) || seriesCount < 2)
            return [];

        // Allow the combo line at series index 0 — Excel routinely emits the <c:lineChart> series
        // first (e.g. a shaded target-band chart). Mirrors the loader/sanitizer which keep index 0.
        return chart.ComboLineSeriesIndexes
            .Where(index => index >= 0 && index < seriesCount)
            .Distinct()
            .ToHashSet();
    }

    private static IEnumerable<XElement> BuildBubbleChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var layout = GetSeriesStripLayout(chart);
        var xValueStrip = chart.SeriesInRows ? chart.DataRange.Start.Row : chart.DataRange.Start.Col;
        if (layout.LastStrip - xValueStrip < 2)
            yield break;

        var xValueRange = FormatStripRange(layout, sheet.Name, xValueStrip);

        var seriesIndex = 0;
        for (var yValueStrip = xValueStrip + 1; yValueStrip < layout.LastStrip; yValueStrip += 2)
        {
            var sizeStrip = yValueStrip + 1;
            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var effectiveXValueRange = verbatim?.CatFormula ?? xValueRange;
            var yValueRange = verbatim?.ValFormula
                ?? FormatStripRange(layout, sheet.Name, yValueStrip);
            var sizeRange = FormatStripRange(layout, sheet.Name, sizeStrip);
            var xValueCache = verbatim?.CatFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, xValueStrip), chartNs)
                : null;
            var yValueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, yValueStrip), chartNs)
                : null;
            var sizeCache = BuildNumCacheXml(GetStripPointValues(sheet, layout, sizeStrip), chartNs);

            XElement? txElement = verbatim?.TxFormula is { } txFormula
                ? new XElement(chartNs + "tx", new XElement(chartNs + "strRef", new XElement(chartNs + "f", txFormula)))
                : ToSeriesTitleXml(chart, sheet, layout, yValueStrip, chartNs);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToDataPointsXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToAdditionalTrendlinesXml(chart, seriesIndex),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                ToAdditionalErrorBarsXml(chart, seriesIndex),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", effectiveXValueRange),
                        xValueCache)),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange),
                        yValueCache)),
                new XElement(chartNs + "bubbleSize",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", sizeRange),
                        sizeCache)),
                ToRangeDataLabelsExtXml(chart, seriesIndex, chartNs));
            seriesIndex++;
        }
    }

    private static IEnumerable<XElement> BuildPieFamilyChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var layout = GetSeriesStripLayout(chart);
        if (chart.FirstColIsCategories && layout.LastStrip <= layout.CategoryStrip)
            yield break;

        var categoryRange = chart.FirstColIsCategories
            ? FormatStripRange(layout, sheet.Name, layout.CategoryStrip)
            : null;
        var categoryIsNumeric = chart.FirstColIsCategories &&
            IsCategoryRangeNumeric(sheet, layout);
        var categoryStripValues = chart.FirstColIsCategories
            ? GetStripPointValues(sheet, layout, layout.CategoryStrip)
            : null;

        var seriesIndex = 0;
        for (var valueStrip = layout.FirstValueStrip; valueStrip <= layout.LastStrip; valueStrip++)
        {
            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var valueRange = verbatim?.ValFormula
                ?? FormatStripRange(layout, sheet.Name, valueStrip);
            var effectiveCategoryRange = verbatim?.CatFormula ?? categoryRange;
            var effectiveCategoryIsNumeric = verbatim?.CatFormula is null && categoryIsNumeric;
            var valueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, valueStrip), chartNs)
                : null;
            var categoryCache = verbatim?.CatFormula is null && categoryStripValues is not null
                ? (effectiveCategoryIsNumeric
                    ? BuildNumCacheXml(categoryStripValues, chartNs)
                    : BuildStrCacheXml(categoryStripValues, chartNs))
                : null;

            XElement? txElement = verbatim?.TxFormula is { } txFormula
                ? new XElement(chartNs + "tx", new XElement(chartNs + "strRef", new XElement(chartNs + "f", txFormula)))
                : ToSeriesTitleXml(chart, sheet, layout, valueStrip, chartNs);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToDataPointsXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(effectiveCategoryRange, effectiveCategoryIsNumeric, chartNs, categoryCache),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange),
                        valueCache)),
                ToRangeDataLabelsExtXml(chart, seriesIndex, chartNs));
            seriesIndex++;
        }
    }

    private static XElement? ToSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        ChartSeriesStripLayout layout,
        uint seriesStrip,
        XNamespace chartNs)
    {
        if (!chart.FirstRowIsHeader)
            return null;

        var titleRange = FormatStripHeaderCell(layout, sheet.Name, seriesStrip);
        return new XElement(chartNs + "tx",
            new XElement(chartNs + "strRef",
                new XElement(chartNs + "f", titleRange)));
    }

    private static XElement? ToFirstSliceAngleXml(ChartModel chart, XNamespace chartNs)
    {
        var normalized = chart.FirstSliceAngle % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized == 0
            ? null
            : new XElement(chartNs + "firstSliceAng",
                new XAttribute("val", Math.Clamp((int)Math.Round(normalized), 0, 360)));
    }

    /// <summary>
    /// Emits one <c>&lt;c:dPt&gt;</c> element per data point that needs a per-point override —
    /// either an exploded-slice distance or an explicit per-point fill color
    /// (<see cref="ChartModel.PointFillColors"/>). Child element order follows CT_DPt: idx,
    /// then explosion, then spPr.
    /// </summary>
    private static IEnumerable<XElement> ToDataPointsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        var explodedPoints = chart.ExplodedSlices
            .Where(point => point.SeriesIndex == seriesIndex &&
                point.PointIndex >= 0 && point.PointIndex < pointCount &&
                point.Distance > 0)
            .ToDictionary(point => point.PointIndex, point => point.Distance);

        // Fall back to the scalar single-explosion representation (used by the pie-format UI
        // commands, which only ever set one exploded slice) when no per-point data was read.
        if (explodedPoints.Count == 0 &&
            seriesIndex == 0 &&
            chart.ExplodedSliceIndex >= 0 &&
            chart.ExplodedSliceIndex < pointCount &&
            chart.ExplodedSliceDistance > 0)
        {
            explodedPoints[chart.ExplodedSliceIndex] = chart.ExplodedSliceDistance;
        }

        var pointIndexes = chart.PointFillColors
            .Where(point => point.SeriesIndex == seriesIndex)
            .Select(point => point.PointIndex)
            .Concat(explodedPoints.Keys)
            .Distinct()
            .OrderBy(index => index);

        foreach (var pointIndex in pointIndexes)
        {
            var explosion = explodedPoints.TryGetValue(pointIndex, out var distance)
                ? new XElement(chartNs + "explosion",
                    new XAttribute("val", Math.Clamp((int)Math.Round(distance * 100), 0, 50)))
                : null;
            var spPr = ToPointShapeProperties(chart, seriesIndex, pointIndex, chartNs, drawingNs);

            yield return new XElement(chartNs + "dPt",
                new XElement(chartNs + "idx", new XAttribute("val", pointIndex)),
                explosion,
                spPr);
        }
    }

    private static XElement? ToPointShapeProperties(
        ChartModel chart,
        int seriesIndex,
        int pointIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var point = chart.PointFillColors.LastOrDefault(item =>
            item.SeriesIndex == seriesIndex && item.PointIndex == pointIndex);
        if (point is null)
            return null;

        var fill = ToSolidFill(point.FillThemeColor, point.FillColor, drawingNs);
        return fill is null ? null : new XElement(chartNs + "spPr", fill);
    }

    private static XElement? ToSeriesLineShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var fill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = fill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null;

        return !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                new XElement(drawingNs + "ln",
                    format.StrokeThickness is { } strokeThickness
                        ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * DrawingMlUnits.EmuPerPoint)))
                        : null,
                    fill,
                    format.DashStyle is { } dashStyle
                        ? ToPresetDash(dashStyle, drawingNs)
                        : null));
    }

    private static XElement? ToSeriesMarkerXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
            return null;

        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var shapeProperties = ToShapeProperties(
            chartNs,
            drawingNs,
            format.FillThemeColor,
            format.FillColor,
            format.MarkerBorderThemeColor,
            format.MarkerBorderColor,
            format.MarkerBorderThickness);
        if (format.MarkerStyle is null && format.MarkerSize is null && shapeProperties is null)
            return null;

        return new XElement(chartNs + "marker",
            format.MarkerStyle is { } markerStyle
                ? new XElement(chartNs + "symbol", new XAttribute("val", ToXlsxMarkerStyle(markerStyle)))
                : null,
            format.MarkerSize is { } markerSize
                ? new XElement(chartNs + "size", new XAttribute("val", Math.Clamp((int)Math.Round(markerSize), 1, 30)))
                : null,
            shapeProperties);
    }

    private static XElement? ToSeriesShapeProperties(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        if (format is null)
            return null;

        var solidFill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
        var fill = solidFill ?? (format.NoFill ? new XElement(drawingNs + "noFill") : null);
        var lineFill = ToSolidFill(format.StrokeThemeColor, format.StrokeColor, drawingNs);
        var hasLineFormatting = lineFill is not null ||
            format.StrokeThickness is not null ||
            format.DashStyle is not null ||
            format.NoLine;

        return fill is null && !hasLineFormatting
            ? null
            : new XElement(chartNs + "spPr",
                fill,
                hasLineFormatting
                    ? new XElement(drawingNs + "ln",
                        format.StrokeThickness is { } strokeThickness
                            ? new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(strokeThickness, 0.5, 10) * DrawingMlUnits.EmuPerPoint)))
                            : null,
                        lineFill ?? (format.NoLine ? new XElement(drawingNs + "noFill") : null),
                        format.DashStyle is { } dashStyle
                            ? ToPresetDash(dashStyle, drawingNs)
                            : null)
                    : null);
    }

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex)
    {
        var format = chart.SeriesFormats.LastOrDefault(item => item.SeriesIndex == seriesIndex);
        return format is null
            ? null
            : format with
            {
                DashStyle = ValidNullableEnumOrNull(format.DashStyle),
                MarkerStyle = ValidNullableEnumOrNull(format.MarkerStyle)
            };
    }

    private static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } enumValue && Enum.IsDefined(enumValue) ? enumValue : null;

}
