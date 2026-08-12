using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartLevelReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static string? ReadTitle(XDocument chartXml)
    {
        var title = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "title")
            ?? chartXml.Root?
                .Element(ChartExNs + "chart")?
                .Element(ChartExNs + "title");

        return FirstNonBlankText(title);
    }

    public static void ApplyChartLevelProperties(XDocument chartXml, ChartModel chart)
    {
        var chartElement = chartXml.Root?.Element(ChartNs + "chart")
            ?? chartXml.Root?.Element(ChartExNs + "chart");
        var chartNs = chartElement?.Name.Namespace ?? ChartNs;
        var title = chartElement?.Element(chartNs + "title");
        chart.TitleLayout = XlsxChartMetadataReader.ReadManualLayout(title?.Element(chartNs + "layout"));
        chart.TitleOverlay = XlsxChartScalarReader.IsTrue(title?.Element(chartNs + "overlay")?.Attribute("val")?.Value);
        XlsxChartFormattingReader.ApplyChartTitleFormatting(title, chart);
        XlsxChartFormattingReader.ApplyChartAreaShapeProperties(chartXml.Root?.Element(ChartNs + "spPr"), chart);
        var plotArea = chartElement?.Element(chartNs + "plotArea");
        chart.PlotAreaLayout = XlsxChartMetadataReader.ReadManualLayout(plotArea?.Element(chartNs + "layout"));
        chart.ThreeDView = Read3DView(chartElement?.Element(ChartNs + "view3D"));
        chart.FloorFormat = XlsxChartFormattingReader.ReadSurfaceFormat(chartElement?.Element(ChartNs + "floor"));
        chart.SideWallFormat = XlsxChartFormattingReader.ReadSurfaceFormat(chartElement?.Element(ChartNs + "sideWall"));
        chart.BackWallFormat = XlsxChartFormattingReader.ReadSurfaceFormat(chartElement?.Element(ChartNs + "backWall"));
        chart.DataTable = ReadChartDataTable(plotArea?.Element(chartNs + "dTable"));
        XlsxChartFormattingReader.ApplyPlotAreaShapeProperties(plotArea?.Element(chartNs + "spPr"), chart);
        XlsxChartAxisReader.ApplyAxisMetadata(plotArea, chart);
        XlsxChartDataLabelReader.ApplyDataLabels(plotArea, chart);

        var legend = chartElement?.Element(chartNs + "legend");
        if (legend is null)
        {
            chart.ShowLegend = false;
            chart.LegendPosition = ChartLegendPosition.None;
            chart.LegendOverlay = false;
            return;
        }

        chart.ShowLegend = true;
        chart.LegendLayout = XlsxChartMetadataReader.ReadManualLayout(legend.Element(chartNs + "layout"));
        // Classic charts use a <c:legendPos val="..."/> child; chartEx uses a "pos" attribute on
        // <cx:legend> directly.
        var explicitLegendPositionValue = legend.Element(chartNs + "legendPos")?.Attribute("val")?.Value
            ?? legend.Attribute("pos")?.Value;
        chart.LegendPosition = explicitLegendPositionValue switch
        {
            "l" => ChartLegendPosition.Left,
            "t" => ChartLegendPosition.Top,
            "b" => ChartLegendPosition.Bottom,
            "r" => ChartLegendPosition.Right,
            // R62-io-chart-legend-datalabels-6-2: "tr" (top-right corner) is a real ST_LegendPos
            // value reachable from Excel's Format Legend pane (commonly on pie charts); without
            // this case it fell through to plain Right, stretching a compact corner legend into a
            // full-height right-side legend on round-trip.
            "tr" => ChartLegendPosition.TopRight,
            _ => ChartLegendPosition.Right
        };
        // R45-io-chart-datatable-legend-3-2: remember whether the file actually declared a
        // position, so the writer's stacked-chart bottom-default heuristic never overwrites a
        // genuinely explicit "Right" loaded from a real file.
        chart.LegendPositionExplicit = explicitLegendPositionValue is not null;
        // Classic: <c:overlay val="1"/> child. chartEx: "overlay" attribute on <cx:legend>.
        chart.LegendOverlay = XlsxChartScalarReader.IsTrue(
            legend.Element(chartNs + "overlay")?.Attribute("val")?.Value
            ?? legend.Attribute("overlay")?.Value);
        chart.LegendEntries = ReadLegendEntries(legend, chartNs);
        ApplyLegendFormatting(legend, chartNs, chart);
    }

    private static List<ChartLegendEntryModel> ReadLegendEntries(XElement legend, XNamespace chartNs) =>
        legend.Elements(chartNs + "legendEntry")
            .Select(entry => ReadLegendEntry(entry, chartNs))
            // R45-io-chart-datatable-legend-3-1: keep an entry that carries ONLY per-entry text
            // formatting (no <c:delete>), not just entries that hide a legend key.
            .Where(entry => entry.Index >= 0 && (entry.IsDeleted is not null || entry.HasTextFormatting))
            .ToList();

    private static ChartLegendEntryModel ReadLegendEntry(XElement entry, XNamespace chartNs)
    {
        var index = XlsxChartScalarReader.ReadOptionalInt(entry.Element(chartNs + "idx")?.Attribute("val")?.Value) ?? -1;
        var isDeleted = XlsxChartScalarReader.ReadOptionalBool(entry.Element(chartNs + "delete")?.Attribute("val")?.Value);

        var textProperties = FirstDefaultRunProperties(entry.Element(chartNs + "txPr"));
        if (textProperties is null)
            return new ChartLegendEntryModel(index, isDeleted);

        var bold = XlsxChartScalarReader.ReadOptionalBool(textProperties.Attribute("b")?.Value);
        var italic = XlsxChartScalarReader.ReadOptionalBool(textProperties.Attribute("i")?.Value);
        double? fontSize = int.TryParse(textProperties.Attribute("sz")?.Value, out var size)
            ? Math.Clamp(size / 100.0, 6, 72)
            : null;

        CellColor? textColor = null;
        WorkbookThemeColorReference? textThemeColor = null;
        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var themeColor))
                textThemeColor = themeColor;
            else if (XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var concreteColor))
                textColor = concreteColor;
        }

        return new ChartLegendEntryModel(index, isDeleted, bold, italic, fontSize, textColor, textThemeColor);
    }

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
            dataTable.BorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

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
        var textProperties = FirstDefaultRunProperties(textPropertiesRoot);
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

    private static void ApplyLegendFormatting(XElement legend, XNamespace chartNs, ChartModel chart)
    {
        var shapeProperties = legend.Element(chartNs + "spPr");
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
                chart.LegendBorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

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

        var textProperties = FirstDefaultRunProperties(legend.Element(chartNs + "txPr"));
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            chart.LegendFontSize = Math.Clamp(size / 100.0, 6, 72);

        // R45-io-chart-datatable-legend-3-3: legend-wide Bold/Italic from the defRPr attributes.
        chart.LegendBold = XlsxChartScalarReader.ReadOptionalBool(textProperties.Attribute("b")?.Value);
        chart.LegendItalic = XlsxChartScalarReader.ReadOptionalBool(textProperties.Attribute("i")?.Value);

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

    private static string? FirstNonBlankText(XElement? element)
    {
        if (element is null)
            return null;

        foreach (var text in element.Descendants(DrawingNs + "t"))
        {
            var value = text.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static XElement? FirstDefaultRunProperties(XElement? element)
    {
        if (element is null)
            return null;

        foreach (var defaultRunProperties in element.Descendants(DrawingNs + "defRPr"))
            return defaultRunProperties;

        return null;
    }
}
