using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxAdvancedConditionalFormatWriter
{
    public static bool HasAdvancedConditionalFormats(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (HasAdvancedConditionalFormats(sheet))
                return true;
        }

        return false;
    }

    public static bool HasAdvancedConditionalFormats(Sheet sheet)
    {
        foreach (var conditionalFormat in sheet.ConditionalFormats)
        {
            if (XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat(conditionalFormat))
                return true;
        }

        return false;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, XlsxWorkbookWorksheetPathMap.TryCreate(archive));
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, worksheetPathMap);
    }

    private static void Save(ZipArchive archive, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dxfIds = SaveDifferentialStyles(archive, workbook, workbookNs);

        foreach (var sheet in workbook.Sheets)
        {
            List<ConditionalFormat>? advancedRules = null;
            foreach (var conditionalFormat in sheet.ConditionalFormats)
            {
                if (!XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat(conditionalFormat))
                    continue;

                advancedRules ??= [];
                advancedRules.Add(conditionalFormat);
            }

            if (advancedRules is null ||
                !worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            List<ConditionalFormat>? newX14DataBars = null;
            foreach (var cf in advancedRules)
            {
                root.Add(ToAdvancedConditionalFormattingXml(cf, workbookNs, dxfIds));
                if (cf.RuleType == CfRuleType.DataBar &&
                    XlsxAdvancedConditionalFormatMetadata.RequiresGeneratedOrExistingX14DataBar(cf))
                {
                    newX14DataBars ??= [];
                    newX14DataBars.Add(cf);
                }
            }

            if (newX14DataBars is not null)
                AppendX14ConditionalFormattingsExt(root, newX14DataBars, workbookNs);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static XElement ToAdvancedConditionalFormattingXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds) =>
        AddAdvancedConditionalFormattingNativeMetadata(
            new XElement(
                worksheetNs + "conditionalFormatting",
                new XAttribute("sqref", cf.AppliesTo.ToString()),
                ToAdvancedCfRuleXml(cf, worksheetNs, differentialStyleIds)),
            cf,
            worksheetNs);

    private static XElement AddAdvancedConditionalFormattingNativeMetadata(
        XElement element,
        ConditionalFormat cf,
        XNamespace worksheetNs)
    {
        if (cf.NativeContainerAttributes is { } attributes)
        {
            foreach (var (name, value) in attributes)
                TrySetNativeAttributeIfMissing(element, name, value);
        }

        if (cf.NativeContainerChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                if (string.IsNullOrWhiteSpace(nativeChildXml))
                    continue;

                try
                {
                    var nativeChild = XElement.Parse(nativeChildXml);
                    if (nativeChild.Name.Namespace == worksheetNs && nativeChild.Name.LocalName != "cfRule")
                        element.Add(nativeChild);
                }
                catch
                {
                    // Ignore malformed native conditional-format container payloads from older saves.
                }
            }
        }

        return element;
    }

    private static XElement ToAdvancedCfRuleXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds)
    {
        var rule = new XElement(
            worksheetNs + "cfRule",
            new XAttribute("type", XlsxAdvancedConditionalFormatMetadata.ToAdvancedCfRuleType(cf.RuleType)),
            new XAttribute("priority", cf.Priority));
        if (differentialStyleIds.TryGetValue(cf.Id, out var dxfId))
            rule.SetAttributeValue("dxfId", dxfId.ToString(CultureInfo.InvariantCulture));
        if (cf.StopIfTrue)
            rule.SetAttributeValue("stopIfTrue", "1");
        switch (cf.RuleType)
        {
            case CfRuleType.ColorScale:
                rule.Add(AddConditionalFormatPayloadNativeMetadata(new XElement(
                    worksheetNs + "colorScale",
                    ToCfvoXml(worksheetNs, cf.MinThresholdType, cf.MinThresholdValue, cf.MinThresholdGreaterThanOrEqual),
                    cf.UseThreeColorScale ? ToCfvoXml(worksheetNs, cf.MidThresholdType, cf.MidThresholdValue, cf.MidThresholdGreaterThanOrEqual) : null,
                    ToCfvoXml(worksheetNs, cf.MaxThresholdType, cf.MaxThresholdValue, cf.MaxThresholdGreaterThanOrEqual),
                    ToColorXml(worksheetNs, cf.MinColor),
                    cf.UseThreeColorScale ? ToColorXml(worksheetNs, cf.MidColor) : null,
                    ToColorXml(worksheetNs, cf.MaxColor)), cf, worksheetNs));
                break;
            case CfRuleType.DataBar:
                var dataBar = new XElement(
                    worksheetNs + "dataBar",
                    new XAttribute("showValue", cf.DataBarShowValue ? "1" : "0"),
                    ToCfvoXml(worksheetNs, cf.DataBarMinThresholdType, cf.DataBarMinThresholdValue),
                    ToCfvoXml(worksheetNs, cf.DataBarMaxThresholdType, cf.DataBarMaxThresholdValue),
                    ToColorXml(worksheetNs, cf.DataBarColor));
                if (cf.DataBarMinLength.HasValue)
                    dataBar.SetAttributeValue("minLength", cf.DataBarMinLength.Value.ToString(CultureInfo.InvariantCulture));
                if (cf.DataBarMaxLength.HasValue)
                    dataBar.SetAttributeValue("maxLength", cf.DataBarMaxLength.Value.ToString(CultureInfo.InvariantCulture));
                rule.Add(AddConditionalFormatPayloadNativeMetadata(dataBar, cf, worksheetNs));
                if (XlsxAdvancedConditionalFormatMetadata.RequiresGeneratedOrExistingX14DataBar(cf) &&
                    XlsxAdvancedConditionalFormatMetadata.TryGetExistingX14Id(cf) is null)
                {
                    XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
                    rule.Add(new XElement(
                        worksheetNs + "extLst",
                        new XElement(
                            worksheetNs + "ext",
                            new XAttribute("uri", "{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"),
                            new XElement(x14Ns + "id", GetX14DataBarId(cf)))));
                }

                break;
            case CfRuleType.IconSet:
            {
                var thresholdXmls = GetIconSetThresholds(cf)
                    .Select(threshold => ToCfvoXml(worksheetNs, threshold.Type, threshold.Value, threshold.GreaterThanOrEqual));
                var overrideXmls = cf.IconOverrides
                    .Where(IsValidIconOverride)
                    .Select(o => new XElement(
                        worksheetNs + "cfIcon",
                        new XAttribute("iconSet", o.IconSet.Trim()),
                        new XAttribute("iconId", o.IconId.ToString(CultureInfo.InvariantCulture))));
                rule.Add(AddConditionalFormatPayloadNativeMetadata(new XElement(
                    worksheetNs + "iconSet",
                    new XAttribute("iconSet", string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle.Trim()),
                    new XAttribute("showValue", cf.IconSetShowValue ? "1" : "0"),
                    new XAttribute("reverse", cf.IconSetReverse ? "1" : "0"),
                    thresholdXmls,
                    overrideXmls), cf, worksheetNs));
                break;
            }
            case CfRuleType.AboveAverage:
                rule.SetAttributeValue("aboveAverage", cf.AboveAverage ? "1" : "0");
                break;
            case CfRuleType.Top10:
                rule.SetAttributeValue("rank", Math.Clamp(cf.TopBottomRank, 1, 1000).ToString(CultureInfo.InvariantCulture));
                rule.SetAttributeValue("bottom", cf.AboveAverage ? "0" : "1");
                rule.SetAttributeValue("percent", cf.TopBottomPercent ? "1" : "0");
                break;
            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                if (!string.IsNullOrWhiteSpace(cf.TextRuleText))
                    rule.SetAttributeValue("text", cf.TextRuleText);
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
            case CfRuleType.DateOccurring:
                rule.SetAttributeValue("timePeriod", XlsxAdvancedConditionalFormatMetadata.NormalizeDateOccurringPeriod(cf.DateOccurringPeriod));
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
            case CfRuleType.Blanks:
            case CfRuleType.NoBlanks:
            case CfRuleType.Errors:
            case CfRuleType.NoErrors:
            case CfRuleType.UniqueValues:
            case CfRuleType.DuplicateValues:
                if (!string.IsNullOrWhiteSpace(cf.FormulaText))
                    rule.Add(new XElement(worksheetNs + "formula", cf.FormulaText));
                break;
        }

        if (cf.NativeAttributes is { } attributes)
        {
            foreach (var (name, value) in attributes)
                TrySetNativeAttributeIfMissing(rule, name, value);
        }

        if (cf.NativeChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                if (string.IsNullOrWhiteSpace(nativeChildXml))
                    continue;

                try
                {
                    var nativeChild = XElement.Parse(nativeChildXml);
                    if (nativeChild.Name.Namespace == worksheetNs)
                        rule.Add(nativeChild);
                }
                catch
                {
                    // Ignore malformed native conditional-format payloads from older saves.
                }
            }
        }

        return rule;
    }

    private static XElement AddConditionalFormatPayloadNativeMetadata(
        XElement payload,
        ConditionalFormat cf,
        XNamespace worksheetNs)
    {
        var modeledDataBarAttributes = cf.RuleType == CfRuleType.DataBar
            ? XlsxAdvancedConditionalFormatMetadata.ModeledDataBarPayloadAttributes(cf)
            : [];
        if (cf.NativePayloadAttributes is { } attributes)
        {
            foreach (var (name, value) in attributes)
            {
                if (!modeledDataBarAttributes.Contains(name))
                    TrySetNativeAttributeIfMissing(payload, name, value);
            }
        }

        var modeledDataBarChildren = cf.RuleType == CfRuleType.DataBar
            ? XlsxAdvancedConditionalFormatMetadata.ModeledDataBarPayloadChildren(cf)
            : [];
        if (cf.NativePayloadChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                if (string.IsNullOrWhiteSpace(nativeChildXml))
                    continue;

                try
                {
                    var nativeChild = XElement.Parse(nativeChildXml);
                    if (nativeChild.Name.Namespace == worksheetNs &&
                        !modeledDataBarChildren.Contains(nativeChild.Name.LocalName))
                    {
                        payload.Add(nativeChild);
                    }
                }
                catch
                {
                    // Ignore malformed native conditional-format payload metadata from older saves.
                }
            }
        }

        return payload;
    }

    private static IReadOnlyList<CfThresholdModel> GetIconSetThresholds(ConditionalFormat cf) =>
        cf.IconSetThresholds.Count > 0
            ? cf.IconSetThresholds
            :
            [
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "33"),
                new CfThresholdModel(CfThresholdType.Percent, "67")
            ];

    private static bool IsValidIconOverride(CfIconOverride icon) =>
        !string.IsNullOrWhiteSpace(icon.IconSet) && icon.IconId >= 0;

    private static bool TrySetNativeAttributeIfMissing(XElement element, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            var attributeName = XName.Get(name);
            if (element.Attribute(attributeName) is not null)
                return false;

            element.SetAttributeValue(attributeName, value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static XElement ToCfvoXml(XNamespace worksheetNs, CfThresholdType type, string? value)
    {
        return ToCfvoXml(worksheetNs, type, value, greaterThanOrEqual: null);
    }

    private static XElement ToCfvoXml(
        XNamespace worksheetNs,
        CfThresholdType type,
        string? value,
        bool? greaterThanOrEqual)
    {
        var element = new XElement(worksheetNs + "cfvo", new XAttribute("type", XlsxAdvancedConditionalFormatMetadata.ToCfvoType(type)));
        if (!string.IsNullOrWhiteSpace(value))
            element.SetAttributeValue("val", value);
        if (greaterThanOrEqual.HasValue)
            element.SetAttributeValue("gte", greaterThanOrEqual.Value ? "1" : "0");
        return element;
    }

    private static XElement ToColorXml(XNamespace worksheetNs, RgbColor color) =>
        new(worksheetNs + "color", new XAttribute("rgb", $"FF{color.R:X2}{color.G:X2}{color.B:X2}"));

    private static string ToArgb(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ToPatternType(CellFillPatternStyle style) =>
        style switch
        {
            CellFillPatternStyle.Solid => "solid",
            CellFillPatternStyle.Gray0625 => "gray0625",
            CellFillPatternStyle.Gray125 => "gray125",
            CellFillPatternStyle.LightGray => "lightGray",
            CellFillPatternStyle.MediumGray => "mediumGray",
            CellFillPatternStyle.DarkGray => "darkGray",
            CellFillPatternStyle.LightHorizontal => "lightHorizontal",
            CellFillPatternStyle.LightVertical => "lightVertical",
            CellFillPatternStyle.LightDown => "lightDown",
            CellFillPatternStyle.LightUp => "lightUp",
            CellFillPatternStyle.LightGrid => "lightGrid",
            CellFillPatternStyle.LightTrellis => "lightTrellis",
            CellFillPatternStyle.DarkHorizontal => "darkHorizontal",
            CellFillPatternStyle.DarkVertical => "darkVertical",
            CellFillPatternStyle.DarkDown => "darkDown",
            CellFillPatternStyle.DarkUp => "darkUp",
            CellFillPatternStyle.DarkGrid => "darkGrid",
            CellFillPatternStyle.DarkTrellis => "darkTrellis",
            _ => "solid"
        };

    private static string GetX14DataBarId(ConditionalFormat cf) =>
        XlsxAdvancedConditionalFormatMetadata.TryGetExistingX14Id(cf) ?? $"{{{cf.Id.ToString().ToUpperInvariant()}}}";

    private static void AppendX14ConditionalFormattingsExt(
        XElement worksheetRoot,
        IReadOnlyList<ConditionalFormat> newGradientFalseRules,
        XNamespace worksheetNs)
    {
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        const string x14CfUri = "{78C0D931-6437-407d-A8EE-F0AAD7539E65}";

        var x14CfElements = new List<XElement>(newGradientFalseRules.Count);
        foreach (var cf in newGradientFalseRules)
        {
            var dataBar = new XElement(
                x14Ns + "dataBar",
                new XAttribute("minLength", (cf.DataBarMinLength ?? 0).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("maxLength", (cf.DataBarMaxLength ?? 100).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("gradient", cf.DataBarGradient ? "1" : "0"),
                cf.DataBarBorder ? new XAttribute("border", "1") : null,
                string.IsNullOrWhiteSpace(cf.DataBarAxisPosition) ? null : new XAttribute("axisPosition", cf.DataBarAxisPosition),
                new XElement(x14Ns + "cfvo", new XAttribute("type", "autoMin")),
                new XElement(x14Ns + "cfvo", new XAttribute("type", "autoMax")),
                ToX14ColorXml(x14Ns, "axisColor", cf.DataBarAxisColor),
                ToX14ColorXml(x14Ns, "negativeFillColor", cf.DataBarNegativeFillColor),
                ToX14ColorXml(x14Ns, "negativeBorderColor", cf.DataBarNegativeBorderColor));
            AddNativeX14DataBarChildren(dataBar, cf, x14Ns);

            x14CfElements.Add(new XElement(
                x14Ns + "conditionalFormatting",
                new XAttribute("sqref", cf.AppliesTo.ToString()),
                new XElement(
                    x14Ns + "cfRule",
                    new XAttribute("type", "dataBar"),
                    new XAttribute("id", GetX14DataBarId(cf)),
                    dataBar)));
        }

        worksheetRoot.Add(new XElement(
            worksheetNs + "extLst",
            new XElement(
                worksheetNs + "ext",
                new XAttribute(XNamespace.Xmlns + "x14", x14Ns.NamespaceName),
                new XAttribute("uri", x14CfUri),
                new XElement(x14Ns + "conditionalFormattings", x14CfElements))));
    }

    private static XElement? ToX14ColorXml(XNamespace x14Ns, string elementName, RgbColor? color) =>
        color is null
            ? null
            : new XElement(x14Ns + elementName, new XAttribute("rgb", $"FF{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}"));

    private static void AddNativeX14DataBarChildren(XElement dataBar, ConditionalFormat cf, XNamespace x14Ns)
    {
        var modeledChildren = XlsxAdvancedConditionalFormatMetadata.ModeledDataBarPayloadChildren(cf);
        if (cf.NativePayloadChildXmls is null)
            return;

        foreach (var nativeChildXml in cf.NativePayloadChildXmls)
        {
            if (string.IsNullOrWhiteSpace(nativeChildXml))
                continue;

            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == x14Ns &&
                    !modeledChildren.Contains(nativeChild.Name.LocalName))
                {
                    dataBar.Add(nativeChild);
                }
            }
            catch
            {
                // Ignore malformed native x14 data-bar payload metadata from older saves.
            }
        }
    }

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;
}
