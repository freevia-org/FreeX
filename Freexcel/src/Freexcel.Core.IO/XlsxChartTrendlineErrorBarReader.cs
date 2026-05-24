using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartTrendlineErrorBarReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyTrendline(XElement series, ChartModel chart)
    {
        if (chart.ShowLinearTrendline)
            return;

        var trendline = series.Element(ChartNs + "trendline");
        if (trendline is null)
            return;

        chart.ShowLinearTrendline = true;
        chart.TrendlineType = FromXlsxTrendlineType(trendline.Element(ChartNs + "trendlineType")?.Attribute("val")?.Value);
        if (int.TryParse(trendline.Element(ChartNs + "period")?.Attribute("val")?.Value, out var period))
            chart.TrendlinePeriod = Math.Max(2, period);
        if (int.TryParse(trendline.Element(ChartNs + "order")?.Attribute("val")?.Value, out var order))
            chart.TrendlineOrder = Math.Clamp(order, 2, 6);

        chart.ShowTrendlineEquation = XlsxChartScalarReader.IsTrue(trendline.Element(ChartNs + "dispEq")?.Attribute("val")?.Value);
        chart.ShowTrendlineRSquared = XlsxChartScalarReader.IsTrue(trendline.Element(ChartNs + "dispRSqr")?.Attribute("val")?.Value);
        ApplyTrendlineShapeProperties(trendline.Element(ChartNs + "spPr"), chart);
    }

    public static void ApplyErrorBars(XElement series, ChartModel chart)
    {
        if (chart.ShowErrorBars)
            return;

        var errorBars = series.Element(ChartNs + "errBars");
        if (errorBars is null)
            return;

        chart.ShowErrorBars = true;
        chart.ErrorBarKind = FromXlsxErrorBarKind(errorBars.Element(ChartNs + "errValType")?.Attribute("val")?.Value);
        chart.ErrorBarDirection = FromXlsxErrorBarDirection(errorBars.Element(ChartNs + "errBarType")?.Attribute("val")?.Value);
        chart.ErrorBarEndCaps = !XlsxChartScalarReader.IsTrue(errorBars.Element(ChartNs + "noEndCap")?.Attribute("val")?.Value);

        if (double.TryParse(errorBars.Element(ChartNs + "val")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            chart.ErrorBarValue = Math.Clamp(value, 0, 1000);

        ApplyErrorBarShapeProperties(errorBars.Element(ChartNs + "spPr"), chart);
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
            "dot" => ChartLineDashStyle.Dot,
            "dash" => ChartLineDashStyle.Dash,
            _ => ChartLineDashStyle.Solid
        };

    private static void ApplyTrendlineShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.TrendlineThickness = Math.Clamp(emus / 12700.0, 0.5, 10);

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
            setThickness(Math.Clamp(emus / 12700.0, 0.5, 10));

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
            setBorderThickness(Math.Clamp(emus / 12700.0, 0, 10));

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
            _ => ChartErrorBarKind.StandardError
        };

    private static ChartErrorBarDirection FromXlsxErrorBarDirection(string? value) =>
        value switch
        {
            "plus" => ChartErrorBarDirection.Plus,
            "minus" => ChartErrorBarDirection.Minus,
            _ => ChartErrorBarDirection.Both
        };
}
