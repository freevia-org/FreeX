using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartDataLabelReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyDataLabels(XElement? plotArea, ChartModel chart)
    {
        var dataLabels = FindPlotChartElement(plotArea)?.Element(ChartNs + "dLbls");
        if (dataLabels is null)
            return;

        chart.ShowDataLabels = true;
        chart.DataLabelPosition = FromXlsxDataLabelPosition(dataLabels.Element(ChartNs + "dLblPos")?.Attribute("val")?.Value);
        chart.DataLabelNumberFormat = XlsxChartAxisReader.FromXlsxNumberFormatCode(dataLabels.Element(ChartNs + "numFmt")?.Attribute("formatCode")?.Value);
        chart.ShowDataLabelValue = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showVal")?.Attribute("val")?.Value);
        chart.ShowDataLabelLegendKey = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showLegendKey")?.Attribute("val")?.Value);
        chart.ShowDataLabelBubbleSize = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showBubbleSize")?.Attribute("val")?.Value);
        chart.ShowDataLabelCategoryName = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showCatName")?.Attribute("val")?.Value);
        chart.ShowDataLabelSeriesName = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showSerName")?.Attribute("val")?.Value);
        chart.ShowDataLabelPercentage = ChartTypeSupport.SupportsPercentageDataLabels(chart.Type)
            && XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showPercent")?.Attribute("val")?.Value);
        chart.ShowDataLabelCallouts = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showLeaderLines")?.Attribute("val")?.Value);
        var separator = dataLabels.Element(ChartNs + "separator");
        chart.DataLabelSeparator = FromXlsxDataLabelSeparator(separator?.Attribute("val")?.Value ?? separator?.Value);
        ApplyDataLabelShapeProperties(dataLabels.Element(ChartNs + "spPr"), chart);
        ApplyDataLabelTextProperties(dataLabels.Element(ChartNs + "txPr"), chart);
    }

    public static void ApplyPointDataLabels(XElement series, int seriesIndex, ChartModel chart)
    {
        var dataLabels = series.Element(ChartNs + "dLbls");
        if (dataLabels is null)
            return;

        foreach (var label in dataLabels.Elements(ChartNs + "dLbl"))
        {
            if (!int.TryParse(label.Element(ChartNs + "idx")?.Attribute("val")?.Value, out var pointIndex) ||
                pointIndex < 0)
            {
                continue;
            }

            var format = ReadPointDataLabelFormat(label, seriesIndex, pointIndex);
            if (format.FillColor is null &&
                format.BorderColor is null &&
                format.BorderThickness is null &&
                format.TextColor is null &&
                format.FontSize is null &&
                format.FillThemeColor is null &&
                format.BorderThemeColor is null &&
                format.TextThemeColor is null)
            {
                continue;
            }

            chart.PointDataLabelFormats.RemoveAll(existing =>
                existing.SeriesIndex == seriesIndex &&
                existing.PointIndex == pointIndex);
            chart.PointDataLabelFormats.Add(format);
        }
    }

    private static XElement? FindPlotChartElement(XElement? plotArea) =>
        plotArea?.Elements().FirstOrDefault(element => element.Name == ChartNs + "barChart"
            || element.Name == ChartNs + "lineChart"
            || element.Name == ChartNs + "line3DChart"
            || element.Name == ChartNs + "scatterChart"
            || element.Name == ChartNs + "areaChart"
            || element.Name == ChartNs + "area3DChart"
            || element.Name == ChartNs + "radarChart"
            || element.Name == ChartNs + "stockChart"
            || element.Name == ChartNs + "bubbleChart"
            || element.Name == ChartNs + "pie3DChart"
            || element.Name == ChartNs + "pieChart"
            || element.Name == ChartNs + "doughnutChart");

    private static void ApplyDataLabelShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                chart.DataLabelFillThemeColor = fillThemeColor;
                chart.DataLabelFillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                chart.DataLabelFillColor = fillColor;
                chart.DataLabelFillThemeColor = null;
            }
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.DataLabelBorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            chart.DataLabelBorderThemeColor = borderThemeColor;
            chart.DataLabelBorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            chart.DataLabelBorderColor = borderColor;
            chart.DataLabelBorderThemeColor = null;
        }
    }

    private static void ApplyDataLabelTextProperties(XElement? textPropertiesRoot, ChartModel chart)
    {
        var bodyProperties = textPropertiesRoot?.Element(DrawingNs + "bodyPr");
        if (int.TryParse(bodyProperties?.Attribute("rot")?.Value, out var rotation))
            chart.DataLabelAngle = Math.Clamp(rotation / 60000.0, -90, 90);

        var textProperties = textPropertiesRoot?
            .Descendants(DrawingNs + "defRPr")
            .FirstOrDefault();
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            chart.DataLabelFontSize = Math.Clamp(size / 100.0, 6, 72);

        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var textThemeColor))
        {
            chart.DataLabelTextThemeColor = textThemeColor;
            chart.DataLabelTextColor = null;
        }
        else if (textFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var textColor))
        {
            chart.DataLabelTextColor = textColor;
            chart.DataLabelTextThemeColor = null;
        }
    }

    private static ChartPointDataLabelFormat ReadPointDataLabelFormat(
        XElement label,
        int seriesIndex,
        int pointIndex)
    {
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        CellColor? borderColor = null;
        WorkbookThemeColorReference? borderThemeColor = null;
        double? borderThickness = null;
        CellColor? textColor = null;
        WorkbookThemeColorReference? textThemeColor = null;
        double? fontSize = null;

        var shapeProperties = label.Element(ChartNs + "spPr");
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var theme))
                fillThemeColor = theme;
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var color))
                fillColor = color;
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is not null)
        {
            if (int.TryParse(line.Attribute("w")?.Value, out var emus))
                borderThickness = Math.Clamp(emus / 12700.0, 0, 10);

            var lineFill = line.Element(DrawingNs + "solidFill");
            if (lineFill is not null)
            {
                if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var theme))
                    borderThemeColor = theme;
                else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var color))
                    borderColor = color;
            }
        }

        var textProperties = label
            .Element(ChartNs + "txPr")?
            .Descendants(DrawingNs + "defRPr")
            .FirstOrDefault();
        if (textProperties is not null)
        {
            if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
                fontSize = Math.Clamp(size / 100.0, 6, 72);

            var textFill = textProperties.Element(DrawingNs + "solidFill");
            if (textFill is not null)
            {
                if (XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var theme))
                    textThemeColor = theme;
                else if (XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var color))
                    textColor = color;
            }
        }

        return new ChartPointDataLabelFormat(
            seriesIndex,
            pointIndex,
            fillColor,
            borderColor,
            borderThickness,
            textColor,
            fontSize,
            fillThemeColor,
            borderThemeColor,
            textThemeColor);
    }

    private static ChartDataLabelPosition FromXlsxDataLabelPosition(string? value) =>
        value switch
        {
            "ctr" => ChartDataLabelPosition.Center,
            "inEnd" => ChartDataLabelPosition.InsideEnd,
            "outEnd" => ChartDataLabelPosition.OutsideEnd,
            _ => ChartDataLabelPosition.BestFit
        };

    private static ChartDataLabelSeparator FromXlsxDataLabelSeparator(string? value) =>
        value is not null && value.Contains('\n')
            ? ChartDataLabelSeparator.NewLine
            : value switch
            {
                "semicolon" => ChartDataLabelSeparator.Semicolon,
                "newLine" => ChartDataLabelSeparator.NewLine,
                "space" => ChartDataLabelSeparator.Space,
                "comma" => ChartDataLabelSeparator.Comma,
                "; " or ";" => ChartDataLabelSeparator.Semicolon,
                " " => ChartDataLabelSeparator.Space,
                _ => ChartDataLabelSeparator.Comma
            };
}
