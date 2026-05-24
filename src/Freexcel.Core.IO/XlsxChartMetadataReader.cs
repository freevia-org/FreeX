using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxChartMetadataReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Chart14Ns = "http://schemas.microsoft.com/office/drawing/2007/8/2/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void ApplyPackageMetadata(XDocument chartXml, ChartModel chart)
    {
        chart.Uses1904DateSystem = XlsxChartScalarReader.IsTrue(chartXml.Root?
            .Element(ChartNs + "date1904")?
            .Attribute("val")?
            .Value);

        chart.Language = chartXml.Root?
            .Element(ChartNs + "lang")?
            .Attribute("val")?
            .Value;

        var styleValue = chartXml.Root?
            .Element(ChartNs + "style")?
            .Attribute("val")?
            .Value;
        if (int.TryParse(styleValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleId))
            chart.ChartStyleId = styleId;

        chart.ColorMapOverride = ReadColorMapOverride(chartXml.Root?.Element(ChartNs + "clrMapOvr"));
        chart.ExternalData = ReadExternalData(chartXml.Root?.Element(ChartNs + "externalData"));
        chart.Protection = ReadProtection(chartXml.Root?.Element(ChartNs + "protection"));
        chart.PrintSettings = ReadPrintSettings(chartXml.Root?.Element(ChartNs + "printSettings"));
        ApplyPivotChartFieldButtonMetadata(chartXml.Root?.Element(ChartNs + "chart"), chart);

        chart.RoundedCorners = XlsxChartScalarReader.IsTrue(chartXml.Root?
            .Element(ChartNs + "roundedCorners")?
            .Attribute("val")?
            .Value);
    }

    public static ChartManualLayoutModel? ReadManualLayout(XElement? layout)
    {
        var manualLayout = layout?.Element(ChartNs + "manualLayout");
        if (manualLayout is null)
            return null;

        var result = new ChartManualLayoutModel
        {
            LayoutTarget = manualLayout.Element(ChartNs + "layoutTarget")?.Attribute("val")?.Value,
            XMode = manualLayout.Element(ChartNs + "xMode")?.Attribute("val")?.Value,
            YMode = manualLayout.Element(ChartNs + "yMode")?.Attribute("val")?.Value,
            WidthMode = manualLayout.Element(ChartNs + "wMode")?.Attribute("val")?.Value,
            HeightMode = manualLayout.Element(ChartNs + "hMode")?.Attribute("val")?.Value,
            X = XlsxChartScalarReader.ReadOptionalDouble(manualLayout.Element(ChartNs + "x")?.Attribute("val")?.Value),
            Y = XlsxChartScalarReader.ReadOptionalDouble(manualLayout.Element(ChartNs + "y")?.Attribute("val")?.Value),
            Width = XlsxChartScalarReader.ReadOptionalDouble(manualLayout.Element(ChartNs + "w")?.Attribute("val")?.Value),
            Height = XlsxChartScalarReader.ReadOptionalDouble(manualLayout.Element(ChartNs + "h")?.Attribute("val")?.Value)
        };

        return string.IsNullOrWhiteSpace(result.LayoutTarget) &&
            string.IsNullOrWhiteSpace(result.XMode) &&
            string.IsNullOrWhiteSpace(result.YMode) &&
            string.IsNullOrWhiteSpace(result.WidthMode) &&
            string.IsNullOrWhiteSpace(result.HeightMode) &&
            result.X is null &&
            result.Y is null &&
            result.Width is null &&
            result.Height is null
            ? null
            : result;
    }

    private static void ApplyPivotChartFieldButtonMetadata(XElement? chartElement, ChartModel chart)
    {
        var pivotOptions = chartElement?
            .Element(ChartNs + "extLst")?
            .Descendants(Chart14Ns + "pivotOptions")
            .FirstOrDefault();
        if (pivotOptions is null)
            return;

        if (XlsxChartScalarReader.ReadOptionalBool(pivotOptions.Element(Chart14Ns + "dropZonesVisible")?.Attribute("val")?.Value) is { } visible)
            chart.ShowPivotChartFieldButtons = visible;
        if (XlsxChartScalarReader.ReadOptionalBool(pivotOptions.Element(Chart14Ns + "dropZoneFilter")?.Attribute("val")?.Value) is { } reportFilter)
            chart.ShowPivotChartReportFilterButtons = reportFilter;
        if (XlsxChartScalarReader.ReadOptionalBool(pivotOptions.Element(Chart14Ns + "dropZoneCategories")?.Attribute("val")?.Value) is { } axis)
            chart.ShowPivotChartAxisFieldButtons = axis;
        if (XlsxChartScalarReader.ReadOptionalBool(pivotOptions.Element(Chart14Ns + "dropZoneData")?.Attribute("val")?.Value) is { } values)
            chart.ShowPivotChartValueFieldButtons = values;
    }

    private static ChartColorMapOverrideModel? ReadColorMapOverride(XElement? colorMapOverride)
    {
        if (colorMapOverride is null)
            return null;

        var masterMapping = colorMapOverride.Element(DrawingNs + "masterClrMapping");
        var overrideMapping = colorMapOverride.Element(DrawingNs + "overrideClrMapping");
        if (masterMapping is null && overrideMapping is null)
            return null;

        var result = new ChartColorMapOverrideModel
        {
            UseMasterColorMapping = masterMapping is not null
        };
        if (overrideMapping is not null)
        {
            foreach (var attribute in overrideMapping.Attributes())
                result.OverrideMappings[attribute.Name.LocalName] = attribute.Value;
        }

        return result;
    }

    private static ChartExternalDataModel? ReadExternalData(XElement? externalData)
    {
        if (externalData is null)
            return null;

        var relationshipId = externalData.Attribute(OfficeRelNs + "id")?.Value;
        var autoUpdate = XlsxChartScalarReader.ReadOptionalBool(externalData
            .Element(ChartNs + "autoUpdate")?
            .Attribute("val")?
            .Value);
        if (string.IsNullOrWhiteSpace(relationshipId) && autoUpdate is null)
            return null;

        return new ChartExternalDataModel
        {
            RelationshipId = relationshipId,
            AutoUpdate = autoUpdate
        };
    }

    private static ChartProtectionModel? ReadProtection(XElement? protection)
    {
        if (protection is null)
            return null;

        return new ChartProtectionModel
        {
            ChartObject = XlsxChartScalarReader.ReadOptionalBool(protection.Attribute("chartObject")?.Value),
            Data = XlsxChartScalarReader.ReadOptionalBool(protection.Attribute("data")?.Value),
            Formatting = XlsxChartScalarReader.ReadOptionalBool(protection.Attribute("formatting")?.Value),
            Selection = XlsxChartScalarReader.ReadOptionalBool(protection.Attribute("selection")?.Value),
            UserInterface = XlsxChartScalarReader.ReadOptionalBool(protection.Attribute("userInterface")?.Value)
        };
    }

    private static ChartPrintSettingsModel? ReadPrintSettings(XElement? printSettings)
    {
        if (printSettings is null)
            return null;

        var pageMargins = printSettings.Element(ChartNs + "pageMargins");
        var pageSetup = printSettings.Element(ChartNs + "pageSetup");
        return new ChartPrintSettingsModel
        {
            PageMargins = pageMargins is null ? null : new ChartPageMarginsModel
            {
                Left = XlsxChartScalarReader.ReadOptionalDouble(pageMargins.Attribute("l")?.Value),
                Right = XlsxChartScalarReader.ReadOptionalDouble(pageMargins.Attribute("r")?.Value),
                Top = XlsxChartScalarReader.ReadOptionalDouble(pageMargins.Attribute("t")?.Value),
                Bottom = XlsxChartScalarReader.ReadOptionalDouble(pageMargins.Attribute("b")?.Value),
                Header = XlsxChartScalarReader.ReadOptionalDouble(pageMargins.Attribute("header")?.Value),
                Footer = XlsxChartScalarReader.ReadOptionalDouble(pageMargins.Attribute("footer")?.Value)
            },
            PageSetup = pageSetup is null ? null : new ChartPageSetupModel
            {
                PaperSize = pageSetup.Attribute("paperSize")?.Value,
                Orientation = pageSetup.Attribute("orientation")?.Value,
                Copies = XlsxChartScalarReader.ReadOptionalInt(pageSetup.Attribute("copies")?.Value),
                UsePrinterDefaults = XlsxChartScalarReader.ReadOptionalBool(pageSetup.Attribute("usePrinterDefaults")?.Value),
                FirstPageNumber = XlsxChartScalarReader.ReadOptionalInt(pageSetup.Attribute("firstPageNumber")?.Value),
                HorizontalDpi = XlsxChartScalarReader.ReadOptionalInt(pageSetup.Attribute("horizontalDpi")?.Value),
                VerticalDpi = XlsxChartScalarReader.ReadOptionalInt(pageSetup.Attribute("verticalDpi")?.Value),
                BlackAndWhite = XlsxChartScalarReader.ReadOptionalBool(pageSetup.Attribute("blackAndWhite")?.Value),
                Draft = XlsxChartScalarReader.ReadOptionalBool(pageSetup.Attribute("draft")?.Value)
            }
        };
    }
}
