using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static XElement? ToPointDataLabelsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var pointCount = ChartTypeSupport.GetDataPointCount(chart);
        var labels = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex == seriesIndex && format.PointIndex >= 0 && format.PointIndex < pointCount)
            .GroupBy(format => format.PointIndex)
            .Select(group => group.Last())
            .Where(format => ChartFormatPresence.HasPointDataLabelFormatting(
                format,
                includeLayoutAndCustomText: true))
            .OrderBy(format => format.PointIndex)
            .Select(format => ToPointDataLabelXml(format, chart.Type, chartNs, drawingNs))
            .ToArray();
        ChartSeriesDataLabelFormat? seriesDefaults = null;
        foreach (var format in chart.SeriesDataLabelFormats)
        {
            if (format.SeriesIndex == seriesIndex)
                seriesDefaults = format;
        }

        if (seriesDefaults is not null && !ChartFormatPresence.HasSeriesDataLabelFormatting(
                seriesDefaults,
                includeDeletion: true))
            seriesDefaults = null;

        return labels.Length == 0 && seriesDefaults is null
            ? null
            : new XElement(chartNs + "dLbls",
                labels,
                seriesDefaults is null ? null : ToSeriesDataLabelDefaultsXml(seriesDefaults, chart.Type, chartNs, drawingNs));
    }

    private static IEnumerable<XElement?> ToSeriesDataLabelDefaultsXml(
        ChartSeriesDataLabelFormat format,
        ChartType chartType,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        // R63-io-chart-legend-datalabels-6-1 (write side): CT_DLbls' trailing content is a choice
        // between <c:delete> and the Group_DLbls defaults (numFmt, spPr, txPr, dLblPos, show*,
        // separator) -- they are mutually exclusive per the OOXML schema, so a series-level
        // "delete all data labels" must emit ONLY <c:delete val="1"/> and suppress the rest.
        // Without this, a round trip (load -> save -> reload) resurrects the labels the user
        // deleted, because the delete flag would be silently dropped on save.
        if (format.IsDeleted == true)
        {
            yield return new XElement(chartNs + "delete", new XAttribute("val", "1"));
            yield break;
        }

        yield return ToSeriesDataLabelNumberFormatXml(format, chartNs);
        yield return ToShapeProperties(
            chartNs,
            drawingNs,
            format.FillThemeColor,
            format.FillColor,
            format.BorderThemeColor,
            format.BorderColor,
            format.BorderThickness);
        yield return ToSeriesDataLabelTextProperties(format, chartNs, drawingNs);
        // R11-xlsx-charts-2: gate the same as the chart-level dLblPos (GatedDataLabelPositionXml) —
        // an ungated position can survive a chart-type change (e.g. clustered column -> area/stacked)
        // into a family that has no valid dLblPos at all, or only accepts ctr, and Excel repairs/drops
        // the whole chart on open.
        yield return format.Position is { } position && GateDataLabelPosition(ToXlsxDataLabelPosition(position), chartType) is { } gatedSeriesPosition
            ? new XElement(chartNs + "dLblPos", new XAttribute("val", gatedSeriesPosition))
            : null;
        yield return ToPointDataLabelBoolXml("showLegendKey", format.ShowLegendKey, chartNs);
        yield return ToPointDataLabelBoolXml("showVal", format.ShowValue, chartNs);
        yield return ToPointDataLabelBoolXml("showCatName", format.ShowCategoryName, chartNs);
        yield return ToPointDataLabelBoolXml("showSerName", format.ShowSeriesName, chartNs);
        yield return ToPointDataLabelBoolXml("showPercent", format.ShowPercentage, chartNs);
        yield return ToPointDataLabelBoolXml("showBubbleSize", format.ShowBubbleSize, chartNs);
        yield return format.SeparatorText is { } separator
            ? new XElement(chartNs + "separator", separator)
            : null;
    }

    private static XElement ToPointDataLabelXml(
        ChartPointDataLabelFormat format,
        ChartType chartType,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "dLbl",
            new XElement(chartNs + "idx", new XAttribute("val", format.PointIndex)),
            ToPointDataLabelBodyXml(format, chartType, chartNs, drawingNs));

    /// <summary>
    /// R64-io-chart-datalabel-point-delete (round 67, completing R62/R63 for the per-point case):
    /// CT_DLbl's trailing content (after &lt;c:idx&gt;) is a &lt;xsd:choice&gt; between
    /// &lt;c:delete&gt; and the Group_DLbl defaults (layout, tx, numFmt, spPr, txPr, dLblPos, show*,
    /// separator) -- they are mutually exclusive per the OOXML schema, exactly like CT_DLbls at the
    /// series level (see <see cref="ToSeriesDataLabelDefaultsXml"/>, the template for this fix). A
    /// per-point "delete this point's label" must emit ONLY &lt;c:delete val="1"/&gt; and suppress
    /// the rest, or Excel rejects the chart part with a repair prompt on reload. Matching the r63
    /// series-level pattern, an explicit IsDeleted == false is not re-emitted as
    /// &lt;c:delete val="0"/&gt; -- it is treated the same as IsDeleted == null (no &lt;c:delete&gt;
    /// at all, just the ordinary Group_DLbl content).
    /// </summary>
    private static IEnumerable<XElement?> ToPointDataLabelBodyXml(
        ChartPointDataLabelFormat format,
        ChartType chartType,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (format.IsDeleted == true)
        {
            yield return new XElement(chartNs + "delete", new XAttribute("val", "1"));
            yield break;
        }

        // Schema order for CT_DLbl/Group_DLbl: idx, delete?, layout?, tx?, numFmt?, spPr?,
        // txPr?, dLblPos?, show*?, separator?.
        yield return ToManualLayoutXml(format.Layout, chartNs);
        yield return TryParseChartXml(format.CustomTextXml);
        yield return ToPointDataLabelNumberFormatXml(format, chartNs);
        yield return ToShapeProperties(
            chartNs,
            drawingNs,
            format.FillThemeColor,
            format.FillColor,
            format.BorderThemeColor,
            format.BorderColor,
            format.BorderThickness);
        yield return ToPointDataLabelTextProperties(format, chartNs, drawingNs);
        // R11-xlsx-charts-2: gate the same as the chart-level dLblPos — an ungated per-point
        // position can survive a chart-type change into a family with no valid dLblPos at all
        // (e.g. area) or one that only accepts ctr (e.g. stacked column), and Excel repairs/drops
        // the whole chart on open.
        yield return format.Position is { } position && GateDataLabelPosition(ToXlsxDataLabelPosition(position), chartType) is { } gatedPosition
            ? new XElement(chartNs + "dLblPos", new XAttribute("val", gatedPosition))
            : null;
        yield return ToPointDataLabelBoolXml("showLegendKey", format.ShowLegendKey, chartNs);
        yield return ToPointDataLabelBoolXml("showVal", format.ShowValue, chartNs);
        yield return ToPointDataLabelBoolXml("showCatName", format.ShowCategoryName, chartNs);
        yield return ToPointDataLabelBoolXml("showSerName", format.ShowSeriesName, chartNs);
        yield return ToPointDataLabelBoolXml("showPercent", format.ShowPercentage, chartNs);
        yield return ToPointDataLabelBoolXml("showBubbleSize", format.ShowBubbleSize, chartNs);
        yield return format.SeparatorText is { } separator
            ? new XElement(chartNs + "separator", separator)
            : null;
    }

    private static XElement? ToPointDataLabelBoolXml(string name, bool? value, XNamespace chartNs) =>
        value is { } flag
            ? new XElement(chartNs + name, new XAttribute("val", flag ? "1" : "0"))
            : null;

    private static XElement? ToPointDataLabelNumberFormatXml(ChartPointDataLabelFormat format, XNamespace chartNs) =>
        string.IsNullOrEmpty(format.NumberFormatCode) && format.NumberFormatSourceLinked is null
            ? null
            : new XElement(chartNs + "numFmt",
                string.IsNullOrEmpty(format.NumberFormatCode)
                    ? null
                    : new XAttribute("formatCode", format.NumberFormatCode),
                format.NumberFormatSourceLinked is { } sourceLinked
                    ? new XAttribute("sourceLinked", sourceLinked ? "1" : "0")
                    : null);

    private static XElement? ToSeriesDataLabelNumberFormatXml(ChartSeriesDataLabelFormat format, XNamespace chartNs) =>
        string.IsNullOrEmpty(format.NumberFormatCode) && format.NumberFormatSourceLinked is null
            ? null
            : new XElement(chartNs + "numFmt",
                string.IsNullOrEmpty(format.NumberFormatCode)
                    ? null
                    : new XAttribute("formatCode", format.NumberFormatCode),
                format.NumberFormatSourceLinked is { } sourceLinked
                    ? new XAttribute("sourceLinked", sourceLinked ? "1" : "0")
                    : null);

    private static XElement? ToPointDataLabelTextProperties(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(format.TextThemeColor, format.TextColor, drawingNs);
        if (textFill is null && format.FontSize is null)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(0, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        format.FontSize is { } fontSize
                            ? new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200))
                            : null,
                        textFill))));
    }

    private static XElement? ToTrendlineXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!chart.ShowLinearTrendline || seriesIndex != chart.TrendlineSeriesIndex || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return null;

        return new XElement(chartNs + "trendline",
            string.IsNullOrWhiteSpace(chart.TrendlineName)
                ? null
                : new XElement(chartNs + "name", chart.TrendlineName),
            ToTrendlineShapeProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "trendlineType",
                new XAttribute("val", ToXlsxTrendlineType(chart.TrendlineType))),
            chart.TrendlineType == ChartTrendlineType.Polynomial
                ? new XElement(chartNs + "order", new XAttribute("val", Math.Clamp(chart.TrendlineOrder, 2, 6)))
                : null,
            chart.TrendlineType == ChartTrendlineType.MovingAverage
                ? new XElement(chartNs + "period", new XAttribute("val", Math.Max(2, chart.TrendlinePeriod)))
                : null,
            ToOptionalTrendlineDoubleXml("forward", chart.TrendlineForward, chartNs),
            ToOptionalTrendlineDoubleXml("backward", chart.TrendlineBackward, chartNs),
            ToOptionalTrendlineDoubleXml("intercept", chart.TrendlineIntercept, chartNs),
            new XElement(chartNs + "dispRSqr", new XAttribute("val", chart.ShowTrendlineRSquared ? "1" : "0")),
            new XElement(chartNs + "dispEq", new XAttribute("val", chart.ShowTrendlineEquation ? "1" : "0")),
            ToTrendlineLabelXml(chart, chartNs, drawingNs));
    }

    private static XElement? ToOptionalTrendlineDoubleXml(string name, double? value, XNamespace chartNs) =>
        value is { } number && double.IsFinite(number)
            ? new XElement(chartNs + name, new XAttribute("val", number.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToTrendlineLabelXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        var layout = ToManualLayoutXml(chart.TrendlineLabelLayout, chartNs);
        var shapeProperties = ToShapeProperties(
            chartNs,
            drawingNs,
            chart.TrendlineLabelFillThemeColor,
            chart.TrendlineLabelFillColor,
            chart.TrendlineLabelBorderThemeColor,
            chart.TrendlineLabelBorderColor,
            chart.TrendlineLabelBorderThickness);
        var textProperties = ToTrendlineLabelTextProperties(chart, chartNs, drawingNs);
        var numberFormat = ToTrendlineLabelNumberFormatXml(chart, chartNs);

        return layout is null && shapeProperties is null && textProperties is null && numberFormat is null
            ? null
            : new XElement(chartNs + "trendlineLbl", layout, numberFormat, shapeProperties, textProperties);
    }

    private static XElement? ToTrendlineLabelNumberFormatXml(ChartModel chart, XNamespace chartNs) =>
        string.IsNullOrEmpty(chart.TrendlineLabelNumberFormatCode) && chart.TrendlineLabelNumberFormatSourceLinked is null
            ? null
            : new XElement(chartNs + "numFmt",
                string.IsNullOrEmpty(chart.TrendlineLabelNumberFormatCode)
                    ? null
                    : new XAttribute("formatCode", chart.TrendlineLabelNumberFormatCode),
                chart.TrendlineLabelNumberFormatSourceLinked is { } sourceLinked
                    ? new XAttribute("sourceLinked", sourceLinked ? "1" : "0")
                    : null);

    private static XElement? ToTrendlineLabelTextProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(chart.TrendlineLabelTextThemeColor, chart.TrendlineLabelTextColor, drawingNs);
        if (textFill is null && chart.TrendlineLabelFontSize is null && chart.TrendlineLabelAngle is null)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(chart.TrendlineLabelAngle ?? 0, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        chart.TrendlineLabelFontSize is { } fontSize
                            ? new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200))
                            : null,
                        textFill))));
    }

    private static XElement? ToSeriesDataLabelTextProperties(
        ChartSeriesDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(format.TextThemeColor, format.TextColor, drawingNs);
        if (textFill is null && format.FontSize is null)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(0, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        format.FontSize is { } fontSize
                            ? new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200))
                            : null,
                        textFill))));
    }

    private static XElement? ToTrendlineShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(chart.TrendlineThemeColor, chart.TrendlineColor, drawingNs);
        if (fill is null && chart.TrendlineThickness == 1.5 && chart.TrendlineDashStyle == ChartLineDashStyle.Solid)
            return null;

        return new XElement(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(chart.TrendlineThickness, 0.5, 10) * DrawingMlUnits.EmuPerPoint))),
                fill,
                ToPresetDash(chart.TrendlineDashStyle, drawingNs)));
    }

    private static XElement? ToPresetDash(ChartLineDashStyle dashStyle, XNamespace drawingNs) =>
        dashStyle == ChartLineDashStyle.Solid
            ? null
            : new XElement(drawingNs + "prstDash",
                new XAttribute("val", dashStyle == ChartLineDashStyle.Dot ? "dot" : "dash"));

    private static string ToXlsxMarkerStyle(ChartMarkerStyle markerStyle) =>
        markerStyle switch
        {
            ChartMarkerStyle.None => "none",
            ChartMarkerStyle.Square => "square",
            ChartMarkerStyle.Diamond => "diamond",
            ChartMarkerStyle.Triangle => "triangle",
            // R65-default-fallback-swallow-sweep-2: keep these distinct instead of collapsing to
            // "circle" on write.
            ChartMarkerStyle.X => "x",
            ChartMarkerStyle.Star => "star",
            ChartMarkerStyle.Plus => "plus",
            ChartMarkerStyle.Dot => "dot",
            ChartMarkerStyle.Dash => "dash",
            ChartMarkerStyle.Auto => "auto",
            _ => "circle"
        };

    private static string ToXlsxTrendlineType(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => "exp",
            ChartTrendlineType.Logarithmic => "log",
            ChartTrendlineType.Power => "power",
            ChartTrendlineType.MovingAverage => "movingAvg",
            ChartTrendlineType.Polynomial => "poly",
            _ => "linear"
        };

    private static XElement? ToErrorBarsXml(
        ChartModel chart,
        int seriesIndex,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!chart.ShowErrorBars || seriesIndex != chart.ErrorBarSeriesIndex || !SupportsErrorBars(chart.Type))
            return null;

        return new XElement(chartNs + "errBars",
            new XElement(chartNs + "errDir", new XAttribute("val", ToXlsxErrorBarAxisDirection(chart.ErrorBarAxisDirection))),
            new XElement(chartNs + "errBarType", new XAttribute("val", ToXlsxErrorBarDirection(chart.ErrorBarDirection))),
            new XElement(chartNs + "errValType", new XAttribute("val", ToXlsxErrorBarKind(chart.ErrorBarKind))),
            chart.ErrorBarEndCaps ? null : new XElement(chartNs + "noEndCap", new XAttribute("val", "1")),
            // R41-io-chart-errorbars-trendline-3-1: StdDev also carries a user-set multiplier
            // (like Percentage/FixedValue) — StandardError and Custom are the only kinds with no
            // <c:val> (StandardError has no user-configurable value; Custom uses plus/minus ranges).
            chart.ErrorBarKind is ChartErrorBarKind.Percentage or ChartErrorBarKind.FixedValue or ChartErrorBarKind.StdDev
                ? new XElement(chartNs + "val", new XAttribute("val", Math.Clamp(chart.ErrorBarValue, 0, 1000).ToString(CultureInfo.InvariantCulture)))
                : null,
            chart.ErrorBarKind == ChartErrorBarKind.Custom
                ? ToErrorBarRangeXml("plus", chart.ErrorBarPlusRangeFormula, chart.ErrorBarPlusRangeCacheXml, chartNs)
                : null,
            chart.ErrorBarKind == ChartErrorBarKind.Custom
                ? ToErrorBarRangeXml("minus", chart.ErrorBarMinusRangeFormula, chart.ErrorBarMinusRangeCacheXml, chartNs)
                : null,
            ToErrorBarShapeProperties(chart, chartNs, drawingNs));
    }

    private static XElement? ToErrorBarRangeXml(string name, string? formula, string? cacheXml, XNamespace chartNs) =>
        string.IsNullOrWhiteSpace(formula)
            ? null
            : new XElement(chartNs + name,
                new XElement(chartNs + "numRef",
                    new XElement(chartNs + "f", formula),
                    TryParseChartXml(cacheXml)));

    private static XElement? TryParseChartXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            return XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static XElement? ToErrorBarShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(chart.ErrorBarThemeColor, chart.ErrorBarColor, drawingNs);
        if (fill is null && chart.ErrorBarThickness == 1 && chart.ErrorBarDashStyle == ChartLineDashStyle.Solid)
            return null;

        return new XElement(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(chart.ErrorBarThickness, 0.5, 10) * DrawingMlUnits.EmuPerPoint))),
                fill,
                ToPresetDash(chart.ErrorBarDashStyle, drawingNs)));
    }

    private static bool SupportsErrorBars(ChartType chartType) =>
        ChartTypeSupport.SupportsTrendlines(chartType);

    private static string ToXlsxErrorBarKind(ChartErrorBarKind kind) =>
        kind switch
        {
            ChartErrorBarKind.Percentage => "percentage",
            ChartErrorBarKind.FixedValue => "fixedVal",
            ChartErrorBarKind.Custom => "cust",
            // R41-io-chart-errorbars-trendline-3-1: StdDev must round-trip to "stdDev", not fall
            // through to the StandardError default ("stdErr") — they are different Excel
            // error-amount kinds with visually different bar lengths.
            ChartErrorBarKind.StdDev => "stdDev",
            _ => "stdErr"
        };

    private static string ToXlsxErrorBarAxisDirection(ChartErrorBarAxisDirection direction) =>
        direction == ChartErrorBarAxisDirection.X ? "x" : "y";

    private static string ToXlsxErrorBarDirection(ChartErrorBarDirection direction) =>
        direction switch
        {
            ChartErrorBarDirection.Plus => "plus",
            ChartErrorBarDirection.Minus => "minus",
            _ => "both"
        };
}
