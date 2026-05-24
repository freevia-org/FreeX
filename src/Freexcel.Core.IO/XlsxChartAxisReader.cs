using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartAxisReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyAxisMetadata(XElement? plotArea, ChartModel chart)
    {
        if (plotArea is null)
            return;

        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
        {
            var plotChart = chart.Type == ChartType.Bubble
                ? plotArea.Element(ChartNs + "bubbleChart")
                : plotArea.Element(ChartNs + "scatterChart");
            var axisIds = plotChart?
                .Elements(ChartNs + "axId")
                .Select(element => element.Attribute("val")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList() ?? [];
            var valueAxes = plotArea.Elements(ChartNs + "valAx").ToList();
            var xAxis = FindAxisById(valueAxes, axisIds.FirstOrDefault()) ?? valueAxes.FirstOrDefault();
            var yAxis = FindAxisById(valueAxes, axisIds.Skip(1).FirstOrDefault()) ?? valueAxes.Skip(1).FirstOrDefault();
            chart.XAxisTitle = ReadAxisTitle(xAxis);
            chart.YAxisTitle = ReadAxisTitle(yAxis);
            ApplyAxisTitleFormatting(xAxis, chart);
            ApplyAxisTitleFormatting(yAxis, chart);
            ApplyValueAxisProperties(xAxis, chart, useXAxis: true);
            ApplyValueAxisProperties(yAxis, chart, useXAxis: false);
            ApplyAxisLabelFormatting(xAxis, chart, useXAxis: true);
            ApplyAxisLabelFormatting(yAxis, chart, useXAxis: false);
            return;
        }

        var categoryAxis = plotArea.Element(ChartNs + "dateAx") ?? plotArea.Element(ChartNs + "catAx");
        chart.XAxisIsDateAxis = categoryAxis?.Name == ChartNs + "dateAx";
        chart.XAxisTitle = ReadAxisTitle(categoryAxis);
        ApplyAxisTitleFormatting(categoryAxis, chart);
        ApplyCategoryAxisProperties(categoryAxis, chart);
        ApplyAxisLabelFormatting(categoryAxis, chart, useXAxis: true);
        var valueAxis = plotArea.Element(ChartNs + "valAx");
        chart.YAxisTitle = ReadAxisTitle(valueAxis);
        ApplyAxisTitleFormatting(valueAxis, chart);
        ApplyValueAxisProperties(valueAxis, chart, useXAxis: false);
        ApplyAxisLabelFormatting(valueAxis, chart, useXAxis: false);
    }

    public static ChartDataLabelNumberFormat FromXlsxNumberFormatCode(string? formatCode) =>
        formatCode switch
        {
            "0.00" => ChartDataLabelNumberFormat.Number,
            "$#,##0.00" => ChartDataLabelNumberFormat.Currency,
            "0%" => ChartDataLabelNumberFormat.Percent,
            _ => ChartDataLabelNumberFormat.General
        };

    private static string? ReadAxisTitle(XElement? axisElement) =>
        axisElement?
            .Element(ChartNs + "title")?
            .Descendants(DrawingNs + "t")
            .Select(element => element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

    private static void ApplyAxisTitleFormatting(XElement? axisElement, ChartModel chart)
    {
        var runProperties = axisElement?
            .Element(ChartNs + "title")?
            .Descendants(DrawingNs + "rPr")
            .FirstOrDefault();
        if (runProperties is null)
            return;

        if (int.TryParse(runProperties.Attribute("sz")?.Value, out var size))
            chart.AxisTitleFontSize = Math.Clamp(size / 100.0, 6, 72);

        var solidFill = runProperties.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
        {
            chart.AxisTitleTextThemeColor = themeColor;
            chart.AxisTitleTextColor = null;
        }
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
        {
            chart.AxisTitleTextColor = color;
            chart.AxisTitleTextThemeColor = null;
        }
    }

    private static void ApplyAxisLabelFormatting(XElement? axisElement, ChartModel chart, bool useXAxis)
    {
        var textProperties = axisElement?.Element(ChartNs + "txPr");
        if (textProperties is null)
            return;

        var runProperties = textProperties
            .Descendants(DrawingNs + "defRPr")
            .Concat(textProperties.Descendants(DrawingNs + "rPr"))
            .FirstOrDefault();

        var textColor = TryReadTextColor(runProperties);
        var textThemeColor = TryReadTextThemeColor(runProperties);
        var fontSize = int.TryParse(runProperties?.Attribute("sz")?.Value, out var size)
            ? Math.Clamp(size / 100.0, 6, 72)
            : (double?)null;
        var angle = int.TryParse(textProperties.Element(DrawingNs + "bodyPr")?.Attribute("rot")?.Value, out var rotation)
            ? Math.Clamp(rotation / 60000.0, -90, 90)
            : (double?)null;

        if (useXAxis)
        {
            if (textThemeColor is { } themeColor)
            {
                chart.XAxisLabelTextThemeColor = themeColor;
                chart.XAxisLabelTextColor = null;
            }
            else if (textColor is not null)
            {
                chart.XAxisLabelTextThemeColor = null;
            }
            if (textColor is { } color)
                chart.XAxisLabelTextColor = color;
            if (fontSize is { } labelFontSize)
                chart.XAxisLabelFontSize = labelFontSize;
            if (angle is { } labelAngle)
                chart.XAxisLabelAngle = labelAngle;
            return;
        }

        if (textThemeColor is { } yThemeColor)
        {
            chart.YAxisLabelTextThemeColor = yThemeColor;
            chart.YAxisLabelTextColor = null;
        }
        else if (textColor is not null)
        {
            chart.YAxisLabelTextThemeColor = null;
        }
        if (textColor is { } yColor)
            chart.YAxisLabelTextColor = yColor;
        if (fontSize is { } yFontSize)
            chart.YAxisLabelFontSize = yFontSize;
        if (angle is { } yAngle)
            chart.YAxisLabelAngle = yAngle;
    }

    private static CellColor? TryReadTextColor(XElement? runProperties)
    {
        var solidFill = runProperties?.Element(DrawingNs + "solidFill");
        return solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color)
            ? color
            : null;
    }

    private static WorkbookThemeColorReference? TryReadTextThemeColor(XElement? runProperties)
    {
        var solidFill = runProperties?.Element(DrawingNs + "solidFill");
        return solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor)
            ? themeColor
            : null;
    }

    private static XElement? FindAxisById(IEnumerable<XElement> axes, string? axisId)
    {
        if (string.IsNullOrWhiteSpace(axisId))
            return null;

        return axes.FirstOrDefault(axis => axis.Element(ChartNs + "axId")?.Attribute("val")?.Value == axisId);
    }

    private static void ApplyValueAxisProperties(XElement? axisElement, ChartModel chart, bool useXAxis)
    {
        if (axisElement is null)
            return;

        var scaling = axisElement.Element(ChartNs + "scaling");
        var minimum = ReadDouble(scaling?.Element(ChartNs + "min")?.Attribute("val")?.Value);
        var maximum = ReadDouble(scaling?.Element(ChartNs + "max")?.Attribute("val")?.Value);
        var majorUnit = ReadDouble(axisElement.Element(ChartNs + "majorUnit")?.Attribute("val")?.Value);
        var minorUnit = ReadDouble(axisElement.Element(ChartNs + "minorUnit")?.Attribute("val")?.Value);
        var logScale = scaling?.Element(ChartNs + "logBase") is not null;
        var numberFormat = FromXlsxNumberFormatCode(axisElement.Element(ChartNs + "numFmt")?.Attribute("formatCode")?.Value);
        var majorGridline = ReadAxisGridline(axisElement.Element(ChartNs + "majorGridlines"));
        var minorGridline = ReadAxisGridline(axisElement.Element(ChartNs + "minorGridlines"));
        var majorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "majorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.Outside);
        var minorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "minorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.None);
        var showLabels = axisElement.Element(ChartNs + "tickLblPos")?.Attribute("val")?.Value != "none";
        var axisLine = ReadAxisLine(axisElement.Element(ChartNs + "spPr"));
        var crossing = ReadAxisCrossing(axisElement);

        if (useXAxis)
        {
            chart.XAxisMinimum = minimum;
            chart.XAxisMaximum = maximum;
            chart.XAxisMajorUnit = majorUnit;
            chart.XAxisMinorUnit = minorUnit;
            chart.XAxisLogScale = logScale;
            chart.XAxisNumberFormat = numberFormat;
            ApplyXAxisGridlineProperties(chart, majorGridline, minorGridline);
            chart.XAxisMajorTickStyle = majorTickStyle;
            chart.XAxisMinorTickStyle = minorTickStyle;
            chart.ShowXAxisLabels = showLabels;
            ApplyXAxisLineProperties(chart, axisLine);
            chart.XAxisCrosses = crossing.Crosses;
            chart.XAxisCrossesAt = crossing.CrossesAt;
            chart.XAxisCrossBetween = crossing.CrossBetween;
            return;
        }

        chart.YAxisMinimum = minimum;
        chart.YAxisMaximum = maximum;
        chart.YAxisMajorUnit = majorUnit;
        chart.YAxisMinorUnit = minorUnit;
        chart.YAxisLogScale = logScale;
        chart.YAxisNumberFormat = numberFormat;
        ApplyYAxisGridlineProperties(chart, majorGridline, minorGridline);
        chart.YAxisMajorTickStyle = majorTickStyle;
        chart.YAxisMinorTickStyle = minorTickStyle;
        chart.ShowYAxisLabels = showLabels;
        ApplyYAxisLineProperties(chart, axisLine);
        chart.YAxisCrosses = crossing.Crosses;
        chart.YAxisCrossesAt = crossing.CrossesAt;
        chart.YAxisCrossBetween = crossing.CrossBetween;
    }

    private static void ApplyCategoryAxisProperties(XElement? axisElement, ChartModel chart)
    {
        if (axisElement is null)
            return;

        ApplyXAxisGridlineProperties(
            chart,
            ReadAxisGridline(axisElement.Element(ChartNs + "majorGridlines")),
            ReadAxisGridline(axisElement.Element(ChartNs + "minorGridlines")));
        chart.XAxisMajorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "majorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "minorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.None);
        chart.ShowXAxisLabels = axisElement.Element(ChartNs + "tickLblPos")?.Attribute("val")?.Value != "none";
        chart.XAxisLabelSkip = Math.Max(0, ReadInt(axisElement.Element(ChartNs + "tickLblSkip")?.Attribute("val")?.Value) ?? 0);
        chart.XAxisTickMarkSkip = Math.Max(0, ReadInt(axisElement.Element(ChartNs + "tickMarkSkip")?.Attribute("val")?.Value) ?? 0);
        chart.XAxisLabelOffset = Math.Max(0, ReadInt(axisElement.Element(ChartNs + "lblOffset")?.Attribute("val")?.Value) ?? 0);
        chart.XAxisNoMultiLevelLabels = ReadBool(axisElement.Element(ChartNs + "noMultiLvlLbl")?.Attribute("val")?.Value);
        ApplyXAxisLineProperties(chart, ReadAxisLine(axisElement.Element(ChartNs + "spPr")));
    }

    private static void ApplyXAxisGridlineProperties(
        ChartModel chart,
        AxisGridlineProperties majorGridline,
        AxisGridlineProperties minorGridline)
    {
        chart.ShowXAxisMajorGridlines = majorGridline.Visible;
        chart.ShowXAxisMinorGridlines = minorGridline.Visible;
        if (majorGridline.Color is { } majorColor)
            chart.XAxisMajorGridlineColor = majorColor;
        if (minorGridline.Color is { } minorColor)
            chart.XAxisMinorGridlineColor = minorColor;
        if (majorGridline.Thickness is { } majorThickness)
            chart.XAxisGridlineThickness = majorThickness;
        else if (minorGridline.Thickness is { } minorThickness)
            chart.XAxisGridlineThickness = minorThickness;
    }

    private static void ApplyYAxisGridlineProperties(
        ChartModel chart,
        AxisGridlineProperties majorGridline,
        AxisGridlineProperties minorGridline)
    {
        chart.ShowYAxisMajorGridlines = majorGridline.Visible;
        chart.ShowYAxisMinorGridlines = minorGridline.Visible;
        if (majorGridline.Color is { } majorColor)
            chart.YAxisMajorGridlineColor = majorColor;
        if (minorGridline.Color is { } minorColor)
            chart.YAxisMinorGridlineColor = minorColor;
        if (majorGridline.Thickness is { } majorThickness)
            chart.YAxisGridlineThickness = majorThickness;
        else if (minorGridline.Thickness is { } minorThickness)
            chart.YAxisGridlineThickness = minorThickness;
    }

    private static AxisGridlineProperties ReadAxisGridline(XElement? gridlineElement)
    {
        if (gridlineElement is null)
            return new AxisGridlineProperties(false, null, null);

        var line = gridlineElement
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");
        var thickness = int.TryParse(line?.Attribute("w")?.Value, out var emus)
            ? Math.Clamp(emus / 12700.0, 0.25, 10)
            : (double?)null;
        CellColor? color = null;
        var fill = line?.Element(DrawingNs + "solidFill");
        if (fill is not null && XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var concreteColor))
            color = concreteColor;

        return new AxisGridlineProperties(true, color, thickness);
    }

    private readonly record struct AxisGridlineProperties(bool Visible, CellColor? Color, double? Thickness);

    private static AxisCrossingProperties ReadAxisCrossing(XElement axisElement)
    {
        var crossesAt = ReadDouble(axisElement.Element(ChartNs + "crossesAt")?.Attribute("val")?.Value);
        var crosses = crossesAt is not null
            ? ChartAxisCrosses.Custom
            : FromXlsxAxisCrosses(axisElement.Element(ChartNs + "crosses")?.Attribute("val")?.Value);
        var crossBetween = FromXlsxAxisCrossBetween(axisElement.Element(ChartNs + "crossBetween")?.Attribute("val")?.Value);
        return new AxisCrossingProperties(crosses, crossesAt, crossBetween);
    }

    private readonly record struct AxisCrossingProperties(
        ChartAxisCrosses Crosses,
        double? CrossesAt,
        ChartAxisCrossBetween? CrossBetween);

    private static void ApplyXAxisLineProperties(ChartModel chart, AxisLineProperties axisLine)
    {
        if (axisLine.Color is { } color)
            chart.XAxisLineColor = color;
        if (axisLine.Thickness is { } thickness)
            chart.XAxisLineThickness = thickness;
    }

    private static void ApplyYAxisLineProperties(ChartModel chart, AxisLineProperties axisLine)
    {
        if (axisLine.Color is { } color)
            chart.YAxisLineColor = color;
        if (axisLine.Thickness is { } thickness)
            chart.YAxisLineThickness = thickness;
    }

    private static AxisLineProperties ReadAxisLine(XElement? shapeProperties)
    {
        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return new AxisLineProperties(null, null);

        var thickness = int.TryParse(line.Attribute("w")?.Value, out var emus)
            ? Math.Clamp(emus / 12700.0, 0.5, 10)
            : (double?)null;
        CellColor? color = null;
        var fill = line.Element(DrawingNs + "solidFill");
        if (fill is not null && XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var concreteColor))
            color = concreteColor;

        return new AxisLineProperties(color, thickness);
    }

    private static ChartAxisTickStyle FromXlsxTickMark(string? value, ChartAxisTickStyle fallback) =>
        value switch
        {
            "in" => ChartAxisTickStyle.Inside,
            "out" => ChartAxisTickStyle.Outside,
            "cross" => ChartAxisTickStyle.Cross,
            "none" => ChartAxisTickStyle.None,
            _ => fallback
        };

    private static ChartAxisCrosses FromXlsxAxisCrosses(string? value) =>
        value switch
        {
            "min" => ChartAxisCrosses.Minimum,
            "max" => ChartAxisCrosses.Maximum,
            _ => ChartAxisCrosses.AutoZero
        };

    private static ChartAxisCrossBetween? FromXlsxAxisCrossBetween(string? value) =>
        value switch
        {
            "between" => ChartAxisCrossBetween.Between,
            "midCat" => ChartAxisCrossBetween.MidCategory,
            _ => null
        };

    private readonly record struct AxisLineProperties(CellColor? Color, double? Thickness);

    private static double? ReadDouble(string? value) =>
        XlsxChartScalarReader.ReadOptionalDouble(value);

    private static int? ReadInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static bool ReadBool(string? value) =>
        value is "1" or "true";
}
