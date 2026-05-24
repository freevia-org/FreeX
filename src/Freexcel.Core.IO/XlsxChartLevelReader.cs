using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartLevelReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static string? ReadTitle(XDocument chartXml)
    {
        var title = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "title");

        return title?
            .Descendants(DrawingNs + "t")
            .Select(element => element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    public static void ApplyChartLevelProperties(XDocument chartXml, ChartModel chart)
    {
        var chartElement = chartXml.Root?.Element(ChartNs + "chart");
        var title = chartElement?.Element(ChartNs + "title");
        chart.TitleLayout = XlsxChartMetadataReader.ReadManualLayout(title?.Element(ChartNs + "layout"));
        chart.TitleOverlay = XlsxChartScalarReader.IsTrue(title?.Element(ChartNs + "overlay")?.Attribute("val")?.Value);
        XlsxChartFormattingReader.ApplyChartTitleFormatting(title, chart);
        XlsxChartFormattingReader.ApplyChartAreaShapeProperties(chartXml.Root?.Element(ChartNs + "spPr"), chart);
        var plotArea = chartElement?.Element(ChartNs + "plotArea");
        chart.PlotAreaLayout = XlsxChartMetadataReader.ReadManualLayout(plotArea?.Element(ChartNs + "layout"));
        chart.ThreeDView = Read3DView(chartElement?.Element(ChartNs + "view3D"));
        chart.FloorFormat = XlsxChartFormattingReader.ReadSurfaceFormat(chartElement?.Element(ChartNs + "floor"));
        chart.SideWallFormat = XlsxChartFormattingReader.ReadSurfaceFormat(chartElement?.Element(ChartNs + "sideWall"));
        chart.BackWallFormat = XlsxChartFormattingReader.ReadSurfaceFormat(chartElement?.Element(ChartNs + "backWall"));
        chart.DataTable = ReadChartDataTable(plotArea?.Element(ChartNs + "dTable"));
        XlsxChartFormattingReader.ApplyPlotAreaShapeProperties(plotArea?.Element(ChartNs + "spPr"), chart);
        XlsxChartAxisReader.ApplyAxisMetadata(plotArea, chart);
        XlsxChartDataLabelReader.ApplyDataLabels(plotArea, chart);

        var legend = chartElement?.Element(ChartNs + "legend");
        if (legend is null)
        {
            chart.ShowLegend = false;
            chart.LegendPosition = ChartLegendPosition.None;
            chart.LegendOverlay = false;
            return;
        }

        chart.ShowLegend = true;
        chart.LegendLayout = XlsxChartMetadataReader.ReadManualLayout(legend.Element(ChartNs + "layout"));
        chart.LegendPosition = legend.Element(ChartNs + "legendPos")?.Attribute("val")?.Value switch
        {
            "l" => ChartLegendPosition.Left,
            "t" => ChartLegendPosition.Top,
            "b" => ChartLegendPosition.Bottom,
            "r" => ChartLegendPosition.Right,
            _ => ChartLegendPosition.Right
        };
        chart.LegendOverlay = XlsxChartScalarReader.IsTrue(legend.Element(ChartNs + "overlay")?.Attribute("val")?.Value);
        chart.LegendEntries = ReadLegendEntries(legend);
        ApplyLegendFormatting(legend, chart);
    }

    private static List<ChartLegendEntryModel> ReadLegendEntries(XElement legend) =>
        legend.Elements(ChartNs + "legendEntry")
            .Select(entry => new ChartLegendEntryModel(
                XlsxChartScalarReader.ReadOptionalInt(entry.Element(ChartNs + "idx")?.Attribute("val")?.Value) ?? -1,
                XlsxChartScalarReader.ReadOptionalBool(entry.Element(ChartNs + "delete")?.Attribute("val")?.Value)))
            .Where(entry => entry.Index >= 0 && entry.IsDeleted is not null)
            .ToList();

    private static ChartDataTableModel? ReadChartDataTable(XElement? dataTable)
    {
        if (dataTable is null)
            return null;

        var result = new ChartDataTableModel
        {
            ShowHorizontalBorder = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showHorzBorder")?.Attribute("val")?.Value),
            ShowVerticalBorder = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showVertBorder")?.Attribute("val")?.Value),
            ShowOutline = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showOutline")?.Attribute("val")?.Value),
            ShowLegendKeys = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showKeys")?.Attribute("val")?.Value)
        };

        ApplyDataTableShapeProperties(dataTable.Element(ChartNs + "spPr"), result);
        ApplyDataTableTextProperties(dataTable.Element(ChartNs + "txPr"), result);
        return result;
    }

    private static void ApplyDataTableShapeProperties(XElement? shapeProperties, ChartDataTableModel dataTable)
    {
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                dataTable.FillThemeColor = fillThemeColor;
                dataTable.FillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                dataTable.FillColor = fillColor;
                dataTable.FillThemeColor = null;
            }
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            dataTable.BorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            dataTable.BorderThemeColor = borderThemeColor;
            dataTable.BorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            dataTable.BorderColor = borderColor;
            dataTable.BorderThemeColor = null;
        }
    }

    private static void ApplyDataTableTextProperties(XElement? textPropertiesRoot, ChartDataTableModel dataTable)
    {
        var textProperties = textPropertiesRoot?
            .Descendants(DrawingNs + "defRPr")
            .FirstOrDefault();
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            dataTable.FontSize = Math.Clamp(size / 100.0, 6, 72);

        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var textThemeColor))
        {
            dataTable.TextThemeColor = textThemeColor;
            dataTable.TextColor = null;
        }
        else if (textFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var textColor))
        {
            dataTable.TextColor = textColor;
            dataTable.TextThemeColor = null;
        }
    }

    private static Chart3DViewModel? Read3DView(XElement? view3D)
    {
        if (view3D is null)
            return null;

        var result = new Chart3DViewModel
        {
            RotationX = XlsxChartScalarReader.ReadOptionalInt(view3D.Element(ChartNs + "rotX")?.Attribute("val")?.Value),
            HeightPercent = XlsxChartScalarReader.ReadOptionalInt(view3D.Element(ChartNs + "hPercent")?.Attribute("val")?.Value),
            RotationY = XlsxChartScalarReader.ReadOptionalInt(view3D.Element(ChartNs + "rotY")?.Attribute("val")?.Value),
            DepthPercent = XlsxChartScalarReader.ReadOptionalInt(view3D.Element(ChartNs + "depthPercent")?.Attribute("val")?.Value),
            RightAngleAxes = XlsxChartScalarReader.ReadOptionalBool(view3D.Element(ChartNs + "rAngAx")?.Attribute("val")?.Value),
            Perspective = XlsxChartScalarReader.ReadOptionalInt(view3D.Element(ChartNs + "perspective")?.Attribute("val")?.Value)
        };

        return result.RotationX is null
            && result.HeightPercent is null
            && result.RotationY is null
            && result.DepthPercent is null
            && result.RightAngleAxes is null
            && result.Perspective is null
                ? null
                : result;
    }

    private static void ApplyLegendFormatting(XElement legend, ChartModel chart)
    {
        var shapeProperties = legend.Element(ChartNs + "spPr");
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                chart.LegendFillThemeColor = fillThemeColor;
                chart.LegendFillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                chart.LegendFillColor = fillColor;
                chart.LegendFillThemeColor = null;
            }
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is not null)
        {
            if (int.TryParse(line.Attribute("w")?.Value, out var emus))
                chart.LegendBorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

            var lineFill = line.Element(DrawingNs + "solidFill");
            if (lineFill is not null)
            {
                if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
                {
                    chart.LegendBorderThemeColor = borderThemeColor;
                    chart.LegendBorderColor = null;
                }
                else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
                {
                    chart.LegendBorderColor = borderColor;
                    chart.LegendBorderThemeColor = null;
                }
            }
        }

        var textProperties = legend
            .Element(ChartNs + "txPr")?
            .Descendants(DrawingNs + "defRPr")
            .FirstOrDefault();
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            chart.LegendFontSize = Math.Clamp(size / 100.0, 6, 72);

        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var textThemeColor))
        {
            chart.LegendTextThemeColor = textThemeColor;
            chart.LegendTextColor = null;
        }
        else if (textFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var textColor))
        {
            chart.LegendTextColor = textColor;
            chart.LegendTextThemeColor = null;
        }
    }
}
