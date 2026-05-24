using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartFormattingReader
{
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyChartTitleFormatting(XElement? titleElement, ChartModel chart)
    {
        var runProperties = titleElement?
            .Descendants(DrawingNs + "rPr")
            .FirstOrDefault();
        if (runProperties is null)
            return;

        if (int.TryParse(runProperties.Attribute("sz")?.Value, out var size))
            chart.ChartTitleFontSize = Math.Clamp(size / 100.0, 6, 72);

        var solidFill = runProperties.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
        {
            chart.ChartTitleTextThemeColor = themeColor;
            chart.ChartTitleTextColor = null;
        }
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
        {
            chart.ChartTitleTextColor = color;
            chart.ChartTitleTextThemeColor = null;
        }
    }

    public static void ApplyChartAreaShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        if (shapeProperties is null)
            return;

        var solidFill = shapeProperties.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
        {
            chart.ChartAreaFillThemeColor = themeColor;
            chart.ChartAreaFillColor = null;
        }
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
        {
            chart.ChartAreaFillColor = color;
            chart.ChartAreaFillThemeColor = null;
        }

        var line = shapeProperties.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.ChartAreaBorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            chart.ChartAreaBorderThemeColor = borderThemeColor;
            chart.ChartAreaBorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            chart.ChartAreaBorderColor = borderColor;
            chart.ChartAreaBorderThemeColor = null;
        }
    }

    public static void ApplyPlotAreaShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        if (shapeProperties is null)
            return;

        var solidFill = shapeProperties.Element(DrawingNs + "solidFill");
        if (solidFill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
            {
                chart.PlotAreaFillThemeColor = themeColor;
                chart.PlotAreaFillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
            {
                chart.PlotAreaFillColor = color;
                chart.PlotAreaFillThemeColor = null;
            }
        }

        var line = shapeProperties.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.PlotAreaBorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            chart.PlotAreaBorderThemeColor = borderThemeColor;
            chart.PlotAreaBorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            chart.PlotAreaBorderColor = borderColor;
            chart.PlotAreaBorderThemeColor = null;
        }
    }

    public static ChartSurfaceFormatModel? ReadSurfaceFormat(XElement? surfaceElement)
    {
        var shapeProperties = surfaceElement?.Element(surfaceElement.Name.Namespace + "spPr");
        if (shapeProperties is null)
            return null;

        var result = new ChartSurfaceFormatModel();
        ApplySurfaceFill(shapeProperties, result);
        ApplySurfaceBorder(shapeProperties, result);

        return result.FillColor is null
            && result.FillThemeColor is null
            && result.BorderColor is null
            && result.BorderThemeColor is null
            && result.BorderThickness is null
                ? null
                : result;
    }

    private static void ApplySurfaceFill(XElement shapeProperties, ChartSurfaceFormatModel result)
    {
        var solidFill = shapeProperties.Element(DrawingNs + "solidFill");
        if (solidFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
        {
            result.FillThemeColor = themeColor;
            result.FillColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
        {
            result.FillColor = color;
            result.FillThemeColor = null;
        }
    }

    private static void ApplySurfaceBorder(XElement shapeProperties, ChartSurfaceFormatModel result)
    {
        var line = shapeProperties.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            result.BorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            result.BorderThemeColor = borderThemeColor;
            result.BorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            result.BorderColor = borderColor;
            result.BorderThemeColor = null;
        }
    }
}
