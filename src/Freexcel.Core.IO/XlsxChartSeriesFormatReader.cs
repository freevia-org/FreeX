using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartSeriesFormatReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static bool TryReadSeriesFill(XElement series, int seriesIndex, out ChartSeriesFormat format)
    {
        format = default!;
        var shapeProperties = series.Element(ChartNs + "spPr");
        var solidFill = shapeProperties?.Element(DrawingNs + "solidFill");

        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
            fillThemeColor = themeColor;
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
            fillColor = color;

        var line = shapeProperties?.Element(DrawingNs + "ln");
        var lineFill = line?.Element(DrawingNs + "solidFill");
        CellColor? strokeColor = null;
        WorkbookThemeColorReference? strokeThemeColor = null;
        if (lineFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var lineThemeColor))
            strokeThemeColor = lineThemeColor;
        else if (lineFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var lineColor))
            strokeColor = lineColor;

        double? strokeThickness = null;
        if (int.TryParse(line?.Attribute("w")?.Value, out var emus))
            strokeThickness = Math.Clamp(emus / 12700.0, 0.5, 10);

        ChartLineDashStyle? dashStyle = line?.Element(DrawingNs + "prstDash") is { } dashElement
            ? XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(dashElement.Attribute("val")?.Value)
            : null;
        if (fillColor is null &&
            fillThemeColor is null &&
            strokeColor is null &&
            strokeThemeColor is null &&
            strokeThickness is null &&
            dashStyle is null)
        {
            return false;
        }

        format = new ChartSeriesFormat(
            seriesIndex,
            FillColor: fillColor,
            StrokeColor: strokeColor,
            StrokeThickness: strokeThickness,
            DashStyle: dashStyle,
            FillThemeColor: fillThemeColor,
            StrokeThemeColor: strokeThemeColor);
        return true;
    }

    public static bool TryReadSeriesLine(XElement series, int seriesIndex, out ChartSeriesFormat format)
    {
        format = default!;
        var line = series
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");

        CellColor? strokeColor = null;
        WorkbookThemeColorReference? strokeThemeColor = null;
        var solidFill = line?.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
            strokeThemeColor = themeColor;
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
            strokeColor = color;

        double? strokeThickness = null;
        if (int.TryParse(line?.Attribute("w")?.Value, out var emus))
            strokeThickness = Math.Clamp(emus / 12700.0, 0.5, 10);

        ChartLineDashStyle? dashStyle = line?.Element(DrawingNs + "prstDash") is { } dashElement
            ? XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(dashElement.Attribute("val")?.Value)
            : null;
        var smooth = XlsxChartScalarReader.ReadOptionalBool(series.Element(ChartNs + "smooth")?.Attribute("val")?.Value);

        var marker = series.Element(ChartNs + "marker");
        var markerStyle = marker?.Element(ChartNs + "symbol") is { } symbolElement
            ? FromXlsxMarkerStyle(symbolElement.Attribute("val")?.Value)
            : (ChartMarkerStyle?)null;
        double? markerSize = null;
        if (int.TryParse(marker?.Element(ChartNs + "size")?.Attribute("val")?.Value, out var size))
            markerSize = Math.Clamp(size, 1, 30);
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        var markerFill = marker?
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "solidFill");
        if (markerFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerFill, DrawingNs, out var markerThemeColor))
            fillThemeColor = markerThemeColor;
        else if (markerFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerFill, DrawingNs, out var markerColor))
            fillColor = markerColor;

        var markerLine = marker?
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");
        CellColor? markerBorderColor = null;
        WorkbookThemeColorReference? markerBorderThemeColor = null;
        var markerLineFill = markerLine?.Element(DrawingNs + "solidFill");
        if (markerLineFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerLineFill, DrawingNs, out var markerBorderTheme))
            markerBorderThemeColor = markerBorderTheme;
        else if (markerLineFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerLineFill, DrawingNs, out var markerBorder))
            markerBorderColor = markerBorder;

        double? markerBorderThickness = null;
        if (int.TryParse(markerLine?.Attribute("w")?.Value, out var markerLineEmus))
            markerBorderThickness = Math.Clamp(markerLineEmus / 12700.0, 0, 10);

        if (strokeColor is null &&
            strokeThemeColor is null &&
            strokeThickness is null &&
            dashStyle is null &&
            fillColor is null &&
            fillThemeColor is null &&
            markerStyle is null &&
            markerSize is null &&
            markerBorderColor is null &&
            markerBorderThemeColor is null &&
            markerBorderThickness is null &&
            smooth is null)
        {
            return false;
        }

        format = new ChartSeriesFormat(
            seriesIndex,
            FillColor: fillColor,
            StrokeColor: strokeColor,
            StrokeThickness: strokeThickness,
            DashStyle: dashStyle,
            MarkerStyle: markerStyle,
            MarkerSize: markerSize,
            FillThemeColor: fillThemeColor,
            StrokeThemeColor: strokeThemeColor,
            Smooth: smooth,
            MarkerBorderColor: markerBorderColor,
            MarkerBorderThemeColor: markerBorderThemeColor,
            MarkerBorderThickness: markerBorderThickness);
        return true;
    }

    private static ChartMarkerStyle FromXlsxMarkerStyle(string? value) =>
        value switch
        {
            "none" => ChartMarkerStyle.None,
            "square" => ChartMarkerStyle.Square,
            "diamond" => ChartMarkerStyle.Diamond,
            "triangle" => ChartMarkerStyle.Triangle,
            _ => ChartMarkerStyle.Circle
        };
}
