using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
            .Select(format => ToPointDataLabelXml(format, chartNs, drawingNs))
            .ToArray();

        return labels.Length == 0
            ? null
            : new XElement(chartNs + "dLbls", labels);
    }

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null;

    private static XElement ToPointDataLabelXml(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "dLbl",
            new XElement(chartNs + "idx", new XAttribute("val", format.PointIndex)),
            ToShapeProperties(
                chartNs,
                drawingNs,
                format.FillThemeColor,
                format.FillColor,
                format.BorderThemeColor,
                format.BorderColor,
                format.BorderThickness),
            ToPointDataLabelTextProperties(format, chartNs, drawingNs));

    private static XElement? ToPointDataLabelTextProperties(
        ChartPointDataLabelFormat format,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(format.TextThemeColor, format.TextColor, drawingNs);
        if (textFill is null && format.FontSize is null)
            return null;

        return new XElement(chartNs + "txPr",
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
        if (!chart.ShowLinearTrendline || seriesIndex != 0 || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return null;

        return new XElement(chartNs + "trendline",
            new XElement(chartNs + "trendlineType",
                new XAttribute("val", ToXlsxTrendlineType(chart.TrendlineType))),
            chart.TrendlineType == ChartTrendlineType.Polynomial
                ? new XElement(chartNs + "order", new XAttribute("val", Math.Clamp(chart.TrendlineOrder, 2, 6)))
                : null,
            chart.TrendlineType == ChartTrendlineType.MovingAverage
                ? new XElement(chartNs + "period", new XAttribute("val", Math.Max(2, chart.TrendlinePeriod)))
                : null,
            ToTrendlineShapeProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "dispEq", new XAttribute("val", chart.ShowTrendlineEquation ? "1" : "0")),
            new XElement(chartNs + "dispRSqr", new XAttribute("val", chart.ShowTrendlineRSquared ? "1" : "0")));
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
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(chart.TrendlineThickness, 0.5, 10) * 12700))),
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
        if (!chart.ShowErrorBars || seriesIndex != 0 || !SupportsErrorBars(chart.Type))
            return null;

        return new XElement(chartNs + "errBars",
            new XElement(chartNs + "errBarType", new XAttribute("val", ToXlsxErrorBarDirection(chart.ErrorBarDirection))),
            new XElement(chartNs + "errValType", new XAttribute("val", ToXlsxErrorBarKind(chart.ErrorBarKind))),
            chart.ErrorBarEndCaps ? null : new XElement(chartNs + "noEndCap", new XAttribute("val", "1")),
            chart.ErrorBarKind is ChartErrorBarKind.Percentage or ChartErrorBarKind.FixedValue
                ? new XElement(chartNs + "val", new XAttribute("val", Math.Clamp(chart.ErrorBarValue, 0, 1000).ToString(CultureInfo.InvariantCulture)))
                : null,
            ToErrorBarShapeProperties(chart, chartNs, drawingNs));
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
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(chart.ErrorBarThickness, 0.5, 10) * 12700))),
                fill,
                ToPresetDash(chart.ErrorBarDashStyle, drawingNs)));
    }

    private static bool SupportsErrorBars(ChartType chartType) =>
        chartType is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or
            ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or
            ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter or ChartType.Area or ChartType.ThreeDArea;

    private static string ToXlsxErrorBarKind(ChartErrorBarKind kind) =>
        kind switch
        {
            ChartErrorBarKind.Percentage => "percentage",
            ChartErrorBarKind.FixedValue => "fixedVal",
            _ => "stdErr"
        };

    private static string ToXlsxErrorBarDirection(ChartErrorBarDirection direction) =>
        direction switch
        {
            ChartErrorBarDirection.Plus => "plus",
            ChartErrorBarDirection.Minus => "minus",
            _ => "both"
        };
}
