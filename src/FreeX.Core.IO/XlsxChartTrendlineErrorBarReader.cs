using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartTrendlineErrorBarReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyTrendline(XElement series, ChartModel chart)
    {
        var trendlineElements = series.Elements(ChartNs + "trendline").ToList();
        if (trendlineElements.Count == 0)
            return;

        var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, 0);
        foreach (var trendline in trendlineElements)
        {
            if (chart.ShowLinearTrendline)
            {
                // R41-io-chart-errorbars-trendline-3-3: only the FIRST <c:trendline> encountered
                // chart-wide is modeled by the scalar Trendline* properties below. Every additional
                // one — a second trendline on this same series, or any trendline on a later series
                // — would otherwise be silently and permanently dropped. Preserve it verbatim
                // (keyed by the series it belongs to) so it survives an open/save round-trip.
                chart.AdditionalSeriesTrendlinesXml.Add(
                    new ChartSeriesRawXmlEntry(seriesIndex, trendline.ToString(SaveOptions.DisableFormatting)));
                continue;
            }

            chart.ShowLinearTrendline = true;
            // R30-io-chart-series-cache-deep-1: this is a chart-global capture (only the first series in
            // document order that carries a <c:trendline> is kept), so remember WHICH series it came from —
            // every <c:ser> is required by the OOXML schema to carry its own <c:idx>, so this is always
            // available — otherwise the writer has no way to reattach it to anything but series 0.
            chart.TrendlineSeriesIndex = seriesIndex;
            chart.TrendlineName = trendline.Element(ChartNs + "name")?.Value;
            chart.TrendlineType = FromXlsxTrendlineType(trendline.Element(ChartNs + "trendlineType")?.Attribute("val")?.Value);
            if (int.TryParse(trendline.Element(ChartNs + "period")?.Attribute("val")?.Value, out var period))
                chart.TrendlinePeriod = Math.Max(2, period);
            if (int.TryParse(trendline.Element(ChartNs + "order")?.Attribute("val")?.Value, out var order))
                chart.TrendlineOrder = Math.Clamp(order, 2, 6);
            chart.TrendlineForward = ReadOptionalDouble(trendline.Element(ChartNs + "forward")?.Attribute("val")?.Value);
            chart.TrendlineBackward = ReadOptionalDouble(trendline.Element(ChartNs + "backward")?.Attribute("val")?.Value);
            chart.TrendlineIntercept = ReadOptionalDouble(trendline.Element(ChartNs + "intercept")?.Attribute("val")?.Value);

            chart.ShowTrendlineEquation = XlsxChartScalarReader.IsTrue(trendline.Element(ChartNs + "dispEq")?.Attribute("val")?.Value);
            chart.ShowTrendlineRSquared = XlsxChartScalarReader.IsTrue(trendline.Element(ChartNs + "dispRSqr")?.Attribute("val")?.Value);
            var trendlineLabel = trendline.Element(ChartNs + "trendlineLbl");
            var trendlineLabelNumberFormat = trendlineLabel?.Element(ChartNs + "numFmt");
            chart.TrendlineLabelNumberFormatCode = trendlineLabelNumberFormat?.Attribute("formatCode")?.Value;
            chart.TrendlineLabelNumberFormatSourceLinked = ReadNullableBool(trendlineLabelNumberFormat?.Attribute("sourceLinked")?.Value);
            ApplyTrendlineLabelMetadata(trendlineLabel, chart);
            ApplyTrendlineShapeProperties(trendline.Element(ChartNs + "spPr"), chart);
        }
    }

    public static void ApplyErrorBars(XElement series, ChartModel chart)
    {
        var errorBarsElements = series.Elements(ChartNs + "errBars").ToList();
        if (errorBarsElements.Count == 0)
            return;

        var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, 0);
        foreach (var errorBars in errorBarsElements)
        {
            if (chart.ShowErrorBars)
            {
                // R41-io-chart-errorbars-trendline-3-2: Excel writes TWO sibling <c:errBars>
                // elements on the same series when both horizontal (X) and vertical (Y) error bars
                // are configured, but only the first one encountered chart-wide is modeled by the
                // scalar ErrorBar* properties below. Preserve every additional one verbatim (keyed
                // by the series it belongs to) so it survives an open/save round-trip instead of
                // being silently dropped.
                chart.AdditionalSeriesErrorBarsXml.Add(
                    new ChartSeriesRawXmlEntry(seriesIndex, errorBars.ToString(SaveOptions.DisableFormatting)));
                continue;
            }

            chart.ShowErrorBars = true;
            // R30-io-chart-series-cache-deep-1: see the matching comment in ApplyTrendline above — track
            // which series this chart-global capture actually came from.
            chart.ErrorBarSeriesIndex = seriesIndex;
            chart.ErrorBarKind = FromXlsxErrorBarKind(errorBars.Element(ChartNs + "errValType")?.Attribute("val")?.Value);
            chart.ErrorBarAxisDirection = FromXlsxErrorBarAxisDirection(errorBars.Element(ChartNs + "errDir")?.Attribute("val")?.Value);
            chart.ErrorBarDirection = FromXlsxErrorBarDirection(errorBars.Element(ChartNs + "errBarType")?.Attribute("val")?.Value);
            chart.ErrorBarEndCaps = !XlsxChartScalarReader.IsTrue(errorBars.Element(ChartNs + "noEndCap")?.Attribute("val")?.Value);
            chart.ErrorBarPlusRangeFormula = ReadErrorBarRangeFormula(errorBars.Element(ChartNs + "plus"));
            chart.ErrorBarMinusRangeFormula = ReadErrorBarRangeFormula(errorBars.Element(ChartNs + "minus"));
            chart.ErrorBarPlusRangeCacheXml = ReadErrorBarRangeCacheXml(errorBars.Element(ChartNs + "plus"));
            chart.ErrorBarMinusRangeCacheXml = ReadErrorBarRangeCacheXml(errorBars.Element(ChartNs + "minus"));

            if (double.TryParse(errorBars.Element(ChartNs + "val")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                chart.ErrorBarValue = Math.Clamp(value, 0, 1000);

            ApplyErrorBarShapeProperties(errorBars.Element(ChartNs + "spPr"), chart);
        }
    }

    public static void ApplyChartGuideLineMetadata(XElement plotChart, ChartModel chart)
    {
        if (plotChart.Element(ChartNs + "dropLines") is { } dropLines)
        {
            chart.ShowDropLines = true;
            ApplyLineShapeProperties(
                dropLines.Element(ChartNs + "spPr"),
                color => chart.DropLineColor = color,
                theme => chart.DropLineThemeColor = theme,
                thickness => chart.DropLineThickness = thickness,
                dashStyle => chart.DropLineDashStyle = dashStyle,
                () => chart.DropLineColor = null,
                () => chart.DropLineThemeColor = null);
        }

        if (plotChart.Element(ChartNs + "hiLowLines") is { } highLowLines)
        {
            chart.ShowHighLowLines = true;
            ApplyLineShapeProperties(
                highLowLines.Element(ChartNs + "spPr"),
                color => chart.HighLowLineColor = color,
                theme => chart.HighLowLineThemeColor = theme,
                thickness => chart.HighLowLineThickness = thickness,
                dashStyle => chart.HighLowLineDashStyle = dashStyle,
                () => chart.HighLowLineColor = null,
                () => chart.HighLowLineThemeColor = null);
        }

        if (plotChart.Element(ChartNs + "serLines") is { } seriesLines)
        {
            chart.ShowSeriesLines = true;
            ApplyLineShapeProperties(
                seriesLines.Element(ChartNs + "spPr"),
                color => chart.SeriesLineColor = color,
                theme => chart.SeriesLineThemeColor = theme,
                thickness => chart.SeriesLineThickness = thickness,
                dashStyle => chart.SeriesLineDashStyle = dashStyle,
                () => chart.SeriesLineColor = null,
                () => chart.SeriesLineThemeColor = null);
        }

        if (plotChart.Element(ChartNs + "upDownBars") is { } upDownBars)
        {
            chart.ShowUpDownBars = true;
            if (int.TryParse(upDownBars.Element(ChartNs + "gapWidth")?.Attribute("val")?.Value, out var gapWidth))
                chart.UpDownBarGapWidth = Math.Clamp(gapWidth, 0, 500);
            ApplyBarShapeProperties(
                upDownBars.Element(ChartNs + "upBars")?.Element(ChartNs + "spPr"),
                color => chart.UpBarFillColor = color,
                theme => chart.UpBarFillThemeColor = theme,
                color => chart.UpBarBorderColor = color,
                theme => chart.UpBarBorderThemeColor = theme,
                thickness => chart.UpBarBorderThickness = thickness,
                () => chart.UpBarFillColor = null,
                () => chart.UpBarFillThemeColor = null,
                () => chart.UpBarBorderColor = null,
                () => chart.UpBarBorderThemeColor = null);
            ApplyBarShapeProperties(
                upDownBars.Element(ChartNs + "downBars")?.Element(ChartNs + "spPr"),
                color => chart.DownBarFillColor = color,
                theme => chart.DownBarFillThemeColor = theme,
                color => chart.DownBarBorderColor = color,
                theme => chart.DownBarBorderThemeColor = theme,
                thickness => chart.DownBarBorderThickness = thickness,
                () => chart.DownBarFillColor = null,
                () => chart.DownBarFillThemeColor = null,
                () => chart.DownBarBorderColor = null,
                () => chart.DownBarBorderThemeColor = null);
        }
    }

    public static ChartLineDashStyle FromXlsxPresetDash(string? value) =>
        value switch
        {
            "dot" or "sysDot" or "dotDot" => ChartLineDashStyle.Dot,
            "dash" or "sysDash" or "dashDot" or "lgDash" or "lgDashDot" or "lgDashDotDot" => ChartLineDashStyle.Dash,
            _ => ChartLineDashStyle.Solid
        };

    private static void ApplyTrendlineShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.TrendlineThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.5, 10);

        chart.TrendlineDashStyle = FromXlsxPresetDash(line.Element(DrawingNs + "prstDash")?.Attribute("val")?.Value);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var themeColor))
        {
            chart.TrendlineThemeColor = themeColor;
            chart.TrendlineColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var color))
        {
            chart.TrendlineColor = color;
            chart.TrendlineThemeColor = null;
        }
    }

    private static void ApplyTrendlineLabelMetadata(XElement? trendlineLabel, ChartModel chart)
    {
        if (trendlineLabel is null)
            return;

        chart.TrendlineLabelLayout = XlsxChartMetadataReader.ReadManualLayout(trendlineLabel.Element(ChartNs + "layout"));
        ApplyTrendlineLabelShapeProperties(trendlineLabel.Element(ChartNs + "spPr"), chart);
        ApplyTrendlineLabelTextProperties(trendlineLabel.Element(ChartNs + "txPr"), chart);
    }

    private static void ApplyTrendlineLabelShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                chart.TrendlineLabelFillThemeColor = fillThemeColor;
                chart.TrendlineLabelFillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                chart.TrendlineLabelFillColor = fillColor;
                chart.TrendlineLabelFillThemeColor = null;
            }
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.TrendlineLabelBorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            chart.TrendlineLabelBorderThemeColor = borderThemeColor;
            chart.TrendlineLabelBorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            chart.TrendlineLabelBorderColor = borderColor;
            chart.TrendlineLabelBorderThemeColor = null;
        }
    }

    private static void ApplyTrendlineLabelTextProperties(XElement? textPropertiesRoot, ChartModel chart)
    {
        var bodyProperties = textPropertiesRoot?.Element(DrawingNs + "bodyPr");
        if (int.TryParse(bodyProperties?.Attribute("rot")?.Value, out var rotation))
            chart.TrendlineLabelAngle = Math.Clamp(rotation / 60000.0, -90, 90);

        var textProperties = FirstDescendant(textPropertiesRoot, DrawingNs + "defRPr");
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            chart.TrendlineLabelFontSize = Math.Clamp(size / 100.0, 6, 72);

        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var textThemeColor))
        {
            chart.TrendlineLabelTextThemeColor = textThemeColor;
            chart.TrendlineLabelTextColor = null;
        }
        else if (textFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var textColor))
        {
            chart.TrendlineLabelTextColor = textColor;
            chart.TrendlineLabelTextThemeColor = null;
        }
    }

    private static void ApplyErrorBarShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        ApplyLineShapeProperties(
            shapeProperties,
            color => chart.ErrorBarColor = color,
            theme => chart.ErrorBarThemeColor = theme,
            thickness => chart.ErrorBarThickness = thickness,
            dashStyle => chart.ErrorBarDashStyle = dashStyle,
            () => chart.ErrorBarColor = null,
            () => chart.ErrorBarThemeColor = null);
    }

    private static void ApplyLineShapeProperties(
        XElement? shapeProperties,
        Action<CellColor> setColor,
        Action<WorkbookThemeColorReference> setThemeColor,
        Action<double> setThickness,
        Action<ChartLineDashStyle> setDashStyle,
        Action clearColor,
        Action clearThemeColor)
    {
        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            setThickness(Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.5, 10));

        setDashStyle(FromXlsxPresetDash(line.Element(DrawingNs + "prstDash")?.Attribute("val")?.Value));

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var themeColor))
        {
            setThemeColor(themeColor);
            clearColor();
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var color))
        {
            setColor(color);
            clearThemeColor();
        }
    }

    private static void ApplyBarShapeProperties(
        XElement? shapeProperties,
        Action<CellColor> setFillColor,
        Action<WorkbookThemeColorReference> setFillThemeColor,
        Action<CellColor> setBorderColor,
        Action<WorkbookThemeColorReference> setBorderThemeColor,
        Action<double> setBorderThickness,
        Action clearFillColor,
        Action clearFillThemeColor,
        Action clearBorderColor,
        Action clearBorderThemeColor)
    {
        if (shapeProperties is null)
            return;

        if (shapeProperties.Element(DrawingNs + "solidFill") is { } fill)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                setFillThemeColor(fillThemeColor);
                clearFillColor();
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                setFillColor(fillColor);
                clearFillThemeColor();
            }
        }

        var line = shapeProperties.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            setBorderThickness(Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10));

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            setBorderThemeColor(borderThemeColor);
            clearBorderColor();
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            setBorderColor(borderColor);
            clearBorderThemeColor();
        }
    }

    private static ChartTrendlineType FromXlsxTrendlineType(string? value) =>
        value switch
        {
            "exp" => ChartTrendlineType.Exponential,
            "log" => ChartTrendlineType.Logarithmic,
            "power" => ChartTrendlineType.Power,
            "movingAvg" => ChartTrendlineType.MovingAverage,
            "poly" => ChartTrendlineType.Polynomial,
            _ => ChartTrendlineType.Linear
        };

    private static ChartErrorBarKind FromXlsxErrorBarKind(string? value) =>
        value switch
        {
            "percentage" => ChartErrorBarKind.Percentage,
            "fixedVal" => ChartErrorBarKind.FixedValue,
            "cust" => ChartErrorBarKind.Custom,
            // R41-io-chart-errorbars-trendline-3-1: "stdDev" (Standard Deviation, with a
            // user-configurable multiplier) is a distinct Excel error-amount kind from "stdErr"
            // (Standard Error, no multiplier) — folding it into StandardError silently retypes the
            // error bars and drops the multiplier value on the next save.
            "stdDev" => ChartErrorBarKind.StdDev,
            _ => ChartErrorBarKind.StandardError
        };

    private static ChartErrorBarAxisDirection FromXlsxErrorBarAxisDirection(string? value) =>
        value == "x" ? ChartErrorBarAxisDirection.X : ChartErrorBarAxisDirection.Y;

    private static ChartErrorBarDirection FromXlsxErrorBarDirection(string? value) =>
        value switch
        {
            "plus" => ChartErrorBarDirection.Plus,
            "minus" => ChartErrorBarDirection.Minus,
            _ => ChartErrorBarDirection.Both
        };

    private static double? ReadOptionalDouble(string? value) =>
        XlsxChartScalarReader.ReadOptionalDouble(value);

    private static bool? ReadNullableBool(string? value) =>
        value switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null
        };

    private static string? ReadErrorBarRangeFormula(XElement? element)
    {
        if (element is null)
            return null;

        foreach (var formula in element.Descendants(ChartNs + "f"))
        {
            var value = formula.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ReadErrorBarRangeCacheXml(XElement? element) =>
        element?
            .Element(ChartNs + "numRef")?
            .Element(ChartNs + "numCache")?
            .ToString(SaveOptions.DisableFormatting);

    private static XElement? FirstDescendant(XElement? element, XName name)
    {
        if (element is null)
            return null;

        foreach (var descendant in element.Descendants(name))
            return descendant;

        return null;
    }
}
