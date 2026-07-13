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
            .Where(HasPointDataLabelFormatting)
            .OrderBy(format => format.PointIndex)
            .Select(format => ToPointDataLabelXml(format, chart.Type, chartNs, drawingNs))
            .ToArray();
        ChartSeriesDataLabelFormat? seriesDefaults = null;
        foreach (var format in chart.SeriesDataLabelFormats)
        {
            if (format.SeriesIndex == seriesIndex)
                seriesDefaults = format;
        }

        if (seriesDefaults is not null && !HasSeriesDataLabelFormatting(seriesDefaults))
            seriesDefaults = null;

        return labels.Length == 0 && seriesDefaults is null
            ? null
            : new XElement(chartNs + "dLbls",
                labels,
                seriesDefaults is null ? null : ToSeriesDataLabelDefaultsXml(seriesDefaults, chart.Type, chartNs, drawingNs));
    }

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null
        || format.IsDeleted is not null
        || format.Position is not null
        || format.ShowValue is not null
        || format.ShowCategoryName is not null
        || format.ShowSeriesName is not null
        || format.ShowLegendKey is not null
        || format.ShowPercentage is not null
        || format.ShowBubbleSize is not null
        || !string.IsNullOrEmpty(format.NumberFormatCode)
        || format.NumberFormatSourceLinked is not null
        || format.SeparatorText is not null
        || format.Layout is not null
        || !string.IsNullOrEmpty(format.CustomTextXml);

    private static bool HasSeriesDataLabelFormatting(ChartSeriesDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null
        || format.Position is not null
        || format.ShowValue is not null
        || format.ShowCategoryName is not null
        || format.ShowSeriesName is not null
        || format.ShowLegendKey is not null
        || format.ShowPercentage is not null
        || format.ShowBubbleSize is not null
        || !string.IsNullOrEmpty(format.NumberFormatCode)
        || format.NumberFormatSourceLinked is not null
        || format.SeparatorText is not null;

    private static IEnumerable<XElement?> ToSeriesDataLabelDefaultsXml(
        ChartSeriesDataLabelFormat format,
        ChartType chartType,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
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
            format.IsDeleted is { } isDeleted
                ? new XElement(chartNs + "delete", new XAttribute("val", isDeleted ? "1" : "0"))
                : null,
            // Schema order for CT_DLbl/Group_DLbl: idx, delete?, layout?, tx?, numFmt?, spPr?,
            // txPr?, dLblPos?, show*?, separator?.
            ToManualLayoutXml(format.Layout, chartNs),
            TryParseChartXml(format.CustomTextXml),
            ToPointDataLabelNumberFormatXml(format, chartNs),
            ToShapeProperties(
                chartNs,
                drawingNs,
                format.FillThemeColor,
                format.FillColor,
                format.BorderThemeColor,
                format.BorderColor,
                format.BorderThickness),
            ToPointDataLabelTextProperties(format, chartNs, drawingNs),
            // R11-xlsx-charts-2: gate the same as the chart-level dLblPos — an ungated per-point
            // position can survive a chart-type change into a family with no valid dLblPos at all
            // (e.g. area) or one that only accepts ctr (e.g. stacked column), and Excel repairs/drops
            // the whole chart on open.
            format.Position is { } position && GateDataLabelPosition(ToXlsxDataLabelPosition(position), chartType) is { } gatedPosition
                ? new XElement(chartNs + "dLblPos", new XAttribute("val", gatedPosition))
                : null,
            ToPointDataLabelBoolXml("showLegendKey", format.ShowLegendKey, chartNs),
            ToPointDataLabelBoolXml("showVal", format.ShowValue, chartNs),
            ToPointDataLabelBoolXml("showCatName", format.ShowCategoryName, chartNs),
            ToPointDataLabelBoolXml("showSerName", format.ShowSeriesName, chartNs),
            ToPointDataLabelBoolXml("showPercent", format.ShowPercentage, chartNs),
            ToPointDataLabelBoolXml("showBubbleSize", format.ShowBubbleSize, chartNs),
            format.SeparatorText is { } separator
                ? new XElement(chartNs + "separator", separator)
                : null);

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
            chart.ErrorBarKind is ChartErrorBarKind.Percentage or ChartErrorBarKind.FixedValue
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
