using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartFormattingReader
{
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyChartTitleFormatting(XElement? titleElement, ChartModel chart)
    {
        XElement? runProperties = null;
        foreach (var candidate in titleElement?.Descendants(DrawingNs + "rPr") ?? [])
        {
            runProperties = candidate;
            break;
        }

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

        // R42-io-chart-plotarea-legend-3-1: an explicit <a:noFill/> (the user picked "No Fill")
        // must be distinguished from simply having no fill element at all, so it can be
        // re-emitted on save instead of silently reverting to the themed default.
        if (shapeProperties.Element(DrawingNs + "noFill") is not null)
        {
            chart.ChartAreaNoFill = true;
            chart.ChartAreaFillColor = null;
            chart.ChartAreaFillThemeColor = null;
        }
        else
        {
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
            else if (TryReadGradientFillFirstStop(shapeProperties, out var gradThemeColor, out var gradColor))
            {
                chart.ChartAreaFillThemeColor = gradThemeColor;
                chart.ChartAreaFillColor = gradColor;
            }
        }

        var line = shapeProperties.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.ChartAreaBorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

        // Same noFill-vs-absent distinction as above, but for the border/line ("No Line").
        if (line.Element(DrawingNs + "noFill") is not null)
        {
            chart.ChartAreaNoLine = true;
            chart.ChartAreaBorderColor = null;
            chart.ChartAreaBorderThemeColor = null;
            return;
        }

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

        // R42-io-chart-plotarea-legend-3-1: see ApplyChartAreaShapeProperties -- an explicit
        // <a:noFill/> must be preserved as an explicit "No Fill" choice, not just treated as
        // "nothing set".
        if (shapeProperties.Element(DrawingNs + "noFill") is not null)
        {
            chart.PlotAreaNoFill = true;
            chart.PlotAreaFillColor = null;
            chart.PlotAreaFillThemeColor = null;
        }
        else
        {
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
            else if (TryReadGradientFillFirstStop(shapeProperties, out var gradThemeColor, out var gradColor))
            {
                chart.PlotAreaFillThemeColor = gradThemeColor;
                chart.PlotAreaFillColor = gradColor;
            }
        }

        var line = shapeProperties.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.PlotAreaBorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

        // Same noFill-vs-absent distinction as above, but for the border/line ("No Line").
        if (line.Element(DrawingNs + "noFill") is not null)
        {
            chart.PlotAreaNoLine = true;
            chart.PlotAreaBorderColor = null;
            chart.PlotAreaBorderThemeColor = null;
            return;
        }

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
            result.BorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

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

    /// <summary>
    /// Reads the first gradient stop from a &lt;a:gradFill&gt; element and returns its
    /// color as either a theme-color reference or a concrete color.  Used to approximate
    /// gradient chart-area and plot-area backgrounds as a single solid fill.
    /// </summary>
    private static bool TryReadGradientFillFirstStop(
        XElement shapeProperties,
        out WorkbookThemeColorReference? gradThemeColor,
        out CellColor? gradColor)
    {
        gradThemeColor = null;
        gradColor = null;

        var gradFill = shapeProperties.Element(DrawingNs + "gradFill");
        if (gradFill is null)
            return false;

        // Walk the gradient stop list; use the first stop whose color we can resolve.
        var gsLst = gradFill.Element(DrawingNs + "gsLst");
        if (gsLst is null)
            return false;

        foreach (var gs in gsLst.Elements(DrawingNs + "gs"))
        {
            // A gradient stop (<a:gs>) carries its color directly as a child element
            // (e.g. <a:schemeClr> or <a:srgbClr>), NOT wrapped in a <a:solidFill>.
            // The color-reader helpers look for schemeClr/srgbClr as children of the
            // element they receive, so we can pass the <a:gs> element directly.
            if (XlsxDrawingColorReader.TryReadThemeColorReference(gs, DrawingNs, out var themeRef))
            {
                gradThemeColor = themeRef;
                return true;
            }

            if (XlsxDrawingColorReader.TryReadConcreteColor(gs, DrawingNs, out var concrete))
            {
                gradColor = concrete;
                return true;
            }
        }

        return false;
    }
}
