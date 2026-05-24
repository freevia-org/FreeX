using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static XElement ToChartTitleXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + "title",
            new XElement(chartNs + "tx",
                new XElement(chartNs + "rich",
                        new XElement(drawingNs + "p",
                            new XElement(drawingNs + "r",
                                ToTextRunProperties(chart.ChartTitleTextThemeColor, chart.ChartTitleTextColor, chart.ChartTitleFontSize, drawingNs),
                                new XElement(drawingNs + "t", chart.Title))))),
            ToManualLayoutXml(chart.TitleLayout, chartNs),
            chart.TitleOverlay
                ? new XElement(chartNs + "overlay", new XAttribute("val", "1"))
                : null);

    private static XElement? ToAxisTitleXml(
        string? title,
        ChartManualLayoutModel? layout,
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        string.IsNullOrWhiteSpace(title)
            ? null
            : new XElement(chartNs + "title",
                new XElement(chartNs + "tx",
                    new XElement(chartNs + "rich",
                        new XElement(drawingNs + "p",
                            new XElement(drawingNs + "r",
                                ToTextRunProperties(textThemeColor, textColor, fontSize, drawingNs),
                                new XElement(drawingNs + "t", title))))),
                ToManualLayoutXml(layout, chartNs));

    private static XElement? ToTextRunProperties(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var size = Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200);
        return new XElement(drawingNs + "rPr",
            new XAttribute("sz", size),
            ToTextRunPropertiesContent(textThemeColor, textColor, fontSize, drawingNs));
    }

    private static IEnumerable<object> ToTextRunPropertiesContent(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(textThemeColor, textColor, drawingNs);
        if (fill is not null)
        {
            yield return fill;
        }
    }

    private static XElement? ToChartAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.ChartAreaFillThemeColor,
            chart.ChartAreaFillColor,
            chart.ChartAreaBorderThemeColor,
            chart.ChartAreaBorderColor,
            chart.ChartAreaBorderThickness);

    private static XElement? ToChartDefaultTextPropertiesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.ChartDefaultTextColor is null &&
            chart.ChartDefaultTextThemeColor is null &&
            chart.ChartDefaultFontSize == 11)
        {
            return null;
        }

        return new XElement(chartNs + "txPr",
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.ChartDefaultFontSize * 100), 600, 7200)),
                        ToTextRunPropertiesContent(chart.ChartDefaultTextThemeColor, chart.ChartDefaultTextColor, chart.ChartDefaultFontSize, drawingNs)))));
    }

    private static XElement? ToPlotAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness);

    private static XElement? ToShapeProperties(
        XNamespace chartNs,
        XNamespace drawingNs,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? borderThemeColor,
        CellColor? borderColor,
        double? borderThickness)
    {
        var fill = ToSolidFill(fillThemeColor, fillColor, drawingNs);
        var lineFill = ToSolidFill(borderThemeColor, borderColor, drawingNs);
        var line = lineFill is null && borderThickness is null
            ? null
            : new XElement(drawingNs + "ln",
                borderThickness is null
                    ? null
                    : new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(borderThickness.Value, 0, 10) * 12700))),
                lineFill);

        return fill is null && line is null
            ? null
            : new XElement(chartNs + "spPr", fill, line);
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        XElement? colorElement = null;
        if (themeColor is { } theme)
        {
            colorElement = new XElement(drawingNs + "schemeClr",
                new XAttribute("val", ToDrawingSchemeColor(theme.Slot)));
            ApplyTint(colorElement, theme.Tint, drawingNs);
        }
        else if (color is { } concrete)
        {
            colorElement = new XElement(drawingNs + "srgbClr",
                new XAttribute("val", FormatThemeColor(concrete)));
        }

        return colorElement is null
            ? null
            : new XElement(drawingNs + "solidFill", colorElement);
    }

    private static string FormatThemeColor(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void ApplyTint(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", Math.Clamp((int)Math.Round((1 - tint) * 100000), 0, 100000))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", Math.Clamp((int)Math.Round(tint * 100000), 0, 100000))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(drawingNs + "lumMod",
                new XAttribute("val", Math.Clamp((int)Math.Round((1 + tint) * 100000), 0, 100000))));
        }
    }

    private static XElement? ToLegendXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return null;

        return new XElement(chartNs + "legend",
            new XElement(chartNs + "legendPos",
                new XAttribute("val", ToXlsxLegendPosition(chart.LegendPosition))),
            chart.LegendEntries
                .Where(entry => entry.Index >= 0 && entry.IsDeleted is not null)
                .Select(entry => new XElement(chartNs + "legendEntry",
                    new XElement(chartNs + "idx", new XAttribute("val", entry.Index)),
                    new XElement(chartNs + "delete", new XAttribute("val", entry.IsDeleted == true ? "1" : "0")))),
            ToManualLayoutXml(chart.LegendLayout, chartNs),
            new XElement(chartNs + "overlay",
                new XAttribute("val", chart.LegendOverlay ? "1" : "0")),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.LegendFillThemeColor,
                chart.LegendFillColor,
                chart.LegendBorderThemeColor,
                chart.LegendBorderColor,
                chart.LegendBorderThickness),
            ToLegendTextProperties(chart, chartNs, drawingNs));
    }

    private static XElement? ToManualLayoutXml(ChartManualLayoutModel? layout, XNamespace chartNs)
    {
        if (layout is null ||
            string.IsNullOrWhiteSpace(layout.LayoutTarget) &&
            string.IsNullOrWhiteSpace(layout.XMode) &&
            string.IsNullOrWhiteSpace(layout.YMode) &&
            string.IsNullOrWhiteSpace(layout.WidthMode) &&
            string.IsNullOrWhiteSpace(layout.HeightMode) &&
            layout.X is null &&
            layout.Y is null &&
            layout.Width is null &&
            layout.Height is null)
        {
            return null;
        }

        return new XElement(chartNs + "layout",
            new XElement(chartNs + "manualLayout",
                string.IsNullOrWhiteSpace(layout.LayoutTarget) ? null : new XElement(chartNs + "layoutTarget", new XAttribute("val", layout.LayoutTarget)),
                string.IsNullOrWhiteSpace(layout.XMode) ? null : new XElement(chartNs + "xMode", new XAttribute("val", layout.XMode)),
                string.IsNullOrWhiteSpace(layout.YMode) ? null : new XElement(chartNs + "yMode", new XAttribute("val", layout.YMode)),
                string.IsNullOrWhiteSpace(layout.WidthMode) ? null : new XElement(chartNs + "wMode", new XAttribute("val", layout.WidthMode)),
                string.IsNullOrWhiteSpace(layout.HeightMode) ? null : new XElement(chartNs + "hMode", new XAttribute("val", layout.HeightMode)),
                layout.X is { } x ? new XElement(chartNs + "x", new XAttribute("val", ToChartLayoutDecimal(x))) : null,
                layout.Y is { } y ? new XElement(chartNs + "y", new XAttribute("val", ToChartLayoutDecimal(y))) : null,
                layout.Width is { } width ? new XElement(chartNs + "w", new XAttribute("val", ToChartLayoutDecimal(width))) : null,
                layout.Height is { } height ? new XElement(chartNs + "h", new XAttribute("val", ToChartLayoutDecimal(height))) : null));
    }

    private static string ToChartLayoutDecimal(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    private static XElement? ToLegendTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.LegendTextColor is null && chart.LegendTextThemeColor is null && chart.LegendFontSize == 12)
            return null;

        return new XElement(chartNs + "txPr",
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.LegendFontSize * 100), 600, 7200)),
                        ToTextRunPropertiesContent(chart.LegendTextThemeColor, chart.LegendTextColor, chart.LegendFontSize, drawingNs)))));
    }

    private static XElement? ToDataLabelsXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowDataLabels)
            return null;

        return new XElement(chartNs + "dLbls",
            new XElement(chartNs + "dLblPos", new XAttribute("val", ToXlsxDataLabelPosition(chart.DataLabelPosition))),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(chart.DataLabelNumberFormat, chart.DataLabelNumberFormatCode)),
                new XAttribute("sourceLinked", ToXlsxNumberFormatSourceLinked(chart.DataLabelNumberFormat, chart.DataLabelNumberFormatSourceLinked))),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.DataLabelFillThemeColor,
                chart.DataLabelFillColor,
                chart.DataLabelBorderThemeColor,
                chart.DataLabelBorderColor,
                chart.DataLabelBorderThickness),
            ToDataLabelTextProperties(chart, chartNs, drawingNs),
            new XElement(chartNs + "showLegendKey", new XAttribute("val", chart.ShowDataLabelLegendKey ? "1" : "0")),
            new XElement(chartNs + "showVal", new XAttribute("val", chart.ShowDataLabelValue ? "1" : "0")),
            new XElement(chartNs + "showCatName", new XAttribute("val", chart.ShowDataLabelCategoryName ? "1" : "0")),
            new XElement(chartNs + "showSerName", new XAttribute("val", chart.ShowDataLabelSeriesName ? "1" : "0")),
            new XElement(chartNs + "showPercent", new XAttribute("val", chart.ShowDataLabelPercentage && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type) ? "1" : "0")),
            new XElement(chartNs + "showBubbleSize", new XAttribute("val", chart.ShowDataLabelBubbleSize ? "1" : "0")),
            new XElement(chartNs + "separator",
                chart.DataLabelSeparator == ChartDataLabelSeparator.NewLine
                    ? new XAttribute(XNamespace.Xml + "space", "preserve")
                    : null,
                ToXlsxDataLabelSeparator(chart.DataLabelSeparator)),
            new XElement(chartNs + "showLeaderLines", new XAttribute("val", chart.ShowDataLabelCallouts ? "1" : "0")),
            ToDataLabelLeaderLinesXml(chart, chartNs, drawingNs));
    }

    private static XElement? ToDataLabelLeaderLinesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        var shapeProperties = ToChartGuideLineShapeProperties(
            chart.DataLabelLeaderLineThemeColor,
            chart.DataLabelLeaderLineColor,
            chart.DataLabelLeaderLineThickness,
            chart.DataLabelLeaderLineDashStyle,
            chartNs,
            drawingNs);

        return shapeProperties is null
            ? null
            : new XElement(chartNs + "leaderLines", shapeProperties);
    }

    private static XElement? ToDataLabelTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.DataLabelTextColor is null && chart.DataLabelTextThemeColor is null && chart.DataLabelFontSize == 11 && chart.DataLabelAngle == 0)
            return null;

        var textFill = ToSolidFill(chart.DataLabelTextThemeColor, chart.DataLabelTextColor, drawingNs);
        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(chart.DataLabelAngle, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.DataLabelFontSize * 100), 600, 7200)),
                        textFill))));
    }

    private static XElement? ToTextBodyProperties(double angle, XNamespace drawingNs) =>
        angle == 0
            ? null
            : new XElement(drawingNs + "bodyPr",
                new XAttribute("rot", Math.Clamp((int)Math.Round(angle * 60000), -5400000, 5400000)));

    private static string ToXlsxDataLabelPosition(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => "ctr",
            ChartDataLabelPosition.InsideEnd => "inEnd",
            ChartDataLabelPosition.OutsideEnd => "outEnd",
            _ => "bestFit"
        };

    private static string ToXlsxDataLabelSeparator(ChartDataLabelSeparator separator) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => "\n",
            ChartDataLabelSeparator.Space => " ",
            _ => ", "
        };

    private static string ToXlsxLegendPosition(ChartLegendPosition position) =>
        position switch
        {
            ChartLegendPosition.Left => "l",
            ChartLegendPosition.Top => "t",
            ChartLegendPosition.Bottom => "b",
            _ => "r"
        };

    private static string ToDrawingSchemeColor(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => "dk1",
            WorkbookThemeColorSlot.Light1 => "lt1",
            WorkbookThemeColorSlot.Dark2 => "dk2",
            WorkbookThemeColorSlot.Light2 => "lt2",
            WorkbookThemeColorSlot.Accent1 => "accent1",
            WorkbookThemeColorSlot.Accent2 => "accent2",
            WorkbookThemeColorSlot.Accent3 => "accent3",
            WorkbookThemeColorSlot.Accent4 => "accent4",
            WorkbookThemeColorSlot.Accent5 => "accent5",
            WorkbookThemeColorSlot.Accent6 => "accent6",
            WorkbookThemeColorSlot.Hyperlink => "hlink",
            WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "accent1"
        };
}
