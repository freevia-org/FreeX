using System.Globalization;
using System.Xml.Linq;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static IEnumerable<XElement> ToChartAxesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
        {
            yield return ToValueAxisXml(
                chart.XAxisTitle,
                CategoryAxisId,
                ValueAxisId,
                "b",
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisNumberFormat,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisMajorGridlineColor,
                chart.XAxisMinorGridlineColor,
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.XAxisLineColor,
                chart.XAxisLineThickness,
                chart.ShowXAxisLabels,
                chart.XAxisLabelTextColor,
                chart.XAxisLabelFontSize,
                chart.XAxisLabelAngle,
                chart.XAxisLabelTextThemeColor,
                chart.AxisTitleTextThemeColor,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chart.XAxisCrosses,
                chart.XAxisCrossesAt,
                chart.XAxisCrossBetween,
                chart.XAxisDisplayUnit,
                chartNs,
                drawingNs);
            yield return ToValueAxisXml(
                chart.YAxisTitle,
                ValueAxisId,
                CategoryAxisId,
                "l",
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                chart.YAxisMajorGridlineColor,
                chart.YAxisMinorGridlineColor,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.YAxisLabelTextColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                chart.YAxisLabelTextThemeColor,
                chart.AxisTitleTextThemeColor,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chart.YAxisCrosses,
                chart.YAxisCrossesAt,
                chart.YAxisCrossBetween,
                chart.YAxisDisplayUnit,
                chartNs,
                drawingNs);
            var scatterSecondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
            if (chart.Type == ChartType.Scatter && scatterSecondaryIndexes.Count > 0)
            {
                yield return ToValueAxisXml(
                    null,
                    SecondaryValueAxisId,
                    CategoryAxisId,
                    "r",
                    chart.YAxisMinimum,
                    chart.YAxisMaximum,
                    chart.YAxisMajorUnit,
                    chart.YAxisMinorUnit,
                    chart.YAxisLogScale,
                    chart.YAxisNumberFormat,
                    false,
                    false,
                    null,
                    null,
                    chart.YAxisGridlineThickness,
                    chart.YAxisMajorTickStyle,
                    chart.YAxisMinorTickStyle,
                    chart.YAxisLineColor,
                    chart.YAxisLineThickness,
                    chart.ShowYAxisLabels,
                    chart.YAxisLabelTextColor,
                    chart.YAxisLabelFontSize,
                    chart.YAxisLabelAngle,
                    chart.YAxisLabelTextThemeColor,
                    chart.AxisTitleTextThemeColor,
                    chart.AxisTitleTextColor,
                    chart.AxisTitleFontSize,
                    chart.YAxisCrosses,
                    chart.YAxisCrossesAt,
                    chart.YAxisCrossBetween,
                    chart.YAxisDisplayUnit,
                    chartNs,
                    drawingNs);
            }
            yield break;
        }

        yield return ToCategoryAxisXml(chart, chartNs, drawingNs);
        yield return ToValueAxisXml(
            chart.YAxisTitle,
            ValueAxisId,
            CategoryAxisId,
            "l",
            chart.YAxisMinimum,
            chart.YAxisMaximum,
            chart.YAxisMajorUnit,
            chart.YAxisMinorUnit,
            chart.YAxisLogScale,
            chart.YAxisNumberFormat,
            chart.ShowYAxisMajorGridlines,
            chart.ShowYAxisMinorGridlines,
            chart.YAxisMajorGridlineColor,
            chart.YAxisMinorGridlineColor,
            chart.YAxisGridlineThickness,
            chart.YAxisMajorTickStyle,
            chart.YAxisMinorTickStyle,
            chart.YAxisLineColor,
            chart.YAxisLineThickness,
            chart.ShowYAxisLabels,
            chart.YAxisLabelTextColor,
            chart.YAxisLabelFontSize,
            chart.YAxisLabelAngle,
            chart.YAxisLabelTextThemeColor,
            chart.AxisTitleTextThemeColor,
            chart.AxisTitleTextColor,
            chart.AxisTitleFontSize,
            chart.YAxisCrosses,
            chart.YAxisCrossesAt,
            chart.YAxisCrossBetween,
            chart.YAxisDisplayUnit,
            chartNs,
            drawingNs);

        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
        if (secondaryIndexes.Count > 0)
        {
            yield return ToValueAxisXml(
                null,
                SecondaryValueAxisId,
                CategoryAxisId,
                "r",
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisNumberFormat,
                false,
                false,
                null,
                null,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.YAxisLabelTextColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                chart.YAxisLabelTextThemeColor,
                chart.AxisTitleTextThemeColor,
                chart.AxisTitleTextColor,
                chart.AxisTitleFontSize,
                chart.YAxisCrosses,
                chart.YAxisCrossesAt,
                chart.YAxisCrossBetween,
                chart.YAxisDisplayUnit,
                chartNs,
                drawingNs);
        }

        if (chart.Type is ChartType.Surface or ChartType.ThreeDSurface)
            yield return ToSeriesAxisXml(chartNs);
    }

    private static XElement ToCategoryAxisXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + (chart.XAxisIsDateAxis ? "dateAx" : "catAx"),
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "scaling",
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax"))),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", "b")),
            ToAxisTitleXml(chart.XAxisTitle, chart.AxisTitleTextThemeColor, chart.AxisTitleTextColor, chart.AxisTitleFontSize, chartNs, drawingNs),
            ToAxisGridlinesXml("majorGridlines", chart.ShowXAxisMajorGridlines, chart.XAxisMajorGridlineColor, chart.XAxisGridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", chart.ShowXAxisMinorGridlines, chart.XAxisMinorGridlineColor, chart.XAxisGridlineThickness, chartNs, drawingNs),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(chart.XAxisMajorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(chart.XAxisMinorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(chart.ShowXAxisLabels))),
            ToUnsignedAxisValueXml("tickLblSkip", chart.XAxisLabelSkip, chartNs),
            ToUnsignedAxisValueXml("tickMarkSkip", chart.XAxisTickMarkSkip, chartNs),
            ToUnsignedAxisValueXml("lblOffset", chart.XAxisLabelOffset, chartNs),
            ToBooleanAxisValueXml("noMultiLvlLbl", chart.XAxisNoMultiLevelLabels, chartNs),
            ToAxisLabelAlignmentXml(chart.XAxisLabelAlignment, chartNs),
            ToDateAxisUnitXml("baseTimeUnit", chart.XAxisIsDateAxis ? chart.XAxisBaseTimeUnit : null, chartNs),
            ToDateAxisUnitXml("majorTimeUnit", chart.XAxisIsDateAxis ? chart.XAxisMajorTimeUnit : null, chartNs),
            ToDateAxisUnitXml("minorTimeUnit", chart.XAxisIsDateAxis ? chart.XAxisMinorTimeUnit : null, chartNs),
            ToAxisLabelTextProperties(chart.XAxisLabelTextThemeColor, chart.XAxisLabelTextColor, chart.XAxisLabelFontSize, chart.XAxisLabelAngle, chartNs, drawingNs),
            ToAxisLineShapeProperties(chart.XAxisLineColor, chart.XAxisLineThickness, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", ValueAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement ToValueAxisXml(
        string? title,
        int axisId,
        int crossAxisId,
        string axisPosition,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        ChartDataLabelNumberFormat numberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        CellColor? lineColor,
        double lineThickness,
        bool showLabels,
        CellColor? labelTextColor,
        double labelFontSize,
        double labelAngle,
        WorkbookThemeColorReference? labelTextThemeColor,
        WorkbookThemeColorReference? axisTitleTextThemeColor,
        CellColor? axisTitleTextColor,
        double axisTitleFontSize,
        ChartAxisCrosses crosses,
        double? crossesAt,
        ChartAxisCrossBetween? crossBetween,
        ChartAxisDisplayUnit? displayUnit,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "valAx",
            new XElement(chartNs + "axId", new XAttribute("val", axisId)),
            new XElement(chartNs + "scaling",
                logScale ? new XElement(chartNs + "logBase", new XAttribute("val", "10")) : null,
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax")),
                ToAxisBoundXml("max", maximum, chartNs),
                ToAxisBoundXml("min", minimum, chartNs)),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", axisPosition)),
            ToAxisTitleXml(title, axisTitleTextThemeColor, axisTitleTextColor, axisTitleFontSize, chartNs, drawingNs),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(numberFormat)),
                new XAttribute("sourceLinked", numberFormat == ChartDataLabelNumberFormat.General ? "1" : "0")),
            ToAxisGridlinesXml("majorGridlines", showMajorGridlines, majorGridlineColor, gridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", showMinorGridlines, minorGridlineColor, gridlineThickness, chartNs, drawingNs),
            ToAxisUnitXml("majorUnit", majorUnit, chartNs),
            ToAxisUnitXml("minorUnit", minorUnit, chartNs),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(majorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(minorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(showLabels))),
            ToAxisDisplayUnitXml(displayUnit, chartNs),
            ToAxisLabelTextProperties(labelTextThemeColor, labelTextColor, labelFontSize, labelAngle, chartNs, drawingNs),
            ToAxisLineShapeProperties(lineColor, lineThickness, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", crossAxisId)),
            ToAxisCrossesXml(crosses, crossesAt, chartNs),
            ToAxisCrossBetweenXml(crossBetween, chartNs));

    private static XElement ToSeriesAxisXml(XNamespace chartNs) =>
        new(chartNs + "serAx",
            new XElement(chartNs + "axId", new XAttribute("val", SeriesAxisId)),
            new XElement(chartNs + "scaling",
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax"))),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", "r")),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", "none")),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", "none")),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", "nextTo")),
            new XElement(chartNs + "crossAx", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement? ToAxisGridlinesXml(
        string elementName,
        bool visible,
        CellColor? color,
        double thickness,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (!visible)
            return null;

        return new XElement(chartNs + elementName,
            ToShapeProperties(
                chartNs,
                drawingNs,
                fillThemeColor: null,
                fillColor: null,
                borderThemeColor: null,
                borderColor: color,
                borderThickness: Math.Clamp(thickness, 0.25, 10)));
    }

    private static XElement? ToAxisLineShapeProperties(
        CellColor? lineColor,
        double lineThickness,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            fillThemeColor: null,
            fillColor: null,
            borderThemeColor: null,
            borderColor: lineColor,
            borderThickness: Math.Clamp(lineThickness, 0.5, 10));

    private static string ToXlsxTickMark(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => "none",
            ChartAxisTickStyle.Inside => "in",
            ChartAxisTickStyle.Cross => "cross",
            _ => "out"
        };

    private static string ToXlsxTickLabelPosition(bool showLabels) =>
        showLabels ? "nextTo" : "none";

    private static XElement ToAxisCrossesXml(ChartAxisCrosses crosses, double? crossesAt, XNamespace chartNs)
    {
        if (crosses == ChartAxisCrosses.Custom && crossesAt is { } numeric && double.IsFinite(numeric))
            return new XElement(chartNs + "crossesAt", new XAttribute("val", numeric.ToString(CultureInfo.InvariantCulture)));

        return new XElement(chartNs + "crosses", new XAttribute("val", ToXlsxAxisCrosses(crosses)));
    }

    private static XElement? ToAxisCrossBetweenXml(ChartAxisCrossBetween? crossBetween, XNamespace chartNs) =>
        crossBetween is null
            ? null
            : new XElement(chartNs + "crossBetween", new XAttribute("val", ToXlsxAxisCrossBetween(crossBetween.Value)));

    private static string ToXlsxAxisCrosses(ChartAxisCrosses crosses) =>
        crosses switch
        {
            ChartAxisCrosses.Minimum => "min",
            ChartAxisCrosses.Maximum => "max",
            _ => "autoZero"
        };

    private static string ToXlsxAxisCrossBetween(ChartAxisCrossBetween crossBetween) =>
        crossBetween == ChartAxisCrossBetween.MidCategory ? "midCat" : "between";

    private static XElement? ToAxisLabelTextProperties(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        double angle,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (textThemeColor is null && textColor is null && fontSize == 11 && angle == 0)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(angle, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200)),
                        ToSolidFill(textThemeColor, textColor, drawingNs)))));
    }

    private static XElement? ToAxisBoundXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", numeric.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToAxisUnitXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", Math.Max(numeric, double.Epsilon).ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToUnsignedAxisValueXml(string elementName, int value, XNamespace chartNs) =>
        value > 0
            ? new XElement(chartNs + elementName, new XAttribute("val", value.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToBooleanAxisValueXml(string elementName, bool value, XNamespace chartNs) =>
        value ? new XElement(chartNs + elementName, new XAttribute("val", "1")) : null;

    private static XElement? ToAxisLabelAlignmentXml(ChartAxisLabelAlignment alignment, XNamespace chartNs) =>
        alignment == ChartAxisLabelAlignment.Center
            ? null
            : new XElement(chartNs + "lblAlgn", new XAttribute("val", ToXlsxAxisLabelAlignment(alignment)));

    private static string ToXlsxAxisLabelAlignment(ChartAxisLabelAlignment alignment) =>
        alignment == ChartAxisLabelAlignment.Right ? "r" : "l";

    private static XElement? ToDateAxisUnitXml(string elementName, ChartDateAxisUnit? unit, XNamespace chartNs) =>
        unit is null
            ? null
            : new XElement(chartNs + elementName, new XAttribute("val", ToXlsxDateAxisUnit(unit.Value)));

    private static string ToXlsxDateAxisUnit(ChartDateAxisUnit unit) =>
        unit switch
        {
            ChartDateAxisUnit.Days => "days",
            ChartDateAxisUnit.Years => "years",
            _ => "months"
        };

    private static XElement? ToAxisDisplayUnitXml(ChartAxisDisplayUnit? unit, XNamespace chartNs) =>
        unit is null
            ? null
            : new XElement(chartNs + "dispUnits",
                new XElement(chartNs + "builtInUnit", new XAttribute("val", ToXlsxAxisDisplayUnit(unit.Value))));

    private static string ToXlsxAxisDisplayUnit(ChartAxisDisplayUnit unit) =>
        unit switch
        {
            ChartAxisDisplayUnit.Hundreds => "hundreds",
            ChartAxisDisplayUnit.Thousands => "thousands",
            ChartAxisDisplayUnit.TenThousands => "tenThousands",
            ChartAxisDisplayUnit.HundredThousands => "hundredThousands",
            ChartAxisDisplayUnit.Millions => "millions",
            ChartAxisDisplayUnit.TenMillions => "tenMillions",
            ChartAxisDisplayUnit.HundredMillions => "hundredMillions",
            ChartAxisDisplayUnit.Billions => "billions",
            ChartAxisDisplayUnit.Trillions => "trillions",
            _ => "thousands"
        };

    private static string ToXlsxNumberFormatCode(ChartDataLabelNumberFormat format) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => "0.00",
            ChartDataLabelNumberFormat.Currency => "$#,##0.00",
            ChartDataLabelNumberFormat.Percent => "0%",
            _ => "General"
        };
}
