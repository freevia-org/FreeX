using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxAdvancedConditionalFormatWriter
{
    public static bool HasAdvancedConditionalFormats(Workbook workbook) =>
        workbook.Sheets.Any(sheet => sheet.ConditionalFormats.Any(IsAdvancedConditionalFormat));

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var dxfIds = SaveDifferentialStyles(archive, workbook, workbookNs);

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                !relTargets.TryGetValue(relId, out var worksheetPath))
            {
                continue;
            }

            var advancedRules = sheet.ConditionalFormats.Where(IsAdvancedConditionalFormat).ToList();
            if (advancedRules.Count == 0)
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            foreach (var cf in advancedRules)
                root.Add(ToAdvancedConditionalFormattingXml(cf, workbookNs, dxfIds));

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static IReadOnlyDictionary<Guid, int> SaveDifferentialStyles(
        ZipArchive archive,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var rules = workbook.Sheets
            .SelectMany(sheet => sheet.ConditionalFormats)
            .Where(cf => IsAdvancedConditionalFormat(cf) && cf.FormatIfTrue is not null)
            .ToList();
        if (rules.Count == 0)
            return new Dictionary<Guid, int>();

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        var stylesXml = stylesEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(stylesEntry)
            : new XDocument(new XElement(workbookNs + "styleSheet"));
        var root = stylesXml.Root;
        if (root is null)
            return new Dictionary<Guid, int>();

        var dxfs = root.Element(workbookNs + "dxfs");
        if (dxfs is null)
        {
            dxfs = new XElement(workbookNs + "dxfs");
            root.Add(dxfs);
        }

        var result = new Dictionary<Guid, int>();
        var nextIndex = dxfs.Elements(workbookNs + "dxf").Count();
        foreach (var rule in rules)
        {
            if (rule.FormatIfTrue is null)
                continue;

            result[rule.Id] = nextIndex++;
            dxfs.Add(ToDifferentialStyleXml(rule.FormatIfTrue, workbookNs, nextIndex));
        }

        dxfs.SetAttributeValue("count", dxfs.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        return result;
    }

    private static XElement ToDifferentialStyleXml(CellStyle style, XNamespace workbookNs, int numberFormatId)
    {
        var def = CellStyle.Default;
        var dxf = new XElement(
            workbookNs + "dxf",
            style.NumberFormat != def.NumberFormat
                ? new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", (164 + numberFormatId).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", style.NumberFormat))
                : null,
            HasDifferentialFont(style)
                ? new XElement(
                    workbookNs + "font",
                    style.Bold != def.Bold ? new XElement(workbookNs + "b") : null,
                    style.Italic != def.Italic ? new XElement(workbookNs + "i") : null,
                    style.Underline != def.Underline ? new XElement(workbookNs + "u") : null,
                    style.Strikethrough != def.Strikethrough ? new XElement(workbookNs + "strike") : null,
                    style.Superscript != def.Superscript
                        ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "superscript"))
                        : style.Subscript != def.Subscript
                            ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "subscript"))
                            : null,
                    style.FontColor != def.FontColor ? new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(style.FontColor))) : null,
                    style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize)
                        ? new XElement(workbookNs + "sz", new XAttribute("val", style.FontSize.ToString(CultureInfo.InvariantCulture)))
                        : null,
                    style.FontName != def.FontName ? new XElement(workbookNs + "name", new XAttribute("val", style.FontName)) : null)
                : null,
            HasDifferentialFill(style)
                ? new XElement(
                    workbookNs + "fill",
                    new XElement(
                        workbookNs + "patternFill",
                        new XAttribute("patternType", ToPatternType(style.FillPatternStyle)),
                        style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid
                            ? style.FillColor is { } fill
                                ? new XElement(workbookNs + "fgColor", new XAttribute("rgb", ToArgb(fill)))
                                : null
                            : style.FillPatternColor is { } pattern
                                ? new XElement(workbookNs + "fgColor", new XAttribute("rgb", ToArgb(pattern)))
                                : null,
                        style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid
                            ? new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64"))
                            : style.FillColor is { } background
                                ? new XElement(workbookNs + "bgColor", new XAttribute("rgb", ToArgb(background)))
                                : new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64"))))
                : null,
            HasDifferentialBorder(style)
                ? new XElement(
                    workbookNs + "border",
                    ToDifferentialBorderXml("left", style.BorderLeft, workbookNs),
                    ToDifferentialBorderXml("right", style.BorderRight, workbookNs),
                    ToDifferentialBorderXml("top", style.BorderTop, workbookNs),
                    ToDifferentialBorderXml("bottom", style.BorderBottom, workbookNs))
                : null);

        MergeDifferentialStyleElementNativeMetadata(dxf, style, workbookNs);

        foreach (var (name, value) in style.NativeDifferentialAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && dxf.Attribute(name) is null)
                dxf.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (style.NativeDifferentialChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == workbookNs &&
                    nativeChild.Name.LocalName is not "font" and not "numFmt" and not "fill" and not "alignment" and not "border" and not "protection")
                {
                    dxf.Add(nativeChild);
                }
            }
            catch
            {
                // Ignore malformed native differential-style payloads from older saves.
            }
        }

        return dxf;
    }

    private static void MergeDifferentialStyleElementNativeMetadata(
        XElement dxf,
        CellStyle style,
        XNamespace workbookNs)
    {
        foreach (var (localName, sourceXml) in style.NativeDifferentialElementXmls ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(sourceXml))
                continue;

            try
            {
                var sourceElement = XElement.Parse(sourceXml);
                if (sourceElement.Name.Namespace != workbookNs || !IsModeledDifferentialStyleElement(sourceElement.Name.LocalName))
                    continue;

                var targetElement = dxf.Element(workbookNs + localName);
                if (targetElement is null)
                    dxf.Add(sourceElement);
                else
                    XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceElement, targetElement);
            }
            catch
            {
                // Ignore malformed nested dxf metadata from older saves.
            }
        }
    }

    private static bool IsModeledDifferentialStyleElement(string localName) =>
        localName is "font" or "numFmt" or "fill" or "alignment" or "border" or "protection";

    private static bool HasDifferentialFont(CellStyle style)
    {
        var def = CellStyle.Default;
        return style.Bold != def.Bold ||
            style.Italic != def.Italic ||
            style.Underline != def.Underline ||
            style.Strikethrough != def.Strikethrough ||
            style.Superscript != def.Superscript ||
            style.Subscript != def.Subscript ||
            style.FontColor != def.FontColor ||
            style.FontSize != def.FontSize ||
            style.FontName != def.FontName;
    }

    private static bool HasDifferentialBorder(CellStyle style) =>
        style.BorderLeft.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None;

    private static bool HasDifferentialFill(CellStyle style) =>
        style.FillColor is not null ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.FillPatternColor is not null;

    private static XElement ToDifferentialBorderXml(string edgeName, CellBorder border, XNamespace workbookNs)
    {
        var element = new XElement(workbookNs + edgeName);
        if (border.Style != BorderStyle.None)
        {
            element.SetAttributeValue("style", ToDifferentialBorderStyle(border.Style));
            element.Add(new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(border.Color))));
        }

        return element;
    }

    private static string ToDifferentialBorderStyle(BorderStyle style) =>
        style switch
        {
            BorderStyle.Thin => "thin",
            BorderStyle.Medium => "medium",
            BorderStyle.Thick => "thick",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Dotted => "dotted",
            BorderStyle.Double => "double",
            _ => "none"
        };


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
        foreach (var (name, value) in cf.NativeContainerAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && element.Attribute(name) is null)
                element.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (cf.NativeContainerChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
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

        return element;
    }

    private static XElement ToAdvancedCfRuleXml(
        ConditionalFormat cf,
        XNamespace worksheetNs,
        IReadOnlyDictionary<Guid, int> differentialStyleIds)
    {
        var rule = new XElement(
            worksheetNs + "cfRule",
            new XAttribute("type", ToAdvancedCfRuleType(cf.RuleType)),
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
                    ToCfvoXml(worksheetNs, cf.MinThresholdType, cf.MinThresholdValue),
                    cf.UseThreeColorScale ? ToCfvoXml(worksheetNs, cf.MidThresholdType, cf.MidThresholdValue) : null,
                    ToCfvoXml(worksheetNs, cf.MaxThresholdType, cf.MaxThresholdValue),
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
                break;
            case CfRuleType.IconSet:
                rule.Add(AddConditionalFormatPayloadNativeMetadata(new XElement(
                    worksheetNs + "iconSet",
                    new XAttribute("iconSet", string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle),
                    new XAttribute("showValue", cf.IconSetShowValue ? "1" : "0"),
                    new XAttribute("reverse", cf.IconSetReverse ? "1" : "0"),
                    GetIconSetThresholds(cf).Select(threshold => ToCfvoXml(worksheetNs, threshold.Type, threshold.Value))), cf, worksheetNs));
                break;
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
                rule.SetAttributeValue("timePeriod", string.IsNullOrWhiteSpace(cf.DateOccurringPeriod) ? "today" : cf.DateOccurringPeriod);
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

        foreach (var (name, value) in cf.NativeAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && rule.Attribute(name) is null)
                rule.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (cf.NativeChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
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

        return rule;
    }

    private static XElement AddConditionalFormatPayloadNativeMetadata(
        XElement payload,
        ConditionalFormat cf,
        XNamespace worksheetNs)
    {
        foreach (var (name, value) in cf.NativePayloadAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && payload.Attribute(name) is null)
                payload.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (cf.NativePayloadChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == worksheetNs)
                    payload.Add(nativeChild);
            }
            catch
            {
                // Ignore malformed native conditional-format payload metadata from older saves.
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

    private static XElement ToCfvoXml(XNamespace worksheetNs, CfThresholdType type, string? value)
    {
        var element = new XElement(worksheetNs + "cfvo", new XAttribute("type", ToCfvoType(type)));
        if (!string.IsNullOrWhiteSpace(value))
            element.SetAttributeValue("val", value);
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

    private static bool IsAdvancedConditionalFormat(ConditionalFormat cf) =>
        cf.RuleType is CfRuleType.ColorScale or CfRuleType.DataBar or CfRuleType.IconSet or
            CfRuleType.AboveAverage or CfRuleType.Top10 or
            CfRuleType.UniqueValues or CfRuleType.DuplicateValues or
            CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith or
            CfRuleType.DateOccurring or
            CfRuleType.Blanks or CfRuleType.NoBlanks or CfRuleType.Errors or CfRuleType.NoErrors;

    private static string ToAdvancedCfRuleType(CfRuleType type) =>
        type switch
        {
            CfRuleType.ColorScale => "colorScale",
            CfRuleType.DataBar => "dataBar",
            CfRuleType.IconSet => "iconSet",
            CfRuleType.AboveAverage => "aboveAverage",
            CfRuleType.Top10 => "top10",
            CfRuleType.UniqueValues => "uniqueValues",
            CfRuleType.DuplicateValues => "duplicateValues",
            CfRuleType.ContainsText => "containsText",
            CfRuleType.NotContainsText => "notContainsText",
            CfRuleType.BeginsWith => "beginsWith",
            CfRuleType.EndsWith => "endsWith",
            CfRuleType.DateOccurring => "timePeriod",
            CfRuleType.Blanks => "containsBlanks",
            CfRuleType.NoBlanks => "notContainsBlanks",
            CfRuleType.Errors => "containsErrors",
            CfRuleType.NoErrors => "notContainsErrors",
            _ => throw new InvalidOperationException("Conditional format is not an advanced rule.")
        };

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

    private static string ToCfvoType(CfThresholdType type) =>
        type switch
        {
            CfThresholdType.Max => "max",
            CfThresholdType.Number => "num",
            CfThresholdType.Percent => "percent",
            CfThresholdType.Percentile => "percentile",
            CfThresholdType.Formula => "formula",
            _ => "min"
        };
}
