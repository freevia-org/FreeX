using System.Globalization;
using System.Linq;
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
        Workbook workbook,
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
            // R103-io-chart-series-verbatim-cache: when the category formula itself was unparsable
            // (named range/multi-area/external link), the numRef-vs-strRef choice must follow the
            // SOURCE cache's own root element name, not the recomputed heuristic — the recomputed
            // heuristic reflects live worksheet data that has no relation to the verbatim range, and
            // choosing the wrong ref type here would nest a captured <c:numCache> inside a <c:strRef>
            // (or vice versa), which is schema-invalid.
            XElement? verbatimCategoryCache = null;
            var effectiveCategoryIsNumeric = categoryIsNumeric;
            if (verbatim?.CatFormula is not null)
            {
                verbatimCategoryCache = ResolveVerbatimCategoryCacheXml(workbook, sheet.Id, verbatim.CatFormula, verbatim.CatCacheXml, chartNs, out var catIsNumeric);
                effectiveCategoryIsNumeric = catIsNumeric;
            }
            var valueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, strip), GetStripNumberFormatCode(workbook, sheet, layout, strip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.ValFormula, verbatim.ValCacheXml, chartNs);
            var categoryCache = verbatim?.CatFormula is not null
                ? verbatimCategoryCache
                : (categoryStripValues is not null
                    ? (effectiveCategoryIsNumeric
                        ? BuildNumCacheXml(categoryStripValues, GetStripNumberFormatCode(workbook, sheet, layout, layout.CategoryStrip), chartNs)
                        : BuildStrCacheXml(categoryStripValues, chartNs))
                    : null);

            var txElement = ResolveSeriesTitleXml(chart, sheet, layout, strip, seriesIndex, chartNs, verbatim?.TxFormula);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", GetSeriesOrder(chart, seriesIndex))),
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
                ToVerbatimMultiLevelCategoryXml(chart, seriesIndex)
                    ?? ToCategoryRangeXml(effectiveCategoryRange, effectiveCategoryIsNumeric, chartNs, categoryCache),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange),
                        valueCache)),
                // R133-io-chart-radar-stale-smooth: gated on forceLineShapeProperties (the same
                // flag the marker/spPr branch above already keys off) AND chart.Type != Radar,
                // rather than on a standalone chart.Type allowlist. Two hazards, not one:
                //   1. forceLineShapeProperties is true for Radar too (it shares line-style
                //      marker/spPr handling above), but CT_RadarSer has no <c:smooth> child at
                //      all -- only CT_LineSer (Line/ThreeDLine, and the stockChart <c:ser> built
                //      by CreateStockPlotChart) does. A bare "chart.Type is ... or
                //      ChartType.Stock" check does not exclude Radar by itself, so the Type!=Radar
                //      guard is still required alongside forceLineShapeProperties.
                //   2. chart.Type stays ChartType.Stock for BOTH series CreateStockPlotChart
                //      writes -- e.g. VolumeHighLowClose/VolumeOpenHighLowClose subtypes pair a
                //      <c:barChart> (the volume series, via CreateStockVolumeBarChart, CT_BarSer --
                //      also has no <c:smooth>) with the <c:stockChart> (CT_LineSer). A bare
                //      chart.Type check cannot tell those two <c:ser> emissions apart and would
                //      wrongly emit <c:smooth> into the volume <c:barChart> series too. Gating on
                //      forceLineShapeProperties instead is correct here because
                //      CreateStockVolumeBarChart is the ONLY BuildChartSeries caller that leaves it
                //      at its default (false) for a Stock chart -- CreateStockPlotChart always
                //      passes true.
                // Either hazard alone would leave a series with a stale Smooth flag from a prior
                // Line/ThreeDLine/Scatter chart type (e.g. a chart type change that never ran
                // through SetChartLayoutCommand's model-side ClampSeriesFormat, or a model
                // constructed directly) producing a schema-invalid file Excel has to repair.
                forceLineShapeProperties && chart.Type is not ChartType.Radar
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
            var mappedSeriesIndexes = new HashSet<int>();
            foreach (var mapping in chart.SeriesColumnMappings.OrderBy(m => m.SeriesXmlIndex))
            {
                mappedSeriesIndexes.Add(mapping.SeriesXmlIndex);
                yield return (mapping.ValueColumn, mapping.SeriesXmlIndex);
            }

            // R106-io-chart-series-cross-sheet: a series whose <c:val> formula points at a
            // DIFFERENT sheet than the chart's own host sheet never gets a column mapping —
            // TryReadSeriesValueColumn only maps same-sheet single-column ranges — but it DOES get
            // a captured verbatim formula/cache (see XlsxChartSeriesRangeReader's cross-sheet
            // handling of HasUnparsableFormula/TryCollectVerbatimFormulas). It must still be
            // emitted here: BuildChartSeries already prefers `verbatim?.ValFormula` over the
            // recomputed strip range for any series returned from this sequence, so the strip
            // value below is never actually used for these entries — only the series index
            // matters, to look up the verbatim formula/cache by. Without this fallback, the
            // "authoritative mappings" fast path above silently drops the cross-sheet series from
            // the saved file entirely (it is present in neither loop).
            foreach (var verbatimSeriesIndex in (chart.VerbatimSeriesFormulas ?? [])
                .Where(formulas => formulas.ValFormula is not null && !mappedSeriesIndexes.Contains(formulas.SeriesIndex))
                .Select(formulas => formulas.SeriesIndex)
                .Distinct()
                .OrderBy(index => index))
            {
                yield return (layout.FirstValueStrip, verbatimSeriesIndex);
            }

            yield break;
        }

        // R108-io-chart-series-embedded-fastpath: when EVERY series' val/cat formula is a named
        // range (e.g. an OFFSET-based dynamic range) or points at a sheet other than the chart's
        // own host sheet, none of them can ever get a SeriesColumnMappings entry (that requires a
        // parseable single-column LOCAL reference), so SeriesColumnMappings stays empty and the
        // legacy positional scan below is driven by chart.DataRange — which the reader can only
        // populate from whichever formula (if any) happened to parse (often just the <c:tx> title
        // cell), producing a degenerate/backwards strip span that silently yields zero series. Every
        // series in that shape was captured verbatim on load instead (formula + cache) — use that
        // directly rather than trusting the broken strip scan. A row-major chart is excluded because
        // its (legitimate) multi-column value ranges parse fine and never engage the verbatim bypass,
        // so the ordinary legacy scan below is the correct path for it.
        if (chart.SeriesColumnMappings.Count == 0
            && !chart.SeriesInRows
            && chart.VerbatimSeriesFormulas is { Count: > 0 } allVerbatim)
        {
            foreach (var seriesIndex in allVerbatim
                .Where(formulas => formulas.ValFormula is not null)
                .Select(formulas => formulas.SeriesIndex)
                .Distinct()
                .OrderBy(index => index))
            {
                yield return (layout.FirstValueStrip, seriesIndex);
            }

            yield break;
        }

        var legacySeriesIndex = 0;
        for (var strip = layout.FirstValueStrip; strip <= layout.LastStrip; strip++)
        {
            yield return (strip, legacySeriesIndex);
            legacySeriesIndex++;
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
    /// numeric OR date/time value. An all-blank strip returns false (fall back to strRef).
    /// </summary>
    /// <remarks>
    /// R100-io-chart-category-date-axis: <see cref="DateTimeValue"/> is FreeX's own sealed record
    /// for dates — it is NOT a <see cref="NumberValue"/> — but Excel dates are stored as plain
    /// numeric serials too and a date category column must round-trip as a
    /// <c>&lt;c:cat&gt;&lt;c:numRef&gt;</c> date axis (with its own formatCode, see
    /// <see cref="GetStripNumberFormatCode"/>), never as text. Treating only
    /// <c>value is not NumberValue</c> as "not numeric" silently demoted every date category axis
    /// to <c>&lt;c:strRef&gt;</c>/&lt;c:strCache&gt;, printing the bare OA serial (e.g. "45658") as
    /// literal text with no date formatting at all — even when merely re-saving a chart Excel
    /// itself authored with a proper date axis.
    /// </remarks>
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
            if (value is not NumberValue and not DateTimeValue)
                return false;
            hasAnyValue = true;
        }

        return hasAnyValue;
    }

    private static ChartSeriesVerbatimFormulas? GetVerbatimFormulas(ChartModel chart, int seriesIndex) =>
        chart.VerbatimSeriesFormulas?.FirstOrDefault(f => f.SeriesIndex == seriesIndex);

    /// <summary>
    /// R107-io-chart-series-verbatim-refresh: attempts to resolve a verbatim series container's
    /// formula against the CURRENT workbook and read the CURRENT cell values it points at, so the
    /// caller can rebuild a fresh cache instead of re-emitting the frozen snapshot captured when
    /// the source file was originally opened (see <see cref="ChartSeriesVerbatimFormulas"/>).
    /// <para>
    /// <paramref name="formula"/> was only ever captured verbatim because, at load time, it either
    /// could not be parsed as a rectangular range at all (named range, multi-area reference, or
    /// external-workbook link — <c>XlsxChartSeriesRangeReader.TryParseFormulaRange</c> returns
    /// false), or it resolved cleanly but to a sheet OTHER than the chart's own host sheet
    /// (R106-io-chart-series-cross-sheet). Real Excel has no notion of a chart writer "confined" to
    /// one sheet: an ordinary cross-sheet series formula always shows the CURRENT cell values on
    /// every save. So whenever the formula still resolves — its target sheet still exists and the
    /// range collapses to a single row or single column (the only shape a flat cache can express) —
    /// this reads today's values fresh. Only a genuinely unparsable formula, or one whose target
    /// sheet was since deleted, or one that resolves to a non-strip (multi-row-and-column)
    /// rectangle, returns false and leaves the caller to fall back to the frozen verbatim cache —
    /// mirroring Excel, which also cannot show live data for an unresolvable name/link without
    /// recalculating it itself.
    /// </para>
    /// </summary>
    private static bool TryReadVerbatimRangeCurrentValues(
        Workbook workbook,
        SheetId hostSheetId,
        string formula,
        out ScalarValue[] values,
        out string formatCode,
        out bool isNumeric)
    {
        values = [];
        formatCode = "General";
        isNumeric = false;

        // A multi-area union (e.g. "Data!$A$1,Data!$A$3") has no single rectangular range to
        // resolve at all. TryParseFormulaRange only inspects the text after the LAST '!' in the
        // string, so a comma-joined formula like this one would otherwise be misread as a
        // reference to whatever single cell follows that last '!' (here, cell A3) instead of being
        // rejected — silently fabricating a cache from the wrong cell. Reject up front so this
        // always falls back to the frozen verbatim cache, exactly like a genuinely unparsable
        // formula.
        if (formula.Contains(',', StringComparison.Ordinal))
            return false;

        var sheetNameResolver = workbook.Sheets.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);
        if (!XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, hostSheetId, sheetNameResolver, out var range))
            return false;

        var targetSheet = workbook.GetSheet(range.Start.Sheet);
        if (targetSheet is null)
            return false;

        var isSingleColumn = range.Start.Col == range.End.Col;
        if (!isSingleColumn && range.Start.Row != range.End.Row)
            return false;

        var count = isSingleColumn ? (int)range.RowCount : (int)range.ColCount;
        var resolved = new ScalarValue[count];
        for (var i = 0; i < count; i++)
        {
            resolved[i] = isSingleColumn
                ? targetSheet.GetValue(range.Start.Row + (uint)i, range.Start.Col)
                : targetSheet.GetValue(range.Start.Row, range.Start.Col + (uint)i);
        }
        values = resolved;

        var hasAnyValue = false;
        var allNumericOrBlank = true;
        foreach (var value in resolved)
        {
            if (value is BlankValue)
                continue;
            hasAnyValue = true;
            if (value is not NumberValue and not DateTimeValue)
            {
                allNumericOrBlank = false;
                break;
            }
        }
        isNumeric = hasAnyValue && allNumericOrBlank;

        var firstCell = targetSheet.GetCell(range.Start.Row, range.Start.Col);
        if (firstCell is not null)
        {
            var code = workbook.GetStyle(firstCell.StyleId).NumberFormat;
            if (!string.IsNullOrEmpty(code))
                formatCode = code;
        }

        return true;
    }

    /// <summary>
    /// R107-io-chart-series-verbatim-refresh: resolves a verbatim VALUE container (val/xVal/yVal/
    /// bubbleSize — always numeric, mirroring the ordinary recomputed-strip path which never checks
    /// point type before calling <see cref="BuildNumCacheXml"/>). Rebuilds fresh from the current
    /// workbook when the formula still resolves; falls back to the frozen
    /// <paramref name="cachedXml"/> only when it cannot be resolved at all.
    /// </summary>
    private static XElement? ResolveVerbatimValueCacheXml(
        Workbook workbook,
        SheetId hostSheetId,
        string formula,
        string? cachedXml,
        XNamespace chartNs) =>
        TryReadVerbatimRangeCurrentValues(workbook, hostSheetId, formula, out var values, out var formatCode, out _)
            ? BuildNumCacheXml(values, formatCode, chartNs)
            : TryParseChartXml(cachedXml);

    /// <summary>
    /// R107-io-chart-series-verbatim-refresh: resolves a verbatim CATEGORY container (cat — may be
    /// numeric/date or text), setting <paramref name="isNumeric"/> so the caller can also pick the
    /// matching numRef-vs-strRef container type. Rebuilds fresh from the current workbook when the
    /// formula still resolves; falls back to the frozen <paramref name="cachedXml"/> (and that
    /// cache's own root-element name to decide numeric-ness — see R103-io-chart-series-verbatim-
    /// cache) only when it cannot be resolved at all.
    /// </summary>
    private static XElement? ResolveVerbatimCategoryCacheXml(
        Workbook workbook,
        SheetId hostSheetId,
        string formula,
        string? cachedXml,
        XNamespace chartNs,
        out bool isNumeric)
    {
        if (TryReadVerbatimRangeCurrentValues(workbook, hostSheetId, formula, out var values, out var formatCode, out isNumeric))
        {
            return isNumeric
                ? BuildNumCacheXml(values, formatCode, chartNs)
                : BuildStrCacheXml(values, chartNs);
        }

        var frozen = TryParseChartXml(cachedXml);
        isNumeric = frozen?.Name.LocalName == "numCache";
        return frozen;
    }

    /// <summary>
    /// R103-io-chart-series-tx-1: returns the captured &lt;c:tx&gt; formula for this series (see
    /// <see cref="ChartModel.SeriesNameOverrides"/>), independent of whether it happens to also be
    /// unparsable (that case is already covered by <see cref="GetVerbatimFormulas"/>'s TxFormula).
    /// </summary>
    private static string? GetSeriesNameOverrideFormula(ChartModel chart, int seriesIndex) =>
        chart.SeriesNameOverrides.LastOrDefault(o => o.SeriesIndex == seriesIndex)?.Formula;

    /// <summary>
    /// R103-io-chart-series-tx-1: single choke point for resolving a series' &lt;c:tx&gt; element —
    /// preferring, in order, (1) an unparsable verbatim tx formula (named range/multi-area/external
    /// link — <see cref="GetVerbatimFormulas"/>), (2) a captured tx formula that parsed fine but
    /// points somewhere other than the strip's own header cell (Excel's "Select Data &gt; Edit
    /// Series &gt; Series name" lets the user reference ANY cell — <see cref="GetSeriesNameOverrideFormula"/>),
    /// and finally (3) the recomputed strip header-cell guess (<see cref="ToSeriesTitleXml"/>). Used
    /// by every BuildXxxChartSeries family so a fix to the precedence only needs to happen once.
    /// </summary>
    private static XElement? ResolveSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        ChartSeriesStripLayout layout,
        uint seriesStrip,
        int seriesIndex,
        XNamespace chartNs,
        string? verbatimTxFormula)
    {
        var formula = verbatimTxFormula ?? GetSeriesNameOverrideFormula(chart, seriesIndex);
        if (formula is not null)
        {
            return new XElement(chartNs + "tx",
                new XElement(chartNs + "strRef",
                    new XElement(chartNs + "f", formula)));
        }

        return ToSeriesTitleXml(chart, sheet, layout, seriesStrip, chartNs);
    }

    /// <summary>
    /// R82-io-chart-series-5-1: returns the explicit &lt;c:order&gt; captured for this series (see
    /// <see cref="ChartModel.SeriesOrderOverrides"/>), falling back to the recomputed positional
    /// <paramref name="seriesIndex"/> — Excel's ordinary case, where order == idx.
    /// </summary>
    private static int GetSeriesOrder(ChartModel chart, int seriesIndex) =>
        chart.SeriesOrderOverrides.LastOrDefault(o => o.SeriesIndex == seriesIndex)?.Order ?? seriesIndex;

    /// <summary>
    /// R82-io-chart-series-5-2: re-emits a series' captured &lt;c:cat&gt; verbatim when it was a
    /// &lt;c:multiLvlStrRef&gt; (grouped/multi-level category axis) in the source file — see
    /// <see cref="ChartModel.MultiLevelCategoryXml"/>. Returns null when no such capture exists for
    /// this series, so the caller falls back to the ordinary computed &lt;c:cat&gt;.
    /// </summary>
    private static XElement? ToVerbatimMultiLevelCategoryXml(ChartModel chart, int seriesIndex) =>
        chart.MultiLevelCategoryXml
            .LastOrDefault(entry => entry.SeriesIndex == seriesIndex) is { } entry
            ? TryParseChartXml(entry.RawXml)
            : null;

    /// <summary>
    /// R53-io-chart-series-order-3-2: reads a strip's current worksheet values so numRef/strRef
    /// elements can carry a real &lt;c:numCache&gt;/&lt;c:strCache&gt; (real Excel always pairs a
    /// series formula with its cached values, so the chart still displays last-known data when the
    /// referenced range/sheet is unavailable — external link broken, manual calc not yet
    /// recalculated, or a non-recalculating OOXML consumer). Only meaningful for a strip whose range
    /// is known positionally; a verbatim (named-range/multi-area/external-link) formula has no
    /// single strip to read live values from — that case instead re-emits the SOURCE file's own
    /// captured cache verbatim (see R103-io-chart-series-verbatim-cache in
    /// <see cref="ChartSeriesVerbatimFormulas"/>), falling back to no cache only when the source
    /// itself had none.
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

    /// <summary>
    /// R57-io-chart-series-refs-5-3: resolves the number-format code Excel would cache for a
    /// strip's numCache/formatCode — the format of the strip's first data cell — instead of the
    /// hardcoded "General" literal the caller previously always wrote regardless of the source
    /// cells' real format (e.g. Percentage/Currency).
    /// </summary>
    private static string GetStripNumberFormatCode(Workbook workbook, Sheet sheet, ChartSeriesStripLayout layout, uint strip)
    {
        if (layout.LastPoint < layout.FirstDataPoint)
            return "General";

        var cell = layout.SeriesInRows
            ? sheet.GetCell(strip, layout.FirstDataPoint)
            : sheet.GetCell(layout.FirstDataPoint, strip);
        if (cell is null)
            return "General";

        var formatCode = workbook.GetStyle(cell.StyleId).NumberFormat;
        return string.IsNullOrEmpty(formatCode) ? "General" : formatCode;
    }

    private static XElement BuildNumCacheXml(IReadOnlyList<ScalarValue> values, string formatCode, XNamespace chartNs)
    {
        var cache = new XElement(chartNs + "numCache",
            new XElement(chartNs + "formatCode", formatCode),
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
        Workbook workbook,
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
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, xValueStrip), GetStripNumberFormatCode(workbook, sheet, layout, xValueStrip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.CatFormula, verbatim.CatCacheXml, chartNs);
            var yValueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, strip), GetStripNumberFormatCode(workbook, sheet, layout, strip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.ValFormula, verbatim.ValCacheXml, chartNs);

            var txElement = ResolveSeriesTitleXml(chart, sheet, layout, strip, seriesIndex, chartNs, verbatim?.TxFormula);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", GetSeriesOrder(chart, seriesIndex))),
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
        Workbook workbook,
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
            // R57-io-chart-series-refs-5-2: preserve an unparsable (external-link/multi-area)
            // bubbleSize formula verbatim instead of always recomputing it positionally — the
            // xVal/yVal siblings above already had this fallback, bubbleSize did not.
            var sizeRange = verbatim?.BubbleSizeFormula
                ?? FormatStripRange(layout, sheet.Name, sizeStrip);
            var xValueCache = verbatim?.CatFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, xValueStrip), GetStripNumberFormatCode(workbook, sheet, layout, xValueStrip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.CatFormula, verbatim.CatCacheXml, chartNs);
            var yValueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, yValueStrip), GetStripNumberFormatCode(workbook, sheet, layout, yValueStrip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.ValFormula, verbatim.ValCacheXml, chartNs);
            var sizeCache = verbatim?.BubbleSizeFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, sizeStrip), GetStripNumberFormatCode(workbook, sheet, layout, sizeStrip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.BubbleSizeFormula, verbatim.BubbleSizeCacheXml, chartNs);

            var txElement = ResolveSeriesTitleXml(chart, sheet, layout, yValueStrip, seriesIndex, chartNs, verbatim?.TxFormula);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", GetSeriesOrder(chart, seriesIndex))),
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
        Workbook workbook,
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
            // R103-io-chart-series-verbatim-cache: see the identical comment in BuildChartSeries —
            // the ref-type choice for a verbatim category must follow the captured cache's own root
            // element name, not the live-worksheet heuristic.
            XElement? verbatimCategoryCache = null;
            var effectiveCategoryIsNumeric = categoryIsNumeric;
            if (verbatim?.CatFormula is not null)
            {
                verbatimCategoryCache = ResolveVerbatimCategoryCacheXml(workbook, sheet.Id, verbatim.CatFormula, verbatim.CatCacheXml, chartNs, out var catIsNumeric);
                effectiveCategoryIsNumeric = catIsNumeric;
            }
            var valueCache = verbatim?.ValFormula is null
                ? BuildNumCacheXml(GetStripPointValues(sheet, layout, valueStrip), GetStripNumberFormatCode(workbook, sheet, layout, valueStrip), chartNs)
                : ResolveVerbatimValueCacheXml(workbook, sheet.Id, verbatim.ValFormula, verbatim.ValCacheXml, chartNs);
            var categoryCache = verbatim?.CatFormula is not null
                ? verbatimCategoryCache
                : (categoryStripValues is not null
                    ? (effectiveCategoryIsNumeric
                        ? BuildNumCacheXml(categoryStripValues, GetStripNumberFormatCode(workbook, sheet, layout, layout.CategoryStrip), chartNs)
                        : BuildStrCacheXml(categoryStripValues, chartNs))
                    : null);

            var txElement = ResolveSeriesTitleXml(chart, sheet, layout, valueStrip, seriesIndex, chartNs, verbatim?.TxFormula);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", GetSeriesOrder(chart, seriesIndex))),
                txElement,
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToDataPointsXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToVerbatimMultiLevelCategoryXml(chart, seriesIndex)
                    ?? ToCategoryRangeXml(effectiveCategoryRange, effectiveCategoryIsNumeric, chartNs, categoryCache),
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
    /// an exploded-slice distance, an explicit per-point fill color
    /// (<see cref="ChartModel.PointFillColors"/>), or a per-point marker override
    /// (<see cref="ChartModel.PointMarkerFormats"/>, R82-io-chart-series-5-3). Child element order
    /// follows CT_DPt: idx, then marker, then explosion, then spPr.
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
            .Concat(chart.PointMarkerFormats
                .Where(point => point.SeriesIndex == seriesIndex)
                .Select(point => point.PointIndex))
            .Distinct()
            .OrderBy(index => index);

        foreach (var pointIndex in pointIndexes)
        {
            var marker = ToPointMarkerXml(chart, seriesIndex, pointIndex, chartNs, drawingNs);
            var explosion = explodedPoints.TryGetValue(pointIndex, out var distance)
                ? new XElement(chartNs + "explosion",
                    new XAttribute("val", Math.Clamp((int)Math.Round(distance * 100), 0, 50)))
                : null;
            var spPr = ToPointShapeProperties(chart, seriesIndex, pointIndex, chartNs, drawingNs);

            yield return new XElement(chartNs + "dPt",
                new XElement(chartNs + "idx", new XAttribute("val", pointIndex)),
                marker,
                explosion,
                spPr);
        }
    }

    /// <summary>
    /// R82-io-chart-series-5-3: builds a data point's &lt;c:marker&gt; override from
    /// <see cref="ChartModel.PointMarkerFormats"/> (Format Data Point &gt; Marker Options), mirroring
    /// <see cref="ToSeriesMarkerXml"/>'s series-level shape. Returns null when this point has no
    /// captured marker override.
    /// </summary>
    private static XElement? ToPointMarkerXml(
        ChartModel chart,
        int seriesIndex,
        int pointIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!ChartTypeSupport.SupportsSeriesMarkers(chart.Type))
            return null;

        var format = chart.PointMarkerFormats.LastOrDefault(item =>
            item.SeriesIndex == seriesIndex && item.PointIndex == pointIndex);
        if (format is null)
            return null;

        var shapeProperties = ToShapeProperties(
            chartNs,
            drawingNs,
            format.FillThemeColor,
            format.FillColor,
            format.BorderThemeColor,
            format.BorderColor,
            format.BorderThickness);
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

        // R91-render-chart-series-format-5-1: an authored gradient/pattern fill was captured
        // verbatim (no dedicated model representation) — re-emit it as-is instead of synthesizing
        // a fill from FillColor/FillThemeColor/NoFill, which would drop it entirely.
        XElement? fill;
        if (format.RawFillXml is { Length: > 0 } rawFillXml)
        {
            fill = XElement.Parse(rawFillXml);
        }
        else
        {
            var solidFill = ToSolidFill(format.FillThemeColor, format.FillColor, drawingNs);
            // R91-render-chart-series-format-5-4: re-apply the authored <a:alpha> transparency to
            // the solid fill's color element so it survives the round trip.
            if (solidFill is not null && format.FillAlpha is { } fillAlpha)
                XlsxDrawingColorAlpha.ApplyTo(solidFill.Elements().First(), fillAlpha, drawingNs);
            fill = solidFill ?? (format.NoFill ? new XElement(drawingNs + "noFill") : null);
        }

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
