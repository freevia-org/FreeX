using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static XElement? ToPivotFormatsXml(ChartModel chart, XNamespace chartNs)
    {
        if (string.IsNullOrWhiteSpace(chart.PivotFormatsXml))
            return null;

        try
        {
            var element = XElement.Parse(chart.PivotFormatsXml);
            return element.Name == chartNs + "pivotFmts"
                ? element
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static XElement? ToChartDataTableXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.DataTable is not { } dataTable)
            return null;

        return new XElement(chartNs + "dTable",
            ToChartBooleanValueXml(chartNs, "showHorzBorder", dataTable.ShowHorizontalBorder),
            ToChartBooleanValueXml(chartNs, "showVertBorder", dataTable.ShowVerticalBorder),
            ToChartBooleanValueXml(chartNs, "showOutline", dataTable.ShowOutline),
            ToChartBooleanValueXml(chartNs, "showKeys", dataTable.ShowLegendKeys));
    }

    private static XElement? ToChart3DViewXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.ThreeDView is not { } view)
            return null;

        var element = new XElement(chartNs + "view3D");
        AddOptionalIntElement(element, chartNs, "rotX", view.RotationX);
        AddOptionalIntElement(element, chartNs, "hPercent", view.HeightPercent);
        AddOptionalIntElement(element, chartNs, "rotY", view.RotationY);
        AddOptionalIntElement(element, chartNs, "depthPercent", view.DepthPercent);
        AddOptionalBoolElement(element, chartNs, "rAngAx", view.RightAngleAxes);
        AddOptionalIntElement(element, chartNs, "perspective", view.Perspective);
        return element.HasElements ? element : null;
    }

    private static XElement? ToChartSurfaceFormatXml(
        XNamespace chartNs,
        XNamespace drawingNs,
        string elementName,
        ChartSurfaceFormatModel? format)
    {
        if (format is null)
            return null;

        var shapeProperties = ToShapeProperties(
            chartNs,
            drawingNs,
            format.FillThemeColor,
            format.FillColor,
            format.BorderThemeColor,
            format.BorderColor,
            format.BorderThickness);

        return shapeProperties is null
            ? null
            : new XElement(chartNs + elementName, shapeProperties);
    }

    private static XElement? ToBlankDisplayXml(ChartModel chart, XNamespace chartNs) =>
        chart.BlankDisplayMode == ChartBlankDisplayMode.Gap
            ? null
            : new XElement(chartNs + "dispBlanksAs",
                new XAttribute("val", chart.BlankDisplayMode == ChartBlankDisplayMode.Span ? "span" : "zero"));

    private static XElement? ToChartExternalDataXml(ChartModel chart, XNamespace chartNs, XNamespace relNs)
    {
        if (chart.ExternalData is not { } externalData)
            return null;

        if (string.IsNullOrWhiteSpace(externalData.RelationshipId) && externalData.AutoUpdate is null)
            return null;

        return new XElement(chartNs + "externalData",
            string.IsNullOrWhiteSpace(externalData.RelationshipId)
                ? null
                : new XAttribute(relNs + "id", externalData.RelationshipId),
            externalData.AutoUpdate is { } autoUpdate
                ? new XElement(chartNs + "autoUpdate", new XAttribute("val", autoUpdate ? "1" : "0"))
                : null);
    }

    private static XElement? ToChartColorMapOverrideXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.ColorMapOverride is not { } colorMapOverride)
            return null;

        if (colorMapOverride.UseMasterColorMapping)
            return new XElement(chartNs + "clrMapOvr", new XElement(drawingNs + "masterClrMapping"));

        if (colorMapOverride.OverrideMappings.Count == 0)
            return null;

        return new XElement(chartNs + "clrMapOvr",
            new XElement(drawingNs + "overrideClrMapping",
                colorMapOverride.OverrideMappings
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new XAttribute(pair.Key, pair.Value))));
    }

    private static XElement? ToChartProtectionXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.Protection is not { } protection)
            return null;

        var element = new XElement(chartNs + "protection");
        AddOptionalBoolAttribute(element, "chartObject", protection.ChartObject);
        AddOptionalBoolAttribute(element, "data", protection.Data);
        AddOptionalBoolAttribute(element, "formatting", protection.Formatting);
        AddOptionalBoolAttribute(element, "selection", protection.Selection);
        AddOptionalBoolAttribute(element, "userInterface", protection.UserInterface);
        return element.HasAttributes ? element : null;
    }

    private static XElement? ToChartPrintSettingsXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.PrintSettings is not { } printSettings)
            return null;

        var element = new XElement(chartNs + "printSettings",
            ToChartPageMarginsXml(printSettings.PageMargins, chartNs),
            ToChartPageSetupXml(printSettings.PageSetup, chartNs));
        return element.HasElements ? element : null;
    }

    private static XElement? ToChartPageMarginsXml(ChartPageMarginsModel? margins, XNamespace chartNs)
    {
        if (margins is null)
            return null;

        var element = new XElement(chartNs + "pageMargins");
        AddOptionalDoubleAttribute(element, "l", margins.Left);
        AddOptionalDoubleAttribute(element, "r", margins.Right);
        AddOptionalDoubleAttribute(element, "t", margins.Top);
        AddOptionalDoubleAttribute(element, "b", margins.Bottom);
        AddOptionalDoubleAttribute(element, "header", margins.Header);
        AddOptionalDoubleAttribute(element, "footer", margins.Footer);
        return element.HasAttributes ? element : null;
    }

    private static XElement? ToChartPageSetupXml(ChartPageSetupModel? pageSetup, XNamespace chartNs)
    {
        if (pageSetup is null)
            return null;

        var element = new XElement(chartNs + "pageSetup");
        if (!string.IsNullOrWhiteSpace(pageSetup.PaperSize))
            element.SetAttributeValue("paperSize", pageSetup.PaperSize);
        if (!string.IsNullOrWhiteSpace(pageSetup.Orientation))
            element.SetAttributeValue("orientation", pageSetup.Orientation);
        if (pageSetup.Copies is { } copies)
            element.SetAttributeValue("copies", copies.ToString(CultureInfo.InvariantCulture));
        AddOptionalBoolAttribute(element, "blackAndWhite", pageSetup.BlackAndWhite);
        AddOptionalBoolAttribute(element, "draft", pageSetup.Draft);
        return element.HasAttributes ? element : null;
    }

    private static void AddOptionalBoolAttribute(XElement element, string name, bool? value)
    {
        if (value is { } boolValue)
            element.SetAttributeValue(name, boolValue ? "1" : "0");
    }

    private static void AddOptionalBoolElement(XElement element, XNamespace chartNs, string name, bool? value)
    {
        if (value is { } boolValue)
            element.Add(new XElement(chartNs + name, new XAttribute("val", boolValue ? "1" : "0")));
    }

    private static void AddOptionalIntElement(XElement element, XNamespace chartNs, string name, int? value)
    {
        if (value is { } intValue)
            element.Add(new XElement(chartNs + name, new XAttribute("val", intValue.ToString(CultureInfo.InvariantCulture))));
    }

    private static void AddOptionalDoubleAttribute(XElement element, string name, double? value)
    {
        if (value is { } doubleValue)
            element.SetAttributeValue(name, doubleValue.ToString("G15", CultureInfo.InvariantCulture));
    }

    private static XElement? ToPivotSourceXml(ChartModel chart, Sheet sheet, XNamespace chartNs)
    {
        if (!chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return null;

        var sourceSheetName = string.IsNullOrWhiteSpace(chart.PivotSourceSheetName)
            ? sheet.Name
            : chart.PivotSourceSheetName;
        return new XElement(chartNs + "pivotSource",
            new XElement(chartNs + "name", $"{QuoteSheetName(sourceSheetName)}!{chart.PivotTableName}"),
            new XElement(chartNs + "fmtId", new XAttribute("val", Math.Max(0, chart.PivotSourceFormatId ?? 0))));
    }

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Any(ch => char.IsWhiteSpace(ch) || ch == '\'')
            ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'"
            : sheetName;
}
