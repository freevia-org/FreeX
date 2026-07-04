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

    private static IEnumerable<XElement> BuildChartSeries(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null,
        bool forceLineShapeProperties = false)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var seriesStartCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;
        var categoryIsNumeric = chart.FirstColIsCategories &&
            IsCategoryRangeNumeric(sheet, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row);

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var valueRange = verbatim?.ValFormula
                ?? FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            var effectiveCategoryRange = verbatim?.CatFormula ?? categoryRange;
            var effectiveCategoryIsNumeric = verbatim?.CatFormula is null && categoryIsNumeric;

            XElement? txElement = null;
            if (verbatim?.TxFormula is { } txFormula)
            {
                txElement = new XElement(chartNs + "tx",
                    new XElement(chartNs + "strRef",
                        new XElement(chartNs + "f", txFormula)));
            }
            else
            {
                txElement = ToSeriesTitleXml(chart, sheet, col, chartNs);
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
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(effectiveCategoryRange, effectiveCategoryIsNumeric, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))),
                chart.Type is ChartType.Line or ChartType.ThreeDLine || forceLineShapeProperties
                    ? ToSeriesSmoothXml(chart, seriesIndex, chartNs)
                    : null,
                ToRangeDataLabelsExtXml(chart, seriesIndex, chartNs));
            seriesIndex++;
        }
    }

    /// <summary>
    /// Returns true when every non-blank cell in the category column within [dataStartRow, dataEndRow]
    /// contains a numeric value. An all-blank column returns false (fall back to strRef).
    /// </summary>
    private static bool IsCategoryRangeNumeric(Sheet sheet, uint dataStartRow, uint col, uint dataEndRow)
    {
        var hasAnyValue = false;
        for (var row = dataStartRow; row <= dataEndRow; row++)
        {
            var value = sheet.GetValue(row, col);
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

    private static XElement? ToCategoryRangeXml(string? categoryRange, bool numericCategory, XNamespace chartNs)
    {
        if (string.IsNullOrWhiteSpace(categoryRange))
            return null;

        var refElement = numericCategory
            ? new XElement(chartNs + "numRef", new XElement(chartNs + "f", categoryRange))
            : new XElement(chartNs + "strRef", new XElement(chartNs + "f", categoryRange));

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
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var seriesStartCol = chart.DataRange.Start.Col + 1;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            if (includeSeries is not null && !includeSeries(seriesIndex))
            {
                seriesIndex++;
                continue;
            }

            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var effectiveXValueRange = verbatim?.CatFormula ?? xValueRange;
            var yValueRange = verbatim?.ValFormula
                ?? FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);

            XElement? txElement = verbatim?.TxFormula is { } txFormula
                ? new XElement(chartNs + "tx", new XElement(chartNs + "strRef", new XElement(chartNs + "f", txFormula)))
                : ToSeriesTitleXml(chart, sheet, col, chartNs);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                ToScatterSeriesLineShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToSeriesMarkerXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", effectiveXValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))),
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

        return chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
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
        if (chart.DataRange.End.Col - chart.DataRange.Start.Col < 2)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var xValueCol = chart.DataRange.Start.Col;
        var xValueRange = FormatSheetRange(sheet.Name, dataStartRow, xValueCol, chart.DataRange.End.Row, xValueCol);

        var seriesIndex = 0;
        for (var yValueCol = chart.DataRange.Start.Col + 1; yValueCol < chart.DataRange.End.Col; yValueCol += 2)
        {
            var sizeCol = yValueCol + 1;
            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var effectiveXValueRange = verbatim?.CatFormula ?? xValueRange;
            var yValueRange = verbatim?.ValFormula
                ?? FormatSheetRange(sheet.Name, dataStartRow, yValueCol, chart.DataRange.End.Row, yValueCol);
            var sizeRange = FormatSheetRange(sheet.Name, dataStartRow, sizeCol, chart.DataRange.End.Row, sizeCol);

            XElement? txElement = verbatim?.TxFormula is { } txFormula
                ? new XElement(chartNs + "tx", new XElement(chartNs + "strRef", new XElement(chartNs + "f", txFormula)))
                : ToSeriesTitleXml(chart, sheet, yValueCol, chartNs);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToTrendlineXml(chart, seriesIndex, chartNs, drawingNs),
                ToErrorBarsXml(chart, seriesIndex, chartNs, drawingNs),
                new XElement(chartNs + "xVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", effectiveXValueRange))),
                new XElement(chartNs + "yVal",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", yValueRange))),
                new XElement(chartNs + "bubbleSize",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", sizeRange))),
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
        if (chart.FirstColIsCategories && chart.DataRange.End.Col <= chart.DataRange.Start.Col)
            yield break;

        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var firstValueCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;
        var categoryIsNumeric = chart.FirstColIsCategories &&
            IsCategoryRangeNumeric(sheet, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row);

        var seriesIndex = 0;
        for (var valueCol = firstValueCol; valueCol <= chart.DataRange.End.Col; valueCol++)
        {
            var verbatim = GetVerbatimFormulas(chart, seriesIndex);
            var valueRange = verbatim?.ValFormula
                ?? FormatSheetRange(sheet.Name, dataStartRow, valueCol, chart.DataRange.End.Row, valueCol);
            var effectiveCategoryRange = verbatim?.CatFormula ?? categoryRange;
            var effectiveCategoryIsNumeric = verbatim?.CatFormula is null && categoryIsNumeric;

            XElement? txElement = verbatim?.TxFormula is { } txFormula
                ? new XElement(chartNs + "tx", new XElement(chartNs + "strRef", new XElement(chartNs + "f", txFormula)))
                : ToSeriesTitleXml(chart, sheet, valueCol, chartNs);

            yield return new XElement(chartNs + "ser",
                new XElement(chartNs + "idx", new XAttribute("val", seriesIndex)),
                new XElement(chartNs + "order", new XAttribute("val", seriesIndex)),
                txElement,
                ToSeriesShapeProperties(chart, seriesIndex, chartNs, drawingNs),
                ToDataPointsXml(chart, seriesIndex, chartNs, drawingNs),
                ToPointDataLabelsXml(chart, seriesIndex, chartNs, drawingNs),
                ToCategoryRangeXml(effectiveCategoryRange, effectiveCategoryIsNumeric, chartNs),
                new XElement(chartNs + "val",
                    new XElement(chartNs + "numRef",
                        new XElement(chartNs + "f", valueRange))),
                ToRangeDataLabelsExtXml(chart, seriesIndex, chartNs));
            seriesIndex++;
        }
    }

    private static XElement? ToSeriesTitleXml(
        ChartModel chart,
        Sheet sheet,
        uint seriesColumn,
        XNamespace chartNs)
    {
        if (!chart.FirstRowIsHeader)
            return null;

        var titleRange = FormatSheetRange(sheet.Name, chart.DataRange.Start.Row, seriesColumn, chart.DataRange.Start.Row, seriesColumn);
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
    /// either the exploded-slice distance (pie-family series index 0 only, matching Excel) or
    /// an explicit per-point fill color (<see cref="ChartModel.PointFillColors"/>). Child element
    /// order follows CT_DPt: idx, then explosion, then spPr.
    /// </summary>
    private static IEnumerable<XElement> ToDataPointsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        var explodedIndex = seriesIndex == 0 &&
            chart.ExplodedSliceIndex >= 0 &&
            chart.ExplodedSliceIndex < pointCount &&
            chart.ExplodedSliceDistance > 0
            ? chart.ExplodedSliceIndex
            : (int?)null;

        var pointIndexes = chart.PointFillColors
            .Where(point => point.SeriesIndex == seriesIndex)
            .Select(point => point.PointIndex)
            .Concat(explodedIndex is { } idx ? [idx] : [])
            .Distinct()
            .OrderBy(index => index);

        foreach (var pointIndex in pointIndexes)
        {
            var explosion = pointIndex == explodedIndex
                ? new XElement(chartNs + "explosion",
                    new XAttribute("val", Math.Clamp((int)Math.Round(chart.ExplodedSliceDistance * 100), 0, 50)))
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
