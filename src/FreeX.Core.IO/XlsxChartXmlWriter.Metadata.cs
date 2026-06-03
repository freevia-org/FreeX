using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static readonly XNamespace Chart14Ns = "http://schemas.microsoft.com/office/drawing/2007/8/2/chart";

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

    private static XElement? ToChartDataTableXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.DataTable is not { } dataTable)
            return null;

        return new XElement(chartNs + "dTable",
            ToChartBooleanValueXml(chartNs, "showHorzBorder", dataTable.ShowHorizontalBorder),
            ToChartBooleanValueXml(chartNs, "showVertBorder", dataTable.ShowVerticalBorder),
            ToChartBooleanValueXml(chartNs, "showOutline", dataTable.ShowOutline),
            ToChartBooleanValueXml(chartNs, "showKeys", dataTable.ShowLegendKeys),
            ToShapeProperties(
                chartNs,
                drawingNs,
                dataTable.FillThemeColor,
                dataTable.FillColor,
                dataTable.BorderThemeColor,
                dataTable.BorderColor,
                dataTable.BorderThickness),
            ToDataTableTextProperties(dataTable, chartNs, drawingNs));
    }

    private static XElement? ToPivotChartOptionsExtensionXml(ChartModel chart, XNamespace chartNs)
    {
        if (!chart.IsPivotChart)
            return null;

        if (chart.ShowPivotChartFieldButtons &&
            chart.ShowPivotChartReportFilterButtons &&
            chart.ShowPivotChartAxisFieldButtons &&
            chart.ShowPivotChartValueFieldButtons)
        {
            return null;
        }

        return new XElement(chartNs + "extLst",
            new XElement(chartNs + "ext",
                new XAttribute("uri", "{C3380CC4-5D6E-409C-BE32-E72D297353CC}"),
                new XElement(Chart14Ns + "pivotOptions",
                    new XElement(Chart14Ns + "dropZonesVisible", new XAttribute("val", chart.ShowPivotChartFieldButtons ? "1" : "0")),
                    new XElement(Chart14Ns + "dropZoneFilter", new XAttribute("val", chart.ShowPivotChartReportFilterButtons ? "1" : "0")),
                    new XElement(Chart14Ns + "dropZoneCategories", new XAttribute("val", chart.ShowPivotChartAxisFieldButtons ? "1" : "0")),
                    new XElement(Chart14Ns + "dropZoneData", new XAttribute("val", chart.ShowPivotChartValueFieldButtons ? "1" : "0")))));
    }

    private static XElement? ToDataTableTextProperties(
        ChartDataTableModel dataTable,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var textFill = ToSolidFill(dataTable.TextThemeColor, dataTable.TextColor, drawingNs);
        if (textFill is null && dataTable.FontSize is null)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(0, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        dataTable.FontSize is { } fontSize
                            ? new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200))
                            : null,
                        textFill))));
    }

    private static XElement? ToChart3DViewXml(ChartModel chart, XNamespace chartNs)
    {
        if ((chart.ThreeDView ?? ToExcelNativeDefault3DView(chart.Type)) is not { } view)
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

    private static Chart3DViewModel? ToExcelNativeDefault3DView(ChartType chartType) =>
        chartType switch
        {
            ChartType.ThreeDColumn or ChartType.ThreeDBar => new Chart3DViewModel
            {
                RotationX = 15,
                RotationY = 20,
                RightAngleAxes = true
            },
            ChartType.ThreeDLine or ChartType.ThreeDArea or ChartType.ThreeDSurface => new Chart3DViewModel
            {
                RotationX = 15,
                RotationY = 20,
                RightAngleAxes = false
            },
            ChartType.ThreeDPie => new Chart3DViewModel
            {
                RotationX = 30,
                RotationY = 0,
                RightAngleAxes = false
            },
            _ => null
        };

    private static XElement? ToChartSurfaceFormatXml(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs,
        string elementName,
        ChartSurfaceFormatModel? format)
    {
        if (format is null)
            return ToExcelNativeDefault3DSurfaceFormatXml(chart.Type, chartNs, drawingNs, elementName);

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

    private static XElement? ToExcelNativeDefault3DSurfaceFormatXml(
        ChartType chartType,
        XNamespace chartNs,
        XNamespace drawingNs,
        string elementName) =>
        chartType is ChartType.ThreeDColumn
            or ChartType.ThreeDBar
            or ChartType.ThreeDLine
            or ChartType.ThreeDArea
            or ChartType.ThreeDPie
            or ChartType.ThreeDSurface
                ? new XElement(chartNs + elementName,
                    new XElement(chartNs + "thickness", new XAttribute("val", "0")),
                    new XElement(chartNs + "spPr",
                        new XElement(drawingNs + "noFill"),
                        new XElement(drawingNs + "ln",
                            new XElement(drawingNs + "noFill")),
                        new XElement(drawingNs + "effectLst"),
                        new XElement(drawingNs + "sp3d")))
                : null;

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

    private static XElement? ToChartUserShapesXml(ChartModel chart, XNamespace chartNs, XNamespace relNs)
    {
        if (chart.UserShapes is not { } userShapes ||
            string.IsNullOrWhiteSpace(userShapes.RelationshipId))
        {
            return null;
        }

        return new XElement(chartNs + "userShapes", new XAttribute(relNs + "id", userShapes.RelationshipId));
    }

    private static XElement? ToChartColorMapOverrideXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.ColorMapOverride is not { } colorMapOverride)
            return null;

        if (colorMapOverride.UseMasterColorMapping)
            return null;

        if (colorMapOverride.OverrideMappings.Count == 0)
            return null;

        var mappings = DefaultChartColorMappings.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in colorMapOverride.OverrideMappings)
        {
            if (mappings.ContainsKey(pair.Key))
                mappings[pair.Key] = pair.Value;
        }

        return new XElement(chartNs + "clrMapOvr",
            DefaultChartColorMappings
                .Select(pair => new XAttribute(pair.Name, mappings[pair.Name])));
    }

    private static readonly (string Name, string Value)[] DefaultChartColorMappings =
    [
        ("bg1", "lt1"),
        ("tx1", "dk1"),
        ("bg2", "lt2"),
        ("tx2", "dk2"),
        ("accent1", "accent1"),
        ("accent2", "accent2"),
        ("accent3", "accent3"),
        ("accent4", "accent4"),
        ("accent5", "accent5"),
        ("accent6", "accent6"),
        ("hlink", "hlink"),
        ("folHlink", "folHlink")
    ];

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
            ToChartPageSetupXml(printSettings.PageSetup, chartNs),
            ToChartHeaderFooterXml(printSettings.HeaderFooter, chartNs));
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
        AddOptionalBoolAttribute(element, "usePrinterDefaults", pageSetup.UsePrinterDefaults);
        AddOptionalIntAttribute(element, "firstPageNumber", pageSetup.FirstPageNumber);
        AddOptionalIntAttribute(element, "horizontalDpi", pageSetup.HorizontalDpi);
        AddOptionalIntAttribute(element, "verticalDpi", pageSetup.VerticalDpi);
        AddOptionalBoolAttribute(element, "blackAndWhite", pageSetup.BlackAndWhite);
        AddOptionalBoolAttribute(element, "draft", pageSetup.Draft);
        return element.HasAttributes ? element : null;
    }

    private static XElement? ToChartHeaderFooterXml(ChartHeaderFooterModel? headerFooter, XNamespace chartNs)
    {
        if (headerFooter is null)
            return null;

        var element = new XElement(chartNs + "headerFooter");
        AddOptionalBoolAttribute(element, "differentOddEven", headerFooter.DifferentOddEven);
        AddOptionalBoolAttribute(element, "differentFirst", headerFooter.DifferentFirst);
        AddOptionalBoolAttribute(element, "alignWithMargins", headerFooter.AlignWithMargins);
        AddOptionalStringElement(element, chartNs, "oddHeader", headerFooter.OddHeader);
        AddOptionalStringElement(element, chartNs, "oddFooter", headerFooter.OddFooter);
        AddOptionalStringElement(element, chartNs, "evenHeader", headerFooter.EvenHeader);
        AddOptionalStringElement(element, chartNs, "evenFooter", headerFooter.EvenFooter);
        AddOptionalStringElement(element, chartNs, "firstHeader", headerFooter.FirstHeader);
        AddOptionalStringElement(element, chartNs, "firstFooter", headerFooter.FirstFooter);
        return element.HasAttributes || element.HasElements ? element : null;
    }

    private static void AddOptionalStringElement(XElement element, XNamespace chartNs, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            element.Add(new XElement(chartNs + name, value));
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

    private static void AddOptionalIntAttribute(XElement element, string name, int? value)
    {
        if (value is { } intValue)
            element.SetAttributeValue(name, intValue.ToString(CultureInfo.InvariantCulture));
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
