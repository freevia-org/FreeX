using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadBarLineComboChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> barCharts,
        IReadOnlyList<XElement> lineCharts,
        IReadOnlyList<XElement> scatterCharts,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var firstBarChart = FirstChartElement(barCharts);
        var barDirection = firstBarChart?.Element(ChartNs + "barDir")?.Attribute("val")?.Value;
        if (barDirection is not ("col" or "bar"))
        {
            chart = new ChartModel();
            return false;
        }

        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = ReadBarChartType(firstBarChart!, barDirection),
            Title = XlsxChartLevelReader.ReadTitle(chartXml),
            UseComboLineForSecondarySeries = true
        };
        ApplyBarChartMetadata(firstBarChart!, result);

        foreach (var barChart in barCharts)
        {
            if (barChart.Element(ChartNs + "barDir")?.Attribute("val")?.Value != barDirection)
            {
                chart = new ChartModel();
                return false;
            }

            var barUsesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, barChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in barChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                // Record the declaration order (legend-position order) — see ChartModel.SeriesPlotOrder.
                result.SeriesPlotOrder.Add(seriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, seriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                if (XlsxChartSeriesRangeReader.TryReadSeriesValueColumn(series, sheetId, sheetNameResolver) is { } barValueColumn)
                    result.SeriesColumnMappings.Add(new ChartSeriesColumnMapping(seriesIndex, barValueColumn));

                // Secondary-axis membership comes straight from which value axis the series' own
                // plot element (<c:barChart>) targets, so it is authoritative even for series index
                // 0 — Excel allows "Format Data Series > Secondary Axis" on any series regardless of
                // position (see the ComboLineSeriesIndexes comment below for the analogous fix).
                if (barUsesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                // R44-io-chart-datapoint-3-1: per-point <c:dPt> fill overrides (e.g. highlighting a
                // single column with Format Data Point > Fill) were previously only read for the
                // pie/doughnut family; bar/column series dropped them silently. ApplyPiePointFills is
                // generic over any <c:ser> with <c:dPt> children, not pie-specific.
                XlsxChartSeriesFormatReader.ApplyPiePointFills(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyRangeDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        foreach (var lineChart in lineCharts)
        {
            XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(lineChart, result);
            var lineUsesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, lineChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in lineChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                result.SeriesPlotOrder.Add(seriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, seriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                result.ComboLineSeriesIndexes.Add(seriesIndex);
                if (XlsxChartSeriesRangeReader.TryReadSeriesValueColumn(series, sheetId, sheetNameResolver) is { } lineValueColumn)
                    result.SeriesColumnMappings.Add(new ChartSeriesColumnMapping(seriesIndex, lineValueColumn));
                // Same rationale as the barChart loop above: <c:lineChart> declaring the secondary
                // axId is authoritative regardless of series index (a line series is frequently
                // plotted first, at idx 0, over a primary-axis column series).
                if (lineUsesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesLine(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                // R44-io-chart-datapoint-3-1: see the barChart loop above.
                XlsxChartSeriesFormatReader.ApplyPiePointFills(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyRangeDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        foreach (var scatterChart in scatterCharts)
        {
            var scatterUsesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, scatterChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in scatterChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                result.SeriesPlotOrder.Add(seriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, seriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                // Scatter series uses xVal/yVal; read their ranges as category/value ranges
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series, "tx", "xVal", "yVal"))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                result.ComboScatterSeriesIndexes.Add(seriesIndex);
                if (XlsxChartSeriesRangeReader.TryReadSeriesValueColumn(series, sheetId, sheetNameResolver, "yVal") is { } scatterValueColumn)
                    result.SeriesColumnMappings.Add(new ChartSeriesColumnMapping(seriesIndex, scatterValueColumn));
                if (scatterUsesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesLine(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                // R44-io-chart-datapoint-3-1: see the barChart loop above.
                XlsxChartSeriesFormatReader.ApplyPiePointFills(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        // When all val/cat formulas are named ranges, fall back to embedded cache data.
        // Also fall back when all formulas are cross-sheet refs (viewport can't provide those cells).
        var allComboSeriesElements = barCharts.Concat(lineCharts).Concat(scatterCharts)
            .SelectMany(c => c.Elements(ChartNs + "ser"))
            .ToList();
        var comboEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allComboSeriesElements, sheetId)
                                ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allComboSeriesElements, sheetId, sheetNameResolver);
        if (comboEmbeddedData is not null)
        {
            // Prefer the actual parsed DataRange so the chart host knows which data sheet is
            // referenced.  Fall back to a synthetic 1×1 placeholder only when no formula was parseable.
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.FirstRowIsHeader = hasTitleRange;
            result.FirstColIsCategories = hasCategoryRange;
            result.EmbeddedSeriesData = comboEmbeddedData;
            // R108-io-chart-series-embedded-fastpath: this branch returns early (before the
            // SeriesInRows/ApplyVerbatimSeriesFormulasIfNeeded calls further down that every other
            // return path in this function reaches) precisely BECAUSE every series' val/cat formula
            // is a named range or a cross-sheet reference — i.e. exactly the shape that
            // ApplyVerbatimSeriesFormulasIfNeeded exists to capture. Skipping these two calls here
            // left chart.VerbatimSeriesFormulas null and chart.SeriesInRows false-by-default, so
            // XlsxChartXmlWriter had nothing but a (frequently degenerate) recomputed chart.DataRange
            // to re-derive series from on save — silently dropping the whole series set. Must run
            // regardless of which return branch is taken.
            result.SeriesColumnMappings = NormalizeSeriesColumnMappings(result.SeriesColumnMappings);
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                barCharts.Concat(lineCharts).SelectMany(c => c.Elements(ChartNs + "ser")),
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(
                barCharts.Concat(lineCharts).Concat(scatterCharts).SelectMany(c => c.Elements(ChartNs + "ser")),
                sheetId,
                sheetNameResolver,
                result);
            XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
            XlsxChartSanitizer.SanitizeLoadedChart(result);
            chart = result;
            return true;
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        // Secondary-axis membership is authoritative even for series index 0 (see the per-loop
        // comments above) — do NOT drop index 0 here, matching the ComboLineSeriesIndexes fix below.
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        // Combo line/scatter membership comes straight from which plot element the series lived in
        // (<c:lineChart>/<c:scatterChart>), so it is authoritative even for series index 0 — Excel
        // frequently emits the line series first (e.g. a "shaded target band" chart where Qty is the
        // line at idx 0 over Target helper columns). Do NOT drop index 0 here or that line collapses
        // back into a column.
        result.ComboLineSeriesIndexes = result.ComboLineSeriesIndexes
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToList();
        result.ComboScatterSeriesIndexes = result.ComboScatterSeriesIndexes
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.UseComboLineForSecondarySeries = result.ComboLineSeriesIndexes.Count > 0;
        result.SeriesColumnMappings = NormalizeSeriesColumnMappings(result.SeriesColumnMappings);
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            barCharts.Concat(lineCharts).SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            barCharts.Concat(lineCharts).Concat(scatterCharts).SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static bool TryReadBarChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> barCharts,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var firstBarChart = FirstChartElement(barCharts);
        var barDirection = firstBarChart?.Element(ChartNs + "barDir")?.Attribute("val")?.Value;
        if (barDirection is not ("col" or "bar"))
        {
            chart = new ChartModel();
            return false;
        }

        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = ReadBarChartType(firstBarChart!, barDirection),
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };
        ApplyBarChartMetadata(firstBarChart!, result);

        foreach (var barChart in barCharts)
        {
            if (barChart.Element(ChartNs + "barDir")?.Attribute("val")?.Value != barDirection)
            {
                chart = new ChartModel();
                return false;
            }

            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, barChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in barChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                result.SeriesPlotOrder.Add(seriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, seriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                // See the combo-chart loops above: secondary-axis membership is authoritative for
                // any series index, including 0 (Excel allows Format Data Series > Secondary Axis
                // on the first series just as it does on any other).
                if (usesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                if (XlsxChartSeriesRangeReader.TryReadSeriesValueColumn(series, sheetId, sheetNameResolver) is { } valueColumn)
                    result.SeriesColumnMappings.Add(new ChartSeriesColumnMapping(seriesIndex, valueColumn));

                // R44-io-chart-datapoint-3-1: per-point <c:dPt> fill overrides (e.g. highlighting a
                // single column with Format Data Point > Fill) were previously only read for the
                // pie/doughnut family; bar/column series dropped them silently. ApplyPiePointFills is
                // generic over any <c:ser> with <c:dPt> children, not pie-specific.
                XlsxChartSeriesFormatReader.ApplyPiePointFills(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartDataLabelReader.ApplyRangeDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        // When all val/cat formulas are named ranges (e.g. OFFSET-based dynamic names like
        // 'Sheet1'!rngCount), TryParseFormulaRange fails and ranges stays empty or only
        // contains the tx (title) cell.  Fall back to the embedded numCache/strCache values.
        // Also fall back when all formulas are cross-sheet cell refs — the viewport live-cell
        // lookup only covers the chart's host sheet, so cross-sheet cells yield nothing.
        var allBarSeriesElements = barCharts.SelectMany(c => c.Elements(ChartNs + "ser")).ToList();
        var embeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allBarSeriesElements, sheetId)
                           ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allBarSeriesElements, sheetId, sheetNameResolver);
        if (embeddedData is not null)
        {
            // Prefer the actual parsed DataRange so the chart host knows which data sheet is
            // referenced (important for BuildChartCellLookup sheet-ID matching).  Fall back to
            // a synthetic 1×1 placeholder only when no formula could be parsed (e.g. all named-range
            // formulas that TryParseFormulaRange cannot decode).
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.FirstRowIsHeader = hasTitleRange;
            result.FirstColIsCategories = hasCategoryRange;
            result.EmbeddedSeriesData = embeddedData;
            // R108-io-chart-series-embedded-fastpath: see the identical comment in
            // TryReadBarLineComboChart above — this branch returns early precisely BECAUSE every
            // series' val/cat formula is a named range or cross-sheet reference, exactly the shape
            // ApplyVerbatimSeriesFormulasIfNeeded/DetectSeriesInRows exist to capture. Skipping them
            // here left chart.VerbatimSeriesFormulas null and chart.SeriesInRows false, so
            // XlsxChartXmlWriter had nothing to re-derive the series from on save but a frequently
            // degenerate recomputed chart.DataRange — silently dropping the whole series set.
            result.SeriesColumnMappings = NormalizeSeriesColumnMappings(result.SeriesColumnMappings);
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                barCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(
                barCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
                sheetId,
                sheetNameResolver,
                result);
            XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
            XlsxChartSanitizer.SanitizeLoadedChart(result);
            chart = result;
            return true;
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.SeriesColumnMappings = NormalizeSeriesColumnMappings(result.SeriesColumnMappings);
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            barCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            barCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    /// <summary>
    /// De-duplicates (by series idx, keeping the first) and orders series-column mappings by their
    /// declared chart-XML index. The renderer treats a non-empty, gap-free mapping as the
    /// authoritative series list; partial/ambiguous mappings are still passed through and the
    /// renderer decides whether to trust them.
    /// </summary>
    private static List<ChartSeriesColumnMapping> NormalizeSeriesColumnMappings(
        List<ChartSeriesColumnMapping> mappings)
    {
        if (mappings.Count == 0)
            return mappings;

        var seen = new HashSet<int>();
        var result = new List<ChartSeriesColumnMapping>(mappings.Count);
        foreach (var mapping in mappings.OrderBy(m => m.SeriesXmlIndex))
        {
            if (seen.Add(mapping.SeriesXmlIndex))
                result.Add(mapping);
        }

        return result;
    }

    private static void ApplyBarChartMetadata(XElement barChart, ChartModel chart)
    {
        chart.BarGapWidth = NormalizeExcelNativeDefaultBarGapWidth(
            chart.Type,
            XlsxChartScalarReader.ReadOptionalInt(barChart.Element(ChartNs + "gapWidth")?.Attribute("val")?.Value));
        chart.BarOverlap = NormalizeExcelNativeDefaultBarOverlap(
            chart.Type,
            XlsxChartScalarReader.ReadOptionalInt(barChart.Element(ChartNs + "overlap")?.Attribute("val")?.Value));
        chart.VaryColorsByPoint = XlsxChartScalarReader.ReadOptionalBool(barChart.Element(ChartNs + "varyColors")?.Attribute("val")?.Value);
        XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(barChart, chart);
    }

    // Mirror the writer's Excel-native defaults so a default chart round-trips to null.
    private static int? NormalizeExcelNativeDefaultBarGapWidth(ChartType chartType, int? gapWidth) =>
        gapWidth == 219 && chartType is (ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDColumn
            or ChartType.ThreeDBar)
                ? null
                : gapWidth;

    private static int? NormalizeExcelNativeDefaultBarOverlap(ChartType chartType, int? overlap) =>
        overlap == -27 && chartType is (ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar)
                ? null
                : overlap;

    private static ChartType ReadBarChartType(XElement barChart, string? barDirection)
    {
        var grouping = barChart.Element(ChartNs + "grouping")?.Attribute("val")?.Value;
        return (barDirection, grouping) switch
        {
            ("bar", "stacked") => ChartType.StackedBar,
            ("bar", "percentStacked") => ChartType.PercentStackedBar,
            ("bar", _) => ChartType.Bar,
            (_, "stacked") => ChartType.StackedColumn,
            (_, "percentStacked") => ChartType.PercentStackedColumn,
            _ => ChartType.Column
        };
    }
}
